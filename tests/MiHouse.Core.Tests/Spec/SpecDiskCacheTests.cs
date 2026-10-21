using MiHouse.Core.Spec;

namespace MiHouse.Core.Tests.Spec;

public sealed class SpecDiskCacheTests : IDisposable
{
    private const string _urn = "urn:miot-spec-v2:device:light:0000A001:test-one:1";
    private const string _json = """{ "type": "urn:miot-spec-v2:device:light:0000A001:test-one:1" }""";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "MiHouse.Core.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryRead_ReturnsContentWrittenForSameKey()
    {
        var cache = new SpecDiskCache(_directory);

        cache.Write(_urn, _json);

        Assert.Equal(_json, cache.TryRead(_urn));
    }

    [Fact]
    public void Write_KeyWithUrnCharacters_UsesHashedFileName()
    {
        var cache = new SpecDiskCache(_directory);

        cache.Write(_urn, _json);

        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(file));
        Assert.Equal(_json, File.ReadAllText(file));
    }

    [Fact]
    public void Write_SameKeyTwice_ReplacesContentAtomically()
    {
        var cache = new SpecDiskCache(_directory);

        cache.Write(_urn, "first");
        cache.Write(_urn, "second");

        Assert.Equal("second", cache.TryRead(_urn));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void TryRead_MissingDirectory_ReturnsNull()
    {
        var cache = new SpecDiskCache(Path.Combine(_directory, "missing"));

        Assert.Null(cache.TryRead(_urn));
    }

    [Fact]
    public void TryRead_LockedFile_ReturnsNull()
    {
        var cache = new SpecDiskCache(_directory);
        cache.Write(_urn, _json);
        var file = Assert.Single(Directory.GetFiles(_directory));
        using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Null(cache.TryRead(_urn));
    }

    [Fact]
    public void Write_UnusableDirectory_DoesNotThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "blocked");
        var cache = new SpecDiskCache(_directory);

        cache.Write(_urn, _json);

        Assert.Null(cache.TryRead(_urn));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
        else if (File.Exists(_directory))
        {
            File.Delete(_directory);
        }
    }
}
