using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace CodexHomeMover
{
    internal static class NativeMethods
    {
        private const uint FileReadAttributes = 0x80;
        private const uint FileShareRead = 0x1;
        private const uint FileShareWrite = 0x2;
        private const uint FileShareDelete = 0x4;
        private const uint GenericWrite = 0x40000000;
        private const uint OpenExisting = 3;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FsctlSetReparsePoint = 0x000900A4;
        private const uint IoReparseTagMountPoint = 0xA0000003;
        private const int MaximumReparseDataBufferSize = 16 * 1024;
        private const int ErrorMoreData = 234;

        [StructLayout(LayoutKind.Sequential)]
        private struct RmUniqueProcess
        {
            internal int ProcessId;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        private enum RmAppType
        {
            Unknown = 0,
            MainWindow = 1,
            OtherWindow = 2,
            Service = 3,
            Explorer = 4,
            Console = 5,
            Critical = 1000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RmProcessInfo
        {
            internal RmUniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string AppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            internal string ServiceShortName;
            internal RmAppType ApplicationType;
            internal uint AppStatus;
            internal uint TerminalSessionId;
            [MarshalAs(UnmanagedType.Bool)]
            internal bool Restartable;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateDirectory(string pathName, IntPtr securityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveDirectory(string pathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle device,
            uint controlCode,
            byte[] inputBuffer,
            int inputBufferSize,
            IntPtr outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            StringBuilder filePath,
            uint filePathSize,
            uint flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSize(string fileName, out uint fileSizeHigh);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint sessionHandle,
            uint fileCount,
            string[] fileNames,
            uint applicationCount,
            RmUniqueProcess[] applications,
            uint serviceCount,
            string[] serviceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint sessionHandle,
            out uint processInfoNeeded,
            ref uint processInfoCount,
            [In, Out] RmProcessInfo[] affectedApplications,
            ref uint rebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint sessionHandle);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
        private static extern int sqlite3_open_v2(byte[] utf8Filename, out IntPtr database, int flags, IntPtr vfs);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SqliteCallback(IntPtr data, int columnCount, IntPtr values, IntPtr names);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_exec(
            IntPtr database,
            string sql,
            SqliteCallback callback,
            IntPtr data,
            out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr memory);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr database);

        internal static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        internal static string ResolveFinalPath(string path)
        {
            string nativePath = ToExtendedPath(NormalizeAbsolutePath(path, "path"));
            using (SafeFileHandle handle = CreateFile(
                nativePath,
                FileReadAttributes,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法解析目录链接：" + path);
                }

                StringBuilder buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取目录链接目标：" + path);
                }

                string result = buffer.ToString();
                if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    return @"\\" + result.Substring(8);
                }
                if (result.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                {
                    return result.Substring(4);
                }
                return result;
            }
        }

        internal static long GetAllocatedFileSize(string filePath, long fallbackLength)
        {
            uint high;
            uint low = GetCompressedFileSize(filePath, out high);
            if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0)
            {
                return fallbackLength;
            }

            ulong size = ((ulong)high << 32) | low;
            return size > long.MaxValue ? fallbackLength : (long)size;
        }

        internal static IList<string> GetLockingProcesses(IList<string> filePaths)
        {
            List<string> result = new List<string>();
            if (filePaths == null || filePaths.Count == 0)
            {
                return result;
            }

            uint session;
            StringBuilder key = new StringBuilder(33);
            if (RmStartSession(out session, 0, key) != 0)
            {
                return result;
            }

            try
            {
                List<string> compatibleFiles = new List<string>();
                for (int index = 0; index < filePaths.Count; index++)
                {
                    string filePath = filePaths[index];
                    if (!string.IsNullOrWhiteSpace(filePath) && filePath.Length < 260)
                    {
                        compatibleFiles.Add(filePath);
                    }
                }
                if (compatibleFiles.Count == 0)
                {
                    return result;
                }
                string[] files = compatibleFiles.ToArray();
                if (RmRegisterResources(session, (uint)files.Length, files, 0, null, 0, null) != 0)
                {
                    return result;
                }

                uint needed;
                uint count = 0;
                uint reasons = 0;
                int error = RmGetList(session, out needed, ref count, null, ref reasons);
                if (error != ErrorMoreData || needed == 0)
                {
                    return result;
                }

                RmProcessInfo[] processes = new RmProcessInfo[needed];
                count = needed;
                error = RmGetList(session, out needed, ref count, processes, ref reasons);
                if (error != 0)
                {
                    return result;
                }

                for (int index = 0; index < count; index++)
                {
                    string name = processes[index].AppName;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = string.IsNullOrWhiteSpace(processes[index].ServiceShortName)
                            ? "未知进程"
                            : processes[index].ServiceShortName;
                    }
                    string display = string.Format("{0} (PID {1})", name, processes[index].Process.ProcessId);
                    if (!result.Contains(display))
                    {
                        result.Add(display);
                    }
                }
                return result;
            }
            catch
            {
                return result;
            }
            finally
            {
                RmEndSession(session);
            }
        }

        internal static void CreateJunction(string junctionPath, string targetPath)
        {
            string normalizedJunction = NormalizeAbsolutePath(junctionPath, "junctionPath");
            string normalizedTarget = NormalizeAbsolutePath(targetPath, "targetPath");
            string nativeJunction = ToExtendedPath(normalizedJunction);
            byte[] reparseData = BuildJunctionReparseData(normalizedTarget);

            if (!CreateDirectory(nativeJunction, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw CreateJunctionIOException("无法创建 Junction 目录：" + junctionPath, error);
            }

            bool junctionCreated = false;
            try
            {
                using (SafeFileHandle handle = CreateFile(
                    nativeJunction,
                    GenericWrite,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics | FileFlagOpenReparsePoint,
                    IntPtr.Zero))
                {
                    if (handle.IsInvalid)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw CreateJunctionIOException("无法打开 Junction 目录：" + junctionPath, error);
                    }

                    int bytesReturned;
                    if (!DeviceIoControl(
                        handle,
                        FsctlSetReparsePoint,
                        reparseData,
                        reparseData.Length,
                        IntPtr.Zero,
                        0,
                        out bytesReturned,
                        IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw CreateJunctionIOException(
                            string.Format("无法创建 Junction：{0} -> {1}", junctionPath, targetPath), error);
                    }
                    junctionCreated = true;
                }
            }
            finally
            {
                if (!junctionCreated)
                {
                    RemoveDirectory(nativeJunction);
                }
            }
        }

        internal static bool QuickCheckSqlite(string databasePath, out string error)
        {
            error = null;
            IntPtr database;
            const int SqliteOpenReadOnly = 0x00000001;
            byte[] utf8Path = Encoding.UTF8.GetBytes(databasePath + "\0");
            int openCode = sqlite3_open_v2(utf8Path, out database, SqliteOpenReadOnly, IntPtr.Zero);
            if (openCode != 0)
            {
                error = database == IntPtr.Zero ? "SQLite 打开失败。" : Marshal.PtrToStringAnsi(sqlite3_errmsg(database));
                if (database != IntPtr.Zero)
                {
                    sqlite3_close(database);
                }
                return false;
            }

            string result = null;
            SqliteCallback callback = delegate(IntPtr data, int count, IntPtr values, IntPtr names)
            {
                if (count > 0)
                {
                    IntPtr valuePointer = Marshal.ReadIntPtr(values);
                    result = valuePointer == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(valuePointer);
                }
                return 0;
            };

            IntPtr errorPointer;
            int execCode = sqlite3_exec(database, "PRAGMA quick_check;", callback, IntPtr.Zero, out errorPointer);
            if (execCode != 0)
            {
                error = errorPointer == IntPtr.Zero
                    ? Marshal.PtrToStringAnsi(sqlite3_errmsg(database))
                    : Marshal.PtrToStringAnsi(errorPointer);
            }
            if (errorPointer != IntPtr.Zero)
            {
                sqlite3_free(errorPointer);
            }
            sqlite3_close(database);

            if (execCode != 0)
            {
                return false;
            }
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                error = string.IsNullOrWhiteSpace(result) ? "SQLite quick_check 未返回结果。" : result;
                return false;
            }
            return true;
        }

        private static byte[] BuildJunctionReparseData(string targetPath)
        {
            string substituteName = targetPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\??\UNC\" + targetPath.Substring(2)
                : @"\??\" + targetPath;
            byte[] substituteBytes = Encoding.Unicode.GetBytes(substituteName);
            byte[] printBytes = Encoding.Unicode.GetBytes(targetPath);

            const int genericHeaderSize = 8;
            const int mountPointHeaderSize = 8;
            const int nullTerminatorSize = 2;
            int pathBufferSize = substituteBytes.Length + nullTerminatorSize + printBytes.Length + nullTerminatorSize;
            int reparseDataLength = mountPointHeaderSize + pathBufferSize;
            int totalSize = genericHeaderSize + reparseDataLength;
            if (totalSize > MaximumReparseDataBufferSize || reparseDataLength > ushort.MaxValue)
            {
                throw new PathTooLongException("Junction 目标路径超过 Windows reparse point 可保存的长度：" + targetPath);
            }

            byte[] buffer = new byte[totalSize];
            WriteUInt32(buffer, 0, IoReparseTagMountPoint);
            WriteUInt16(buffer, 4, (ushort)reparseDataLength);
            WriteUInt16(buffer, 8, 0);
            WriteUInt16(buffer, 10, (ushort)substituteBytes.Length);
            WriteUInt16(buffer, 12, (ushort)(substituteBytes.Length + nullTerminatorSize));
            WriteUInt16(buffer, 14, (ushort)printBytes.Length);
            Buffer.BlockCopy(substituteBytes, 0, buffer, 16, substituteBytes.Length);
            Buffer.BlockCopy(printBytes, 0, buffer,
                16 + substituteBytes.Length + nullTerminatorSize, printBytes.Length);
            return buffer;
        }

        private static string NormalizeAbsolutePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("目录路径不能为空。", parameterName);
            }

            string compatiblePath = path;
            if (compatiblePath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                compatiblePath = @"\\" + compatiblePath.Substring(8);
            }
            else if (compatiblePath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                compatiblePath = compatiblePath.Substring(4);
            }
            if (!Path.IsPathRooted(compatiblePath))
            {
                throw new ArgumentException("Junction 路径必须是绝对路径。", parameterName);
            }

            string fullPath = Path.GetFullPath(compatiblePath);
            string root = Path.GetPathRoot(fullPath);
            return fullPath.Length > root.Length ? fullPath.TrimEnd('\\') : fullPath;
        }

        private static string ToExtendedPath(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            return path.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + path.Substring(2)
                : @"\\?\" + path;
        }

        private static IOException CreateJunctionIOException(string message, int error)
        {
            Win32Exception cause = new Win32Exception(error);
            return new IOException(string.Format("{0}（Windows 错误 {1}：{2}）", message, error, cause.Message), cause);
        }

        private static void WriteUInt16(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
