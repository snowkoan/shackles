using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Shackles.Wfp.Internal;
using Shackles.Wfp.Interop;

namespace Shackles.Wfp;

public sealed class WfpSession : IDisposable
{
    private const ushort SubLayerWeight = 0x5000;
    private readonly object _gate = new();
    private readonly List<WfpInstalledRule> _rules = [];
    private readonly Func<nint, uint> _closeEngine;
    private readonly Func<ICollection<string>, bool>? _removeAllObjects;
    private nint _engine;
    private bool _closed;

    private WfpSession(Guid sessionKey, nint engine)
        : this(sessionKey, engine, NativeMethods.FwpmEngineClose, null)
    {
    }

    internal WfpSession(Guid sessionKey, nint engine, Func<nint, uint> closeEngine,
        Func<ICollection<string>, bool>? removeAllObjects)
    {
        SessionKey = sessionKey;
        _engine = engine;
        _closeEngine = closeEngine;
        _removeAllObjects = removeAllObjects;
    }

    public Guid SessionKey { get; }

    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    public int RuleCount
    {
        get
        {
            lock (_gate)
            {
                return _rules.Count;
            }
        }
    }

    public static WfpSession Open()
    {
        WfpProcessElevation.EnsureHighIntegrity();

        var sessionKey = Guid.NewGuid();
        using var memory = new UnmanagedMemoryScope();
        var nativeSession = new NativeSession
        {
            SessionKey = sessionKey,
            DisplayData = new NativeDisplayData
            {
                Name = memory.AllocateString($"Shackles WFP / {sessionKey:N}"),
                Description = memory.AllocateString(
                    "Dynamic user-mode WFP policy owned by the Shackles WFP workspace.")
            },
            Flags = WfpNativeConstants.DynamicSession,
            TransactionWaitTimeoutMilliseconds = 5000
        };

        Check(
            NativeMethods.FwpmEngineOpen(
                null,
                WfpNativeConstants.RpcAuthenticationWinNt,
                0,
                in nativeSession,
                out var engine),
            WfpOperation.OpenEngine,
            "Shackles could not open a dynamic Windows Filtering Platform session");

        try
        {
            InstallInfrastructure(engine, sessionKey);
            return new WfpSession(sessionKey, engine);
        }
        catch
        {
            _ = NativeMethods.FwpmEngineClose(engine);
            throw;
        }
    }

    public IReadOnlyList<WfpInstalledRule> GetRules()
    {
        lock (_gate)
        {
            return _rules.ToArray();
        }
    }

    public static WfpBlockRuleOptions ValidateBlockRule(WfpBlockRuleOptions options) =>
        WfpRuleNormalizer.Normalize(options).ToOptions();

    public WfpInstalledRule AddBlockRule(WfpBlockRuleOptions options)
    {
        var normalized = WfpRuleNormalizer.Normalize(options);
        var plans = WfpFilterPlanBuilder.Create(normalized);
        var ruleKey = Guid.NewGuid();
        var ruleName = $"Shackles WFP / {Path.GetFileName(normalized.ExecutablePath)} / {ruleKey:N}";

        lock (_gate)
        {
            ThrowIfClosed();
            nint appId = 0;
            try
            {
                Check(
                    NativeMethods.FwpmGetAppIdFromFileName(
                        normalized.ExecutablePath,
                        out appId),
                    WfpOperation.AddRule,
                    $"Windows could not derive a WFP application ID from '{normalized.ExecutablePath}'");

                BeginTransaction(WfpOperation.AddRule);
                var transactionOpen = true;
                try
                {
                    var installedFilters = new List<WfpInstalledFilter>(plans.Count);
                    foreach (var plan in plans)
                    {
                        installedFilters.Add(AddFilter(
                            normalized,
                            plan,
                            appId,
                            ruleKey,
                            ruleName));
                    }

                    Check(
                        NativeMethods.FwpmTransactionCommit(_engine),
                        WfpOperation.AddRule,
                        "WFP could not commit the complete block rule");
                    transactionOpen = false;

                    var installedRule = new WfpInstalledRule(
                        ruleKey,
                        ruleName,
                        DateTimeOffset.Now,
                        normalized.ToOptions(),
                        installedFilters);
                    _rules.Add(installedRule);
                    return installedRule;
                }
                catch
                {
                    if (transactionOpen)
                    {
                        _ = NativeMethods.FwpmTransactionAbort(_engine);
                    }

                    throw;
                }
            }
            finally
            {
                if (appId != 0)
                {
                    NativeMethods.FwpmFreeMemory(ref appId);
                }
            }
        }
    }

    public bool RemoveRule(Guid ruleKey)
    {
        lock (_gate)
        {
            ThrowIfClosed();
            var rule = _rules.FirstOrDefault(item => item.RuleKey == ruleKey);
            if (rule is null)
            {
                return false;
            }

            BeginTransaction(WfpOperation.RemoveRule);
            var transactionOpen = true;
            try
            {
                foreach (var filter in rule.Filters)
                {
                    var filterKey = filter.FilterKey;
                    var result = NativeMethods.FwpmFilterDeleteByKey(
                        _engine,
                        in filterKey);
                    if (result != 0 && result != WfpNativeConstants.FwpFilterNotFound)
                    {
                        throw WfpException.FromNativeError(
                            WfpOperation.RemoveRule,
                            result,
                            $"WFP could not remove '{filter.DisplayName}'");
                    }
                }

                Check(
                    NativeMethods.FwpmTransactionCommit(_engine),
                    WfpOperation.RemoveRule,
                    "WFP could not commit block-rule removal");
                transactionOpen = false;
                _rules.Remove(rule);
                return true;
            }
            catch
            {
                if (transactionOpen)
                {
                    _ = NativeMethods.FwpmTransactionAbort(_engine);
                }

                throw;
            }
        }
    }

    public WfpCloseResult Close()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return new WfpCloseResult(0, 0, true, true, []);
            }

            var ruleCount = _rules.Count;
            var filterCount = _rules.Sum(rule => rule.Filters.Count);
            var warnings = new List<string>();
            var explicitRemovalSucceeded = _removeAllObjects?.Invoke(warnings) ?? TryRemoveAllObjects(warnings);
            var closeResult = _closeEngine(_engine);
            if (closeResult == 0)
            {
                _engine = 0;
                _closed = true;
                _rules.Clear();
            }
            else
            {
                warnings.Add(WfpException.FromNativeError(
                    WfpOperation.CloseSession,
                    closeResult,
                    "WFP did not confirm that the dynamic Shackles session closed").Message);
                if (explicitRemovalSucceeded)
                {
                    _rules.Clear();
                }
            }

            return new WfpCloseResult(
                explicitRemovalSucceeded ? ruleCount : 0,
                explicitRemovalSucceeded ? filterCount : 0,
                explicitRemovalSucceeded,
                _closed,
                warnings);
        }
    }

    public void Dispose()
    {
        // A failed native close leaves ownership here so Close/Dispose can retry it.
        // Close exposes the diagnostics to interactive callers.
        _ = Close();
    }

    private static void InstallInfrastructure(nint engine, Guid sessionKey)
    {
        using var memory = new UnmanagedMemoryScope();
        var providerDataBytes = CreateProviderData(sessionKey, null);
        var providerData = new NativeByteBlob
        {
            Size = checked((uint)providerDataBytes.Length),
            Data = memory.AllocateBytes(providerDataBytes)
        };
        var provider = new NativeProvider
        {
            ProviderKey = WfpNativeConstants.ProviderKey,
            DisplayData = new NativeDisplayData
            {
                Name = memory.AllocateString($"Shackles WFP provider / {sessionKey:N}"),
                Description = memory.AllocateString(
                    "Dynamic Shackles provider; BFE removes it if Shackles exits unexpectedly.")
            },
            ProviderData = providerData
        };
        var providerKeyPointer = memory.Allocate(WfpNativeConstants.ProviderKey);
        var subLayer = new NativeSubLayer
        {
            SubLayerKey = WfpNativeConstants.SubLayerKey,
            DisplayData = new NativeDisplayData
            {
                Name = memory.AllocateString($"Shackles WFP sublayer / {sessionKey:N}"),
                Description = memory.AllocateString(
                    "Private dynamic sublayer for Shackles executable network blocks.")
            },
            ProviderKey = providerKeyPointer,
            ProviderData = providerData,
            Weight = SubLayerWeight
        };

        Check(
            NativeMethods.FwpmTransactionBegin(engine, 0),
            WfpOperation.InstallInfrastructure,
            "WFP could not begin the Shackles infrastructure transaction");
        var transactionOpen = true;
        try
        {
            var providerResult = NativeMethods.FwpmProviderAdd(engine, in provider, 0);
            if (providerResult == WfpNativeConstants.FwpAlreadyExists)
            {
                throw new WfpException(
                    WfpOperation.InstallInfrastructure,
                    "Another Shackles WFP workspace is already active. Close it before starting a second WFP session.",
                    providerResult);
            }

            Check(
                providerResult,
                WfpOperation.InstallInfrastructure,
                "WFP could not add the Shackles provider");
            Check(
                NativeMethods.FwpmSubLayerAdd(engine, in subLayer, 0),
                WfpOperation.InstallInfrastructure,
                "WFP could not add the private Shackles sublayer");
            Check(
                NativeMethods.FwpmTransactionCommit(engine),
                WfpOperation.InstallInfrastructure,
                "WFP could not commit the Shackles provider and sublayer");
            transactionOpen = false;
        }
        catch
        {
            if (transactionOpen)
            {
                _ = NativeMethods.FwpmTransactionAbort(engine);
            }

            throw;
        }
    }

    private WfpInstalledFilter AddFilter(
        NormalizedWfpRule rule,
        WfpFilterPlan plan,
        nint appId,
        Guid ruleKey,
        string ruleName)
    {
        using var memory = new UnmanagedMemoryScope();
        var conditions = new List<NativeFilterCondition>
        {
            CreateCondition(
                WfpNativeConstants.ConditionAleAppId,
                NativeConditionValue.Pointer(FwpDataType.ByteBlob, appId))
        };

        if (rule.CurrentUserOnly)
        {
            conditions.Add(CreateCurrentUserCondition(memory));
        }

        AddNetworkCondition(
            conditions,
            memory,
            WfpNativeConstants.ConditionLocalAddress,
            rule.LocalNetwork,
            plan);
        AddNetworkCondition(
            conditions,
            memory,
            WfpNativeConstants.ConditionRemoteAddress,
            rule.RemoteNetwork,
            plan);
        if (plan.MatchesIpv4Mapped &&
            rule.LocalNetwork is null &&
            rule.RemoteNetwork is null)
        {
            AddNetworkCondition(
                conditions,
                memory,
                WfpNativeConstants.ConditionRemoteAddress,
                WfpRuleNormalizer.ParseNetwork("0.0.0.0/0", "remote")!,
                plan);
        }
        else if (plan.Ipv6Partition != Ipv6MappedExclusionPartition.None)
        {
            AddIpv6MappedBoundaryCondition(
                conditions,
                memory,
                plan.Ipv6Partition);
        }

        if (plan.IpProtocol is { } protocol)
        {
            conditions.Add(CreateCondition(
                WfpNativeConstants.ConditionProtocol,
                NativeConditionValue.UInt8(protocol)));
        }

        if (rule.LocalPort is { } localPort)
        {
            conditions.Add(CreateCondition(
                WfpNativeConstants.ConditionLocalPort,
                NativeConditionValue.UInt16(localPort)));
        }

        if (rule.RemotePort is { } remotePort)
        {
            conditions.Add(CreateCondition(
                WfpNativeConstants.ConditionRemotePort,
                NativeConditionValue.UInt16(remotePort)));
        }

        if (rule.InterfaceLuid is { } interfaceLuid)
        {
            conditions.Add(CreateCondition(
                WfpNativeConstants.ConditionLocalInterface,
                NativeConditionValue.Pointer(
                    FwpDataType.UInt64,
                    memory.Allocate(interfaceLuid))));
        }

        var filterKey = Guid.NewGuid();
        var filterName = CreateFilterDisplayName(
            SessionKey,
            ruleKey,
            plan.LayerLabel,
            filterKey);
        var providerDataBytes = CreateProviderData(SessionKey, ruleKey);
        var filter = new NativeFilter
        {
            FilterKey = filterKey,
            DisplayData = new NativeDisplayData
            {
                Name = memory.AllocateString(filterName),
                Description = memory.AllocateString(BuildDescription(rule, plan))
            },
            ProviderKey = memory.Allocate(WfpNativeConstants.ProviderKey),
            ProviderData = new NativeByteBlob
            {
                Size = checked((uint)providerDataBytes.Length),
                Data = memory.AllocateBytes(providerDataBytes)
            },
            LayerKey = plan.LayerKey,
            SubLayerKey = WfpNativeConstants.SubLayerKey,
            Weight = NativeValue.Empty,
            ConditionCount = checked((uint)conditions.Count),
            Conditions = memory.AllocateArray(conditions),
            Action = new NativeAction { Type = WfpNativeConstants.ActionBlock }
        };

        Check(
            NativeMethods.FwpmFilterAdd(_engine, in filter, 0, out var filterId),
            WfpOperation.AddRule,
            $"WFP rejected the {plan.LayerLabel} filter for '{rule.ExecutablePath}'");
        return new WfpInstalledFilter(
            filterKey,
            filterId,
            filterName,
            plan.Direction,
            plan.IpVersion);
    }

    private static NativeFilterCondition CreateCurrentUserCondition(
        UnmanagedMemoryScope memory)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ??
            throw new WfpException(
                WfpOperation.AddRule,
                "Windows did not report a user SID for the current Shackles process.");
        var sddl = $"D:(A;;0x{WfpNativeConstants.FwpActrlMatchFilter:X8};;;{sid})";
        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                WfpNativeConstants.SecurityDescriptorRevision,
                out var securityDescriptor,
                out var securityDescriptorSize))
        {
            throw WfpException.FromNativeError(
                WfpOperation.AddRule,
                checked((uint)Marshal.GetLastWin32Error()),
                "Windows could not build the current-user WFP match descriptor");
        }

        memory.TrackLocalAllocation(securityDescriptor);
        var blobPointer = memory.Allocate(new NativeByteBlob
        {
            Size = securityDescriptorSize,
            Data = securityDescriptor
        });
        return CreateCondition(
            WfpNativeConstants.ConditionAleUserId,
            NativeConditionValue.Pointer(FwpDataType.SecurityDescriptor, blobPointer));
    }

    private static void AddNetworkCondition(
        List<NativeFilterCondition> conditions,
        UnmanagedMemoryScope memory,
        Guid fieldKey,
        ParsedNetwork? network,
        WfpFilterPlan plan,
        FwpMatchType matchType = FwpMatchType.Equal)
    {
        if (network is null)
        {
            return;
        }

        if (plan.MatchesIpv4Mapped)
        {
            network = network.ToIpv4MappedIpv6();
        }

        if (plan.LayerIpVersion == WfpIpVersion.Ipv4)
        {
            var address = new NativeV4AddressAndMask
            {
                Address = network.GetIpv4AddressHostOrder(),
                Mask = network.GetIpv4MaskHostOrder()
            };
            conditions.Add(CreateCondition(
                fieldKey,
                NativeConditionValue.Pointer(
                    FwpDataType.V4AddressMask,
                    memory.Allocate(address)),
                matchType));
        }
        else
        {
            var address = NativeV6AddressAndMask.Create(
                network.NetworkBytes,
                network.PrefixLength);
            conditions.Add(CreateCondition(
                fieldKey,
                NativeConditionValue.Pointer(
                    FwpDataType.V6AddressMask,
                    memory.Allocate(address)),
                matchType));
        }
    }

    private static void AddIpv6MappedBoundaryCondition(
        List<NativeFilterCondition> conditions,
        UnmanagedMemoryScope memory,
        Ipv6MappedExclusionPartition partition)
    {
        var (boundaryBytes, matchType) =
            WfpFilterPlanBuilder.GetIpv6MappedBoundary(partition);
        var boundary = NativeByteArray16.Create(boundaryBytes);
        conditions.Add(CreateCondition(
            WfpNativeConstants.ConditionRemoteAddress,
            NativeConditionValue.Pointer(
                FwpDataType.ByteArray16,
                memory.Allocate(boundary)),
            matchType));
    }

    private static NativeFilterCondition CreateCondition(
        Guid fieldKey,
        NativeConditionValue value,
        FwpMatchType matchType = FwpMatchType.Equal) => new()
    {
        FieldKey = fieldKey,
        MatchType = matchType,
        ConditionValue = value
    };

    private bool TryRemoveAllObjects(List<string> warnings)
    {
        var beginResult = NativeMethods.FwpmTransactionBegin(_engine, 0);
        if (beginResult != 0)
        {
            warnings.Add(WfpException.FromNativeError(
                WfpOperation.CloseSession,
                beginResult,
                "WFP could not begin explicit Shackles cleanup").Message);
            return false;
        }

        try
        {
            foreach (var filter in _rules.SelectMany(rule => rule.Filters))
            {
                var filterKey = filter.FilterKey;
                var result = NativeMethods.FwpmFilterDeleteByKey(
                    _engine,
                    in filterKey);
                if (result != 0 && result != WfpNativeConstants.FwpFilterNotFound)
                {
                    throw WfpException.FromNativeError(
                        WfpOperation.CloseSession,
                        result,
                        $"WFP could not explicitly delete '{filter.DisplayName}'");
                }
            }

            var subLayerKey = WfpNativeConstants.SubLayerKey;
            var subLayerResult = NativeMethods.FwpmSubLayerDeleteByKey(
                _engine,
                in subLayerKey);
            if (subLayerResult != 0 &&
                subLayerResult != WfpNativeConstants.FwpSubLayerNotFound)
            {
                throw WfpException.FromNativeError(
                    WfpOperation.CloseSession,
                    subLayerResult,
                    "WFP could not explicitly delete the Shackles sublayer");
            }

            var providerKey = WfpNativeConstants.ProviderKey;
            var providerResult = NativeMethods.FwpmProviderDeleteByKey(
                _engine,
                in providerKey);
            if (providerResult != 0 &&
                providerResult != WfpNativeConstants.FwpProviderNotFound)
            {
                throw WfpException.FromNativeError(
                    WfpOperation.CloseSession,
                    providerResult,
                    "WFP could not explicitly delete the Shackles provider");
            }

            var commitResult = NativeMethods.FwpmTransactionCommit(_engine);
            if (commitResult != 0)
            {
                throw WfpException.FromNativeError(
                    WfpOperation.CloseSession,
                    commitResult,
                    "WFP could not commit explicit Shackles cleanup");
            }

            return true;
        }
        catch (WfpException exception)
        {
            _ = NativeMethods.FwpmTransactionAbort(_engine);
            warnings.Add(
                $"{exception.Message} Closing the dynamic engine session will ask BFE to remove the same objects automatically.");
            return false;
        }
    }

    private void BeginTransaction(WfpOperation operation)
    {
        Check(
            NativeMethods.FwpmTransactionBegin(_engine, 0),
            operation,
            "WFP could not begin a policy transaction");
    }

    internal static string CreateFilterDisplayName(
        Guid sessionKey,
        Guid ruleKey,
        string layerLabel,
        Guid filterKey) =>
        $"Shackles/WFP/{sessionKey:N}/{ruleKey:N}/{layerLabel}/{filterKey:N}";

    internal static byte[] CreateProviderData(Guid sessionKey, Guid? ruleKey)
    {
        var magic = Encoding.ASCII.GetBytes("SHWFP001");
        var data = new byte[magic.Length + 16 + (ruleKey.HasValue ? 16 : 0)];
        magic.CopyTo(data, 0);
        sessionKey.TryWriteBytes(data.AsSpan(magic.Length, 16));
        if (ruleKey is { } key)
        {
            key.TryWriteBytes(data.AsSpan(magic.Length + 16, 16));
        }

        return data;
    }

    private static string BuildDescription(
        NormalizedWfpRule rule,
        WfpFilterPlan plan)
    {
        var scope = new List<string>();
        if (rule.LocalNetwork is not null)
        {
            scope.Add($"local {rule.LocalNetwork.CanonicalText}");
        }

        if (rule.RemoteNetwork is not null)
        {
            scope.Add($"remote {rule.RemoteNetwork.CanonicalText}");
        }

        if (rule.InterfaceLuid is not null)
        {
            scope.Add($"interface {rule.InterfaceName ?? $"LUID 0x{rule.InterfaceLuid:X}"}");
        }

        if (rule.LocalPort is not null)
        {
            scope.Add($"local port {rule.LocalPort}");
        }

        if (rule.RemotePort is not null)
        {
            scope.Add($"remote port {rule.RemotePort}");
        }

        var versionDescription = plan.MatchesIpv4Mapped
            ? "IPv4 traffic classified at the V6 ALE layer with IPv4-mapped addresses"
            : plan.Ipv6Partition != Ipv6MappedExclusionPartition.None
                ? $"native IPv6 traffic ({plan.Ipv6Partition})"
                : plan.IpVersion.ToString();
        return $"Block {plan.Direction.ToString().ToLowerInvariant()} {versionDescription} " +
               $"{rule.Protocol} traffic for {rule.ExecutablePath}; " +
               (scope.Count == 0 ? "all addresses" : string.Join(", ", scope)) +
               (rule.CurrentUserOnly ? "; current user only" : "; all users") +
               ". Dynamic: removed if Shackles exits.";
    }

    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(_closed, this);

    private static void Check(uint result, WfpOperation operation, string detail)
    {
        if (result != 0)
        {
            throw WfpException.FromNativeError(operation, result, detail);
        }
    }
}
