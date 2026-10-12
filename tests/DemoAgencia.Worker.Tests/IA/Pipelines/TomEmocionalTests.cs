using DemoAgencia.Worker.IA.Pipelines;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.Pipelines;

public class TomEmocionalTests
{
    [Fact]
    public void Verbal_WhenEmocao_ShouldReturnEmotionalInstruction()
    {
        var result = TomEmocional.Verbal("E");
        result.Should().Contain("emocional", "instrução deve priorizar apelo emocional");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Verbal_WhenRazao_ShouldReturnRationalInstruction()
    {
        var result = TomEmocional.Verbal("R");
        result.Should().Contain("racional", "instrução deve priorizar clareza racional");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Verbal_WhenEquilibrio_ShouldReturnBalancedInstruction()
    {
        var result = TomEmocional.Verbal("ER");
        result.Should().Contain("equilíbrio", "instrução deve balancear emoção e razão");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Verbal_WhenNull_ShouldReturnEmpty()
    {
        var result = TomEmocional.Verbal(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Verbal_WhenEmpty_ShouldReturnEmpty()
    {
        var result = TomEmocional.Verbal("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Verbal_WhenUnknown_ShouldReturnEmpty()
    {
        var result = TomEmocional.Verbal("XYZ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Visual_WhenEmocao_ShouldReturnInspirationalInstruction()
    {
        var result = TomEmocional.Visual("E");
        result.Should().Contain("inspirador", "instrução visual deve ser inspiradora");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Visual_WhenRazao_ShouldReturnCleanInstruction()
    {
        var result = TomEmocional.Visual("R");
        result.Should().Contain("informativo", "instrução visual deve ser informativa");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Visual_WhenEquilibrio_ShouldReturnBalancedInstruction()
    {
        var result = TomEmocional.Visual("ER");
        result.Should().Contain("equilíbrio", "instrução visual deve balancear");
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Visual_WhenNull_ShouldReturnEmpty()
    {
        var result = TomEmocional.Visual(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Visual_WhenEmpty_ShouldReturnEmpty()
    {
        var result = TomEmocional.Visual("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Visual_WhenUnknown_ShouldReturnEmpty()
    {
        var result = TomEmocional.Visual("XYZ");
        result.Should().BeEmpty();
    }
}
