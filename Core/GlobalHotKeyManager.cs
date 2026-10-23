using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AudioMirror.Core;

/// <summary>
/// Gestiona el registro y captura de atajos de teclado globales en Windows a nivel de sistema operativo.
/// </summary>
public sealed class GlobalHotKeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _windowHandle;
    private readonly HwndSource _hwndSource;
    private readonly int _hotKeyId;
    private bool _isRegistered;
    private bool _disposed;

    public event Action? HotKeyPressed;

    public GlobalHotKeyManager(IntPtr windowHandle, ModifierKeys modifiers = ModifierKeys.Control | ModifierKeys.Alt, Key key = Key.A)
    {
        _windowHandle = windowHandle;
        _hotKeyId = GetHashCode() ^ (int)DateTime.UtcNow.Ticks;

        _hwndSource = HwndSource.FromHwnd(_windowHandle)
                      ?? throw new InvalidOperationException("No se pudo obtener el HwndSource de la ventana.");

        _hwndSource.AddHook(HwndHook);

        Register(modifiers, key);
    }

    public bool Register(ModifierKeys modifiers, Key key)
    {
        if (_isRegistered)
        {
            Unregister();
        }

        uint fsModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) fsModifiers |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) fsModifiers |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) fsModifiers |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) fsModifiers |= MOD_WIN;

        // Evita disparos repetidos infinitos si la tecla se mantiene pulsada
        fsModifiers |= MOD_NOREPEAT;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        _isRegistered = RegisterHotKey(_windowHandle, _hotKeyId, fsModifiers, vk);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_isRegistered)
        {
            UnregisterHotKey(_windowHandle, _hotKeyId);
            _isRegistered = false;
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotKeyId)
        {
            HotKeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Unregister();
            _hwndSource.RemoveHook(HwndHook);
            _disposed = true;
        }
    }
}
