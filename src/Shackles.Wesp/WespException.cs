using System.Runtime.InteropServices;

namespace Shackles.Wesp;

public enum WespOperation
{
    CheckIntegrity,
    CheckSupport,
    ValidatePolicy,
    ClearPreviousClientState,
    RegisterClient,
    ConnectClient,
    ClearRules,
    CheckCapabilities,
    CreateFilter,
    CreateRule,
    CreateEventQueue,
    InstallRules,
    OpenProcess,
    ReadProcessIdentity,
    CreateProcess,
    TagProcess,
    ResumeProcess,
    CloseSession
}

public sealed class WespException : Exception
{
    public WespException(
        WespOperation operation,
        string message,
        int? hresultCode = null,
        Exception? innerException = null)
        : base(BuildMessage(operation, message, hresultCode), innerException)
    {
        Operation = operation;
        NativeHResult = hresultCode;
        if (hresultCode.HasValue)
        {
            HResult = hresultCode.Value;
        }
    }

    public WespOperation Operation { get; }

    public int? NativeHResult { get; }

    internal static WespException FromHResult(
        WespOperation operation,
        int hresult,
        string detail)
    {
        var nativeMessage = Marshal.GetExceptionForHR(hresult)?.Message;
        var message = string.IsNullOrWhiteSpace(nativeMessage)
            ? detail
            : $"{detail} {nativeMessage}";
        return new WespException(operation, message, hresult);
    }

    private static string BuildMessage(
        WespOperation operation,
        string message,
        int? hresultCode) =>
        hresultCode.HasValue
            ? $"{operation} failed (0x{hresultCode.Value:X8}): {message}"
            : $"{operation} failed: {message}";
}
