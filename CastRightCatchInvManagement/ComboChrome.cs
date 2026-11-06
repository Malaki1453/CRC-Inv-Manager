using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Draws a text-field rectangle on combo boxes and opens the list when the
    /// field is clicked, so the control matches StyleField inputs.
    /// </summary>
    internal static class ComboChrome
    {
        private static readonly ConditionalWeakTable<ComboBox, Hook> Attached = new();

        /// <summary>Hook paint and click on this combo if it is not already hooked.</summary>
        public static void Attach(ComboBox box)
        {
            if (Attached.TryGetValue(box, out _))
                return;
            Attached.Add(box, new Hook(box));
        }

        /// <summary>Native subclass that paints the border and opens the drop-down on click.</summary>
        private sealed class Hook : NativeWindow
        {
            private const int WmPaint = 0x000F;
            private const int WmNcPaint = 0x0085;
            private const int WmLButtonDown = 0x0201;
            private const int WmLButtonDblClk = 0x0203;
            private const int EmSetReadOnly = 0x00CF;
            private const uint GwChild = 5;

            private readonly ComboBox _box;

            public Hook(ComboBox box)
            {
                _box = box;
                box.HandleCreated += (_, _) => Bind();
                box.HandleDestroyed += (_, _) => Release();
                box.Disposed += (_, _) => Release();
                box.SizeChanged += (_, _) => _box.Invalidate();
                box.DropDownClosed += (_, _) => _box.Invalidate();
                if (box.IsHandleCreated)
                    Bind();
            }

            /// <summary>Subclass the current HWND and lock typing in the embedded edit.</summary>
            private void Bind()
            {
                Release();
                // Handle is not created yet: wait for HandleCreated.
                if (!_box.IsHandleCreated)
                    return;
                AssignHandle(_box.Handle);
                LockEdit();
            }

            /// <summary>Drop the subclass when the combo handle is destroyed.</summary>
            private void Release()
            {
                if (Handle != IntPtr.Zero)
                    ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                // Click or double-click on the field: open the list instead of focusing an edit caret.
                if ((m.Msg == WmLButtonDown || m.Msg == WmLButtonDblClk) && _box.Enabled)
                {
                    if (!_box.DroppedDown)
                    {
                        _box.Focus();
                        _box.DroppedDown = true;
                    }
                    else
                    {
                        _box.DroppedDown = false;
                    }

                    m.Result = IntPtr.Zero;
                    return;
                }

                base.WndProc(ref m);
                // After the combo paints, draw the same 1px rectangle the text boxes use.
                if (m.Msg == WmPaint || m.Msg == WmNcPaint)
                    DrawBorder();
            }

            /// <summary>1px paper-field border around the combo, including the arrow.</summary>
            private void DrawBorder()
            {
                if (_box.IsDisposed || !_box.IsHandleCreated || _box.Width <= 1 || _box.Height <= 1)
                    return;

                using var g = Graphics.FromHwnd(_box.Handle);
                using var pen = new Pen(_box.Enabled ? Theme.FieldBorder : Theme.CreamDark);
                g.DrawRectangle(pen, 0, 0, _box.Width - 1, _box.Height - 1);
            }

            /// <summary>DropDown combos still have an edit child; lock it so click cannot type.</summary>
            private void LockEdit()
            {
                if (_box.DropDownStyle == ComboBoxStyle.DropDownList)
                    return;

                IntPtr edit = GetWindow(_box.Handle, GwChild);
                // No child edit (DropDownList or not yet created): nothing to lock.
                if (edit == IntPtr.Zero)
                    return;
                SendMessage(edit, EmSetReadOnly, (IntPtr)1, IntPtr.Zero);
            }

            [DllImport("user32.dll")]
            private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        }
    }
}
