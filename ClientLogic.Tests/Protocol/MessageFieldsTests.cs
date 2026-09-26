using ClientLogic.Protocol;

using Xunit;

namespace ClientLogic.Tests.Protocol;

public class MessageFieldsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("semi;colon")]
    [InlineData("100% sure")]
    [InlineData("%3B is not a semicolon")]
    [InlineData("tab\tnew\nline\u0001lan\u0002separators")]
    [InlineData("Ünïcödé ✓")]
    public void EscapedTextRoundTrips(string text)
    {
        string escaped = MessageFields.Escape(text);

        Assert.DoesNotContain(';', escaped);
        Assert.DoesNotContain('\u0001', escaped);
        Assert.DoesNotContain('\u0002', escaped);
        Assert.DoesNotContain('\n', escaped);
        Assert.True(MessageFields.TryUnescape(escaped, out string unescaped));
        Assert.Equal(text, unescaped);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("abc%4")]
    [InlineData("%ZZ")]
    public void InvalidEscapesAreRejected(string text)
    {
        Assert.False(MessageFields.TryUnescape(text, out _));
    }

    [Fact]
    public void ReaderRejectsMissingAndMalformedFields()
    {
        var reader = new MessageFieldReader("12;x;1");

        Assert.True(reader.TryReadInt(out int value));
        Assert.Equal(12, value);
        Assert.False(reader.TryReadInt(out _));
        Assert.True(reader.TryReadBool(out bool flag));
        Assert.True(flag);
        Assert.True(reader.IsAtEnd);
        Assert.False(reader.TryReadText(out _));
    }

    [Fact]
    public void PackedPlayerOptionsUseOneByteOrder()
    {
        var options = new PackedPlayerOptions(Side: 4, Color: 3, Start: 2, Team: 1);

        Assert.Equal(0x04030201, options.Pack());
        Assert.Equal(options, PackedPlayerOptions.Unpack(options.Pack()));
        Assert.Equal(new PackedPlayerOptions(255, 255, 255, 255), PackedPlayerOptions.Unpack(-1));
    }
}
