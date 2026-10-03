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
}
