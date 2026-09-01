using System.ComponentModel;

namespace Shackles.AppContainers;

public enum AppContainerOperation
{
    CreateProfile,
    DeleteProfile,
    DeriveCapability,
    ApplyFileGrant,
    ApplyRegistryGrant,
    ConfigureBrokeredFileSystem,
    ClearBrokeredFileSystem,
    RevokeGrant,
    CreateProcess,
    TrackProcess,
    RecoverSession
}

public sealed class AppContainerException : Exception
{
    public AppContainerException(
        AppContainerOperation operation,
        string message,
        int? nativeErrorCode = null,
        Exception? innerException = null)
        : base(BuildMessage(operation, message, nativeErrorCode), innerException)
    {
        Operation = operation;
        NativeErrorCode = nativeErrorCode;
    }

    public AppContainerOperation Operation { get; }

    public int? NativeErrorCode { get; }

    internal static AppContainerException FromWin32(
        AppContainerOperation operation,
        int nativeErrorCode,
        string? detail = null)
    {
        var nativeMessage = new Win32Exception(nativeErrorCode).Message;
        var message = string.IsNullOrWhiteSpace(detail)
            ? nativeMessage
            : $"{detail} Native error: {nativeMessage}";
        return new AppContainerException(operation, message, nativeErrorCode);
    }

    private static string BuildMessage(
        AppContainerOperation operation,
        string message,
        int? nativeErrorCode) =>
        nativeErrorCode.HasValue
            ? $"{operation} failed ({nativeErrorCode.Value}): {message}"
            : $"{operation} failed: {message}";
}
