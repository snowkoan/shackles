using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal readonly record struct WespReferencedProcessIdentity(
    int ProcessId,
    long CreationTimeFileTimeUtc,
    string ProcessName,
    string? ImagePath);

internal static class WespProcessTagger
{
    private static readonly uint[] IdentityProperties =
    [
        WespRuleCompiler.ProcessIdProperty,
        WespRuleCompiler.ProcessCreateTimeProperty,
        WespRuleCompiler.ProcessImageNtPathProperty,
        WespRuleCompiler.ProcessImageDosPathProperty
    ];

    internal static void TagProcess(
        nint client,
        uint processId,
        ulong policyId,
        string processDescription)
    {
        _ = WithReferencedProcess(
            client,
            processId,
            processDescription,
            processObject =>
            {
                SetPolicyTag(processObject, policyId, processDescription);
                return true;
            });
    }

    internal static WespReferencedProcessIdentity TagExistingProcess(
        nint client,
        uint processId,
        long expectedCreationTimeFileTimeUtc,
        ulong policyId,
        string processDescription)
    {
        return WithReferencedProcess(
            client,
            processId,
            processDescription,
            processObject =>
            {
                var identity = QueryIdentity(processObject, processDescription);
                ValidateResolvedIdentity(
                    checked((int)processId),
                    expectedCreationTimeFileTimeUtc,
                    identity);
                SetPolicyTag(processObject, policyId, processDescription);
                return identity;
            });
    }

    private static T WithReferencedProcess<T>(
        nint client,
        uint processId,
        string processDescription,
        Func<nint, T> action)
    {
        Check(
            NativeMethods.EspCreateProcessReference(client, processId, out var objectReference),
            WespOperation.TagProcess,
            $"WESP could not reference {processDescription}.");
        try
        {
            Check(
                NativeMethods.EspGetEventObjectFromReference(
                    objectReference,
                    out var processObject),
                WespOperation.TagProcess,
                $"WESP could not resolve the reference for {processDescription}.");
            return action(processObject);
        }
        finally
        {
            _ = NativeMethods.EspCloseEventObjectReference(objectReference);
        }
    }

    private static unsafe WespReferencedProcessIdentity QueryIdentity(
        nint processObject,
        string processDescription)
    {
        nint properties = 0;
        try
        {
            fixed (uint* propertyIds = IdentityProperties)
            {
                Check(
                    NativeMethods.EspQueryProcessProperties(
                        processObject,
                        (uint)IdentityProperties.Length,
                        propertyIds,
                        out properties),
                    WespOperation.ReadProcessIdentity,
                    $"WESP could not query the identity of {processDescription}.");
            }

            return DecodeIdentityProperties(
                properties,
                (uint)IdentityProperties.Length);
        }
        finally
        {
            if (properties != 0)
            {
                NativeMethods.EspFreeMemory(properties);
            }
        }
    }

    private static void SetPolicyTag(
        nint processObject,
        ulong policyId,
        string processDescription)
    {
        var update = WespRuleCompiler.CreateRootTagUpdate(policyId);
        Check(
            NativeMethods.EspSetEventObjectContextKey(processObject, in update),
            WespOperation.TagProcess,
            $"WESP could not attach the blocking-policy identity to {processDescription}.");
    }

    internal static WespReferencedProcessIdentity DecodeIdentityProperties(
        nint properties,
        uint propertiesCount)
    {
        if (properties == 0 || propertiesCount == 0)
        {
            throw new WespException(
                WespOperation.ReadProcessIdentity,
                "WESP returned no process identity properties.");
        }

        long? creationTime = null;
        for (var index = 0u; index < propertiesCount; index++)
        {
            var property = Marshal.PtrToStructure<NativeProperty>(nint.Add(
                properties,
                checked((int)index * WespAbiV013.PropertySize)));
            if (property.PropertyId != WespRuleCompiler.ProcessCreateTimeProperty)
            {
                continue;
            }

            if (property.Type == EspVariantType.Error)
            {
                var hresult = unchecked((int)property.Value.ToInt64());
                throw WespException.FromHResult(
                    WespOperation.ReadProcessIdentity,
                    hresult,
                    "WESP could not read the process creation time.");
            }

            if (property.Type != EspVariantType.FileTime)
            {
                throw new WespException(
                    WespOperation.ReadProcessIdentity,
                    "WESP returned the process creation time with an unexpected property type.");
            }

            creationTime = property.Value.ToInt64();
        }

        var query = new NativePropertyQuery
        {
            PropertiesCount = propertiesCount,
            Properties = properties
        };
        var decoded = WespActivityDecoder.DecodeProcessIdentity(in query);
        if (!decoded.ProcessId.HasValue)
        {
            throw new WespException(
                WespOperation.ReadProcessIdentity,
                "WESP did not return the process ID needed to verify the target.");
        }

        if (!creationTime.HasValue)
        {
            throw new WespException(
                WespOperation.ReadProcessIdentity,
                "WESP did not return the creation time needed to verify the target.");
        }

        return new WespReferencedProcessIdentity(
            decoded.ProcessId.Value,
            creationTime.Value,
            decoded.ProcessName,
            decoded.ImagePath);
    }

    internal static void ValidateResolvedIdentity(
        int expectedProcessId,
        long expectedCreationTimeFileTimeUtc,
        WespReferencedProcessIdentity actual)
    {
        if (actual.ProcessId == expectedProcessId &&
            actual.CreationTimeFileTimeUtc == expectedCreationTimeFileTimeUtc)
        {
            return;
        }

        throw new WespException(
            WespOperation.TagProcess,
            $"WESP resolved PID {expectedProcessId} to a different process; applying WESP Blocking was refused.");
    }

    internal static IReadOnlyList<uint> GetIdentityProperties() =>
        IdentityProperties.ToArray();

    private static void Check(int hresult, WespOperation operation, string detail)
    {
        if (hresult < 0)
        {
            throw WespException.FromHResult(operation, hresult, detail);
        }
    }
}
