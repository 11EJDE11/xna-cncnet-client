using System;

using ClientLogic.UI;

using Xunit;

namespace ClientLogic.Tests.UI;

public class ChatColorTests
{
    [Fact]
    public void ParsesRgbAndRgba()
    {
        Assert.Equal(new ChatColor(12, 34, 56), ChatColor.Parse("12,34,56"));
        Assert.Equal(new ChatColor(12, 34, 56, 78), ChatColor.Parse("12,34,56,78"));
        Assert.Equal(255, ChatColor.Parse("0,0,0").A);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1,2")]
    [InlineData("1,2,300")]
    [InlineData("a,b,c")]
    public void InvalidStringsThrowFormatException(string value) =>
        Assert.Throws<FormatException>(() => ChatColor.Parse(value));
}
