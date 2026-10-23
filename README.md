# AudioMirror — Duplicador y Conmutador de Audio WASAPI en Tiempo Real

**AudioMirror** es una aplicación de escritorio para Windows en **C# / .NET 8 (WPF)** diseñada para duplicar audio entre múltiples salidas simultáneas (como dos Smart TVs HDMI o altavoces y auriculares) con **baja latencia**, **atajo global de teclado**, **nivelación automática de volumen**, **persistencia total**, **arranque con Windows** y **ejecución desatendida en segundo plano**.

---

## 🚀 Funcionalidades Principales

### 1. 💾 Persistencia Total de Configuración
* Almacenada en `%AppData%/AudioMirror/config.json` con `System.Text.Json`.
* Guarda automáticamente:
  * Dispositivo de origen (`PrimaryDeviceId`) y duplicado (`SecondaryDeviceId`).
  * Latencia de buffer elegida.
  * **Casillas de verificación de rotación (`CycleDeviceIds`):** Los dispositivos marcados para rotar con el atajo de teclado se restauran fielmente al abrir la app.
  * Opciones de inicio automático y nivelación de volumen.

### 2. 🔌 Arranque Desatendido y Handshake HDMI / KVM
* **Cold Boot Protection:** Al iniciar Windows, las pantallas HDMI y switches KVM tardan varios segundos en ser reconocidos por el sistema.
* **Sondeo Inteligente:** AudioMirror realiza un sondeo asíncrono de hasta 45 segundos esperando a que los endpoints HDMI guardados estén en estado `DeviceState.Active`.
* En cuanto se detectan, activa automáticamente la duplicación (`AutoStartMirroring`) sin intervención del usuario.

### 3. 🖥️ Inicio Automático con Windows (`--minimized`)
* Opción integrada para registrarse en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
* Inicia de forma desatendida en la bandeja del sistema (System Tray) sin estorbar en pantalla.

### 4. ⌨️ Atajo Global de Teclado (`Ctrl + Alt + A`)
* Conmuta la salida predeterminada de Windows entre las fuentes marcadas con `[✔]`.
* **La configuración del puente de duplicación permanece fija e intacta.**

### 5. 🔊 Nivelación y Sincronización de Volumen
* **Protección anti-aturdimiento:** Iguala el volumen antes de cambiar de TV o a auriculares para que no aturda.
* **Seguimiento dinámico:** Si subes, bajas o silencias el volumen desde el teclado de Windows, el dispositivo secundario copia el cambio al instante.

---

## 📦 Ejecución y Pruebas

Abre tu terminal en la carpeta del proyecto:

```powershell
cd C:\Users\fabri\Desktop\proyectos\AudioMirror
dotnet run
```

O inicia directamente el binario compilado:
`bin\Debug\net8.0-windows\AudioMirror.exe`
