using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CodexHomeMover
{
    internal sealed class MigrationEngine
    {
        private const int CopyBufferSize = 4 * 1024 * 1024;
        private const int CurrentRecordFormatVersion = 1;
        private const string BackupNameMarker = ".codex-home-mover-backup-";
        private const string QuarantineNameMarker = ".codex-home-mover-quarantine-";
        private const string RestoreNameMarker = ".codex-home-mover-restore-";
        private readonly object eventLock = new object();

        public event Action<MigrationProgress> ProgressChanged;
        public event Action<string> LogMessage;

        public PreflightResult Preflight(MigrationOptions options, CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            Report(MigrationStage.Preflight, 0, "正在检查迁移条件……", 0, 0, 0, 0);
            string source = NormalizeDirectoryPath(options.SourcePath);
            string destination = NormalizeDirectoryPath(options.DestinationPath);

            if (options.RequireAdministrator && !NativeMethods.IsAdministrator())
            {
                throw new InvalidOperationException("请以管理员身份运行程序。创建 Junction 和安全权限需要管理员权限。");
            }
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException("找不到 Codex 数据目录：" + source);
            }
            if (IsReparsePoint(source))
            {
                throw new InvalidOperationException("源目录已经是目录链接。请使用“检查状态”或“迁回 C 盘”，不要再次迁移。");
            }
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("源目录和目标目录不能相同。");
            }
            if (IsWithin(destination, source) || IsWithin(source, destination))
            {
                throw new InvalidOperationException("源目录和目标目录不能互相包含。");
            }
            ValidateSafeDirectoryDepth(source, "源目录");
            ValidateSafeDirectoryDepth(destination, "目标目录");
            ValidateCodexHome(source);

            string destinationRoot = Path.GetPathRoot(destination);
            if (string.IsNullOrWhiteSpace(destinationRoot))
            {
                throw new InvalidOperationException("目标目录必须位于本地磁盘。");
            }
            DriveInfo destinationDrive = new DriveInfo(destinationRoot);
            if (!destinationDrive.IsReady || destinationDrive.DriveType != DriveType.Fixed)
            {
                throw new InvalidOperationException("目标必须是已就绪的本地固定磁盘。");
            }
            if (!string.Equals(destinationDrive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("目标磁盘必须使用 NTFS，才能安全创建 Junction 和保留权限。");
            }
            if (Directory.Exists(destination) && IsReparsePoint(destination))
            {
                throw new InvalidOperationException("目标目录必须是真实目录，不能是目录链接。");
            }

            DirectorySnapshot snapshot = BuildSnapshot(source, cancellationToken);
            if (snapshot.Files.Count == 0)
            {
                throw new InvalidOperationException("源目录中没有可迁移文件。");
            }

            bool destinationHasData = Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any();
            if (destinationHasData && !options.AllowExistingDestination)
            {
                throw new InvalidOperationException("目标目录不是空目录。确认它是可信的预复制目录后，再勾选“复用已有目标目录”。");
            }

            long requiredBytes = CalculateRequiredBytes(snapshot, destination, cancellationToken);
            long largestFile = snapshot.Files.Count == 0 ? 0 : snapshot.Files.Max(item => item.Length);
            requiredBytes += Math.Max(512L * 1024L * 1024L, largestFile);
            if (destinationDrive.AvailableFreeSpace < requiredBytes)
            {
                throw new IOException(string.Format(
                    "目标磁盘空间不足。还需要约 {0}，当前可用 {1}。",
                    FormatBytes(requiredBytes),
                    FormatBytes(destinationDrive.AvailableFreeSpace)));
            }

            PreflightResult result = new PreflightResult();
            result.SourcePath = source;
            result.DestinationPath = destination;
            result.SourceBytes = snapshot.TotalBytes;
            result.RequiredAdditionalBytes = requiredBytes;
            result.DestinationFreeBytes = destinationDrive.AvailableFreeSpace;
            result.FileCount = snapshot.Files.Count;
            result.DirectoryCount = snapshot.Directories.Count;
            result.JunctionCount = snapshot.Junctions.Count;
            result.DestinationHasData = destinationHasData;
            if (destinationHasData)
            {
                result.Warnings.Add("目标目录已有数据；多余文件会移到同盘隔离目录，不会直接删除。");
            }
            if (!options.VerifySha256)
            {
                result.Warnings.Add("已关闭 SHA-256 校验，只会核对文件大小；不建议迁移重要会话时关闭。");
            }

            Report(MigrationStage.Preflight, 3, "预检通过。", 0, snapshot.TotalBytes, 0, snapshot.Files.Count);
            return result;
        }

        public MigrationRecord RunMigration(MigrationOptions options, CancellationToken cancellationToken)
        {
            PreflightResult preflight = Preflight(options, cancellationToken);
            string source = preflight.SourcePath;
            string destination = preflight.DestinationPath;

            WaitForCodexToExit(options.IgnoreRunningProcesses, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(2000);

            Log("未检测到已知 Codex/ChatGPT 进程，正在重新扫描最终文件状态。");
            ValidateRealDirectoryRoot(source, "源目录", true);
            DirectorySnapshot snapshot = BuildSnapshot(source, cancellationToken);
            ValidateRealDirectoryRoot(source, "源目录", true);
            Directory.CreateDirectory(destination);
            if (options.RequireAdministrator)
            {
                ApplyPrivateRootAcl(destination);
            }
            ValidateRealDirectoryRoot(destination, "目标目录", false);

            string quarantine = ReconcileDestination(snapshot, destination, options.RequireAdministrator, cancellationToken);
            ValidateActiveMigrationRoots(source, destination, false);
            CopySnapshot(snapshot, destination, 5, 62, cancellationToken);
            ValidateActiveMigrationRoots(source, destination, true);
            CreateOrUpdateJunctions(snapshot, source, destination, quarantine, cancellationToken);
            ValidateActiveMigrationRoots(source, destination, true);
            VerifySnapshot(snapshot, destination, options.VerifySha256, 63, 88, cancellationToken);
            ValidateActiveMigrationRoots(source, destination, true);
            CheckSqliteDatabases(snapshot, destination, 89, 93, cancellationToken);
            if (options.RequireAdministrator)
            {
                SecureChildren(destination, snapshot, 94, 97, cancellationToken);
            }
            ValidateActiveMigrationRoots(source, destination, true);

            cancellationToken.ThrowIfCancellationRequested();
            Report(MigrationStage.Switching, 98, "正在再次确认 Codex 已退出……", 0, snapshot.TotalBytes, 0, snapshot.Files.Count);
            WaitForCodexToExit(options.IgnoreRunningProcesses, cancellationToken);
            Thread.Sleep(1000);
            ValidateActiveMigrationRoots(source, destination, true);
            Report(MigrationStage.Switching, 98, "正在执行原路径瞬时切换……", 0, snapshot.TotalBytes, 0, snapshot.Files.Count);
            ValidateActiveMigrationRoots(source, destination, true);
            string backup = source + ".codex-home-mover-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            if (Directory.Exists(backup) || File.Exists(backup))
            {
                throw new IOException("安全备份路径已存在：" + backup);
            }

            MoveSourceToBackup(source, backup, options.IgnoreRunningProcesses,
                snapshot.Files.Select(item => item.FullPath).ToList(), cancellationToken);
            bool junctionCreated = false;
            try
            {
                NativeMethods.CreateJunction(source, destination);
                junctionCreated = true;
                VerifyJunction(source, destination);

                MigrationRecord record = new MigrationRecord();
                record.FormatVersion = CurrentRecordFormatVersion;
                record.SourcePath = source;
                record.DestinationPath = destination;
                record.BackupPath = backup;
                record.QuarantinePath = quarantine;
                record.CompletedUtc = DateTime.UtcNow;
                record.BackupDeleted = false;
                SaveRecord(record, options.RecordPathOverride);

                Report(MigrationStage.Completed, 100, "迁移成功。请重新打开 Codex 验证，暂时不要清理 C 盘备份。",
                    snapshot.TotalBytes, snapshot.TotalBytes, snapshot.Files.Count, snapshot.Files.Count);
                Log("迁移完成。原路径已通过 Junction 指向目标目录。");
                return record;
            }
            catch
            {
                Log("切换失败，正在自动恢复 C 盘原目录。");
                try
                {
                    if (junctionCreated && Directory.Exists(source) && IsReparsePoint(source))
                    {
                        Directory.Delete(source, false);
                    }
                    if (!Directory.Exists(source) && Directory.Exists(backup))
                    {
                        Directory.Move(backup, source);
                    }
                }
                catch (Exception rollbackError)
                {
                    Log("自动恢复失败：" + rollbackError.Message);
                }
                throw;
            }
        }

        public MigrationRecord LoadRecord(string recordPathOverride)
        {
            string path = GetRecordPath(recordPathOverride);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MigrationRecord));
                    MigrationRecord record = serializer.ReadObject(stream) as MigrationRecord;
                    return ValidateMigrationRecord(record);
                }
            }
            catch (SerializationException error)
            {
                throw new InvalidDataException("迁移记录格式损坏，已拒绝使用。", error);
            }
        }

        public void Rollback(MigrationOptions options, CancellationToken cancellationToken)
        {
            MigrationRecord record = LoadRecord(options.RecordPathOverride);
            if (record == null)
            {
                throw new InvalidOperationException("没有找到可回滚的迁移记录。");
            }
            ValidateOperationOptions(record, options);
            WaitForCodexToExit(options.IgnoreRunningProcesses, cancellationToken);
            VerifyJunction(record.SourcePath, record.DestinationPath);
            ValidateCodexHome(record.DestinationPath);

            if (!record.BackupDeleted && Directory.Exists(record.BackupPath) && !IsReparsePoint(record.BackupPath))
            {
                ValidateCodexHome(record.BackupPath);
                Report(MigrationStage.RollingBack, 20, "正在恢复 C 盘原目录……", 0, 0, 0, 0);
                Directory.Delete(record.SourcePath, false);
                try
                {
                    Directory.Move(record.BackupPath, record.SourcePath);
                }
                catch
                {
                    if (!Directory.Exists(record.SourcePath))
                    {
                        NativeMethods.CreateJunction(record.SourcePath, record.DestinationPath);
                    }
                    throw;
                }
                DeleteRecord(options.RecordPathOverride);
                Report(MigrationStage.Completed, 100, "已回滚到 C 盘，E 盘副本仍然保留。", 0, 0, 0, 0);
                return;
            }

            RestoreAfterCleanup(record, options, cancellationToken);
        }

        public void DeleteSafetyBackup(MigrationOptions options, CancellationToken cancellationToken)
        {
            MigrationRecord record = LoadRecord(options.RecordPathOverride);
            if (record == null)
            {
                throw new InvalidOperationException("没有找到迁移记录。");
            }
            ValidateOperationOptions(record, options);
            VerifyJunction(record.SourcePath, record.DestinationPath);
            ValidateCodexHome(record.DestinationPath);
            if (record.BackupDeleted)
            {
                throw new InvalidOperationException("C 盘安全备份已经清理。");
            }
            ValidateBackupPath(record.SourcePath, record.BackupPath);
            if (!Directory.Exists(record.BackupPath) || IsReparsePoint(record.BackupPath))
            {
                throw new InvalidOperationException("找不到有效的 C 盘安全备份，已停止清理。");
            }
            ValidateCodexHome(record.BackupPath);

            DirectorySnapshot snapshot = BuildSnapshot(record.BackupPath, cancellationToken);
            int total = Math.Max(1, snapshot.Files.Count + snapshot.Junctions.Count + snapshot.Directories.Count + 1);
            int processed = 0;
            foreach (FileSnapshot file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.SetAttributes(file.FullPath, FileAttributes.Normal);
                File.Delete(file.FullPath);
                processed++;
                Report(MigrationStage.CleaningUp, 100 * processed / total, "正在释放 C 盘空间……", 0, 0, processed, total);
            }
            foreach (JunctionSnapshot junction in snapshot.Junctions.OrderByDescending(item => item.FullPath.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.Delete(junction.FullPath, false);
                processed++;
            }
            foreach (string relativeDirectory in snapshot.Directories.OrderByDescending(item => item.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = Path.Combine(record.BackupPath, relativeDirectory);
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, false);
                }
                processed++;
            }
            Directory.Delete(record.BackupPath, false);
            record.BackupDeleted = true;
            SaveRecord(record, options.RecordPathOverride);
            Report(MigrationStage.Completed, 100, "C 盘安全备份已清理，空间已经释放。", 0, 0, total, total);
        }

        private void RestoreAfterCleanup(MigrationRecord record, MigrationOptions options, CancellationToken cancellationToken)
        {
            DirectorySnapshot snapshot = BuildSnapshot(record.DestinationPath, cancellationToken);
            DriveInfo sourceDrive = new DriveInfo(Path.GetPathRoot(record.SourcePath));
            long largestFile = snapshot.Files.Count == 0 ? 0 : snapshot.Files.Max(item => item.Length);
            long needed = snapshot.TotalBytes + Math.Max(512L * 1024L * 1024L, largestFile);
            if (sourceDrive.AvailableFreeSpace < needed)
            {
                throw new IOException(string.Format("C 盘空间不足，回迁至少需要约 {0}。", FormatBytes(needed)));
            }

            string restoreStaging = CreateUniqueSiblingDirectory(
                record.SourcePath, RestoreNameMarker, "回迁暂存目录", options.RequireAdministrator);
            CopySnapshot(snapshot, restoreStaging, 5, 68, cancellationToken);
            CreateOrUpdateJunctions(snapshot, record.DestinationPath, restoreStaging, null, cancellationToken);
            VerifySnapshot(snapshot, restoreStaging, true, 69, 92, cancellationToken);
            CheckSqliteDatabases(snapshot, restoreStaging, 93, 96, cancellationToken);
            if (options.RequireAdministrator)
            {
                SecureChildren(restoreStaging, snapshot, 97, 98, cancellationToken);
            }

            Report(MigrationStage.RollingBack, 99, "正在切回 C 盘……", 0, snapshot.TotalBytes, 0, snapshot.Files.Count);
            Directory.Delete(record.SourcePath, false);
            try
            {
                Directory.Move(restoreStaging, record.SourcePath);
            }
            catch
            {
                if (!Directory.Exists(record.SourcePath))
                {
                    NativeMethods.CreateJunction(record.SourcePath, record.DestinationPath);
                }
                throw;
            }
            DeleteRecord(options.RecordPathOverride);
            Report(MigrationStage.Completed, 100, "已从 E 盘完整回迁到 C 盘，E 盘副本仍然保留。",
                snapshot.TotalBytes, snapshot.TotalBytes, snapshot.Files.Count, snapshot.Files.Count);
        }

        private void WaitForCodexToExit(bool ignoreRunningProcesses, CancellationToken cancellationToken)
        {
            if (ignoreRunningProcesses)
            {
                return;
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IList<string> running = FindRunningCodexProcesses();
                if (running.Count == 0)
                {
                    break;
                }
                Report(MigrationStage.WaitingForCodex, 4,
                    "请彻底退出 Codex/ChatGPT。检测到：" + string.Join("、", running), 0, 0, 0, 0);
                Thread.Sleep(1000);
            }
        }

        private void MoveSourceToBackup(
            string source,
            string backup,
            bool ignoreRunningProcesses,
            IList<string> sourceFiles,
            CancellationToken cancellationToken)
        {
            string lockingProcessSummary = null;
            bool queriedLockingProcesses = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ignoreRunningProcesses)
                {
                    IList<string> running = FindRunningCodexProcesses();
                    if (running.Count > 0)
                    {
                        Report(MigrationStage.WaitingForCodex, 98,
                            "最终切换前请再次退出 Codex/ChatGPT。检测到：" + string.Join("、", running),
                            0, 0, 0, 0);
                        Thread.Sleep(1000);
                        continue;
                    }
                }

                try
                {
                    Directory.Move(source, backup);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException error)
                {
                    if (!IsRetryableMoveError(error))
                    {
                        throw;
                    }
                }

                if (!queriedLockingProcesses)
                {
                    queriedLockingProcesses = true;
                    IList<string> lockingProcesses = NativeMethods.GetLockingProcesses(sourceFiles);
                    if (lockingProcesses.Count > 0)
                    {
                        lockingProcessSummary = string.Join("、", lockingProcesses);
                        Log("Windows 检测到锁定源文件的进程：" + lockingProcessSummary);
                    }
                    else
                    {
                        Log("Windows Restart Manager 未识别到具体锁定进程；可能是目录句柄、安全软件或系统策略。");
                    }
                }

                string waitMessage = string.IsNullOrWhiteSpace(lockingProcessSummary)
                    ? "目录仍被占用：请关闭资源管理器、终端或编辑器；程序会持续等待，也可点“取消”。"
                    : "请关闭占用进程：" + lockingProcessSummary;
                Report(MigrationStage.WaitingForCodex, 98,
                    waitMessage,
                    0, 0, 0, 0);
                Thread.Sleep(1000);
            }
        }

        private static bool IsRetryableMoveError(IOException error)
        {
            int win32Code = error.HResult & 0xFFFF;
            return win32Code == 5 || win32Code == 32 || win32Code == 33;
        }

        private static IList<string> FindRunningCodexProcesses()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ChatGPT", "codex", "codex-code-mode-host"
            };
            List<string> found = new List<string>();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (names.Contains(process.ProcessName))
                    {
                        found.Add(process.ProcessName + " (" + process.Id + ")");
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private DirectorySnapshot BuildSnapshot(string root, CancellationToken cancellationToken)
        {
            root = NormalizeDirectoryPath(root);
            DirectorySnapshot snapshot = new DirectorySnapshot();
            snapshot.RootPath = root;
            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes = File.GetAttributes(directory);
                    string relative = GetRelativePath(root, directory);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        JunctionSnapshot junction = new JunctionSnapshot();
                        junction.FullPath = directory;
                        junction.RelativePath = relative;
                        junction.TargetPath = NativeMethods.ResolveFinalPath(directory);
                        snapshot.Junctions.Add(junction);
                    }
                    else
                    {
                        snapshot.Directories.Add(relative);
                        pending.Push(directory);
                    }
                }

                foreach (string filePath in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileInfo info = new FileInfo(filePath);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new NotSupportedException("暂不支持文件型符号链接：" + filePath);
                    }
                    FileSnapshot file = new FileSnapshot();
                    file.FullPath = filePath;
                    file.RelativePath = GetRelativePath(root, filePath);
                    file.Length = info.Length;
                    file.LastWriteUtc = info.LastWriteTimeUtc;
                    file.Attributes = info.Attributes;
                    snapshot.Files.Add(file);
                    snapshot.TotalBytes += info.Length;
                }
            }

            snapshot.Files = snapshot.Files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
            snapshot.Directories = snapshot.Directories.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToList();
            snapshot.Junctions = snapshot.Junctions.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
            return snapshot;
        }

        private long CalculateRequiredBytes(DirectorySnapshot snapshot, string destination, CancellationToken cancellationToken)
        {
            long needed = 0;
            foreach (FileSnapshot file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(destination, file.RelativePath);
                FileInfo existing = new FileInfo(target);
                if (!existing.Exists || existing.Length != file.Length)
                {
                    needed += file.Length;
                }
            }
            return needed;
        }

        private string ReconcileDestination(
            DirectorySnapshot sourceSnapshot,
            string destination,
            bool secureQuarantine,
            CancellationToken cancellationToken)
        {
            Report(MigrationStage.Reconciling, 4, "正在核对已有目标目录……", 0, sourceSnapshot.TotalBytes, 0, sourceSnapshot.Files.Count);
            DirectorySnapshot destinationSnapshot = BuildSnapshot(destination, cancellationToken);
            HashSet<string> sourceFiles = new HashSet<string>(sourceSnapshot.Files.Select(item => item.RelativePath), StringComparer.OrdinalIgnoreCase);
            HashSet<string> sourceDirectories = new HashSet<string>(sourceSnapshot.Directories, StringComparer.OrdinalIgnoreCase);
            HashSet<string> sourceLinks = new HashSet<string>(sourceSnapshot.Junctions.Select(item => item.RelativePath), StringComparer.OrdinalIgnoreCase);
            string quarantine = null;

            foreach (FileSnapshot extraFile in destinationSnapshot.Files.Where(item => !sourceFiles.Contains(item.RelativePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                quarantine = EnsureQuarantine(destination, quarantine, secureQuarantine);
                ValidateQuarantineDirectory(destination, quarantine);
                string target = Path.Combine(quarantine, extraFile.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                {
                    target = target + "." + Guid.NewGuid().ToString("N");
                }
                File.Move(extraFile.FullPath, target);
                if (secureQuarantine)
                {
                    SetFileToInheritedAcl(target);
                }
                Log("已隔离目标中的多余文件：" + extraFile.RelativePath);
            }

            foreach (JunctionSnapshot extraLink in destinationSnapshot.Junctions.Where(item => !sourceLinks.Contains(item.RelativePath)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                quarantine = EnsureQuarantine(destination, quarantine, secureQuarantine);
                ValidateQuarantineDirectory(destination, quarantine);
                string targetLink = Path.Combine(quarantine, extraLink.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetLink));
                NativeMethods.CreateJunction(targetLink, extraLink.TargetPath);
                Directory.Delete(extraLink.FullPath, false);
                Log("已隔离目标中的多余目录链接：" + extraLink.RelativePath);
            }

            foreach (string extraDirectory in destinationSnapshot.Directories
                .Where(item => !sourceDirectories.Contains(item) && !sourceLinks.Contains(item))
                .OrderByDescending(item => item.Length))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = Path.Combine(destination, extraDirectory);
                if (Directory.Exists(fullPath) && !Directory.EnumerateFileSystemEntries(fullPath).Any())
                {
                    Directory.Delete(fullPath, false);
                }
            }
            return quarantine;
        }

        private void CopySnapshot(DirectorySnapshot snapshot, string destination, int startPercent, int endPercent, CancellationToken cancellationToken)
        {
            foreach (string relativeDirectory in snapshot.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(destination, relativeDirectory));
            }

            long processedBytes = 0;
            int processedFiles = 0;
            foreach (FileSnapshot file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(destination, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                FileInfo existing = new FileInfo(target);
                bool unchanged = existing.Exists && existing.Length == file.Length &&
                    Math.Abs((existing.LastWriteTimeUtc - file.LastWriteUtc).TotalSeconds) < 2;
                if (!unchanged)
                {
                    CopyFileAtomic(file, target, cancellationToken, delegate(long bytes)
                    {
                        long current = processedBytes + bytes;
                        int percent = ScaleByBytes(current, snapshot.TotalBytes, startPercent, endPercent);
                        Report(MigrationStage.Copying, percent,
                            "正在复制：" + file.RelativePath, current, snapshot.TotalBytes, processedFiles, snapshot.Files.Count);
                    });
                }
                processedBytes += file.Length;
                processedFiles++;
                int completedPercent = ScaleByBytes(processedBytes, snapshot.TotalBytes, startPercent, endPercent);
                Report(MigrationStage.Copying, completedPercent,
                    "已复制/复用 " + processedFiles + " / " + snapshot.Files.Count + " 个文件",
                    processedBytes, snapshot.TotalBytes, processedFiles, snapshot.Files.Count);
            }
        }

        private void CopyFileAtomic(FileSnapshot source, string destination, CancellationToken cancellationToken, Action<long> byteProgress)
        {
            string temporary = Path.Combine(
                Path.GetDirectoryName(destination),
                ".chm-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
            FileAttributes? previousDestinationAttributes = null;
            bool replacementCommitted = false;
            try
            {
                long copied = 0;
                byte[] buffer = new byte[CopyBufferSize];
                using (FileStream input = new FileStream(source.FullPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, CopyBufferSize, FileOptions.SequentialScan | FileOptions.WriteThrough))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        copied += read;
                        if (byteProgress != null)
                        {
                            byteProgress(copied);
                        }
                    }
                    output.Flush(true);
                }

                if (File.Exists(destination))
                {
                    previousDestinationAttributes = File.GetAttributes(destination);
                    try
                    {
                        PrepareDestinationForReplacement(destination);
                        File.Replace(temporary, destination, null, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        SetFileToInheritedAcl(destination);
                        PrepareDestinationForReplacement(destination);
                        File.Replace(temporary, destination, null, true);
                    }
                    replacementCommitted = true;
                }
                else
                {
                    File.Move(temporary, destination);
                    replacementCommitted = true;
                }

                File.SetLastWriteTimeUtc(destination, source.LastWriteUtc);
                File.SetAttributes(destination, GetPortableFileAttributes(source.Attributes));
            }
            catch (UnauthorizedAccessException error)
            {
                RestoreDestinationAttributesAfterFailedReplacement(
                    destination, previousDestinationAttributes, replacementCommitted);
                throw new IOException(
                    "Windows 拒绝更新目标文件：" + source.RelativePath +
                    "\r\n目标位置：" + destination +
                    "\r\nC 盘原文件未受影响；请不要手动删除它。",
                    error);
            }
            catch (IOException error)
            {
                RestoreDestinationAttributesAfterFailedReplacement(
                    destination, previousDestinationAttributes, replacementCommitted);
                throw new IOException(
                    "无法更新目标文件：" + source.RelativePath +
                    "\r\n目标位置：" + destination +
                    "\r\n" + error.Message,
                    error);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.SetAttributes(temporary, FileAttributes.Normal);
                        File.Delete(temporary);
                    }
                }
                catch (Exception cleanupError)
                {
                    try
                    {
                        Log("未能清理复制临时文件：" + temporary + "；" + cleanupError.Message);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static FileAttributes GetPortableFileAttributes(FileAttributes sourceAttributes)
        {
            FileAttributes supported =
                FileAttributes.Archive |
                FileAttributes.Hidden |
                FileAttributes.NotContentIndexed |
                FileAttributes.ReadOnly |
                FileAttributes.System |
                FileAttributes.Temporary;
            FileAttributes result = sourceAttributes & supported;
            return result == 0 ? FileAttributes.Normal : result;
        }

        private static void RestoreDestinationAttributesAfterFailedReplacement(
            string path,
            FileAttributes? attributes,
            bool replacementCommitted)
        {
            if (replacementCommitted || !attributes.HasValue || !File.Exists(path))
            {
                return;
            }
            try
            {
                File.SetAttributes(path, attributes.Value);
            }
            catch
            {
            }
        }

        private static void PrepareDestinationForReplacement(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            FileAttributes blocking = FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden;
            if ((attributes & blocking) != 0)
            {
                File.SetAttributes(path, attributes & ~blocking);
            }
        }

        private void CreateOrUpdateJunctions(
            DirectorySnapshot snapshot,
            string sourceRoot,
            string destinationRoot,
            string quarantine,
            CancellationToken cancellationToken)
        {
            foreach (JunctionSnapshot sourceLink in snapshot.Junctions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destinationLink = Path.Combine(destinationRoot, sourceLink.RelativePath);
                string target = sourceLink.TargetPath;
                if (IsWithin(target, sourceRoot) || string.Equals(NormalizeDirectoryPath(target), NormalizeDirectoryPath(sourceRoot), StringComparison.OrdinalIgnoreCase))
                {
                    target = destinationRoot + target.Substring(NormalizeDirectoryPath(sourceRoot).Length);
                }

                if (Directory.Exists(destinationLink))
                {
                    if (IsReparsePoint(destinationLink))
                    {
                        string existingTarget = NativeMethods.ResolveFinalPath(destinationLink);
                        if (PathsEqual(existingTarget, target))
                        {
                            continue;
                        }
                        Directory.Delete(destinationLink, false);
                    }
                    else
                    {
                        if (Directory.EnumerateFileSystemEntries(destinationLink).Any())
                        {
                            throw new IOException("目标中的普通目录与源 Junction 冲突，且目录非空：" + destinationLink);
                        }
                        Directory.Delete(destinationLink, false);
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationLink));
                NativeMethods.CreateJunction(destinationLink, target);
            }
        }

        private void VerifySnapshot(
            DirectorySnapshot snapshot,
            string destination,
            bool verifySha256,
            int startPercent,
            int endPercent,
            CancellationToken cancellationToken)
        {
            int processed = 0;
            foreach (FileSnapshot file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(destination, file.RelativePath);
                FileInfo targetInfo = new FileInfo(target);
                if (!targetInfo.Exists || targetInfo.Length != file.Length)
                {
                    throw new IOException("文件大小校验失败：" + file.RelativePath);
                }

                if (verifySha256)
                {
                    string sourceHash = ComputeSha256(file.FullPath, cancellationToken);
                    string targetHash = ComputeSha256(target, cancellationToken);
                    if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("发现哈希不一致，正在重新复制一次：" + file.RelativePath);
                        CopyFileAtomic(file, target, cancellationToken, null);
                        targetHash = ComputeSha256(target, cancellationToken);
                        if (!string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException("SHA-256 校验失败：" + file.RelativePath);
                        }
                    }
                }

                processed++;
                int percent = startPercent + ((endPercent - startPercent) * processed / Math.Max(1, snapshot.Files.Count));
                string mode = verifySha256 ? "SHA-256" : "文件大小";
                Report(MigrationStage.Verifying, percent,
                    string.Format("{0} 校验：{1} / {2}", mode, processed, snapshot.Files.Count),
                    0, snapshot.TotalBytes, processed, snapshot.Files.Count);
            }
        }

        private void CheckSqliteDatabases(
            DirectorySnapshot snapshot,
            string destination,
            int startPercent,
            int endPercent,
            CancellationToken cancellationToken)
        {
            IList<FileSnapshot> databases = snapshot.Files.Where(item => IsLikelySqlite(item.FullPath)).ToList();
            if (databases.Count == 0)
            {
                Report(MigrationStage.CheckingDatabases, endPercent, "未发现 SQLite 数据库，已跳过数据库检查。", 0, 0, 0, 0);
                return;
            }

            int processed = 0;
            foreach (FileSnapshot database in databases)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = Path.Combine(destination, database.RelativePath);
                string error;
                if (!NativeMethods.QuickCheckSqlite(target, out error))
                {
                    throw new IOException("SQLite 完整性检查失败：" + database.RelativePath + "；" + error);
                }
                processed++;
                int percent = startPercent + ((endPercent - startPercent) * processed / databases.Count);
                Report(MigrationStage.CheckingDatabases, percent,
                    string.Format("数据库检查：{0} / {1}", processed, databases.Count),
                    0, 0, processed, databases.Count);
            }
        }

        private void SecureChildren(
            string destination,
            DirectorySnapshot snapshot,
            int startPercent,
            int endPercent,
            CancellationToken cancellationToken)
        {
            int total = Math.Max(1, snapshot.Directories.Count + snapshot.Files.Count);
            int processed = 0;
            foreach (string directory in snapshot.Directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetDirectoryToInheritedAcl(Path.Combine(destination, directory));
                processed++;
                ReportPermissionProgress(startPercent, endPercent, processed, total);
            }
            foreach (FileSnapshot file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetFileToInheritedAcl(Path.Combine(destination, file.RelativePath));
                processed++;
                ReportPermissionProgress(startPercent, endPercent, processed, total);
            }
            VerifySensitiveFileAcl(Path.Combine(destination, "auth.json"));
        }

        private void ReportPermissionProgress(int startPercent, int endPercent, int processed, int total)
        {
            int percent = startPercent + ((endPercent - startPercent) * processed / total);
            Report(MigrationStage.SecuringPermissions, percent,
                string.Format("正在设置私有权限：{0} / {1}", processed, total), 0, 0, processed, total);
        }

        private static void ApplyPrivateRootAcl(string path)
        {
            Directory.SetAccessControl(path, BuildPrivateRootSecurity());
        }

        private static DirectorySecurity BuildPrivateRootSecurity()
        {
            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            DirectorySecurity security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));

            try
            {
                SecurityIdentifier sandboxUsers = (SecurityIdentifier)new NTAccount(
                    Environment.MachineName, "CodexSandboxUsers").Translate(typeof(SecurityIdentifier));
                security.AddAccessRule(new FileSystemAccessRule(sandboxUsers,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                    inheritance, PropagationFlags.None, AccessControlType.Allow));
            }
            catch (IdentityNotMappedException)
            {
            }
            return security;
        }

        private static void SetDirectoryToInheritedAcl(string path)
        {
            DirectorySecurity security = Directory.GetAccessControl(path);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleSpecific(rule);
            }
            security.SetAccessRuleProtection(false, false);
            Directory.SetAccessControl(path, security);
        }

        private static void SetFileToInheritedAcl(string path)
        {
            FileSecurity security = File.GetAccessControl(path);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                security.RemoveAccessRuleSpecific(rule);
            }
            security.SetAccessRuleProtection(false, false);
            File.SetAccessControl(path, security);
        }

        private static void VerifySensitiveFileAcl(string authPath)
        {
            if (!File.Exists(authPath))
            {
                return;
            }
            SecurityIdentifier authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            SecurityIdentifier builtinUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            FileSecurity security = File.GetAccessControl(authPath);
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                if (rule.AccessControlType == AccessControlType.Allow && sid != null &&
                    (sid.Equals(authenticatedUsers) || sid.Equals(builtinUsers)))
                {
                    throw new UnauthorizedAccessException("auth.json 的权限过宽，已停止切换。");
                }
            }
        }

        private static string ComputeSha256(string path, CancellationToken cancellationToken)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[CopyBufferSize];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    algorithm.TransformBlock(buffer, 0, read, null, 0);
                }
                algorithm.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(algorithm.Hash).Replace("-", string.Empty);
            }
        }

        private static bool IsLikelySqlite(string path)
        {
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".sqlite", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".sqlite3", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".db", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            try
            {
                byte[] header = new byte[16];
                using (FileStream stream = File.OpenRead(path))
                {
                    if (stream.Read(header, 0, header.Length) != header.Length)
                    {
                        return false;
                    }
                }
                return Encoding.ASCII.GetString(header) == "SQLite format 3\0";
            }
            catch
            {
                return false;
            }
        }

        private static void VerifyJunction(string junction, string destination)
        {
            if (!Directory.Exists(junction) || !IsReparsePoint(junction))
            {
                throw new IOException("原路径不是有效的 Junction：" + junction);
            }
            string resolved = NativeMethods.ResolveFinalPath(junction);
            if (!PathsEqual(resolved, destination))
            {
                throw new IOException(string.Format("Junction 目标不一致。实际：{0}；预期：{1}", resolved, destination));
            }
        }

        private static void ValidateCodexHome(string source)
        {
            int markers = 0;
            if (File.Exists(Path.Combine(source, "config.toml"))) markers++;
            if (File.Exists(Path.Combine(source, "auth.json"))) markers++;
            if (Directory.Exists(Path.Combine(source, "sessions"))) markers++;
            if (Directory.EnumerateFiles(source, "state*.sqlite", SearchOption.TopDirectoryOnly).Any()) markers++;
            if (markers == 0)
            {
                throw new InvalidOperationException("所选目录不像 Codex 数据目录：未发现 config.toml、auth.json、sessions 或 state SQLite。");
            }
        }

        private static void ValidateActiveMigrationRoots(
            string source,
            string destination,
            bool requireDestinationMarkers)
        {
            ValidateRealDirectoryRoot(source, "源目录", true);
            ValidateRealDirectoryRoot(destination, "目标目录", requireDestinationMarkers);
        }

        private static void ValidateRealDirectoryRoot(string path, string label, bool requireCodexMarkers)
        {
            string normalized = NormalizeDirectoryPath(path);
            ValidateSafeDirectoryDepth(normalized, label);
            if (!Directory.Exists(normalized))
            {
                throw new DirectoryNotFoundException(label + "不存在或已被替换：" + normalized);
            }
            if (IsReparsePoint(normalized))
            {
                throw new IOException(label + "已被替换为目录链接，已停止操作：" + normalized);
            }
            string resolved = NativeMethods.ResolveFinalPath(normalized);
            if (!PathsEqual(resolved, normalized))
            {
                throw new IOException(label + "的最终路径发生变化，已停止操作。实际：" + resolved);
            }
            if (requireCodexMarkers)
            {
                ValidateCodexHome(normalized);
            }
        }

        private static void ValidateBackupPath(string source, string backup)
        {
            string normalizedSource = NormalizeDirectoryPath(source);
            string normalizedBackup = NormalizeDirectoryPath(backup);
            ValidateSiblingArtifactPath(normalizedSource, normalizedBackup, BackupNameMarker, false, "备份路径");
        }

        private static MigrationRecord ValidateMigrationRecord(MigrationRecord record)
        {
            if (record == null)
            {
                throw new InvalidDataException("迁移记录内容为空，已拒绝使用。");
            }
            if (record.FormatVersion != CurrentRecordFormatVersion)
            {
                throw new InvalidDataException(string.Format(
                    "不支持的迁移记录版本：{0}。为避免误操作，已拒绝使用。", record.FormatVersion));
            }

            string source = NormalizeRecordPath(record.SourcePath, "源目录");
            string destination = NormalizeRecordPath(record.DestinationPath, "目标目录");
            string backup = NormalizeRecordPath(record.BackupPath, "安全备份");
            string quarantine = string.IsNullOrWhiteSpace(record.QuarantinePath)
                ? null
                : NormalizeRecordPath(record.QuarantinePath, "隔离目录");

            ValidateSafeDirectoryDepth(source, "迁移记录中的源目录");
            ValidateSafeDirectoryDepth(destination, "迁移记录中的目标目录");
            ValidateFixedNtfsPath(source, "迁移记录中的源目录");
            ValidateFixedNtfsPath(destination, "迁移记录中的目标目录");

            if (PathsEqual(source, destination) || IsWithin(source, destination) || IsWithin(destination, source))
            {
                throw new InvalidDataException("迁移记录中的源目录和目标目录关系不安全，已拒绝使用。");
            }

            try
            {
                ValidateBackupPath(source, backup);
                if (!string.IsNullOrWhiteSpace(quarantine))
                {
                    ValidateSiblingArtifactPath(destination, quarantine, QuarantineNameMarker, true, "隔离目录");
                }
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidDataException("迁移记录中的辅助目录未通过安全验证。", error);
            }

            if (PathsEqual(backup, source) || PathsEqual(backup, destination) ||
                (!string.IsNullOrWhiteSpace(quarantine) &&
                    (PathsEqual(quarantine, source) || PathsEqual(quarantine, destination) || PathsEqual(quarantine, backup))))
            {
                throw new InvalidDataException("迁移记录包含重复的关键路径，已拒绝使用。");
            }
            if (record.CompletedUtc == default(DateTime) || record.CompletedUtc > DateTime.UtcNow.AddDays(1))
            {
                throw new InvalidDataException("迁移记录中的完成时间无效，已拒绝使用。");
            }

            record.SourcePath = source;
            record.DestinationPath = destination;
            record.BackupPath = backup;
            record.QuarantinePath = quarantine;
            return record;
        }

        private static void ValidateOperationOptions(MigrationRecord record, MigrationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }
            ValidateMigrationRecord(record);
            if (!string.IsNullOrWhiteSpace(options.SourcePath) && !PathsEqual(options.SourcePath, record.SourcePath))
            {
                throw new InvalidOperationException("界面中的源目录与迁移记录不一致，已拒绝操作。请重新打开程序后再试。");
            }
            if (!string.IsNullOrWhiteSpace(options.DestinationPath) && !PathsEqual(options.DestinationPath, record.DestinationPath))
            {
                throw new InvalidOperationException("界面中的目标目录与迁移记录不一致，已拒绝操作。请重新打开程序后再试。");
            }
        }

        private static string NormalizeRecordPath(string path, string label)
        {
            try
            {
                string normalized = NormalizeDirectoryPath(path);
                string root = Path.GetPathRoot(normalized);
                if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || root[1] != ':')
                {
                    throw new InvalidDataException(label + "必须位于本地盘符中。");
                }
                return normalized;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is NotSupportedException) && !(error is PathTooLongException))
                {
                    throw;
                }
                throw new InvalidDataException(label + "不是有效的绝对路径。", error);
            }
        }

        private static void ValidateFixedNtfsPath(string path, string label)
        {
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(path));
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed ||
                    !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(label + "必须位于已就绪的 NTFS 固定磁盘。");
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new InvalidDataException("无法验证" + label + "所在磁盘。", error);
            }
        }

        private static void ValidateSiblingArtifactPath(
            string ownerPath,
            string artifactPath,
            string marker,
            bool allowGuidSuffix,
            string label)
        {
            string owner = NormalizeDirectoryPath(ownerPath);
            string artifact = NormalizeDirectoryPath(artifactPath);
            string ownerParent = Path.GetDirectoryName(owner);
            string artifactParent = Path.GetDirectoryName(artifact);
            string expectedPrefix = Path.GetFileName(owner) + marker;
            string artifactName = Path.GetFileName(artifact);
            if (!string.Equals(ownerParent, artifactParent, StringComparison.OrdinalIgnoreCase) ||
                !artifactName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(label + "必须是数据目录的同级安全目录：" + artifactPath);
            }

            string suffix = artifactName.Substring(expectedPrefix.Length);
            string timestamp = suffix;
            if (allowGuidSuffix && suffix.Length == 48 && suffix[15] == '-')
            {
                timestamp = suffix.Substring(0, 15);
                Guid parsedGuid;
                if (!Guid.TryParseExact(suffix.Substring(16), "N", out parsedGuid))
                {
                    throw new InvalidOperationException(label + "名称中的随机标识无效：" + artifactPath);
                }
            }
            DateTime parsedTimestamp;
            if (timestamp.Length != 15 ||
                !DateTime.TryParseExact(timestamp, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsedTimestamp))
            {
                throw new InvalidOperationException(label + "名称不符合安全格式：" + artifactPath);
            }
        }

        private static void ValidateSafeDirectoryDepth(string path, string label)
        {
            string normalized = NormalizeDirectoryPath(path);
            string root = Path.GetPathRoot(normalized).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase) ||
                normalized.Length <= root.Length + 3)
            {
                throw new InvalidOperationException(label + "过于宽泛，已拒绝操作：" + path);
            }
        }

        private static string EnsureQuarantine(string destination, string current, bool securePermissions)
        {
            if (!string.IsNullOrWhiteSpace(current))
            {
                ValidateQuarantineDirectory(destination, current);
                return current;
            }
            string path = CreateUniqueSiblingDirectory(
                destination, QuarantineNameMarker, "隔离目录", securePermissions);
            ValidateQuarantineDirectory(destination, path);
            return path;
        }

        private static string CreateUniqueSiblingDirectory(
            string ownerPath,
            string marker,
            string label,
            bool securePermissions)
        {
            string owner = NormalizeDirectoryPath(ownerPath);
            string parent = Path.GetDirectoryName(owner);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string name = Path.GetFileName(owner) + marker +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
                string path = Path.Combine(parent, name);
                if (Directory.Exists(path) || File.Exists(path))
                {
                    continue;
                }
                if (securePermissions)
                {
                    Directory.CreateDirectory(path, BuildPrivateRootSecurity());
                }
                else
                {
                    Directory.CreateDirectory(path);
                }
                if (IsReparsePoint(path))
                {
                    throw new IOException("新建的" + label + "被替换为目录链接，已停止操作：" + path);
                }
                ValidateRealDirectoryRoot(path, label, false);
                return path;
            }
            throw new IOException("无法创建唯一的" + label + "。");
        }

        private static void ValidateQuarantineDirectory(string destination, string quarantine)
        {
            try
            {
                ValidateSiblingArtifactPath(destination, quarantine, QuarantineNameMarker, true, "隔离目录");
            }
            catch (InvalidOperationException error)
            {
                throw new IOException("隔离目录未通过安全验证。", error);
            }
            if (!Directory.Exists(quarantine) || IsReparsePoint(quarantine))
            {
                throw new IOException("隔离目录不存在或已被替换为目录链接：" + quarantine);
            }
        }

        private static int ScaleByBytes(long processed, long total, int startPercent, int endPercent)
        {
            if (total <= 0)
            {
                return endPercent;
            }
            double ratio = Math.Max(0, Math.Min(1, (double)processed / total));
            return startPercent + (int)Math.Round((endPercent - startPercent) * ratio);
        }

        private static string GetRelativePath(string root, string fullPath)
        {
            string normalizedRoot = NormalizeDirectoryPath(root);
            string normalizedPath = Path.GetFullPath(fullPath);
            if (!normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("路径不在预期目录内：" + fullPath);
            }
            return normalizedPath.Substring(normalizedRoot.Length + 1);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("目录路径不能为空。", "path");
            }
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            string root = Path.GetPathRoot(full);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithin(string candidate, string parent)
        {
            string normalizedCandidate = NormalizeDirectoryPath(candidate);
            string normalizedParent = NormalizeDirectoryPath(parent);
            return normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(NormalizeDirectoryPath(first), NormalizeDirectoryPath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return string.Format("{0:0.##} {1}", value, units[unit]);
        }

        private static string GetRecordPath(string overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return Path.GetFullPath(overridePath);
            }
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexHomeMover");
            return Path.Combine(directory, "migration.json");
        }

        private static void SaveRecord(MigrationRecord record, string overridePath)
        {
            string path = GetRecordPath(overridePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MigrationRecord));
                    serializer.WriteObject(stream, record);
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null, true);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void DeleteRecord(string overridePath)
        {
            string path = GetRecordPath(overridePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private void Report(
            MigrationStage stage,
            int percent,
            string message,
            long processedBytes,
            long totalBytes,
            int processedFiles,
            int totalFiles)
        {
            MigrationProgress progress = new MigrationProgress();
            progress.Stage = stage;
            progress.Percent = Math.Max(0, Math.Min(100, percent));
            progress.Message = message;
            progress.ProcessedBytes = processedBytes;
            progress.TotalBytes = totalBytes;
            progress.ProcessedFiles = processedFiles;
            progress.TotalFiles = totalFiles;
            Action<MigrationProgress> handler;
            lock (eventLock)
            {
                handler = ProgressChanged;
            }
            if (handler != null)
            {
                handler(progress);
            }
        }

        private void Log(string message)
        {
            Action<string> handler;
            lock (eventLock)
            {
                handler = LogMessage;
            }
            if (handler != null)
            {
                handler(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            }
        }
    }
}
