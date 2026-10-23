using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace AudioMirror.Core;

/// <summary>
/// Permite cambiar el dispositivo de reproducción predeterminado del sistema operativo Windows
/// mediante la interfaz COM IPolicyConfig utilizada internamente por el subsistema de audio.
/// </summary>
public static class WindowsAudioPolicy
{
    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, int bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, int bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftDefaultPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, IntPtr pKey, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, IntPtr pKey, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint(string pszDeviceName, Role role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, int bVisible);
    }

    /// <summary>
    /// Establece el dispositivo con el ID indicado como el dispositivo de reproducción predeterminado de Windows
    /// para multimedia y comunicaciones.
    /// </summary>
    public static bool SetDefaultEndpoint(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        try
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, Role.Multimedia);
            policyConfig.SetDefaultEndpoint(deviceId, Role.Console);
            policyConfig.SetDefaultEndpoint(deviceId, Role.Communications);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowsAudioPolicy] Error cambiando dispositivo: {ex.Message}");
            return false;
        }
    }
}
