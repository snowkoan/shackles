using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Shackles.Wfp;

public enum WfpOperation
{
    CheckSupport,
    CheckIntegrity,
    OpenEngine,
    InstallInfrastructure,
    AddRule,
    RemoveRule,
    CloseSession,
    QueryInterfaces
}

public sealed class WfpException : Exception
{
    internal WfpException(
        WfpOperation operation,
        string message,
        uint? nativeErrorCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        NativeErrorCode = nativeErrorCode;
    }

    public WfpOperation Operation { get; }

    public uint? NativeErrorCode { get; }

    internal static WfpException FromNativeError(
        WfpOperation operation,
        uint error,
        string detail)
    {
        var nativeMessage = error <= ushort.MaxValue
            ? new Win32Exception(unchecked((int)error)).Message
            : Marshal.GetExceptionForHR(unchecked((int)error))?.Message ??
              $"WFP error 0x{error:X8}";
        return new WfpException(
            operation,
            $"{detail} ({nativeMessage}; 0x{error:X8})",
            error);
    }
}
