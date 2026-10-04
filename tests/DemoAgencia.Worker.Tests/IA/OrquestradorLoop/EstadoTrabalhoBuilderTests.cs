using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class EstadoTrabalhoBuilderTests
{
    [Fact]
    public void Build_WithNoArtifacts_ShouldReturnEmptyState()
    {
        var context = new LoopContext();

        var estado = EstadoTrabalhoBuilder.Build(context);

        estado.Should().Contain("## Estado do trabalho");
        estado.Should().Contain("Artefatos: nenhum");
    }

    [Fact]
    public void Build_WithArtifacts_ShouldListThem()
    {
        var context = new LoopContext();
        context.AdicionarArtefato(Artefato.Criar(TipoArtefato.Copy, "Redator", "Texto da copy", "Copy principal"));
        context.AdicionarArtefato(Artefato.Criar(TipoArtefato.Html, "Dev", "<html>...</html>", "HTML do email"));

        var estado = EstadoTrabalhoBuilder.Build(context);

        estado.Should().Contain("art_");
        estado.Should().Contain("Copy");
        estado.Should().Contain("Redator");
        estado.Should().Contain("Copy principal");
        estado.Should().Contain("Html");
        estado.Should().Contain("Dev");
        estado.Should().Contain("HTML do email");
    }

    [Fact]
    public void Build_ShouldIncludeEtapasExecutadas()
    {
        var context = new LoopContext();
        context.Resultado.EtapasExecutadas.Add("loop_agente_Estrategista");
        context.Resultado.EtapasExecutadas.Add("loop_agente_Redator");

        var estado = EstadoTrabalhoBuilder.Build(context);

        estado.Should().Contain("Etapas executadas");
        estado.Should().Contain("Estrategista");
        estado.Should().Contain("Redator");
    }

    [Fact]
    public void Build_ShouldIncludeTurnoAtual()
    {
        var context = new LoopContext { Turnos = 3, MaxTurnos = 8 };

        var estado = EstadoTrabalhoBuilder.Build(context);

        estado.Should().Contain("Turno 3/8");
    }

    [Fact]
    public void Build_WithImages_ShouldShowImagesSection()
    {
        var context = new LoopContext();
        context.ImagensDeck.Add(new ItemDeckImagem("img_1", "capa", "Vistoria MRV", "A professional..."));
        context.ImagensDeck.Add(new ItemDeckImagem("img_2", "como_funciona", "Passo a passo", "Step by step..."));

        var estado = EstadoTrabalhoBuilder.Build(context);

        estado.Should().Contain("Imagens geradas (2)");
        estado.Should().Contain("[img_1]");
        estado.Should().Contain("capa");
        estado.Should().Contain("Vistoria MRV");
        estado.Should().Contain("[img_2]");
        estado.Should().Contain("como_funciona");
    }

    [Fact]
    public void Build_WithNoImages_ShouldShowNoImages()
    {
        var context = new LoopContext();

        var estado = EstadoTrabalhoBuilder.Build(context);

        estado.Should().Contain("Imagens geradas: nenhuma");
    }
}
