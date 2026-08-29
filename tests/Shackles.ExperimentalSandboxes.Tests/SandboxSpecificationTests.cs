using System.Text;
using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes.Tests;

[TestClass]
public sealed class SandboxSpecificationTests
{
    [TestMethod]
    public void SerializeWritesCompleteSboxContract()
    {
        using var directories = TestDirectories.Create(3);
        var options = new ExperimentalSandboxOptions
        {
            DisplayName = "Contract test",
            UseAppContainer = true,
            LeastPrivilege = true,
            DisallowWin32kSystemCalls = true,
            UiRestrictions =
                ExperimentalSandboxUiRestrictions.ReadClipboard |
                ExperimentalSandboxUiRestrictions.InputInjection,
            NetworkMode = ExperimentalSandboxNetworkMode.Proxy,
            ProxyUrl = "http://127.0.0.1:8080",
            CapabilityNames = ["internetClient", "registryRead"],
            FileSystemRules =
            [
                new(directories.Paths[0], ExperimentalSandboxFileAccess.ReadWrite),
                new(directories.Paths[1], ExperimentalSandboxFileAccess.ReadOnly),
                new(directories.Paths[2], ExperimentalSandboxFileAccess.Deny)
            ]
        };

        var bytes = SandboxSpecificationSerializer.Serialize(options);
        CollectionAssert.AreEqual(
            Encoding.ASCII.GetBytes("SBOX"),
            bytes[4..8]);
        var root = FlatTable.Root(bytes);
        Assert.AreEqual("0.1.0", root.String(0));
        Assert.IsTrue(root.Boolean(1));
        Assert.IsTrue(root.Boolean(3));
        Assert.AreEqual(
            (ulong)options.UiRestrictions,
            root.UnsignedLong(4));
        Assert.IsTrue(root.Boolean(5));
        Assert.AreEqual(
            "internetClient,registryRead",
            root.String(6));
        CollectionAssert.AreEqual(
            new[] { directories.Paths[0] },
            root.StringVector(7));
        CollectionAssert.AreEqual(
            new[] { directories.Paths[1] },
            root.StringVector(8));
        CollectionAssert.AreEqual(
            new[] { directories.Paths[2] },
            root.StringVector(11));
        Assert.AreEqual(
            (byte)ExperimentalSandboxIntegrityLevel.SystemDefault,
            root.Byte(10));

        var network = root.Table(9);
        Assert.IsNotNull(network);
        var proxy = network.Value.Table(0);
        Assert.IsNotNull(proxy);
        Assert.AreEqual("http://127.0.0.1:8080", proxy.Value.String(0));
        Assert.IsNull(network.Value.Table(1));
    }

    [TestMethod]
    public void SerializeWritesDirectNetworkDefaultAction()
    {
        var allowed = SandboxSpecificationSerializer.Serialize(
            new ExperimentalSandboxOptions
            {
                DisplayName = "Allowed",
                UseAppContainer = true,
                NetworkMode = ExperimentalSandboxNetworkMode.Allowed
            });
        var blocked = SandboxSpecificationSerializer.Serialize(
            new ExperimentalSandboxOptions
            {
                DisplayName = "Blocked",
                UseAppContainer = true,
                NetworkMode = ExperimentalSandboxNetworkMode.Blocked
            });

        Assert.AreEqual(
            (byte)1,
            FlatTable.Root(allowed).Table(9)!.Value.Table(1)!.Value.Byte(0));
        Assert.AreEqual(
            (byte)0,
            FlatTable.Root(blocked).Table(9)!.Value.Table(1)!.Value.Byte(0));
    }

    [TestMethod]
    public void NormalizeRejectsAppContainerOnlyPolicyWithoutAppContainer()
    {
        var options = new ExperimentalSandboxOptions
        {
            DisplayName = "Invalid",
            UseAppContainer = false,
            CapabilityNames = ["internetClient"]
        };

        Assert.ThrowsExactly<ArgumentException>(
            () => SandboxPolicyNormalizer.Normalize(options));
    }

    [TestMethod]
    public void NormalizeUsesMostRestrictiveExactPathRule()
    {
        using var directories = TestDirectories.Create(1);
        var normalized = SandboxPolicyNormalizer.Normalize(
            new ExperimentalSandboxOptions
            {
                DisplayName = "Precedence",
                FileSystemRules =
                [
                    new(directories.Paths[0], ExperimentalSandboxFileAccess.ReadWrite),
                    new(directories.Paths[0], ExperimentalSandboxFileAccess.ReadOnly),
                    new(directories.Paths[0], ExperimentalSandboxFileAccess.Deny)
                ]
            });

        Assert.HasCount(1, normalized.FileSystemRules);
        Assert.AreEqual(
            ExperimentalSandboxFileAccess.Deny,
            normalized.FileSystemRules[0].Access);
    }

    private readonly struct FlatTable
    {
        private readonly byte[] _bytes;
        private readonly int _table;

        private FlatTable(byte[] bytes, int table)
        {
            _bytes = bytes;
            _table = table;
        }

        internal static FlatTable Root(byte[] bytes) =>
            new(bytes, ReadInt(bytes, 0));

        internal bool Boolean(int slot) => Byte(slot) != 0;

        internal byte Byte(int slot)
        {
            var field = Field(slot);
            return field == 0 ? (byte)0 : _bytes[field];
        }

        internal ulong UnsignedLong(int slot)
        {
            var field = Field(slot);
            return field == 0 ? 0 : BitConverter.ToUInt64(_bytes, field);
        }

        internal string? String(int slot)
        {
            var field = Field(slot);
            if (field == 0)
            {
                return null;
            }

            var value = field + ReadInt(_bytes, field);
            var length = ReadInt(_bytes, value);
            return Encoding.UTF8.GetString(_bytes, value + sizeof(int), length);
        }

        internal string[] StringVector(int slot)
        {
            var field = Field(slot);
            if (field == 0)
            {
                return [];
            }

            var vector = field + ReadInt(_bytes, field);
            var length = ReadInt(_bytes, vector);
            var values = new string[length];
            var first = vector + sizeof(int);
            for (var index = 0; index < length; index++)
            {
                var element = first + (index * sizeof(int));
                var value = element + ReadInt(_bytes, element);
                var stringLength = ReadInt(_bytes, value);
                values[index] = Encoding.UTF8.GetString(
                    _bytes,
                    value + sizeof(int),
                    stringLength);
            }

            return values;
        }

        internal FlatTable? Table(int slot)
        {
            var field = Field(slot);
            return field == 0
                ? null
                : new FlatTable(_bytes, field + ReadInt(_bytes, field));
        }

        private int Field(int slot)
        {
            var vtable = _table - ReadInt(_bytes, _table);
            var vtableSize = BitConverter.ToUInt16(_bytes, vtable);
            var entry = vtable + 4 + (slot * sizeof(ushort));
            if (entry + sizeof(ushort) > vtable + vtableSize)
            {
                return 0;
            }

            var offset = BitConverter.ToUInt16(_bytes, entry);
            return offset == 0 ? 0 : _table + offset;
        }

        private static int ReadInt(byte[] bytes, int offset) =>
            BitConverter.ToInt32(bytes, offset);
    }

    private sealed class TestDirectories : IDisposable
    {
        private TestDirectories(string root, string[] paths)
        {
            Root = root;
            Paths = paths;
        }

        internal string Root { get; }

        internal string[] Paths { get; }

        internal static TestDirectories Create(int count)
        {
            var root = Directory.CreateTempSubdirectory(
                "Shackles.Sbox.Tests.").FullName;
            var paths = Enumerable.Range(0, count)
                .Select(index => Directory.CreateDirectory(
                    Path.Combine(root, $"path-{index}")).FullName)
                .ToArray();
            return new TestDirectories(root, paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
