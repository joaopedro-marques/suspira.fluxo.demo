using DemoAgencia.Worker.Telegram;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DemoAgencia.Worker.Tests.Telegram;

public class UpdateDispatcherTests
{
    private static Update CriarUpdate(int id, long chatId) => new()
    {
        Id = id,
        Message = new Message
        {
            Id = id,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            Text = $"msg-{id}"
        }
    };

    [Fact]
    public async Task ChatsDiferentes_ProcessamConcorrentemente()
    {
        var bloqueioA = new TaskCompletionSource();
        var handlerBIniciou = new TaskCompletionSource();
        var handlerBCompleto = new TaskCompletionSource();

        Task Handler(Update update, CancellationToken ct)
        {
            if (update.Message!.Chat.Id == 100)
            {
                bloqueioA.SetResult();
                return handlerBIniciou.Task;
            }
            handlerBIniciou.SetResult();
            handlerBCompleto.SetResult();
            return Task.CompletedTask;
        }

        var dispatcher = new UpdateDispatcher(Handler, 64, Mock.Of<ILogger<UpdateDispatcher>>());

        var taskA = dispatcher.EnfileirarAsync(CriarUpdate(1, 100));
        var taskB = dispatcher.EnfileirarAsync(CriarUpdate(2, 200));

        await bloqueioA.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await handlerBIniciou.Task.WaitAsync(TimeSpan.FromSeconds(5));

        bloqueioA.Task.IsCompleted.Should().BeTrue();
        handlerBIniciou.Task.IsCompleted.Should().BeTrue();

        dispatcher.Liberar();
        await dispatcher.DrainAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task MesmoChat_ProcessaSequencialmenteEmOrdem()
    {
        var ordem = new List<string>();
        var primeiroIniciou = new TaskCompletionSource();
        var liberarPrimeiro = new TaskCompletionSource();
        var segundoIniciou = new TaskCompletionSource();

        Task Handler(Update update, CancellationToken ct)
        {
            if (update.Message!.Text == "msg-1")
            {
                ordem.Add("primeiro");
                primeiroIniciou.SetResult();
                return liberarPrimeiro.Task;
            }
            ordem.Add("segundo");
            segundoIniciou.SetResult();
            return Task.CompletedTask;
        }

        var dispatcher = new UpdateDispatcher(Handler, 64, Mock.Of<ILogger<UpdateDispatcher>>());

        await dispatcher.EnfileirarAsync(CriarUpdate(1, 300));
        await dispatcher.EnfileirarAsync(CriarUpdate(2, 300));

        await primeiroIniciou.Task.WaitAsync(TimeSpan.FromSeconds(5));

        ordem.Should().Contain("primeiro");
        ordem.Should().NotContain("segundo");

        liberarPrimeiro.SetResult();
        await segundoIniciou.Task.WaitAsync(TimeSpan.FromSeconds(5));

        dispatcher.Liberar();
        await dispatcher.DrainAsync(TimeSpan.FromSeconds(5));

        ordem.Should().ContainInOrder("primeiro", "segundo");
    }

    [Fact]
    public async Task ExcecaoNoHandler_NaoMataConsumer()
    {
        var chamadas = 0;
        var segundoCompleto = new TaskCompletionSource();

        Task Handler(Update update, CancellationToken ct)
        {
            chamadas++;
            if (chamadas == 1)
                throw new InvalidOperationException("boom");
            segundoCompleto.SetResult();
            return Task.CompletedTask;
        }

        var dispatcher = new UpdateDispatcher(Handler, 64, Mock.Of<ILogger<UpdateDispatcher>>());

        await dispatcher.EnfileirarAsync(CriarUpdate(1, 400));
        await dispatcher.EnfileirarAsync(CriarUpdate(2, 400));

        await segundoCompleto.Task.WaitAsync(TimeSpan.FromSeconds(5));

        chamadas.Should().Be(2);
        dispatcher.Liberar();
        await dispatcher.DrainAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TemFilaPendente_RetornaTrueQuandoHaUpdatesEsperando()
    {
        var bloqueio = new TaskCompletionSource();

        Task Handler(Update update, CancellationToken ct)
        {
            if (update.Message!.Text == "msg-1")
            {
                bloqueio.SetResult();
                return Task.Delay(Timeout.Infinite, CancellationToken.None).ContinueWith(_ => { });
            }
            return Task.CompletedTask;
        }

        var dispatcher = new UpdateDispatcher(Handler, 64, Mock.Of<ILogger<UpdateDispatcher>>());

        await dispatcher.EnfileirarAsync(CriarUpdate(1, 500));
        await bloqueio.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await dispatcher.EnfileirarAsync(CriarUpdate(2, 500));
        await Task.Delay(50);

        dispatcher.TemFilaPendente(500).Should().BeTrue();

        dispatcher.Liberar();
        await dispatcher.DrainAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TemFilaPendente_ChatInexistente_RetornaFalse()
    {
        var dispatcher = new UpdateDispatcher((_, _) => Task.CompletedTask, 64, Mock.Of<ILogger<UpdateDispatcher>>());

        dispatcher.TemFilaPendente(999).Should().BeFalse();

        dispatcher.Liberar();
        await dispatcher.DrainAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DrainAsync_AguardaConsumersConcluirem()
    {
        var processados = 0;

        Task Handler(Update update, CancellationToken ct)
        {
            Interlocked.Increment(ref processados);
            return Task.CompletedTask;
        }

        var dispatcher = new UpdateDispatcher(Handler, 64, Mock.Of<ILogger<UpdateDispatcher>>());

        await dispatcher.EnfileirarAsync(CriarUpdate(1, 600));
        await dispatcher.EnfileirarAsync(CriarUpdate(2, 600));
        await dispatcher.EnfileirarAsync(CriarUpdate(3, 600));

        dispatcher.Liberar();
        await dispatcher.DrainAsync(TimeSpan.FromSeconds(5));

        processados.Should().Be(3);
    }
}
