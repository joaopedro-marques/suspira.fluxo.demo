using DemoAgencia.Worker.IA;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.IA;

public class HistoricoChatTests
{
    private readonly HistoricoChat _historico;

    public HistoricoChatTests()
    {
        _historico = new HistoricoChat();
    }

    [Fact]
    public void AdicionarMensagem_ShouldStoreUserMessage()
    {
        _historico.AdicionarMensagem(123, "user", "Olá");

        var mensagens = _historico.ObterHistorico(123);
        mensagens.Should().HaveCount(1);
        mensagens[0].Role.Should().Be("user");
        mensagens[0].Content.Should().Be("Olá");
    }

    [Fact]
    public void AdicionarMensagem_ShouldStoreAssistantMessage()
    {
        _historico.AdicionarMensagem(123, "assistant", "Resposta");

        var mensagens = _historico.ObterHistorico(123);
        mensagens.Should().HaveCount(1);
        mensagens[0].Role.Should().Be("assistant");
        mensagens[0].Content.Should().Be("Resposta");
    }

    [Fact]
    public void AdicionarMensagem_ShouldMaintainOrder()
    {
        _historico.AdicionarMensagem(123, "user", "Mensagem 1");
        _historico.AdicionarMensagem(123, "assistant", "Resposta 1");
        _historico.AdicionarMensagem(123, "user", "Mensagem 2");

        var mensagens = _historico.ObterHistorico(123);
        mensagens.Should().HaveCount(3);
        mensagens[0].Content.Should().Be("Mensagem 1");
        mensagens[1].Content.Should().Be("Resposta 1");
        mensagens[2].Content.Should().Be("Mensagem 2");
    }

    [Fact]
    public void AdicionarMensagem_ShouldEnforceLimitOf20()
    {
        for (int i = 0; i < 25; i++)
        {
            _historico.AdicionarMensagem(123, "user", $"Mensagem {i}");
        }

        var mensagens = _historico.ObterHistorico(123);
        mensagens.Should().HaveCount(20);
        mensagens[0].Content.Should().Be("Mensagem 5");
        mensagens[19].Content.Should().Be("Mensagem 24");
    }

    [Fact]
    public void ObterHistorico_WithDifferentChatIds_ShouldBeIsolated()
    {
        _historico.AdicionarMensagem(123, "user", "Chat 1");
        _historico.AdicionarMensagem(456, "user", "Chat 2");

        var mensagens123 = _historico.ObterHistorico(123);
        var mensagens456 = _historico.ObterHistorico(456);

        mensagens123.Should().HaveCount(1);
        mensagens123[0].Content.Should().Be("Chat 1");

        mensagens456.Should().HaveCount(1);
        mensagens456[0].Content.Should().Be("Chat 2");
    }

    [Fact]
    public void ObterHistorico_WithNonExistentChatId_ShouldReturnEmpty()
    {
        var mensagens = _historico.ObterHistorico(999);
        mensagens.Should().BeEmpty();
    }

    [Fact]
    public void LimparHistorico_ShouldRemoveAllMessages()
    {
        _historico.AdicionarMensagem(123, "user", "Mensagem 1");
        _historico.AdicionarMensagem(123, "assistant", "Resposta 1");

        _historico.LimparHistorico(123);

        var mensagens = _historico.ObterHistorico(123);
        mensagens.Should().BeEmpty();
    }

    [Fact]
    public void LimparHistorico_ShouldNotAffectOtherChats()
    {
        _historico.AdicionarMensagem(123, "user", "Chat 1");
        _historico.AdicionarMensagem(456, "user", "Chat 2");

        _historico.LimparHistorico(123);

        var mensagens123 = _historico.ObterHistorico(123);
        var mensagens456 = _historico.ObterHistorico(456);

        mensagens123.Should().BeEmpty();
        mensagens456.Should().HaveCount(1);
    }

    [Fact]
    public void ObterHistorico_ShouldReturnCopy()
    {
        _historico.AdicionarMensagem(123, "user", "Mensagem");

        var mensagens1 = _historico.ObterHistorico(123);
        var mensagens2 = _historico.ObterHistorico(123);

        mensagens1.Should().NotBeSameAs(mensagens2);
        mensagens1.Should().BeEquivalentTo(mensagens2);
    }

    [Fact]
    public async Task AdicionarMensagem_ShouldBeThreadSafe()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() => _historico.AdicionarMensagem(123, "user", $"Mensagem {index}")));
        }

        await Task.WhenAll(tasks);

        var mensagens = _historico.ObterHistorico(123);
        mensagens.Should().HaveCount(20);
    }
}
