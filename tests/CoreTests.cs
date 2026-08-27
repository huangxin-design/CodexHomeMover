using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace CodexHomeMover
{
    internal static class CoreTests
    {
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        private static extern int sqlite3_open_v2(byte[] utf8Filename, out IntPtr database, int flags, IntPtr vfs);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SqliteCallback(IntPtr data, int count, IntPtr values, IntPtr names);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_exec(
            IntPtr database,
            string sql,
            SqliteCallback callback,
            IntPtr data,
            out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        private static int Main()
        {
            string artifactsRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts"));
            Directory.CreateDirectory(artifactsRoot);
            string testRoot = Path.Combine(artifactsRoot, "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);

            try
            {
                RunNativeJunctionApi(testRoot);
                Console.WriteLine("PASS: native junction creation, command-safe paths, and long paths");
                RunMigrationAndRollback(testRoot);
                Console.WriteLine("PASS: migration, verification, junction switch, quarantine, and rollback");
                RunCancelledResume(testRoot);
                Console.WriteLine("PASS: mid-file cancellation, partial-target reuse, protected-file replacement, SHA repair, and resume");
                RunCleanupRestore(testRoot);
                Console.WriteLine("PASS: safety-backup cleanup and full restore with post-migration data");
                RunTamperedMigrationRecords(testRoot);
                Console.WriteLine("PASS: malformed and tampered migration records are rejected before use");
                RunAclInheritance(testRoot);
                Console.WriteLine("PASS: production private-root ACL and child inheritance helpers");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error);
                return 1;
            }
            finally
            {
                SafeDeleteTestRoot(testRoot, artifactsRoot);
            }
        }

        private static void RunNativeJunctionApi(string testRoot)
        {
            string scenarioRoot = Path.Combine(testRoot, "native-junction");
            string variableName = "CHM_JUNCTION_NATIVE_TEST";
            string previousValue = Environment.GetEnvironmentVariable(variableName);
            Environment.SetEnvironmentVariable(variableName, "expanded-by-command-shell");
            try
            {
                string literalToken = "%" + variableName + "%";
                string target = Path.Combine(scenarioRoot, "target-" + literalToken + " & data");
                string junction = Path.Combine(scenarioRoot, "link-" + literalToken + " & data");
                Directory.CreateDirectory(target);
                File.WriteAllText(Path.Combine(target, "payload.txt"), "native junction payload", new UTF8Encoding(false));

                NativeMethods.CreateJunction(junction, target);
                Assert(IsReparsePoint(junction), "Native junction creation did not set a reparse point.");
                Assert(PathsEqual(NativeMethods.ResolveFinalPath(junction), target),
                    "Native junction did not preserve command-shell metacharacters literally.");
                Assert(File.ReadAllText(Path.Combine(junction, "payload.txt")) == "native junction payload",
                    "Native junction did not expose the target content.");

                bool duplicateRejected = false;
                try
                {
                    NativeMethods.CreateJunction(junction, target);
                }
                catch (IOException)
                {
                    duplicateRejected = true;
                }
                Assert(duplicateRejected, "Creating over an existing junction was not rejected.");
                Assert(PathsEqual(NativeMethods.ResolveFinalPath(junction), target),
                    "A rejected duplicate creation changed the existing junction.");

                string longRoot = scenarioRoot;
                for (int index = 0; index < 5; index++)
                {
                    longRoot = Path.Combine(longRoot,
                        "junction-long-segment-" + index + "-" + new string((char)('a' + index), 42));
                }
                string longTarget = Path.Combine(longRoot, "target");
                string longJunction = Path.Combine(longRoot, "junction");
                Directory.CreateDirectory(longTarget);
                Assert(longJunction.Length > 260, "Long junction fixture did not exceed MAX_PATH.");

                NativeMethods.CreateJunction(longJunction, longTarget);
                Assert(IsReparsePoint(longJunction), "Long-path native junction was not created.");
                Assert(PathsEqual(NativeMethods.ResolveFinalPath(longJunction), longTarget),
                    "Long-path native junction target is incorrect.");

                string unusedJunction = Path.Combine(scenarioRoot, "relative-target-must-not-create");
                bool relativeRejected = false;
                try
                {
                    NativeMethods.CreateJunction(unusedJunction, "relative-target");
                }
                catch (ArgumentException error)
                {
                    relativeRejected = string.Equals(error.ParamName, "targetPath", StringComparison.Ordinal);
                }
                Assert(relativeRejected, "Relative junction target was not rejected clearly.");
                Assert(!Directory.Exists(unusedJunction), "Rejected junction input created a directory.");
            }
            finally
            {
                Environment.SetEnvironmentVariable(variableName, previousValue);
            }
        }

        private static void RunMigrationAndRollback(string testRoot)
        {
            string source = Path.Combine(testRoot, "用户资料-测试", ".codex");
            string destination = Path.Combine(testRoot, "drive-e", "CodexData", ".codex");
            string recordPath = Path.Combine(testRoot, "state", "migration.json");
            Directory.CreateDirectory(Path.Combine(source, "sessions"));
            Directory.CreateDirectory(Path.Combine(source, "nested", "deep"));
            Directory.CreateDirectory(Path.Combine(source, "internal-target"));

            string longRelativeDirectory = string.Empty;
            for (int index = 0; index < 5; index++)
            {
                longRelativeDirectory = Path.Combine(longRelativeDirectory,
                    "long-segment-" + index + "-" + new string((char)('a' + index), 42));
            }
            string longFileName = new string('z', 220) + ".txt";
            string longRelativeFile = Path.Combine(longRelativeDirectory, longFileName);
            string longSourceDirectory = Path.Combine(source, longRelativeDirectory);
            Directory.CreateDirectory(longSourceDirectory);
            string longSourceFile = Path.Combine(source, longRelativeFile);
            Assert(longSourceFile.Length > 500, "Long-path fixture did not exceed the intended length.");

            File.WriteAllText(Path.Combine(source, "config.toml"), "model = \"test\"\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(source, "auth.json"), "{\"test\":true}\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(source, "sessions", "session.jsonl"), "{\"id\":1}\n", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(source, "nested", "deep", "binary.dat"), BuildData(1024 * 1024 + 333));
            File.WriteAllText(longSourceFile, "long path payload", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(source, "internal-target", "linked.txt"), "junction payload", new UTF8Encoding(false));
            CreateSqlite(Path.Combine(source, "state_5.sqlite"));
            NativeMethods.CreateJunction(Path.Combine(source, "cache-link"), Path.Combine(source, "internal-target"));

            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "stale-file.txt"), "must be quarantined", new UTF8Encoding(false));
            Directory.CreateDirectory(Path.Combine(destination, "sessions"));
            string staleSession = Path.Combine(destination, "sessions", "session.jsonl");
            File.WriteAllText(staleSession, "stale", new UTF8Encoding(false));
            File.SetAttributes(staleSession, FileAttributes.ReadOnly | FileAttributes.System);

            MigrationOptions options = new MigrationOptions();
            options.SourcePath = source;
            options.DestinationPath = destination;
            options.VerifySha256 = true;
            options.AllowExistingDestination = true;
            options.IgnoreRunningProcesses = true;
            options.RequireAdministrator = false;
            options.RecordPathOverride = recordPath;

            MigrationEngine engine = new MigrationEngine();
            int lastProgress = -1;
            FileStream switchLock = null;
            bool sawSwitchWait = false;
            engine.ProgressChanged += delegate(MigrationProgress progress)
            {
                if (progress.Percent < lastProgress && progress.Stage != MigrationStage.Preflight)
                {
                    throw new InvalidOperationException("Progress moved backwards unexpectedly.");
                }
                lastProgress = progress.Percent;
                if (progress.Stage == MigrationStage.Switching && switchLock == null)
                {
                    switchLock = new FileStream(Path.Combine(source, "auth.json"), FileMode.Open,
                        FileAccess.Read, FileShare.Read);
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(1400);
                        switchLock.Dispose();
                    });
                }
                if (progress.Stage == MigrationStage.WaitingForCodex && progress.Percent == 98)
                {
                    sawSwitchWait = true;
                }
            };
            engine.LogMessage += delegate(string line) { Console.WriteLine(line); };

            PreflightResult preflight = engine.Preflight(options, CancellationToken.None);
            Assert(preflight.FileCount >= 7, "Preflight file count was too small.");
            Assert(preflight.JunctionCount == 1, "Preflight did not detect the source junction.");
            Assert(preflight.DestinationHasData, "Existing destination was not detected.");

            lastProgress = -1;
            MigrationRecord record = engine.RunMigration(options, CancellationToken.None);
            Assert(sawSwitchWait, "Final switch did not wait for the simulated source lock.");
            Assert(Directory.Exists(source), "Source path disappeared after migration.");
            Assert(IsReparsePoint(source), "Source path is not a junction after migration.");
            Assert(PathsEqual(NativeMethods.ResolveFinalPath(source), destination), "Source junction target is incorrect.");
            Assert(Directory.Exists(record.BackupPath), "Safety backup was not retained.");
            Assert(!File.Exists(Path.Combine(destination, "stale-file.txt")), "Stale destination file was not isolated.");
            Assert(!string.IsNullOrWhiteSpace(record.QuarantinePath), "Quarantine path was not recorded.");
            Assert(File.Exists(Path.Combine(record.QuarantinePath, "stale-file.txt")), "Quarantined file was not preserved.");
            Assert(File.ReadAllText(Path.Combine(source, "sessions", "session.jsonl")).Contains("\"id\":1"),
                "Migrated session content is incorrect.");
            Assert(File.ReadAllText(Path.Combine(destination, longRelativeFile)) == "long path payload",
                "Long-path file was not migrated correctly.");
            string linkedTarget = NativeMethods.ResolveFinalPath(Path.Combine(destination, "cache-link"));
            Assert(PathsEqual(linkedTarget, Path.Combine(destination, "internal-target")),
                "Internal junction was not rewritten to the destination.");

            string sqliteError;
            Assert(NativeMethods.QuickCheckSqlite(Path.Combine(destination, "state_5.sqlite"), out sqliteError),
                "Destination SQLite check failed: " + sqliteError);

            lastProgress = -1;
            engine.Rollback(options, CancellationToken.None);
            Assert(Directory.Exists(source), "Source was not restored by rollback.");
            Assert(!IsReparsePoint(source), "Source is still a junction after rollback.");
            Assert(File.Exists(Path.Combine(source, "auth.json")), "Source files are missing after rollback.");
            Assert(File.Exists(Path.Combine(source, longRelativeFile)), "Long-path file is missing after rollback.");
            Assert(Directory.Exists(destination), "Destination copy should remain after rollback.");
            Assert(!File.Exists(recordPath), "Migration record was not removed after rollback.");
        }

        private static void RunCancelledResume(string testRoot)
        {
            string scenarioRoot = Path.Combine(testRoot, "cancel-resume");
            string source = Path.Combine(scenarioRoot, "profile", ".codex");
            string destination = Path.Combine(scenarioRoot, "drive-f", "CodexData", ".codex");
            string recordPath = Path.Combine(scenarioRoot, "state", "migration.json");
            string sessions = Path.Combine(source, "sessions");
            Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(source, "config.toml"), "model = \"resume-test\"\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(source, "auth.json"), "{\"resume\":true}\n", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(source, "resume.bin"), BuildData(256 * 1024));
            string relativeLargeFile = Path.Combine("sessions", "00-large.bin");
            File.WriteAllBytes(Path.Combine(source, relativeLargeFile), BuildData(12 * 1024 * 1024 + 17));
            for (int index = 0; index < 8; index++)
            {
                byte[] data = BuildData(256 * 1024 + index);
                data[0] = (byte)index;
                File.WriteAllBytes(Path.Combine(sessions, "chunk-" + index.ToString("00") + ".bin"), data);
            }

            MigrationOptions options = new MigrationOptions();
            options.SourcePath = source;
            options.DestinationPath = destination;
            options.VerifySha256 = true;
            options.AllowExistingDestination = true;
            options.IgnoreRunningProcesses = true;
            options.RequireAdministrator = false;
            options.RecordPathOverride = recordPath;

            CancellationTokenSource cancellation = new CancellationTokenSource();
            MigrationEngine interruptedEngine = new MigrationEngine();
            interruptedEngine.ProgressChanged += delegate(MigrationProgress progress)
            {
                if (progress.Stage == MigrationStage.Copying &&
                    progress.Message.IndexOf(relativeLargeFile, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cancellation.Cancel();
                }
            };

            bool cancelled = false;
            try
            {
                interruptedEngine.RunMigration(options, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                cancellation.Dispose();
            }

            Assert(cancelled, "The simulated migration was not cancelled.");
            Assert(Directory.Exists(source) && !IsReparsePoint(source),
                "Cancellation changed the original source directory.");
            Assert(!File.Exists(recordPath), "Cancellation unexpectedly created a migration record.");
            Assert(Directory.EnumerateFiles(destination, ".chm-*.tmp", SearchOption.AllDirectories).Count() == 0,
                "Cancellation left an atomic-copy temporary file behind.");
            Assert(!File.Exists(Path.Combine(destination, relativeLargeFile)),
                "Cancellation committed a file that was interrupted mid-copy.");

            string relativeResumeFile = "resume.bin";
            string sourceResumeFile = Path.Combine(source, relativeResumeFile);
            string targetResumeFile = Path.Combine(destination, relativeResumeFile);
            Assert(File.Exists(targetResumeFile), "Cancellation did not leave a reusable completed file.");

            byte[] updated = BuildData(256 * 1024);
            updated[0] = 231;
            File.WriteAllBytes(sourceResumeFile, updated);
            File.SetLastWriteTimeUtc(sourceResumeFile, DateTime.UtcNow.AddMinutes(5));
            File.SetAttributes(sourceResumeFile, FileAttributes.ReadOnly | FileAttributes.System);
            File.SetAttributes(targetResumeFile, FileAttributes.ReadOnly | FileAttributes.System);

            string relativeHashTrap = Path.Combine("sessions", "chunk-07.bin");
            string sourceHashTrap = Path.Combine(source, relativeHashTrap);
            string targetHashTrap = Path.Combine(destination, relativeHashTrap);
            byte[] wrongSameSizeContent = File.ReadAllBytes(sourceHashTrap);
            wrongSameSizeContent[wrongSameSizeContent.Length - 1] ^= 0x5A;
            Directory.CreateDirectory(Path.GetDirectoryName(targetHashTrap));
            File.WriteAllBytes(targetHashTrap, wrongSameSizeContent);
            File.SetLastWriteTimeUtc(targetHashTrap, File.GetLastWriteTimeUtc(sourceHashTrap));

            bool blockedFailureReported = false;
            using (FileStream targetLock = new FileStream(targetResumeFile, FileMode.Open,
                FileAccess.Read, FileShare.Read))
            {
                try
                {
                    new MigrationEngine().RunMigration(options, CancellationToken.None);
                }
                catch (IOException error)
                {
                    blockedFailureReported =
                        error.Message.IndexOf(relativeResumeFile, StringComparison.OrdinalIgnoreCase) >= 0 &&
                        error.Message.IndexOf(destination, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            Assert(blockedFailureReported,
                "A locked target file did not report its relative and destination paths.");
            Assert(Directory.Exists(source) && !IsReparsePoint(source),
                "A target-file failure changed the original source directory.");
            FileAttributes attributesAfterFailure = File.GetAttributes(targetResumeFile);
            Assert((attributesAfterFailure & FileAttributes.ReadOnly) != 0 &&
                (attributesAfterFailure & FileAttributes.System) != 0,
                "A failed replacement did not restore the target file attributes.");
            Assert(File.ReadAllBytes(targetResumeFile)[0] != 231,
                "A failed locked-file replacement changed the old target content.");

            MigrationEngine resumeEngine = new MigrationEngine();
            MigrationRecord record = resumeEngine.RunMigration(options, CancellationToken.None);
            Assert(IsReparsePoint(source), "Resume did not complete the source junction switch.");
            Assert(File.ReadAllBytes(targetResumeFile)[0] == 231,
                "Resume did not replace the protected stale target file.");
            Assert(File.ReadAllBytes(sourceHashTrap).SequenceEqual(File.ReadAllBytes(targetHashTrap)),
                "SHA-256 verification did not repair same-size, same-time corrupt target content.");
            FileAttributes finalAttributes = File.GetAttributes(targetResumeFile);
            Assert((finalAttributes & FileAttributes.ReadOnly) != 0 &&
                (finalAttributes & FileAttributes.System) != 0,
                "Resume did not restore the source file attributes after replacement.");
            Assert(Directory.Exists(record.BackupPath), "Resume did not retain a safety backup.");

            resumeEngine.Rollback(options, CancellationToken.None);
            Assert(Directory.Exists(source) && !IsReparsePoint(source),
                "Rollback after resume did not restore the source directory.");
            Assert(!File.Exists(recordPath), "Rollback after resume did not remove the migration record.");
        }

        private static void RunCleanupRestore(string testRoot)
        {
            string scenarioRoot = Path.Combine(testRoot, "cleanup-restore");
            string source = Path.Combine(scenarioRoot, "profile", ".codex");
            string destination = Path.Combine(scenarioRoot, "drive-h", "CodexData", ".codex");
            string recordPath = Path.Combine(scenarioRoot, "state", "migration.json");
            Directory.CreateDirectory(Path.Combine(source, "sessions"));
            File.WriteAllText(Path.Combine(source, "config.toml"), "model = \"restore-test\"\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(source, "auth.json"), "{\"restore\":true}\n", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(source, "sessions", "before.bin"), BuildData(512 * 1024 + 9));
            CreateSqlite(Path.Combine(source, "state_5.sqlite"));

            MigrationOptions options = new MigrationOptions();
            options.SourcePath = source;
            options.DestinationPath = destination;
            options.VerifySha256 = true;
            options.AllowExistingDestination = true;
            options.IgnoreRunningProcesses = true;
            options.RequireAdministrator = false;
            options.RecordPathOverride = recordPath;

            MigrationEngine engine = new MigrationEngine();
            MigrationRecord migrated = engine.RunMigration(options, CancellationToken.None);
            Assert(IsReparsePoint(source), "Cleanup-restore setup did not create the source junction.");
            File.WriteAllText(Path.Combine(source, "sessions", "created-after-migration.jsonl"),
                "{\"after\":true}\n", new UTF8Encoding(false));

            engine.DeleteSafetyBackup(options, CancellationToken.None);
            MigrationRecord cleaned = engine.LoadRecord(recordPath);
            Assert(cleaned != null && cleaned.BackupDeleted,
                "Safety-backup cleanup did not persist its state.");
            Assert(!Directory.Exists(migrated.BackupPath), "Safety backup still exists after cleanup.");

            engine.Rollback(options, CancellationToken.None);
            Assert(Directory.Exists(source) && !IsReparsePoint(source),
                "Full restore after cleanup did not recreate a normal source directory.");
            Assert(File.Exists(Path.Combine(source, "sessions", "before.bin")),
                "Full restore lost original migrated data.");
            Assert(File.ReadAllText(Path.Combine(source, "sessions", "created-after-migration.jsonl"))
                .Contains("\"after\":true"),
                "Full restore lost data created after migration.");
            Assert(Directory.Exists(destination), "Full restore should retain the destination copy.");
            Assert(!File.Exists(recordPath), "Full restore did not remove the migration record.");
        }

        private static void RunTamperedMigrationRecords(string testRoot)
        {
            string scenarioRoot = Path.Combine(testRoot, "record-trust");
            string source = Path.Combine(scenarioRoot, "profile", ".codex");
            string destination = Path.Combine(scenarioRoot, "drive-j", "CodexData", ".codex");
            string recordPath = Path.Combine(scenarioRoot, "state", "migration.json");
            string validBackup = source + ".codex-home-mover-backup-20260826-120000";
            string validQuarantine = destination + ".codex-home-mover-quarantine-20260826-120001";
            string victim = Path.Combine(scenarioRoot, "must-not-delete");
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath));
            Directory.CreateDirectory(victim);
            File.WriteAllText(Path.Combine(victim, "keep.txt"), "keep", new UTF8Encoding(false));

            MigrationRecord valid = CreateRecord(source, destination, validBackup, validQuarantine);
            WriteRecord(recordPath, valid);
            MigrationRecord loaded = new MigrationEngine().LoadRecord(recordPath);
            Assert(loaded != null && loaded.FormatVersion == 1, "A valid migration record was rejected.");

            File.WriteAllText(recordPath, "{not-json", new UTF8Encoding(false));
            AssertRejected(delegate { new MigrationEngine().LoadRecord(recordPath); },
                "Malformed migration JSON was accepted.");

            MigrationRecord wrongVersion = CreateRecord(source, destination, validBackup, validQuarantine);
            wrongVersion.FormatVersion = 99;
            WriteRecord(recordPath, wrongVersion);
            AssertRejected(delegate { new MigrationEngine().LoadRecord(recordPath); },
                "An unsupported migration-record version was accepted.");

            MigrationRecord nestedPaths = CreateRecord(
                source,
                Path.Combine(source, "nested-target"),
                validBackup,
                null);
            WriteRecord(recordPath, nestedPaths);
            AssertRejected(delegate { new MigrationEngine().LoadRecord(recordPath); },
                "A record with nested source and destination paths was accepted.");

            MigrationRecord badQuarantine = CreateRecord(source, destination, validBackup, victim);
            WriteRecord(recordPath, badQuarantine);
            AssertRejected(delegate { new MigrationEngine().LoadRecord(recordPath); },
                "A record with an unrelated quarantine directory was accepted.");

            MigrationRecord badBackup = CreateRecord(source, destination, victim, validQuarantine);
            WriteRecord(recordPath, badBackup);
            MigrationOptions options = new MigrationOptions();
            options.SourcePath = source;
            options.DestinationPath = destination;
            options.IgnoreRunningProcesses = true;
            options.RequireAdministrator = false;
            options.RecordPathOverride = recordPath;
            AssertRejected(delegate { new MigrationEngine().Rollback(options, CancellationToken.None); },
                "Rollback accepted a tampered backup path.");
            AssertRejected(delegate { new MigrationEngine().DeleteSafetyBackup(options, CancellationToken.None); },
                "Cleanup accepted a tampered backup path.");
            Assert(File.Exists(Path.Combine(victim, "keep.txt")),
                "A rejected migration record changed the unrelated victim directory.");
        }

        private static MigrationRecord CreateRecord(
            string source,
            string destination,
            string backup,
            string quarantine)
        {
            MigrationRecord record = new MigrationRecord();
            record.FormatVersion = 1;
            record.SourcePath = source;
            record.DestinationPath = destination;
            record.BackupPath = backup;
            record.QuarantinePath = quarantine;
            record.CompletedUtc = DateTime.UtcNow;
            record.BackupDeleted = false;
            return record;
        }

        private static void WriteRecord(string path, MigrationRecord record)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(MigrationRecord)).WriteObject(stream, record);
            }
        }

        private static void AssertRejected(Action action, string failureMessage)
        {
            bool rejected = false;
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Assert(rejected, failureMessage);
        }

        private static void RunAclInheritance(string testRoot)
        {
            string root = Path.Combine(testRoot, "acl", ".codex");
            string child = Path.Combine(root, "auth.json");
            Directory.CreateDirectory(root);
            File.WriteAllText(child, "{\"acl\":true}\n", new UTF8Encoding(false));

            SecurityIdentifier currentUser = WindowsIdentity.GetCurrent().User;
            SecurityIdentifier builtinUsers = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            FileSecurity broadSecurity = new FileSecurity();
            broadSecurity.SetAccessRuleProtection(true, false);
            broadSecurity.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl,
                AccessControlType.Allow));
            broadSecurity.AddAccessRule(new FileSystemAccessRule(builtinUsers, FileSystemRights.ReadAndExecute,
                AccessControlType.Allow));
            File.SetAccessControl(child, broadSecurity);

            InvokePrivateStatic("ApplyPrivateRootAcl", root);
            InvokePrivateStatic("SetFileToInheritedAcl", child);

            FileSecurity finalSecurity = File.GetAccessControl(child);
            bool currentUserHasFullControl = false;
            foreach (FileSystemAccessRule rule in finalSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                Assert(!(rule.AccessControlType == AccessControlType.Allow && sid != null && sid.Equals(builtinUsers)),
                    "Inherited child ACL still grants the broad Builtin Users group.");
                if (rule.AccessControlType == AccessControlType.Allow && sid != null && sid.Equals(currentUser) &&
                    (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
                {
                    currentUserHasFullControl = true;
                }
            }
            Assert(currentUserHasFullControl, "Private ACL did not preserve full control for the current user.");
            InvokePrivateStatic("VerifySensitiveFileAcl", child);

            string quarantineOwner = Path.Combine(testRoot, "acl-quarantine", "CodexData", ".codex");
            Directory.CreateDirectory(quarantineOwner);
            MethodInfo ensureQuarantine = typeof(MigrationEngine).GetMethod("EnsureQuarantine",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(ensureQuarantine != null, "Missing secure quarantine helper.");
            string firstQuarantine = (string)ensureQuarantine.Invoke(null,
                new object[] { quarantineOwner, null, true });
            string secondQuarantine = (string)ensureQuarantine.Invoke(null,
                new object[] { quarantineOwner, null, true });
            Assert(!PathsEqual(firstQuarantine, secondQuarantine),
                "Quarantine directory names were predictable or reused.");
            Assert(!IsReparsePoint(firstQuarantine), "Secure quarantine was created as a reparse point.");
            DirectorySecurity quarantineSecurity = Directory.GetAccessControl(firstQuarantine);
            foreach (FileSystemAccessRule rule in quarantineSecurity.GetAccessRules(
                true, true, typeof(SecurityIdentifier)))
            {
                SecurityIdentifier sid = rule.IdentityReference as SecurityIdentifier;
                Assert(!(rule.AccessControlType == AccessControlType.Allow && sid != null && sid.Equals(builtinUsers)),
                    "Secure quarantine grants the broad Builtin Users group.");
            }
        }

        private static void InvokePrivateStatic(string methodName, string path)
        {
            MethodInfo method = typeof(MigrationEngine).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(method != null, "Missing security helper: " + methodName);
            method.Invoke(null, new object[] { path });
        }

        private static byte[] BuildData(int length)
        {
            byte[] data = new byte[length];
            for (int index = 0; index < data.Length; index++)
            {
                data[index] = (byte)((index * 31 + 17) % 251);
            }
            return data;
        }

        private static void CreateSqlite(string path)
        {
            IntPtr database;
            const int ReadWriteCreate = 0x00000002 | 0x00000004;
            byte[] utf8Path = Encoding.UTF8.GetBytes(path + "\0");
            int openCode = sqlite3_open_v2(utf8Path, out database, ReadWriteCreate, IntPtr.Zero);
            if (openCode != 0)
            {
                throw new InvalidOperationException("Unable to create SQLite test database. Code: " + openCode);
            }
            try
            {
                IntPtr error;
                SqliteCallback callback = delegate { return 0; };
                int code = sqlite3_exec(database,
                    "CREATE TABLE sample(id INTEGER PRIMARY KEY, value TEXT);" +
                    "INSERT INTO sample(value) VALUES('safe migration');",
                    callback, IntPtr.Zero, out error);
                if (code != 0)
                {
                    throw new InvalidOperationException("Unable to seed SQLite test database. Code: " + code);
                }
            }
            finally
            {
                sqlite3_close(database);
            }
        }

        private static void SafeDeleteTestRoot(string testRoot, string artifactsRoot)
        {
            string normalizedRoot = Path.GetFullPath(testRoot).TrimEnd('\\');
            string normalizedArtifacts = Path.GetFullPath(artifactsRoot).TrimEnd('\\');
            if (!normalizedRoot.StartsWith(normalizedArtifacts + "\\", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(normalizedRoot).StartsWith("run-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to delete an unverified test path: " + testRoot);
            }
            if (!Directory.Exists(normalizedRoot))
            {
                return;
            }

            Stack<string> pending = new Stack<string>();
            List<string> regularDirectories = new List<string>();
            pending.Push(normalizedRoot);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                regularDirectories.Add(current);
                foreach (string file in Directory.EnumerateFiles(current))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                foreach (string directory in Directory.EnumerateDirectories(current))
                {
                    if (IsReparsePoint(directory))
                    {
                        Directory.Delete(directory, false);
                    }
                    else
                    {
                        pending.Push(directory);
                    }
                }
            }
            foreach (string directory in regularDirectories.OrderByDescending(item => item.Length))
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, false);
                }
            }
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static bool PathsEqual(string first, string second)
        {
            return string.Equals(Path.GetFullPath(first).TrimEnd('\\'), Path.GetFullPath(second).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
