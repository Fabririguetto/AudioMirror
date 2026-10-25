using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AudioMirror.Core;

public partial class OsdNotificationWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private readonly DispatcherTimer _hideTimer;
    private bool _isClosingAnimation;

    public OsdNotificationWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _hideTimer.Tick += OnHideTimerTick;

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;

        // Establecer estilos extendidos para evitar que la ventana robe el foco al usuario
        IntPtr exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        long newExStyle = exStyle.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newExStyle));
    }

    public void ShowNotification(string deviceName, float volumeScalar, string? secondaryDeviceName, bool isMirroring)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _hideTimer.Stop();

            TxtDeviceName.Text = string.IsNullOrWhiteSpace(deviceName) ? "Dispositivo desconocido" : deviceName;
            
            int volPercent = (int)Math.Round(Math.Clamp(volumeScalar, 0f, 1f) * 100f);
            TxtVolume.Text = $"Vol: {volPercent}%";
            PbVolume.Value = volPercent;

            if (isMirroring && !string.IsNullOrWhiteSpace(secondaryDeviceName))
            {
                TxtStatus.Text = $"Duplicando hacia: {secondaryDeviceName}";
            }
            else
            {
                TxtStatus.Text = "Audio predeterminado de Windows";
            }

            // Posicionar en la parte superior central de la pantalla principal
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2.0;
            Top = workArea.Top + 40;

            _isClosingAnimation = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0.0;

            Show();

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150))
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            BeginAnimation(OpacityProperty, fadeIn);

            _hideTimer.Start();
        });
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();

        if (_isClosingAnimation) return;
        _isClosingAnimation = true;

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(250))
        {
            FillBehavior = FillBehavior.Stop
        };
        fadeOut.Completed += (_, _) =>
        {
            _isClosingAnimation = false;
            Opacity = 0;
            Hide();
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }
}