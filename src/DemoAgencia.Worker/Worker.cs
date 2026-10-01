namespace DemoAgencia.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Heartbeat - {time}", DateTimeOffset.Now);
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }

        logger.LogInformation("Worker encerrando graciosamente");
    }
}
