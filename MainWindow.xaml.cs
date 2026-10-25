using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AudioMirror.Models;
using AudioMirror.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AudioMirror;

/// <summary>
/// Ventana principal de AudioMirror con soporte de arranque en segundo plano y persistencia.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow(bool startMinimized = false, AppSettings? settings = null)
    {
        InitializeComponent();

        // Garantizar que el HWND esté creado de inmediato para que los atajos de teclado
        // y la bandeja del sistema funcionen aun cuando la ventana inicie oculta.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();

        var vm = new MainViewModel(settings);
        DataContext = vm;

        vm.InitializeWindowServices(this, hwnd, startMinimized);

        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        ViewModel?.HandleWindowClosing(e);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ViewModel?.Dispose();
    }

    private void BtnHotKeyRecorder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.StartHotKeyRecording();
            BtnHotKeyRecorder.Focus();
        }
    }

    private void BtnHotKeyRecorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel == null || !ViewModel.IsRecordingHotKey) return;

        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Cancelar con Escape
        if (key == Key.Escape)
        {
            ViewModel.CancelHotKeyRecording();
            Keyboard.ClearFocus();
            return;
        }

        // Ignorar teclas modificadoras solitarias mientras se mantienen presionadas
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin ||
            key == Key.Clear || key == Key.OemClear || key == Key.Apps)
        {
            return;
        }

        // Detectar modificadores activos de forma robusta
        var modifiers = Keyboard.Modifiers;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            modifiers |= ModifierKeys.Windows;
        }
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            modifiers |= ModifierKeys.Control;
        }
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
        {
            modifiers |= ModifierKeys.Alt;
        }
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
        {
            modifiers |= ModifierKeys.Shift;
        }

        // Validación de seguridad para teclas comunes
        bool isFunctionKey = key >= Key.F1 && key <= Key.F24;
        bool isMediaKey = key == Key.MediaNextTrack || key == Key.MediaPreviousTrack ||
                          key == Key.MediaPlayPause || key == Key.MediaStop ||
                          key == Key.VolumeDown || key == Key.VolumeUp || key == Key.VolumeMute;

        if (modifiers == ModifierKeys.None && !isFunctionKey && !isMediaKey)
        {
            ViewModel.HotKeyStatusMessage = "Por seguridad, las teclas comunes requieren al menos un modificador (Ctrl, Alt, Shift o Win).";
            ViewModel.HotKeyStatusColor = "#FFC107";
            return;
        }

        ViewModel.ApplyCapturedHotKey(modifiers, key);
        Keyboard.ClearFocus();
    }

    private void BtnHotKeyRecorder_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel?.IsRecordingHotKey == true)
        {
            ViewModel.CancelHotKeyRecording();
        }
    }
}
