using DemoAgencia.Worker.IA;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA;

public class ImageFormatHelperTests
{
    [Fact]
    public void DetectarExtensao_Png_ShouldReturnPng()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        ImageFormatHelper.DetectarExtensao(bytes).Should().Be(".png");
    }

    [Fact]
    public void DetectarExtensao_Jpeg_ShouldReturnJpg()
    {
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        ImageFormatHelper.DetectarExtensao(bytes).Should().Be(".jpg");
    }

    [Fact]
    public void DetectarExtensao_Gif87a_ShouldReturnGif()
    {
        var bytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 };
        ImageFormatHelper.DetectarExtensao(bytes).Should().Be(".gif");
    }

    [Fact]
    public void DetectarExtensao_Gif89a_ShouldReturnGif()
    {
        var bytes = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 };
        ImageFormatHelper.DetectarExtensao(bytes).Should().Be(".gif");
    }

    [Fact]
    public void DetectarExtensao_Webp_ShouldReturnWebp()
    {
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };
        ImageFormatHelper.DetectarExtensao(bytes).Should().Be(".webp");
    }

    [Fact]
    public void DetectarExtensao_Unknown_ShouldReturnPng()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        ImageFormatHelper.DetectarExtensao(bytes).Should().Be(".png");
    }
}
