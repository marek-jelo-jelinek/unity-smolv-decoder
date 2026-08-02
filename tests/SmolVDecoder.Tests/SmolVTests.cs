namespace SmolVDecoder.Tests;

// Error-path smoke tests only.
public class SmolVTests
{
    [Fact]
    public void TryDecodeStages_EmptyInput_ReturnsFalseWithError()
    {
        var result = SmolV.TryDecodeStages(Array.Empty<byte>(), out var vertexSpirv, out var fragmentSpirv, out var error);

        Assert.False(result);
        Assert.Null(vertexSpirv);
        Assert.Null(fragmentSpirv);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryDecodeStages_MissingSmolMagic_ReturnsFalseWithError()
    {
        var data = new byte[32]; // all zeros - no "SMOL" magic anywhere

        var result = SmolV.TryDecodeStages(data, out var vertexSpirv, out var fragmentSpirv, out var error);

        Assert.False(result);
        Assert.Null(vertexSpirv);
        Assert.Null(fragmentSpirv);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
