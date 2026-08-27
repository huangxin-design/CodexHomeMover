using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Codex Home Mover")]
[assembly: AssemblyDescription("Safely move Codex data on Windows with verification and rollback.")]
[assembly: AssemblyCompany("CodexHomeMover contributors")]
[assembly: AssemblyProduct("Codex Home Mover")]
[assembly: AssemblyCopyright("Copyright (c) 2026 CodexHomeMover contributors")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0-beta.1")]
[assembly: ComVisible(false)]

namespace CodexHomeMover
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            DllSearchSecurity.UseSystemDirectoriesOnly();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
