using Microsoft.Extensions.Options;

namespace DemoAgencia.Worker.Tests;

public static class TestOptions
{
    public static IOptions<T> Create<T>(T value) where T : class, new()
    {
        return Options.Create(value);
    }
}
