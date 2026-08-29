using System.Security.Principal;
using Shackles.ExperimentalSandboxes.Interop;

namespace Shackles.ExperimentalSandboxes.Internal;

internal sealed record SandboxIdentity(string ProfileName, string? Sid)
{
    private const int ErrorFileNotFoundHresult = unchecked((int)0x80070002);
    private const int ErrorNotFoundHresult = unchecked((int)0x80070490);

    internal static SandboxIdentity Create(bool useAppContainer)
    {
        var profileName = $"Shackles.Experimental.{Guid.NewGuid():N}";
        if (!useAppContainer)
        {
            return new SandboxIdentity(profileName, null);
        }

        var result = NativeMethods.DeriveAppContainerSidFromAppContainerName(
            profileName,
            out var rawSid);
        if (result < 0)
        {
            throw new ExperimentalSandboxException(
                ExperimentalSandboxOperation.ValidatePolicy,
                $"Could not derive the AppContainer SID for '{profileName}' " +
                $"(HRESULT 0x{result:X8}).",
                result);
        }

        if (rawSid == 0)
        {
            throw new ExperimentalSandboxException(
                ExperimentalSandboxOperation.ValidatePolicy,
                "Windows returned a null AppContainer SID for the sandbox identity.");
        }

        try
        {
            return new SandboxIdentity(
                profileName,
                new SecurityIdentifier(rawSid).Value);
        }
        finally
        {
            _ = NativeMethods.FreeSid(rawSid);
        }
    }

    internal string? TryDeleteProfile()
    {
        var result = NativeMethods.DeleteAppContainerProfile(ProfileName);
        return result >= 0 || result is ErrorFileNotFoundHresult or ErrorNotFoundHresult
            ? null
            : $"DeleteAppContainerProfile({ProfileName}) returned HRESULT 0x{result:X8}.";
    }
}
