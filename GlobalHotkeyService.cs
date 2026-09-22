using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TimeTask
{
    /// <summary>
    /// 全局快捷键服务：用隐藏的 NativeWindow 承载 WM_HOTKEY，
    /// 让 TimeTask 在任意前台(包括主窗口关闭/最小化)时都能响应一键切换采集。
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 1;

        // 修饰键
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000; // 按住不重复触发

        private readonly HotkeyWindow _window;
        private bool _registered;

        public GlobalHotkeyService(Action onHotkey)
        {
            if (onHotkey == null) throw new ArgumentNullException(nameof(onHotkey));
            _window = new HotkeyWindow(HOTKEY_ID);
            _window.HotkeyPressed += onHotkey;
            _window.CreateHandle(new CreateParams());
        }

        /// <summary>
        /// 注册全局快捷键。失败返回 false（可能被其它程序占用），不抛异常。
        /// </summary>
        public bool Register(Keys key, uint modifiers)
        {
            Unregister();
            _registered = RegisterHotKey(_window.Handle, HOTKEY_ID, modifiers, (uint)key);
            if (!_registered)
            {
                VoiceRuntimeLog.Info($"全局快捷键注册失败（Key={key}, Mod={modifiers}），可能被其它程序占用。");
            }
            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
            {
                UnregisterHotKey(_window.Handle, HOTKEY_ID);
                _registered = false;
            }
        }

        public void Dispose()
        {
            Unregister();
            try { _window.ReleaseHandle(); } catch { }
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly int _id;
            public event Action HotkeyPressed;

            public HotkeyWindow(int id) { _id = id; }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == _id)
                {
                    HotkeyPressed?.Invoke();
                }
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
