using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using AudioMirror.Models;
using AudioMirror.ViewModels;

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
}
