using System.Runtime.InteropServices;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Colors the Windows title bar to match the navy sidebar.
    /// Minimize, maximize, and close stay as system buttons.
    /// </summary>
    internal static class WindowChrome
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);

        /// <summary>Apply navy caption colors once the native handle exists.</summary>
        public static void Apply(Form form)
        {
            form.ShowIcon = false;
            void paint(object? _, EventArgs e) => Paint(form);
            form.HandleCreated -= paint;
            form.HandleCreated += paint;
            // Handle may already exist if Apply is called after Show.
            if (form.IsHandleCreated)
                Paint(form);
        }

        /// <summary>Push DWM caption, text, and border colors for this HWND.</summary>
        private static void Paint(Form form)
        {
            // Disposed or not-yet-created forms have no HWND to color.
            if (!form.IsHandleCreated || form.IsDisposed)
                return;

            IntPtr hwnd = form.Handle;
            int on = 1;
            Set(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, on);
            Set(hwnd, DwmwaUseImmersiveDarkMode, on);
            Set(hwnd, DwmwaCaptionColor, ColorRef(Theme.NavyDark));
            Set(hwnd, DwmwaTextColor, ColorRef(Theme.Cream));
            Set(hwnd, DwmwaBorderColor, ColorRef(Theme.Gold));
        }

        /// <summary>Best-effort DWM attribute write; older Windows may lack the attribute.</summary>
        private static void Set(IntPtr hwnd, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
            }
            catch
            {
                // Older Windows keeps the default caption.
            }
        }

        /// <summary>Pack an RGB color into a COLORREF for DwmSetWindowAttribute.</summary>
        private static int ColorRef(Color color) =>
            color.R | (color.G << 8) | (color.B << 16);
    }
}
