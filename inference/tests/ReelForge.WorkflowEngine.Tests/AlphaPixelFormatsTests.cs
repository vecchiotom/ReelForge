using FluentAssertions;
using ReelForge.WorkflowEngine.Services.Video;
using Xunit;

namespace ReelForge.WorkflowEngine.Tests;

public class AlphaPixelFormatsTests
{
    [Theory]
    [InlineData("yuva420p")]
    [InlineData("yuva444p10le")]
    [InlineData("yuva422p")]
    [InlineData("rgba")]
    [InlineData("bgra")]
    [InlineData("argb")]
    [InlineData("gbrap")]
    [InlineData("ya8")]
    [InlineData("YUVA420P")]
    public void HasAlpha_recognizes_known_alpha_carrying_formats(string pixFmt)
    {
        AlphaPixelFormats.HasAlpha(pixFmt).Should().BeTrue();
    }

    [Theory]
    [InlineData("yuv420p")]
    [InlineData("yuv444p")]
    [InlineData("h264")]
    [InlineData("nv12")]
    [InlineData("")]
    [InlineData(null)]
    public void HasAlpha_rejects_opaque_or_unknown_formats(string? pixFmt)
    {
        AlphaPixelFormats.HasAlpha(pixFmt).Should().BeFalse();
    }
}
