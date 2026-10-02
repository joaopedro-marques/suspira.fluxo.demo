using DemoAgencia.Worker.Configuracoes;
using DemoAgencia.Worker.Seguranca;
using FluentAssertions;

namespace DemoAgencia.Worker.Tests.Seguranca;

public class AnonimizadorServiceTests
{
    private readonly AnonimizadorService _service;

    public AnonimizadorServiceTests()
    {
        _service = new AnonimizadorService(TestOptions.Create(new SegurancaOptions { AnonimizarDados = true }));
    }

    [Fact]
    public void Anonimizar_WithEmail_ShouldReplace()
    {
        var result = _service.Anonimizar("Contato: joao@email.com");
        result.Should().Contain("[email]");
        result.Should().NotContain("joao@email.com");
    }

    [Fact]
    public void Anonimizar_WithPhone_ShouldReplace()
    {
        var result = _service.Anonimizar("Telefone: (11) 99999-8888");
        result.Should().Contain("[telefone]");
    }

    [Fact]
    public void Anonimizar_WithCPF_ShouldReplace()
    {
        var result = _service.Anonimizar("CPF: 123.456.789-00");
        result.Should().Contain("[cpf]");
    }

    [Fact]
    public void Anonimizar_WithCNPJ_ShouldReplace()
    {
        var result = _service.Anonimizar("CNPJ: 12.345.678/0001-90");
        result.Should().Contain("[cnpj]");
    }

    [Fact]
    public void Anonimizar_WithCreditCard_ShouldReplace()
    {
        var result = _service.Anonimizar("Cartao: 4111 2222 3333 4444");
        result.Should().Contain("[cartao]");
    }

    [Fact]
    public void Anonimizar_WithOpenRouterToken_ShouldReplace()
    {
        var result = _service.Anonimizar("Key: sk-or-abc123def456");
        result.Should().Contain("[token]");
    }

    [Fact]
    public void Anonimizar_WithBearerToken_ShouldReplace()
    {
        var result = _service.Anonimizar("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9");
        result.Should().Contain("Bearer [token]");
    }

    [Fact]
    public void Anonimizar_WithUrlKey_ShouldReplace()
    {
        var result = _service.Anonimizar("URL: https://api.example.com?key=secret123");
        result.Should().Contain("[url_com_chave]");
    }

    [Fact]
    public void Anonimizar_WhenDisabled_ShouldReturnOriginal()
    {
        var service = new AnonimizadorService(TestOptions.Create(new SegurancaOptions { AnonimizarDados = false }));
        var result = service.Anonimizar("joao@email.com");
        result.Should().Be("joao@email.com");
    }

    [Fact]
    public void Anonimizar_WithEmptyString_ShouldReturnEmpty()
    {
        var result = _service.Anonimizar("");
        result.Should().BeEmpty();
    }
}
