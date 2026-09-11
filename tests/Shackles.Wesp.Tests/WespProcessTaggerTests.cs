using System.Runtime.InteropServices;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespProcessTaggerTests
{
    [TestMethod]
    public void IdentityQueryRequestsStableIdentityAndDisplayPaths()
    {
        CollectionAssert.AreEqual(
            new uint[]
            {
                WespRuleCompiler.ProcessIdProperty,
                WespRuleCompiler.ProcessCreateTimeProperty,
                WespRuleCompiler.ProcessImageNtPathProperty,
                WespRuleCompiler.ProcessImageDosPathProperty
            },
            WespProcessTagger.GetIdentityProperties().ToArray());
    }

    [TestMethod]
    public void IdentityDecoderReadsCreationTimeAndPrefersDosImagePath()
    {
        const long creationTime = 134000000001234567;
        using var properties = new NativeIdentityProperties(
            processId: 4120,
            creationTime,
            imageNtPath: @"\Device\HarddiskVolume4\Program Files\Cursor\Cursor.exe",
            imageDosPath: @"C:\Program Files\Cursor\Cursor.exe");

        var identity = WespProcessTagger.DecodeIdentityProperties(
            properties.Pointer,
            properties.Count);

        Assert.AreEqual(4120, identity.ProcessId);
        Assert.AreEqual(creationTime, identity.CreationTimeFileTimeUtc);
        Assert.AreEqual("Cursor.exe", identity.ProcessName);
        Assert.AreEqual(@"C:\Program Files\Cursor\Cursor.exe", identity.ImagePath);
    }

    [TestMethod]
    public void ResolvedIdentityMustMatchPidAndCreationTime()
    {
        const long creationTime = 134000000001234567;
        var identity = new WespReferencedProcessIdentity(
            4120,
            creationTime,
            "Cursor.exe",
            @"C:\Program Files\Cursor\Cursor.exe");

        WespProcessTagger.ValidateResolvedIdentity(4120, creationTime, identity);

        var reusedPid = Assert.Throws<WespException>(() =>
            WespProcessTagger.ValidateResolvedIdentity(
                4120,
                creationTime + 1,
                identity));
        Assert.AreEqual(WespOperation.TagProcess, reusedPid.Operation);

        var wrongPid = Assert.Throws<WespException>(() =>
            WespProcessTagger.ValidateResolvedIdentity(
                4121,
                creationTime,
                identity));
        Assert.AreEqual(WespOperation.TagProcess, wrongPid.Operation);
    }

    [TestMethod]
    public void CreationTimePropertyErrorRetainsNativeFailure()
    {
        const int propertyError = unchecked((int)0xD000000B);
        using var properties = new NativeIdentityProperties(
            processId: 4120,
            creationTime: null,
            imageNtPath: null,
            imageDosPath: null,
            creationTimeError: propertyError);

        var exception = Assert.Throws<WespException>(() =>
            WespProcessTagger.DecodeIdentityProperties(
                properties.Pointer,
                properties.Count));

        Assert.AreEqual(WespOperation.ReadProcessIdentity, exception.Operation);
        Assert.AreEqual(propertyError, exception.NativeHResult);
    }

    private sealed class NativeIdentityProperties : IDisposable
    {
        private readonly List<nint> _allocations = [];

        internal NativeIdentityProperties(
            uint processId,
            long? creationTime,
            string? imageNtPath,
            string? imageDosPath,
            int? creationTimeError = null)
        {
            var properties = new List<NativeProperty>
            {
                new()
                {
                    PropertyId = WespRuleCompiler.ProcessIdProperty,
                    Type = EspVariantType.UInt32,
                    Value = (nint)processId
                },
                new()
                {
                    PropertyId = WespRuleCompiler.ProcessCreateTimeProperty,
                    Type = creationTime.HasValue
                        ? EspVariantType.FileTime
                        : EspVariantType.Error,
                    Value = creationTime.HasValue
                        ? (nint)creationTime.Value
                        : (nint)creationTimeError.GetValueOrDefault()
                }
            };
            AddStringProperty(
                properties,
                WespRuleCompiler.ProcessImageNtPathProperty,
                imageNtPath);
            AddStringProperty(
                properties,
                WespRuleCompiler.ProcessImageDosPathProperty,
                imageDosPath);

            Pointer = Marshal.AllocHGlobal(
                checked(properties.Count * WespAbiV013.PropertySize));
            _allocations.Add(Pointer);
            for (var index = 0; index < properties.Count; index++)
            {
                Marshal.StructureToPtr(
                    properties[index],
                    nint.Add(Pointer, index * WespAbiV013.PropertySize),
                    fDeleteOld: false);
            }

            Count = (uint)properties.Count;
        }

        internal nint Pointer { get; }

        internal uint Count { get; }

        public void Dispose()
        {
            foreach (var allocation in Enumerable.Reverse(_allocations))
            {
                Marshal.FreeHGlobal(allocation);
            }
        }

        private void AddStringProperty(
            List<NativeProperty> properties,
            uint propertyId,
            string? value)
        {
            if (value is null)
            {
                return;
            }

            var buffer = Marshal.StringToHGlobalUni(value);
            _allocations.Add(buffer);
            var nativeString = Marshal.AllocHGlobal(Marshal.SizeOf<NativeUnicodeString>());
            _allocations.Add(nativeString);
            Marshal.StructureToPtr(
                new NativeUnicodeString
                {
                    LengthBytes = checked((ushort)(value.Length * sizeof(char))),
                    Buffer = buffer
                },
                nativeString,
                fDeleteOld: false);
            properties.Add(new NativeProperty
            {
                PropertyId = propertyId,
                Type = EspVariantType.UnicodeString,
                Value = nativeString
            });
        }
    }
}
