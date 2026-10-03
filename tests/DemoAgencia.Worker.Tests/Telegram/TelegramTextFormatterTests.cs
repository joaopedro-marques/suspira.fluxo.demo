using DemoAgencia.Worker.Telegram;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.Telegram;

public class TelegramTextFormatterTests
{
    [Fact]
    public void RemoverFormatacao_WithNullInput_ShouldReturnNull()
    {
        var result = TelegramTextFormatter.RemoverFormatacao(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void RemoverFormatacao_WithEmptyInput_ShouldReturnEmpty()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void RemoverFormatacao_WithBoldDoubleAsterisk_ShouldRemoveFormatting()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("Texto **negrito** aqui");
        result.Should().Be("Texto negrito aqui");
    }

    [Fact]
    public void RemoverFormatacao_WithBoldSingleAsterisk_ShouldRemoveFormatting()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("Texto *negrito* aqui");
        result.Should().Be("Texto negrito aqui");
    }

    [Fact]
    public void RemoverFormatacao_WithItalicUnderscore_ShouldRemoveFormatting()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("Texto _italico_ aqui");
        result.Should().Be("Texto italico aqui");
    }

    [Fact]
    public void RemoverFormatacao_WithStrikethrough_ShouldRemoveFormatting()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("Texto ~tachado~ aqui");
        result.Should().Be("Texto tachado aqui");
    }

    [Fact]
    public void RemoverFormatacao_WithMonospace_ShouldRemoveFormatting()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("Texto `codigo` aqui");
        result.Should().Be("Texto codigo aqui");
    }

    [Fact]
    public void RemoverFormatacao_WithMultipleFormats_ShouldRemoveAll()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("**negrito** e _italico_ e ~tachado~");
        result.Should().Be("negrito e italico e tachado");
    }

    [Fact]
    public void RemoverFormatacao_WithPlainText_ShouldReturnUnchanged()
    {
        var result = TelegramTextFormatter.RemoverFormatacao("Texto simples sem formatacao");
        result.Should().Be("Texto simples sem formatacao");
    }
}
