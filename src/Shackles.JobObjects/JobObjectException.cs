using System.ComponentModel;

namespace Shackles.JobObjects;

public class JobObjectException : Exception
{
    public JobObjectException(JobOperation operation, int nativeErrorCode)
        : this(operation, nativeErrorCode, new Win32Exception(nativeErrorCode).Message)
    {
    }

    public JobObjectException(JobOperation operation, int nativeErrorCode, string message)
        : base($"{operation} failed ({nativeErrorCode}): {message}")
    {
        Operation = operation;
        NativeErrorCode = nativeErrorCode;
    }

    public JobOperation Operation { get; }

    public int NativeErrorCode { get; }

    public JobOperationError ToError() => new(Operation, NativeErrorCode, Message);
}

public sealed class UnsupportedJobFeatureException : PlatformNotSupportedException
{
    public UnsupportedJobFeatureException(string feature, string reason)
        : base($"{feature} is unavailable: {reason}")
    {
        Feature = feature;
    }

    public string Feature { get; }
}
