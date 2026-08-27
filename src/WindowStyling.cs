using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexHomeMover
{
    internal static class WindowStyling
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        internal static void Apply(Form form)
        {
            if (form == null || !form.IsHandleCreated)
            {
                return;
            }

            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
                int rounded = 2;
                DwmSetWindowAttribute(form.Handle, DwmWindowCornerPreference, ref rounded, sizeof(int));
            }
            catch
            {
            }
        }
    }
}
