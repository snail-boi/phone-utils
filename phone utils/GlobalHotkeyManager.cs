using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace phone_utils
{
    public class GlobalHotkeyManager
    {
        // P/Invoke declarations
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifier key constants
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NONE = 0x0000;

        // Virtual key codes
        private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
        private const uint VK_MEDIA_PREV_TRACK = 0xB1;
        private const uint VK_VOLUME_UP = 0xAF;
        private const uint VK_VOLUME_DOWN = 0xAE;
        private const uint VK_INSERT = 0x2D;
        private const uint VK_PRIOR = 0x21;  // Page Up
        private const uint VK_NEXT = 0x22;   // Page Down
        private const uint VK_END = 0x23;

        // Hotkey IDs
        private const int HOTKEY_MEDIA_PLAY_PAUSE = 1;
        private const int HOTKEY_MEDIA_NEXT = 2;
        private const int HOTKEY_MEDIA_PREV = 3;
        private const int HOTKEY_SHIFT_VOLUME_UP = 4;
        private const int HOTKEY_SHIFT_VOLUME_DOWN = 5;
        private const int HOTKEY_INSERT = 6;
        private const int HOTKEY_PAGE_UP = 7;
        private const int HOTKEY_PAGE_DOWN = 8;
        private const int HOTKEY_END = 9;

        private IntPtr windowHandle;
        private HwndSource hwndSource;
        private Dictionary<int, Action> hotkeyActions;

        public event Action MediaPlayPause;
        public event Action MediaNext;
        public event Action MediaPrev;
        public event Action ShiftVolumeUp;
        public event Action ShiftVolumeDown;
        public event Action InsertPressed;
        public event Action PageUpPressed;
        public event Action PageDownPressed;
        public event Action EndPressed;

        public GlobalHotkeyManager(Window window)
        {
            windowHandle = new WindowInteropHelper(window).Handle;
            if (windowHandle == IntPtr.Zero)
            {
                // If handle is not available yet, wait for window to be loaded
                window.Loaded += (s, e) => {
                    windowHandle = new WindowInteropHelper(window).Handle;
                    InitializeHotkeys();
                };
            }
            else
            {
                InitializeHotkeys();
            }

            hotkeyActions = new Dictionary<int, Action>
            {
                { HOTKEY_MEDIA_PLAY_PAUSE, () => MediaPlayPause?.Invoke() },
                { HOTKEY_MEDIA_NEXT, () => MediaNext?.Invoke() },
                { HOTKEY_MEDIA_PREV, () => MediaPrev?.Invoke() },
                { HOTKEY_SHIFT_VOLUME_UP, () => ShiftVolumeUp?.Invoke() },
                { HOTKEY_SHIFT_VOLUME_DOWN, () => ShiftVolumeDown?.Invoke() },
                { HOTKEY_INSERT, () => InsertPressed?.Invoke() },
                { HOTKEY_PAGE_UP, () => PageUpPressed?.Invoke() },
                { HOTKEY_PAGE_DOWN, () => PageDownPressed?.Invoke() },
                { HOTKEY_END, () => EndPressed?.Invoke() }
            };
        }

        private void InitializeHotkeys()
        {
            if (windowHandle == IntPtr.Zero) return;

            hwndSource = HwndSource.FromHwnd(windowHandle);
            hwndSource?.AddHook(WndProc);

            RegisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            try
            {
                RegisterHotKey(windowHandle, HOTKEY_MEDIA_PLAY_PAUSE, MOD_SHIFT, VK_MEDIA_PLAY_PAUSE);
                RegisterHotKey(windowHandle, HOTKEY_MEDIA_NEXT, MOD_SHIFT, VK_MEDIA_NEXT_TRACK);
                RegisterHotKey(windowHandle, HOTKEY_MEDIA_PREV, MOD_SHIFT, VK_MEDIA_PREV_TRACK);

                RegisterHotKey(windowHandle, HOTKEY_SHIFT_VOLUME_UP, MOD_SHIFT, VK_VOLUME_UP);
                RegisterHotKey(windowHandle, HOTKEY_SHIFT_VOLUME_DOWN, MOD_SHIFT, VK_VOLUME_DOWN);

                RegisterHotKey(windowHandle, HOTKEY_INSERT, MOD_NONE, VK_INSERT);
                RegisterHotKey(windowHandle, HOTKEY_PAGE_UP, MOD_NONE, VK_PRIOR);
                RegisterHotKey(windowHandle, HOTKEY_PAGE_DOWN, MOD_NONE, VK_NEXT);
                RegisterHotKey(windowHandle, HOTKEY_END, MOD_NONE, VK_END);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to register hotkeys: {ex.Message}", "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }


        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (hotkeyActions.TryGetValue(hotkeyId, out Action action))
                {
                    action.Invoke();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        public void UnregisterHotkeys()
        {
            if (windowHandle == IntPtr.Zero) return;

            try
            {
                UnregisterHotKey(windowHandle, HOTKEY_MEDIA_PLAY_PAUSE);
                UnregisterHotKey(windowHandle, HOTKEY_MEDIA_NEXT);
                UnregisterHotKey(windowHandle, HOTKEY_MEDIA_PREV);
                UnregisterHotKey(windowHandle, HOTKEY_SHIFT_VOLUME_UP);
                UnregisterHotKey(windowHandle, HOTKEY_SHIFT_VOLUME_DOWN);
                UnregisterHotKey(windowHandle, HOTKEY_INSERT);
                UnregisterHotKey(windowHandle, HOTKEY_PAGE_UP);
                UnregisterHotKey(windowHandle, HOTKEY_PAGE_DOWN);
                UnregisterHotKey(windowHandle, HOTKEY_END);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error unregistering hotkeys: {ex.Message}");
            }

            hwndSource?.RemoveHook(WndProc);
        }

        public void Dispose()
        {
            UnregisterHotkeys();
        }
    }
}