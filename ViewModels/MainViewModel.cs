using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private string _hotKeyStatusMessage = "Atajo registrado correctamente en Windows";
    private string _hotKeyStatusColor = "#4CAF50";
    private double _peakLeft;
    private double _peakRight;

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();
    public ObservableCollection<Key> AvailableKeys { get; } = new();

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
                    _settings.PrimaryDeviceName = value.FriendlyName;
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
                    _settings.SecondaryDeviceName = value.FriendlyName;
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

    public ModifierKeys HotKeyModifiers
    {
        get => _settings.HotKeyModifiers;
        set => UpdateHotKey(value, HotKeyKey);
    }

    public Key HotKeyKey
    {
        get => _settings.HotKeyKey;
        set => UpdateHotKey(HotKeyModifiers, value);
    }

    public bool HotKeyCtrl
    {
        get => _settings.HotKeyModifiers.HasFlag(ModifierKeys.Control);
        set => SetModifierFlag(ModifierKeys.Control, value);
    }

    public bool HotKeyAlt
    {
        get => _settings.HotKeyModifiers.HasFlag(ModifierKeys.Alt);
        set => SetModifierFlag(ModifierKeys.Alt, value);
    }

    public bool HotKeyShift
    {
        get => _settings.HotKeyModifiers.HasFlag(ModifierKeys.Shift);
        set => SetModifierFlag(ModifierKeys.Shift, value);
    }

    public bool HotKeyWin
    {
        get => _settings.HotKeyModifiers.HasFlag(ModifierKeys.Windows);
        set => SetModifierFlag(ModifierKeys.Windows, value);
    }

    public string HotKeyDisplay => GlobalHotKeyManager.GetDisplayString(_settings.HotKeyModifiers, _settings.HotKeyKey);

    private bool _isRecordingHotKey;
    public bool IsRecordingHotKey
    {
        get => _isRecordingHotKey;
        set
        {
            if (SetProperty(ref _isRecordingHotKey, value))
            {
                OnPropertyChanged(nameof(RecorderText));
            }
        }
    }

    public string RecorderText => IsRecordingHotKey
        ? "⏳ Presiona la nueva combinación de teclas (o Esc para cancelar)..."
        : $"⌨  {HotKeyDisplay}  (Haz clic aquí y presiona tus teclas)";

    public string HotKeyStatusMessage
    {
        get => _hotKeyStatusMessage;
        set => SetProperty(ref _hotKeyStatusMessage, value);
    }

    public string HotKeyStatusColor
    {
        get => _hotKeyStatusColor;
        set => SetProperty(ref _hotKeyStatusColor, value);
    }

    public bool ShowOsdNotification
    {
        get => _settings.ShowOsdNotification;
        set
        {
            if (_settings.ShowOsdNotification != value)
            {
                _settings.ShowOsdNotification = value;
                OnPropertyChanged();
                _settingsService.Save(_settings);
            }
        }
    }

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
    public RelayCommand TestOsdCommand { get; }
    public RelayCommand ResetHotKeyCommand { get; }

    public MainViewModel(AppSettings? settings = null, SettingsService? settingsService = null)
    {
        _settingsService = settingsService ?? new SettingsService();
        _settings = settings ?? _settingsService.Load();

        // Si el usuario configuró iniciar con Windows, sincronizar el registro asegurando la ruta actual
        if (_settings.StartWithWindows)
        {
            StartupManager.SetStartup(true);
        }

        // Población de teclas disponibles para el atajo
        for (var k = Key.A; k <= Key.Z; k++) AvailableKeys.Add(k);
        for (var k = Key.D0; k <= Key.D9; k++) AvailableKeys.Add(k);
        for (var k = Key.F1; k <= Key.F12; k++) AvailableKeys.Add(k);
        AvailableKeys.Add(Key.Space);
        AvailableKeys.Add(Key.Tab);
        AvailableKeys.Add(Key.Back);
        AvailableKeys.Add(Key.Pause);
        AvailableKeys.Add(Key.Insert);
        AvailableKeys.Add(Key.Delete);
        AvailableKeys.Add(Key.Home);
        AvailableKeys.Add(Key.End);
        AvailableKeys.Add(Key.PageUp);
        AvailableKeys.Add(Key.PageDown);
        AvailableKeys.Add(Key.MediaNextTrack);
        AvailableKeys.Add(Key.MediaPreviousTrack);
        AvailableKeys.Add(Key.MediaPlayPause);
        AvailableKeys.Add(Key.MediaStop);
        AvailableKeys.Add(Key.VolumeMute);
        AvailableKeys.Add(Key.VolumeDown);
        AvailableKeys.Add(Key.VolumeUp);

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
        TestOsdCommand = new RelayCommand(TestOsdNotification);
        ResetHotKeyCommand = new RelayCommand(ResetHotKeyToDefault);

        _meterDecayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _meterDecayTimer.Tick += OnMeterDecayTick;
        _meterDecayTimer.Start();
    }

    public void StartHotKeyRecording()
    {
        IsRecordingHotKey = true;
        HotKeyStatusMessage = "Presiona la combinación deseada en tu teclado...";
        HotKeyStatusColor = "#0098FF";
    }

    public void CancelHotKeyRecording()
    {
        if (IsRecordingHotKey)
        {
            IsRecordingHotKey = false;
            HotKeyStatusMessage = "Atajo conservado.";
            HotKeyStatusColor = "#9E9E9E";
        }
    }

    public void ApplyCapturedHotKey(ModifierKeys modifiers, Key key)
    {
        IsRecordingHotKey = false;
        UpdateHotKey(modifiers, key);
    }

    public void ResetHotKeyToDefault()
    {
        IsRecordingHotKey = false;
        UpdateHotKey(ModifierKeys.Control | ModifierKeys.Alt, Key.A);
    }

    private void SetModifierFlag(ModifierKeys flag, bool enabled)
    {
        var current = _settings.HotKeyModifiers;
        if (enabled) current |= flag;
        else current &= ~flag;

        UpdateHotKey(current, _settings.HotKeyKey);
    }

    public void UpdateHotKey(ModifierKeys modifiers, Key key)
    {
        if (key == Key.None) return;

        bool registered = true;
        if (_hotKeyManager != null)
        {
            registered = _hotKeyManager.Register(modifiers, key);
        }

        if (registered)
        {
            _settings.HotKeyModifiers = modifiers;
            _settings.HotKeyKey = key;
            _settingsService.Save(_settings);

            HotKeyStatusMessage = "Atajo registrado correctamente en Windows";
            HotKeyStatusColor = "#4CAF50";
        }
        else
        {
            HotKeyStatusMessage = "No se pudo registrar (atajo ocupado por otra app o Windows)";
            HotKeyStatusColor = "#F44336";
        }

        OnPropertyChanged(nameof(HotKeyModifiers));
        OnPropertyChanged(nameof(HotKeyKey));
        OnPropertyChanged(nameof(HotKeyCtrl));
        OnPropertyChanged(nameof(HotKeyAlt));
        OnPropertyChanged(nameof(HotKeyShift));
        OnPropertyChanged(nameof(HotKeyWin));
        OnPropertyChanged(nameof(HotKeyDisplay));
        OnPropertyChanged(nameof(RecorderText));

        _systemTrayManager?.UpdateHotKeyText(HotKeyDisplay);
    }

    private void TestOsdNotification()
    {
        var targetDevice = SelectedSource ?? Devices.FirstOrDefault(d => d.IsDefault) ?? Devices.FirstOrDefault();
        if (targetDevice != null)
        {
            float vol = 1.0f;
            try { vol = targetDevice.Device.AudioEndpointVolume.MasterVolumeLevelScalar; } catch { }
            OsdNotificationManager.Show(targetDevice.FriendlyName, vol, SelectedTarget?.FriendlyName, IsRunning);
        }
        else
        {
            OsdNotificationManager.Show("Dispositivo de prueba", 0.75f, "Destino secundario", false);
        }
    }

    /// <summary>
    /// Vincula la ventana principal para registrar atajo global, bandeja del sistema e inicia la rutina desatendida.
    /// </summary>
    public void InitializeWindowServices(System.Windows.Window window, IntPtr hwnd, bool startedMinimized)
    {
        _window = window;

        try
        {
            _hotKeyManager = new GlobalHotKeyManager(hwnd, _settings.HotKeyModifiers, _settings.HotKeyKey);
            _hotKeyManager.HotKeyPressed += OnHotKeyPressed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error registrando atajo global: {ex.Message}");
            HotKeyStatusMessage = "Error registrando atajo global al iniciar";
            HotKeyStatusColor = "#F44336";
        }

        try
        {
            _systemTrayManager = new SystemTrayManager();
            _systemTrayManager.UpdateHotKeyText(HotKeyDisplay);
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

            bool hasPrimary = Devices.Any(d =>
                (!string.IsNullOrEmpty(_settings.PrimaryDeviceId) && d.Id.Equals(_settings.PrimaryDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(_settings.PrimaryDeviceName) && d.FriendlyName.Equals(_settings.PrimaryDeviceName, StringComparison.OrdinalIgnoreCase)));

            bool hasSecondary = Devices.Any(d =>
                (!string.IsNullOrEmpty(_settings.SecondaryDeviceId) && d.Id.Equals(_settings.SecondaryDeviceId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(_settings.SecondaryDeviceName) && d.FriendlyName.Equals(_settings.SecondaryDeviceName, StringComparison.OrdinalIgnoreCase)));

            // Si ambos dispositivos configurados ya están presentes en Windows
            if (hasPrimary && hasSecondary)
            {
                SelectedSource = Devices.FirstOrDefault(d => d.Id.Equals(_settings.PrimaryDeviceId, StringComparison.OrdinalIgnoreCase))
                              ?? Devices.FirstOrDefault(d => !string.IsNullOrEmpty(_settings.PrimaryDeviceName) && d.FriendlyName.Equals(_settings.PrimaryDeviceName, StringComparison.OrdinalIgnoreCase));

                SelectedTarget = Devices.FirstOrDefault(d => d.Id.Equals(_settings.SecondaryDeviceId, StringComparison.OrdinalIgnoreCase))
                              ?? Devices.FirstOrDefault(d => !string.IsNullOrEmpty(_settings.SecondaryDeviceName) && d.FriendlyName.Equals(_settings.SecondaryDeviceName, StringComparison.OrdinalIgnoreCase));

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
                    $"Listo en la bandeja del sistema. Usa {HotKeyDisplay} para conmutar salida.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        }
    }

    public void RefreshDevices(bool forceDefaultSelection)
    {
        if (IsRunning) return;

        var currentActive = _deviceManager.GetActiveRenderDevices();

        // Preservar selecciones actuales si aún existen (con fallback a nombres)
        string? currentSourceId = SelectedSource?.Id ?? _settings.PrimaryDeviceId;
        string? currentSourceName = SelectedSource?.FriendlyName ?? _settings.PrimaryDeviceName;
        string? currentTargetId = SelectedTarget?.Id ?? _settings.SecondaryDeviceId;
        string? currentTargetName = SelectedTarget?.FriendlyName ?? _settings.SecondaryDeviceName;

        // Desuscribir eventos previos
        foreach (var dev in Devices)
        {
            dev.PropertyChanged -= OnDevicePropertyChanged;
        }

        Devices.Clear();
        foreach (var dev in currentActive)
        {
            // Restaurar estado de checkboxes de rotación según IDs o nombres guardados
            bool isMarked = false;
            if (_settings.CycleDeviceIds != null && _settings.CycleDeviceIds.Contains(dev.Id, StringComparer.OrdinalIgnoreCase))
            {
                isMarked = true;
            }
            else if (_settings.CycleDeviceNames != null && _settings.CycleDeviceNames.Contains(dev.FriendlyName, StringComparer.OrdinalIgnoreCase))
            {
                isMarked = true;
            }
            dev.IsIncludedInCycle = isMarked;

            dev.PropertyChanged += OnDevicePropertyChanged;
            Devices.Add(dev);
        }

        // Restaurar selecciones con fallback de ID a nombre
        if (!string.IsNullOrEmpty(currentSourceId))
        {
            SelectedSource = Devices.FirstOrDefault(d => d.Id.Equals(currentSourceId, StringComparison.OrdinalIgnoreCase))
                          ?? Devices.FirstOrDefault(d => !string.IsNullOrEmpty(currentSourceName) && d.FriendlyName.Equals(currentSourceName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(currentTargetId))
        {
            SelectedTarget = Devices.FirstOrDefault(d => d.Id.Equals(currentTargetId, StringComparison.OrdinalIgnoreCase))
                          ?? Devices.FirstOrDefault(d => !string.IsNullOrEmpty(currentTargetName) && d.FriendlyName.Equals(currentTargetName, StringComparison.OrdinalIgnoreCase));
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

            _settings.CycleDeviceNames = Devices
                .Where(d => d.IsIncludedInCycle)
                .Select(d => d.FriendlyName)
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
                $"Por favor marca al menos 2 dispositivos en la lista para alternar con {HotKeyDisplay}.",
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

        // Actualizar la fuente principal seleccionada y persistir en settings
        SelectedSource = nextOutput;
        _settings.PrimaryDeviceId = nextOutput.Id;
        _settings.PrimaryDeviceName = nextOutput.FriendlyName;

        // Si el destino coincide con el nuevo origen, buscar un destino alternativo
        if (SelectedTarget != null && SelectedTarget.Id.Equals(nextOutput.Id, StringComparison.OrdinalIgnoreCase))
        {
            var alternateTarget = markedDevices.FirstOrDefault(d => !d.Id.Equals(nextOutput.Id, StringComparison.OrdinalIgnoreCase))
                               ?? Devices.FirstOrDefault(d => !d.Id.Equals(nextOutput.Id, StringComparison.OrdinalIgnoreCase));
            if (alternateTarget != null)
            {
                SelectedTarget = alternateTarget;
                _settings.SecondaryDeviceId = alternateTarget.Id;
                _settings.SecondaryDeviceName = alternateTarget.FriendlyName;
            }
        }

        // Persistir la configuración inmediatamente tras el cambio por atajo
        _settingsService.Save(_settings);

        // Si la duplicación estaba corriendo, reanudarla con el nuevo origen
        if (IsRunning && SelectedSource != null && SelectedTarget != null)
        {
            try
            {
                _engine.Start(SelectedSource.Device, SelectedTarget.Device, LatencyMs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error reanudando puente tras alternar: {ex.Message}");
            }
        }

        float finalVol = nextOutput.Device.AudioEndpointVolume.MasterVolumeLevelScalar;

        // 1. Mostrar notificación emergente en pantalla (OSD) sin robar foco
        if (ShowOsdNotification)
        {
            OsdNotificationManager.Show(nextOutput.FriendlyName, finalVol, SelectedTarget?.FriendlyName, IsRunning);
        }

        // 2. Notificación en bandeja del sistema (Balloon Tip)
        _systemTrayManager?.ShowNotification(
            "Salida de audio cambiada",
            $"Salida activa: {nextOutput.FriendlyName}\nVolumen: {finalVol:P0}",
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
        if (App.IsSessionEnding)
        {
            // Windows está apagándose o reiniciándose: permitir el cierre sin bloquear
            _settingsService.Save(_settings);
            return;
        }

        if (MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            _window?.Hide();
            _systemTrayManager?.ShowNotification(
                "AudioMirror en segundo plano",
                $"La aplicación sigue ejecutándose. Usa {HotKeyDisplay} para alternar la salida o haz doble clic para abrir.",
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
