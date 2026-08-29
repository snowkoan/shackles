using System.ComponentModel;

namespace Shackles.ExperimentalSandboxes;

public enum ExperimentalSandboxOperation
{
    CheckSupport,
    ValidatePolicy,
    SerializePolicy,
    CreateProcess,
    TrackProcess,
    DeleteProfile
}

public sealed class ExperimentalSandboxException : Exception
{
    public ExperimentalSandboxException(
        ExperimentalSandboxOperation operation,
        string message,
        int? nativeErrorCode = null,
        Exception? innerException = null)
        : base(BuildMessage(operation, message, nativeErrorCode), innerException)
    {
        Operation = operation;
        NativeErrorCode = nativeErrorCode;
    }

    public ExperimentalSandboxOperation Operation { get; }

    public int? NativeErrorCode { get; }

    internal static ExperimentalSandboxException FromWin32(
        ExperimentalSandboxOperation operation,
        int nativeErrorCode,
        string? detail = null)
    {
        var nativeMessage = new Win32Exception(nativeErrorCode).Message;
        var message = string.IsNullOrWhiteSpace(detail)
            ? nativeMessage
            : $"{detail} Native error: {nativeMessage}";
        return new ExperimentalSandboxException(
            operation,
            message,
            nativeErrorCode);
    }

    private static string BuildMessage(
        ExperimentalSandboxOperation operation,
        string message,
        int? nativeErrorCode) =>
        nativeErrorCode.HasValue
            ? $"{operation} failed ({nativeErrorCode.Value}): {message}"
            : $"{operation} failed: {message}";
}
