# Guia de Desenvolvimento

## Pré-requisitos

- .NET 10 SDK
- Docker (para deploy)
- Git

## Setup Local

```bash
# Clone o repositório
git clone <repo-url>
cd suspira.fluxo.demo

# Configure as variáveis de ambiente
cp .env.example .env
# Edite .env com suas credenciais

# Restaure dependências
dotnet restore

# Execute localmente
dotnet run --project src/DemoAgencia.Worker
```

## Estrutura de Código

### Convenções

- **Namespaces**: `DemoAgencia.Worker.<Camada>`
- **Injeção de dependência**: Constructor injection via `IServiceCollection`
- **Async/await**: Todos os métodos I/O são assíncronos
- **Logging**: Serilog com structured logging (`{Property}`)
- **Testes**: Métodos testáveis são `virtual` para mocking

### Camadas

```
Telegram/           → Comunicação com Telegram Bot API (ITelegramGateway)
Agentes/            → IAgentesCatalogo + loader de agentes .md (router, redator, hero, qa)
IA/Router/          → RouterService, RouterParser, Brief
IA/PreFlight/       → ConversaPendenteStore, EstadoPreFlight
IA/Pipelines/       → PipelineRunner, IPipelineStep, PipelineContext, StepRecords
IA/Pipelines/Email/ → PipelineEmail + Steps (Estrategia, Marca, Copy, Hero, Template, QA)
IA/                 → OpenRouterService, OpenRouterPrivacyHandler, interfaces
Referencias/        → IReferenciasCliente (texto + imagens + estratégia por cliente)
Seguranca/          → AnonimizadorService, RateLimiterService
Observabilidade/    → LangfuseClient, LangfuseInterceptor
Contracts/          → LangfuseTrace, LangfuseTraceContext
Configuracoes/      → Options pattern (PreFlightOptions, OpenRouterOptions, etc.)
```

## Testes

```bash
# Todos os testes
dotnet test

# Com coverage
dotnet test --collect:"XPlat Code Coverage"

# Filtrar por classe
dotnet test --filter "AgentesLoaderTests"

# Filtrar por nome
dotnet test --filter "FullyQualifiedName~RouterParser"
```

### Adicionando Testes

1. Crie o arquivo em `tests/DemoAgencia.Worker.Tests/<Camada>/`
2. Use xUnit + Moq + FluentAssertions
3. Métodos do SUT devem ser `virtual`
4. Mock dependências externas

```csharp
public class MeuServiceTests
{
    private readonly Mock<ILogger<MeuService>> _loggerMock;
    private readonly MeuService _service;

    public MeuServiceTests()
    {
        _loggerMock = new Mock<ILogger<MeuService>>();
        _service = new MeuService(_loggerMock.Object);
    }

    [Fact]
    public async Task Metodo_Cenario_DeveResultado()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

## Variáveis de Ambiente

| Variável | Descrição | Obrigatória |
|----------|-----------|-------------|
| `Telegram__BotToken` | Token do bot Telegram | Sim |
| `OpenRouter__ApiKey` | Chave da API OpenRouter | Sim |
| `OpenRouter__DataCollection` | Política de coleta de dados (deny/allow) | Não (default: deny) |
| `Langfuse__PublicKey` | Public key Langfuse | Não |
| `Langfuse__SecretKey` | Secret key Langfuse | Não |
| `Langfuse__Host` | URL do Langfuse | Não (default: cloud) |
| `Seguranca__AnonimizarDados` | Anonimizar dados sensíveis | Não (default: true) |
| `Seguranca__MaxMensagensPorMinuto` | Rate limit por chat | Não (default: 5) |
| `GrafanaLoki__Endpoint` | Endpoint do Grafana Loki | Não |
| `GrafanaLoki__LoginId` | Login ID do Grafana Loki | Não |
| `GrafanaLoki__Password` | API key do Grafana Loki | Não |

## Debug

```bash
# Logs em tempo real
dotnet run --project src/DemoAgencia.Worker

# Ver logs do arquivo
cat logs/demo-log-*.txt

# Debug com VS Code
# Adicione launch.json com "console": "integratedTerminal"
```

## CI/CD Local

```bash
# Simular pipeline
dotnet build --configuration Release
dotnet test --configuration Release
docker build -t demoagencia:test .
```
