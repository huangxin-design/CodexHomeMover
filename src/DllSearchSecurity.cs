using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexHomeMover
{
    internal static class DllSearchSecurity
    {
        private const uint LoadLibrarySearchSystem32 = 0x00000800;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        internal static void UseSystemDirectoriesOnly()
        {
            if (!SetDefaultDllDirectories(LoadLibrarySearchSystem32))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "无法启用安全的 Windows 系统组件加载策略。");
            }
        }
    }
}
