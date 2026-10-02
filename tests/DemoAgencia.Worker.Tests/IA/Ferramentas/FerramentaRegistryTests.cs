using DemoAgencia.Worker.IA.Ferramentas;
using FluentAssertions;
using Moq;

namespace DemoAgencia.Worker.Tests.IA.Ferramentas;

public class FerramentaRegistryTests
{
    [Fact]
    public void Registrar_ShouldMakeToolRetrievable()
    {
        var registry = new FerramentaRegistry();
        var ferramenta = new Mock<IFerramenta>();
        ferramenta.Setup(x => x.Nome).Returns("teste");

        registry.Registrar(ferramenta.Object);

        registry.Obter("teste").Should().Be(ferramenta.Object);
    }

    [Fact]
    public void Obter_ForUnknownTool_ShouldReturnNull()
    {
        var registry = new FerramentaRegistry();

        registry.Obter("inexistente").Should().BeNull();
    }

    [Fact]
    public void Listar_ShouldReturnAllRegisteredTools()
    {
        var registry = new FerramentaRegistry();
        var f1 = new Mock<IFerramenta>();
        f1.Setup(x => x.Nome).Returns("f1");
        var f2 = new Mock<IFerramenta>();
        f2.Setup(x => x.Nome).Returns("f2");

        registry.Registrar(f1.Object);
        registry.Registrar(f2.Object);

        registry.Listar().Should().HaveCount(2);
    }
}
