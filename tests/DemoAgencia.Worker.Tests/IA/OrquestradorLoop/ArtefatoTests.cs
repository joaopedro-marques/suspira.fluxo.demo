using DemoAgencia.Worker.IA.OrquestradorLoop;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA.OrquestradorLoop;

public class ArtefatoTests
{
    [Fact]
    public void Artefato_ShouldGenerateUniqueId()
    {
        var art1 = Artefato.Criar(TipoArtefato.Copy, "Redator", "Conteudo", "Resumo");
        var art2 = Artefato.Criar(TipoArtefato.Copy, "Redator", "Conteudo", "Resumo");

        art1.Id.Should().NotBe(art2.Id);
    }

    [Fact]
    public void Artefato_ShouldStoreFullContent()
    {
        var conteudo = new string('x', 50000);
        var art = Artefato.Criar(TipoArtefato.Html, "Dev", conteudo, "HTML grande");

        art.Conteudo.Should().Be(conteudo);
        art.Tamanho.Should().Be(50000);
    }

    [Fact]
    public void Artefato_ShouldHaveResumo()
    {
        var art = Artefato.Criar(TipoArtefato.Imagem, "Prompt", "bytes", "Imagem de um gato");

        art.Resumo.Should().Be("Imagem de um gato");
        art.Tipo.Should().Be(TipoArtefato.Imagem);
        art.Agente.Should().Be("Prompt");
    }

    [Fact]
    public void LoopContext_ShouldHaveArtefatosList()
    {
        var context = new LoopContext();

        context.Artefatos.Should().NotBeNull();
        context.Artefatos.Should().BeEmpty();
    }

    [Fact]
    public void LoopContext_AdicionarArtefato_ShouldAddToList()
    {
        var context = new LoopContext();
        var art = Artefato.Criar(TipoArtefato.Copy, "Redator", "Texto", "Copy principal");

        context.AdicionarArtefato(art);

        context.Artefatos.Should().HaveCount(1);
        context.Artefatos[0].Id.Should().Be(art.Id);
    }
}
