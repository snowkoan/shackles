using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal static class WespProcessIntegrityProbe
{
    private const uint TokenQuery = 0x0008;
    private const int ErrorInsufficientBuffer = 122;
    private const uint SecurityMandatoryLowRid = 0x1000;
    private const uint SecurityMandatoryMediumRid = 0x2000;
    private const uint SecurityMandatoryMediumPlusRid = 0x2100;
    private const uint SecurityMandatoryHighRid = 0x3000;
    private const uint SecurityMandatorySystemRid = 0x4000;
    private const uint SecurityMandatoryProtectedProcessRid = 0x5000;

    internal static bool IsCurrentProcessHighIntegrity()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsHighIntegrity(GetCurrentIntegrityRid());
    }

    internal static void EnsureHighIntegrity()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new WespException(
                WespOperation.CheckIntegrity,
                "WESP Blocking is only supported on Windows.");
        }

        var integrityRid = GetCurrentIntegrityRid();
        if (IsHighIntegrity(integrityRid))
        {
            return;
        }

        throw new WespException(
            WespOperation.CheckIntegrity,
            $"The current Shackles process is running at {DescribeIntegrity(integrityRid)} integrity " +
            $"(RID 0x{integrityRid:X4}). WESP Blocking requires high integrity. " +
            "Restart Shackles using Run as administrator.");
    }

    internal static bool IsHighIntegrity(uint integrityRid) =>
        integrityRid >= SecurityMandatoryHighRid;

    internal static string DescribeIntegrity(uint integrityRid) => integrityRid switch
    {
        >= SecurityMandatoryProtectedProcessRid => "protected-process",
        >= SecurityMandatorySystemRid => "system",
        >= SecurityMandatoryHighRid => "high",
        >= SecurityMandatoryMediumPlusRid => "medium-plus",
        >= SecurityMandatoryMediumRid => "medium",
        >= SecurityMandatoryLowRid => "low",
        _ => "untrusted"
    };

    private static unsafe uint GetCurrentIntegrityRid()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                TokenQuery,
                out var rawToken))
        {
            throw FromLastWin32Error(
                "Windows could not open the current Shackles process token to verify its integrity level.");
        }

        using var token = new SafeTokenHandle(rawToken);
        var firstResult = NativeMethods.GetTokenInformation(
            token.DangerousGetHandle(),
            NativeTokenInformationClass.TokenIntegrityLevel,
            0,
            0,
            out var requiredLength);
        var firstError = Marshal.GetLastPInvokeError();
        if (firstResult || firstError != ErrorInsufficientBuffer || requiredLength == 0)
        {
            throw FromWin32Error(
                firstError,
                "Windows did not report the buffer required to inspect the current process integrity level.");
        }

        byte[] tokenInformation;
        try
        {
            tokenInformation = GC.AllocateUninitializedArray<byte>(checked((int)requiredLength));
        }
        catch (OverflowException exception)
        {
            throw new WespException(
                WespOperation.CheckIntegrity,
                "Windows reported an invalid process-token information size.",
                innerException: exception);
        }

        fixed (byte* tokenInformationPointer = tokenInformation)
        {
            if (!NativeMethods.GetTokenInformation(
                    token.DangerousGetHandle(),
                    NativeTokenInformationClass.TokenIntegrityLevel,
                    (nint)tokenInformationPointer,
                    requiredLength,
                    out _))
            {
                throw FromLastWin32Error(
                    "Windows could not read the current Shackles process integrity level.");
            }

            var mandatoryLabel = Marshal.PtrToStructure<NativeTokenMandatoryLabel>(
                (nint)tokenInformationPointer);
            var sid = mandatoryLabel.Label.Sid;
            if (sid == 0 || !NativeMethods.IsValidSid(sid))
            {
                throw new WespException(
                    WespOperation.CheckIntegrity,
                    "Windows returned an invalid integrity SID for the current Shackles process token.");
            }

            var subAuthorityCountPointer = NativeMethods.GetSidSubAuthorityCount(sid);
            if (subAuthorityCountPointer == 0 || Marshal.ReadByte(subAuthorityCountPointer) == 0)
            {
                throw new WespException(
                    WespOperation.CheckIntegrity,
                    "Windows returned an integrity SID without an integrity-level RID.");
            }

            var subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
            var integrityRidPointer = NativeMethods.GetSidSubAuthority(
                sid,
                checked((uint)(subAuthorityCount - 1)));
            if (integrityRidPointer == 0)
            {
                throw new WespException(
                    WespOperation.CheckIntegrity,
                    "Windows could not locate the integrity-level RID in the current process token.");
            }

            return unchecked((uint)Marshal.ReadInt32(integrityRidPointer));
        }
    }

    private static WespException FromLastWin32Error(string detail) =>
        FromWin32Error(Marshal.GetLastPInvokeError(), detail);

    private static WespException FromWin32Error(int error, string detail)
    {
        var hresult = error <= 0
            ? unchecked((int)0x80004005)
            : unchecked((int)(0x80070000u | (uint)error));
        return WespException.FromHResult(
            WespOperation.CheckIntegrity,
            hresult,
            detail);
    }
}
