using Google.FlatBuffers;

namespace Shackles.ExperimentalSandboxes.Internal;

internal static class SandboxSpecificationSerializer
{
    internal const string SchemaVersion = "0.1.0";

    internal static byte[] Serialize(ExperimentalSandboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            var builder = new FlatBufferBuilder(1024);
            var version = builder.CreateString(SchemaVersion);
            var capabilityList = options.CapabilityNames.Count == 0
                ? default
                : builder.CreateString(string.Join(',', options.CapabilityNames));
            var readWrite = CreateStringVector(
                builder,
                options.FileSystemRules
                    .Where(rule =>
                        rule.Access == ExperimentalSandboxFileAccess.ReadWrite)
                    .Select(rule => rule.Path));
            var readOnly = CreateStringVector(
                builder,
                options.FileSystemRules
                    .Where(rule =>
                        rule.Access == ExperimentalSandboxFileAccess.ReadOnly)
                    .Select(rule => rule.Path));
            var denied = CreateStringVector(
                builder,
                options.FileSystemRules
                    .Where(rule => rule.Access == ExperimentalSandboxFileAccess.Deny)
                    .Select(rule => rule.Path));
            var networkPolicy = options.UseAppContainer
                ? CreateNetworkPolicy(builder, options)
                : 0;

            builder.StartTable(12);
            builder.AddOffset(0, version.Value, 0);
            builder.AddBool(1, options.UseAppContainer, false);
            builder.AddBool(3, options.DisallowWin32kSystemCalls, false);
            builder.AddUlong(4, (ulong)options.UiRestrictions, 0);
            builder.AddBool(5, options.LeastPrivilege, false);
            if (capabilityList.Value != 0)
            {
                builder.AddOffset(6, capabilityList.Value, 0);
            }

            if (readWrite.Value != 0)
            {
                builder.AddOffset(7, readWrite.Value, 0);
            }

            if (readOnly.Value != 0)
            {
                builder.AddOffset(8, readOnly.Value, 0);
            }

            if (networkPolicy != 0)
            {
                builder.AddOffset(9, networkPolicy, 0);
            }

            builder.AddByte(10, (byte)options.IntegrityLevel, 0);
            if (denied.Value != 0)
            {
                builder.AddOffset(11, denied.Value, 0);
            }

            var root = builder.EndTable();
            builder.Finish(root, "SBOX");
            return builder.SizedByteArray();
        }
        catch (ExperimentalSandboxException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ExperimentalSandboxException(
                ExperimentalSandboxOperation.SerializePolicy,
                "Could not encode the SBOX 0.1.0 policy buffer.",
                innerException: exception);
        }
    }

    private static VectorOffset CreateStringVector(
        FlatBufferBuilder builder,
        IEnumerable<string> values)
    {
        var offsets = values.Select(builder.CreateString).ToArray();
        if (offsets.Length == 0)
        {
            return default;
        }

        builder.StartVector(sizeof(int), offsets.Length, sizeof(int));
        for (var index = offsets.Length - 1; index >= 0; index--)
        {
            builder.AddOffset(offsets[index].Value);
        }

        return builder.EndVector();
    }

    private static int CreateNetworkPolicy(
        FlatBufferBuilder builder,
        ExperimentalSandboxOptions options)
    {
        var proxy = 0;
        var egress = 0;
        if (options.NetworkMode == ExperimentalSandboxNetworkMode.Proxy)
        {
            var url = builder.CreateString(options.ProxyUrl!);
            builder.StartTable(1);
            builder.AddOffset(0, url.Value, 0);
            proxy = builder.EndTable();
        }
        else
        {
            builder.StartTable(3);
            builder.AddByte(
                0,
                options.NetworkMode == ExperimentalSandboxNetworkMode.Allowed
                    ? (byte)1
                    : (byte)0,
                0);
            egress = builder.EndTable();
        }

        builder.StartTable(3);
        if (proxy != 0)
        {
            builder.AddOffset(0, proxy, 0);
        }

        if (egress != 0)
        {
            builder.AddOffset(1, egress, 0);
        }

        return builder.EndTable();
    }
}
