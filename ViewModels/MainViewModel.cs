using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Interop;
using System.Windows.Threading;
using AudioMirror.Core;
using AudioMirror.Models;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace AudioMirror.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioDeviceManager _deviceManager;
    private readonly AudioBridgeEngine _engine;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _meterDecayTimer;

    private GlobalHotKeyManager? _hotKeyManager;
    private SystemTrayManager? _systemTrayManager;
    private System.Windows.Window? _window;
    private CancellationTokenSource? _handshakeCts;

    private AudioDeviceInfo? _selectedSource;
    private AudioDeviceInfo? _selectedTarget;
    private bool _isRunning;
    private bool _isHandshakeInProgress;
    private string _statusText = "Iniciando detección...";
    private string _statusColor = "#9E9E9E";
    private string _formatInfo = "Esperando inicio...";
    private double _peakLeft;
    private double _peakRight;

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();

    public AudioDeviceInfo? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                UpdateFormatsPreview();
                if (!_isHandshakeInProgress && value != null)
                {
                    _settings.PrimaryDeviceId = value.Id;
                    _settingsService.Save(_settings);
                }
            }
        }
    }

    public AudioDeviceInfo? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (SetProperty(ref _selectedTarget, value))
            {
                UpdateFormatsPreview();
                if (!_isHandshakeInProgress && value != null)
                {
                    _settings.SecondaryDeviceId = value.Id;
                    _settingsService.Save(_settings);
                }
            }
        }
    }

    public int LatencyMs
    {
        get => _settings.TargetLatencyMs;
        set
        {
            if (_settings.TargetLatencyMs != value)
            {
                _settings.TargetLatencyMs = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

    public bool SyncVolume
    {
        get => _settings.SyncVolume;
        set
        {
            if (_settings.SyncVolume != value)
            {
                _settings.SyncVolume = value;
                _engine.SyncVolume = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

    public bool MinimizeToTrayOnClose
    {
        get => _settings.MinimizeToTrayOnClose;
        set
        {
            if (_settings.MinimizeToTrayOnClose != value)
            {
                _settings.MinimizeToTrayOnClose = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

    public bool AutoStartMirroring
    {
        get => _settings.AutoStartMirroring;
        set
        {
            if (_settings.AutoStartMirroring != value)
            {
                _settings.AutoStartMirroring = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows != value)
            {
                _settings.StartWithWindows = value;
                StartupManager.SetStartup(value);
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set
        {
            if (_settings.StartMinimized != value)
            {
                _settings.StartMinimized = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanChangeDevices));
                OnPropertyChanged(nameof(ActionButtonText));
                _systemTrayManager?.UpdateStatus(_isRunning, SelectedSource?.FriendlyName ?? "");
            }
        }
    }

    public bool CanChangeDevices => !IsRunning && !_isHandshakeInProgress;
    public string ActionButtonText => IsRunning ? "Detener Duplicación" : "Iniciar Duplicación";
    public string HotKeyDisplay => "Ctrl + Alt + A";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        private set => SetProperty(ref _statusColor, value);
    }

    public string FormatInfo
    {
        get => _formatInfo;
        private set => SetProperty(ref _formatInfo, value);
    }

    public double PeakLeft
    {
        get => _peakLeft;
        set => SetProperty(ref _peakLeft, value);
    }

    public double PeakRight
    {
        get => _peakRight;
        set => SetProperty(ref _peakRight, value);
    }

    public RelayCommand ToggleBridgeCommand { get; }
    public RelayCommand RefreshDevicesCommand { get; }
    public RelayCommand CycleSourceCommand { get; }

    public MainViewModel(AppSettings? settings = null, SettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        _settings = settings ?? _settingsService.Load();

        // Validar consistencia con el Registro de Windows al iniciar
        bool isRegisteredInStartup = StartupManager.IsStartupEnabled();
        if (_settings.StartWithWindows != isRegisteredInStartup)
        {
            _settings.StartWithWindows = isRegisteredInStartup;
            _settingsService.Save(_settings);
        }

        _deviceManager = new AudioDeviceManager();
        _engine = new AudioBridgeEngine
        {
            SyncVolume = _settings.SyncVolume
        };

        _engine.StateChanged += OnEngineStateChanged;
        _engine.ErrorOccurred += OnEngineError;
        _engine.PeakLevelUpdated += OnPeakLevelUpdated;

        ToggleBridgeCommand = new RelayCommand(ToggleBridge);
        RefreshDevicesCommand = new RelayCommand(() => RefreshDevices(forceDefaultSelection: false), () => CanChangeDevices);
        CycleSourceCommand = new RelayCommand(CycleNextSource);

        _meterDecayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _meterDecayTimer.Tick += OnMeterDecayTick;
        _meterDecayTimer.Start();
    }

    /// <summary>
    /// Vincula la ventana principal para registrar atajo global, bandeja del sistema e inicia la rutina desatendida.
    /// </summary>
    public void InitializeWindowServices(System.Windows.Window window, IntPtr hwnd, bool startedMinimized)
    {
        _window = window;

        try
        {
            _hotKeyManager = new GlobalHotKeyManager(hwnd);
            _hotKeyManager.HotKeyPressed += OnHotKeyPressed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error registrando atajo global: {ex.Message}");
        }

        try
        {
            _systemTrayManager = new SystemTrayManager();
            _systemTrayManager.OpenRequested += RestoreWindow;
            _systemTrayManager.NextSourceRequested += CycleNextSource;
            _systemTrayManager.ToggleBridgeRequested += ToggleBridge;
            _systemTrayManager.ExitRequested += ExitApplication;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error en SystemTrayManager: {ex.Message}");
        }

        // Iniciar rutina asíncrona de sondeo para HDMI Handshake y Cold Boot
        _handshakeCts = new CancellationTokenSource();
        _ = RunHandshakeAndAutoStartAsync(startedMinimized, _handshakeCts.Token);
    }

    private void OnHotKeyPressed()
    {
        Application.Current?.Dispatcher.InvokeAsync(CycleNextSource);
    }

    /// <summary>
    /// Rutina de espera asíncrona para cold boot y switches HDMI/KVM (sondeo de hasta 45 segundos).
    /// </summary>
    private async Task RunHandshakeAndAutoStartAsync(bool startedMinimized, CancellationToken cancellationToken)
    {
        _isHandshakeInProgress = true;
        OnPropertyChanged(nameof(CanChangeDevices));

        var timeout = TimeSpan.FromSeconds(45);
        var startTime = DateTime.UtcNow;
        bool targetsResolved = false;

        StatusText = "Detectando endpoints HDMI / Audio...";
        StatusColor = "#FFC107"; // Ámbar

        while (!cancellationToken.IsCancellationRequested && (DateTime.UtcNow - startTime) < timeout)
        {
            RefreshDevices(forceDefaultSelection: false);

            bool hasPrimary = !string.IsNullOrEmpty(_settings.PrimaryDeviceId) &&
                              Devices.Any(d => d.Id.Equals(_settings.PrimaryDeviceId, StringComparison.OrdinalIgnoreCase));

            bool hasSecondary = !string.IsNullOrEmpty(_settings.SecondaryDeviceId) &&
                                Devices.Any(d => d.Id.Equals(_settings.SecondaryDeviceId, StringComparison.OrdinalIgnoreCase));

            // Si ambos dispositivos configurados ya están presentes en Windows
            if (hasPrimary && hasSecondary)
            {
                SelectedSource = Devices.FirstOrDefault(d => d.Id.Equals(_settings.PrimaryDeviceId, StringComparison.OrdinalIgnoreCase));
                SelectedTarget = Devices.FirstOrDefault(d => d.Id.Equals(_settings.SecondaryDeviceId, StringComparison.OrdinalIgnoreCase));
                targetsResolved = true;
                break;
            }

            int elapsedSec = (int)(DateTime.UtcNow - startTime).TotalSeconds;
            StatusText = $"Esperando conexión HDMI/Audio ({elapsedSec}s / 45s)...";

            try
            {
                await Task.Delay(2000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        // Si terminó el sondeo y no se encontraron los exactos, asegurar selección inteligente
        if (!targetsResolved)
        {
            RefreshDevices(forceDefaultSelection: true);
        }

        _isHandshakeInProgress = false;
        OnPropertyChanged(nameof(CanChangeDevices));

        // Autoarranque desatendido de duplicación si está configurado
        if (_settings.AutoStartMirroring && SelectedSource != null && SelectedTarget != null && !SelectedSource.Id.Equals(SelectedTarget.Id, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _engine.Start(SelectedSource.Device, SelectedTarget.Device, LatencyMs);
                StatusText = "Duplicando automáticamente en segundo plano";
                StatusColor = "#4CAF50";

                if (startedMinimized)
                {
                    _systemTrayManager?.ShowNotification(
                        "AudioMirror iniciado",
                        $"Duplicando audio automáticamente:\n{SelectedSource.FriendlyName} ➔ {SelectedTarget.FriendlyName}",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Error en autoarranque: {ex.Message}";
                StatusColor = "#F44336";
            }
        }
        else
        {
            StatusText = "Listo para iniciar";
            StatusColor = "#9E9E9E";

            if (startedMinimized)
            {
                _systemTrayManager?.ShowNotification(
                    "AudioMirror en segundo plano",
                    "Listo en la bandeja del sistema. Usa Ctrl+Alt+A para conmutar salida.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        }
    }

    public void RefreshDevices(bool forceDefaultSelection)
    {
        if (IsRunning) return;

        var currentActive = _deviceManager.GetActiveRenderDevices();

        // Preservar selecciones actuales si aún existen
        string? currentSourceId = SelectedSource?.Id ?? _settings.PrimaryDeviceId;
        string? currentTargetId = SelectedTarget?.Id ?? _settings.SecondaryDeviceId;

        // Desuscribir eventos previos
        foreach (var dev in Devices)
        {
            dev.PropertyChanged -= OnDevicePropertyChanged;
        }

        Devices.Clear();
        foreach (var dev in currentActive)
        {
            // Restaurar estado de checkboxes de rotación según los IDs guardados
            if (_settings.CycleDeviceIds != null && _settings.CycleDeviceIds.Count > 0)
            {
                dev.IsIncludedInCycle = _settings.CycleDeviceIds.Contains(dev.Id, StringComparer.OrdinalIgnoreCase);
            }

            dev.PropertyChanged += OnDevicePropertyChanged;
            Devices.Add(dev);
        }

        // Restaurar selecciones
        if (!string.IsNullOrEmpty(currentSourceId))
        {
            SelectedSource = Devices.FirstOrDefault(d => d.Id.Equals(currentSourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(currentTargetId))
        {
            SelectedTarget = Devices.FirstOrDefault(d => d.Id.Equals(currentTargetId, StringComparison.OrdinalIgnoreCase));
        }

        if (forceDefaultSelection || SelectedSource == null)
        {
            SelectedSource = Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
        }

        if (forceDefaultSelection || SelectedTarget == null || SelectedTarget.Id.Equals(SelectedSource?.Id, StringComparison.OrdinalIgnoreCase))
        {
            SelectedTarget = Devices
                .Where(d => !d.Id.Equals(SelectedSource?.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.FriendlyName.Contains("Digital Output", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .FirstOrDefault();
        }

        UpdateFormatsPreview();
    }

    private void OnDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioDeviceInfo.IsIncludedInCycle) && !_isHandshakeInProgress)
        {
            _settings.CycleDeviceIds = Devices
                .Where(d => d.IsIncludedInCycle)
                .Select(d => d.Id)
                .ToList();

            _settingsService.Save(_settings);
        }
    }

    public void CycleNextSource()
    {
        var markedDevices = Devices.Where(d => d.IsIncludedInCycle).ToList();
        if (markedDevices.Count < 2)
        {
            _systemTrayManager?.ShowNotification(
                "AudioMirror — Atajo de teclado",
                "Por favor marca al menos 2 dispositivos en la lista para alternar la salida con el atajo.",
                System.Windows.Forms.ToolTipIcon.Warning);
            return;
        }

        var currentDefault = _deviceManager.GetDefaultRenderDevice();
        string currentId = currentDefault?.ID ?? "";

        int currentIndex = markedDevices.FindIndex(d => d.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase));
        int nextIndex = (currentIndex + 1) % markedDevices.Count;
        var nextOutput = markedDevices[nextIndex];

        try
        {
            if (currentDefault != null && SyncVolume)
            {
                float currentVolume = currentDefault.AudioEndpointVolume.MasterVolumeLevelScalar;
                bool isMuted = currentDefault.AudioEndpointVolume.Mute;

                nextOutput.Device.AudioEndpointVolume.MasterVolumeLevelScalar = currentVolume;
                nextOutput.Device.AudioEndpointVolume.Mute = isMuted;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error nivelando volumen al alternar: {ex.Message}");
        }

        WindowsAudioPolicy.SetDefaultEndpoint(nextOutput.Id);

        foreach (var dev in Devices)
        {
            dev.IsDefault = dev.Id.Equals(nextOutput.Id, StringComparison.OrdinalIgnoreCase);
        }

        float finalVol = nextOutput.Device.AudioEndpointVolume.MasterVolumeLevelScalar;
        _systemTrayManager?.ShowNotification(
            "Salida de audio cambiada",
            $"Salida activa: {nextOutput.FriendlyName}\nVolumen nivelado al {finalVol:P0}",
            System.Windows.Forms.ToolTipIcon.Info);

        _systemTrayManager?.UpdateStatus(IsRunning, nextOutput.FriendlyName);
    }

    public void RestoreWindow()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (_window == null) return;
            _window.Show();
            _window.WindowState = System.Windows.WindowState.Normal;
            _window.Activate();
        });
    }

    public void HandleWindowClosing(CancelEventArgs e)
    {
        if (MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            _window?.Hide();
            _systemTrayManager?.ShowNotification(
                "AudioMirror en segundo plano",
                "La aplicación sigue ejecutándose. Usa Ctrl+Alt+A para alternar la salida o haz doble clic para abrir.",
                System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void ExitApplication()
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            MinimizeToTrayOnClose = false;
            Dispose();
            Application.Current.Shutdown();
        });
    }

    private void UpdateFormatsPreview()
    {
        if (SelectedSource != null && SelectedTarget != null)
        {
            FormatInfo = $"Origen: {SelectedSource.FormatDescription} ➔ Destino: {SelectedTarget.FormatDescription}";
        }
        else
        {
            FormatInfo = "Seleccione ambos dispositivos.";
        }
    }

    private void ToggleBridge()
    {
        if (IsRunning)
        {
            StopBridge();
        }
        else
        {
            StartBridge();
        }
    }

    private void StartBridge()
    {
        if (SelectedSource == null || SelectedTarget == null)
        {
            MessageBox.Show("Por favor seleccione un dispositivo de origen y uno de destino.", "AudioMirror", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (SelectedSource.Id.Equals(SelectedTarget.Id, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("El dispositivo de origen y destino deben ser diferentes.", "AudioMirror", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            _engine.SyncVolume = SyncVolume;
            _engine.Start(SelectedSource.Device, SelectedTarget.Device, LatencyMs);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo iniciar la duplicación de audio:\n\n{ex.Message}", "Error WASAPI", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void StopBridge()
    {
        try
        {
            _engine.Stop();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al detener:\n{ex.Message}", "Aviso", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnEngineStateChanged(object? sender, AudioBridgeState state)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            switch (state)
            {
                case AudioBridgeState.Running:
                    IsRunning = true;
                    StatusText = _engine.IsResamplingActive
                        ? "Duplicando en tiempo real (Resampleo continuo)"
                        : "Duplicando en tiempo real (Paso directo 1:1)";
                    StatusColor = "#4CAF50";
                    break;
                case AudioBridgeState.Starting:
                    StatusText = "Inicializando endpoints WASAPI...";
                    StatusColor = "#FFC107";
                    break;
                case AudioBridgeState.Stopping:
                    StatusText = "Deteniendo...";
                    StatusColor = "#FF9800";
                    break;
                case AudioBridgeState.Stopped:
                    IsRunning = false;
                    StatusText = "Puente detenido.";
                    StatusColor = "#9E9E9E";
                    PeakLeft = 0;
                    PeakRight = 0;
                    break;
                case AudioBridgeState.Faulted:
                    IsRunning = false;
                    StatusText = "Error en el flujo de audio.";
                    StatusColor = "#F44336";
                    PeakLeft = 0;
                    PeakRight = 0;
                    break;
            }
        });
    }

    private void OnEngineError(object? sender, string errorMessage)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            StatusText = $"Fallo: {errorMessage}";
            StatusColor = "#F44336";
        });
    }

    private void OnPeakLevelUpdated(float left, float right)
    {
        double newLeft = left * 100.0;
        double newRight = right * 100.0;

        if (newLeft > _peakLeft) _peakLeft = newLeft;
        if (newRight > _peakRight) _peakRight = newRight;
    }

    private void OnMeterDecayTick(object? sender, EventArgs e)
    {
        if (!IsRunning)
        {
            PeakLeft = 0;
            PeakRight = 0;
            return;
        }

        _peakLeft = Math.Max(0, _peakLeft - 3.5);
        _peakRight = Math.Max(0, _peakRight - 3.5);

        OnPropertyChanged(nameof(PeakLeft));
        OnPropertyChanged(nameof(PeakRight));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _handshakeCts?.Cancel();
        _handshakeCts?.Dispose();
        _meterDecayTimer.Stop();
        _hotKeyManager?.Dispose();
        _hotKeyManager = null;
        _systemTrayManager?.Dispose();
        _systemTrayManager = null;
        _engine.Dispose();
        _deviceManager.Dispose();
    }
}
