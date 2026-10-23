# AudioMirror — Duplicador y Puente de Audio WASAPI en Tiempo Real

**AudioMirror** es una aplicación de escritorio ligera desarrollada en **C# / .NET 8 (WPF)** diseñada para capturar la salida de audio de un dispositivo de reproducción principal (como una salida HDMI, auriculares o altavoces) y clonarla en tiempo real hacia un segundo dispositivo de salida (por ejemplo, una segunda pantalla HDMI, tarjeta de sonido USB, etc.).

---

## 🎯 Arquitectura Técnica y Funcionamiento

Windows no permite de forma nativa enviar la misma salida de audio a dos dispositivos físicos independientes sin usar cables virtuales de terceros (como VoiceMeeter). **AudioMirror** resuelve esto a nivel de kernel de audio de Windows mediante la **Windows Audio Session API (WASAPI)**:

```
[ Dispositivo Origen (Ej: HDMI 1) ] 
       │
       ▼ (WASAPI Loopback Capture - Shared Mode)
[ AudioBridgeEngine ]
       │
       ├─► [ Buffer Circular Thread-Safe (BufferedWaveProvider) ]
       │         │ (Control de drift y descarte de desbordamiento)
       │         ▼
       ├─► [ Resampleador Automático (MediaFoundationResampler) ] (Si SR/Canales difieren)
       │         │
       ▼         ▼
[ Dispositivo Destino (Ej: HDMI 2) ] ◄── (WASAPI Render Client - Event-Driven Sync)
```

### Características Principales:
1. **WASAPI Loopback Capture:** Captura directa del buffer de mezcla del dispositivo sin instalar drivers de kernel ni tarjetas de sonido virtuales.
2. **Resampleo Automático en Tiempo Real:** Si el dispositivo origen emite a `48000 Hz, 24-bit` y el destino soporta `44100 Hz, 16-bit`, la API de Media Foundation adapta el flujo sobre la marcha con fidelidad audiófila y mínimo impacto en CPU.
3. **Control Anti-Drift:** Dado que dos tarjetas de audio físicas tienen relojes de cuarzo ligeramente diferentes, el buffer circular está configurado con `DiscardOnBufferOverflow = true` y `ReadFully = true`, impidiendo que la latencia se acumule con el paso de las horas.
4. **Baja Latencia Configurable:** Rango de 15 a 100 ms (25 ms recomendado para modo compartido de Windows).
5. **Vúmetro Estéreo en Vivo:** Visualización de picos en tiempo real por canal (L/R) con decaimiento amortiguado a 60 FPS.

---

## 📦 Paquetes NuGet Requeridos

El proyecto utiliza únicamente el paquete estándar de audio:
* [`NAudio`](https://www.nuget.org/packages/NAudio/) (versión `2.2.1` o superior, incluye sub-módulos `NAudio.Wasapi` y `NAudio.Core`).

---

## 🚀 Cómo Ejecutar y Probar

### Requisitos previos:
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para Windows.

### Compilación y Ejecución desde la Terminal:
```powershell
cd AudioMirror
dotnet restore
dotnet run -c Release
```

### O desde Visual Studio:
1. Abrir la carpeta del proyecto en Visual Studio 2022.
2. Presionar `F5` o seleccionar **Iniciar**.

---

## 💻 Uso como Código de Consola / Background Worker

Si deseas usar únicamente el motor de audio (`AudioBridgeEngine`) en una aplicación de consola o servicio en segundo plano (System Tray):

```csharp
using NAudio.CoreAudioApi;
using AudioMirror.Core;

using var deviceManager = new AudioDeviceManager();
var devices = deviceManager.GetActiveRenderDevices();

// 1. Seleccionar dispositivos
var source = devices.First(d => d.IsDefault); // Salida HDMI principal
var target = devices.First(d => !d.IsDefault); // Segunda salida HDMI

// 2. Iniciar el puente con 25ms de latencia
using var engine = new AudioBridgeEngine();
engine.Start(source.Device, target.Device, latencyMilliseconds: 25);

Console.WriteLine($"Duplicando audio desde '{source.FriendlyName}' hacia '{target.FriendlyName}'...");
Console.WriteLine("Presiona cualquier tecla para detener...");
Console.ReadKey();

engine.Stop();
```
# AudioMirror
