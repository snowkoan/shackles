using System.Security.Principal;
using Shackles.AppContainers.Interop;

namespace Shackles.AppContainers.Internal;

internal sealed class AppContainerIdentity
{
    private const int ErrorAlreadyExistsHresult = unchecked((int)0x800700B7);
    private const int ErrorFileNotFoundHresult = unchecked((int)0x80070002);
    private const int ErrorNotFoundHresult = unchecked((int)0x80070490);

    private AppContainerIdentity(string profileName, string sid, byte[] sidBytes)
    {
        ProfileName = profileName;
        Sid = sid;
        SidBytes = sidBytes;
    }

    internal string ProfileName { get; }

    internal string Sid { get; }

    internal byte[] SidBytes { get; }

    internal static AppContainerIdentity Create(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var profileName = $"Shackles.{Guid.NewGuid():N}";
            var result = NativeMethods.CreateAppContainerProfile(
                profileName,
                displayName,
                $"Temporary AppContainer sandbox managed by Shackles for {displayName}.",
                0,
                0,
                out var rawSid);
            if (result == ErrorAlreadyExistsHresult)
            {
                continue;
            }

            if (result < 0)
            {
                throw new AppContainerException(
                    AppContainerOperation.CreateProfile,
                    $"CreateAppContainerProfile returned HRESULT 0x{result:X8}.",
                    result);
            }

            if (rawSid == 0)
            {
                _ = NativeMethods.DeleteAppContainerProfile(profileName);
                throw new AppContainerException(
                    AppContainerOperation.CreateProfile,
                    "Windows returned a null AppContainer SID.");
            }

            try
            {
                var sid = new SecurityIdentifier(rawSid);
                var sidBytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(sidBytes, 0);
                return new AppContainerIdentity(profileName, sid.Value, sidBytes);
            }
            finally
            {
                _ = NativeMethods.FreeSid(rawSid);
            }
        }

        throw new AppContainerException(
            AppContainerOperation.CreateProfile,
            "Could not allocate a unique AppContainer profile name.");
    }

    internal static string? TryDelete(string profileName)
    {
        var result = NativeMethods.DeleteAppContainerProfile(profileName);
        return result >= 0 || result is ErrorFileNotFoundHresult or ErrorNotFoundHresult
            ? null
            : $"DeleteAppContainerProfile({profileName}) returned HRESULT 0x{result:X8}.";
    }
}
