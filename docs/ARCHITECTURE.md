# Arquitetura - DemoAgencia

Documentação técnica da arquitetura do sistema DemoAgencia.

## Visão Geral

O DemoAgencia é uma Prova de Conceito (PoC) de um sistema multi-agente de IA operando via Telegram, com orquestração inteligente e pipeline de produção com controle de qualidade.

```mermaid
graph TB
    subgraph "Cliente"
        User[Usuário]
    end
    
    subgraph "Telegram"
        Bot[Telegram Bot API]
    end
    
    subgraph "DemoAgencia Worker"
        TS[TelegramService]
        AL[AgenteLoader]
        OL[OrquestradorLoopService]
        OR[OpenRouterService]
        SS[StreamingService]
        HC[HistoricoChat]
        LI[LangfuseInterceptor]
        FR[FerramentaRegistry]
        RC[ReferenciaClienteLoader]
    end
    
    subgraph "Servicos Externos"
        OR_API[OpenRouter API]
        LF[Langfuse]
        GL[Grafana Loki]
    end
    
    subgraph "Armazenamento"
        MD[Agentes .md]
        REF[Referencias CLIENTE_*]
        LOG[Logs]
    end
    
    User -->|Mensagens| Bot
    Bot -->|Long Polling| TS
    TS -->|Carrega| AL
    TS -->|Loop| OL
    OL -->|Executa| OR
    OL -->|Ferramentas| FR
    OL -->|Referencias| RC
    OR -->|API| OR_API
    OR -->|Traces| LI
    LI -->|Envia| LF
    TS -->|Streaming| SS
    TS -->|Historico| HC
    AL -->|Le| MD
    RC -->|Le| REF
    TS -->|Logs| LOG
    LOG --> GL
```

## Orquestrador Loop (Supervisor)

O sistema utiliza um **loop de orquestracao** (padrao supervisor/hub-and-spoke) onde o orquestrador decide a cada turno qual acao executar:

```mermaid
sequenceDiagram
    participant U as Usuario
    participant T as TelegramService
    participant L as OrquestradorLoopService
    participant O as Orquestrador (LLM)
    participant A as Agentes (Redator, Dev, etc)
    participant F as Ferramentas (gerar_imagem)
    participant Q as Qualidade

    U->>T: Mensagem livre
    T->>L: ExecutarAsync()

    loop Max 24 turnos (retries gratis ate 4)
        L->>O: Transcript + acao anterior
        O-->>L: JSON {acao: ...}

        alt responder_direto
            L-->>T: Resposta direta
        else fora_contexto
            L-->>T: Mensagem fixa
        else chamar_agente
            L->>A: Briefing autocontido
            A-->>L: Output (entra no transcript)
        else chamar_ferramenta
            L->>F: Parametros
            F-->>L: Resultado (entra no transcript)
        else finalizar
            L->>Q: Entregavel para revisao
            Q-->>L: {aprovado: true/false}
            alt Aprovado
                L-->>T: Resposta final
            else Reprovado (max 2)
                Note over L: Feedback entra no transcript
            end
        end
    end
```

### Acoes do Orquestrador

| Acao | Descricao |
|------|-----------|
| `responder_direto` | Resposta direta a perguntas simples |
| `fora_contexto` | Mensagem fora do escopo de Marketing |
| `chamar_agente` | Delega a um agente especializado (Redator, Dev, etc) |
| `chamar_ferramenta` | Usa uma ferramenta (gerar_imagem) |
| `finalizar` | Entregavel pronto → QA obrigatorio → entrega |

### Papeis dos Agentes

| Papel | Descricao | Exemplos |
|-------|-----------|----------|
| **orquestrador** | Loop supervisor, decide acoes e costura contexto | Orquestrador |
| **producao** | Executa tarefas especificas | Redator, Dev, Estrategista, Prompt para Imagens |
| **qualidade** | Revisor critico independente de qualquer entregavel | Qualidade |

### Ferramentas

Ferramentas sao registradas em codigo e chamadas pelo orquestrador via `chamar_ferramenta`:

| Ferramenta | Descricao |
|------------|-----------|
| `gerar_imagem` | Gera imagem a partir de prompt; suporta `legenda` e `assets: [ids]` para identidade visual |
| `listar_assets` | Lista assets visuais (header, footer, icon, logo, foto, post) de um cliente |
| `anexar_asset` | Anexa um asset pre-existente ao resultado final para envio ao usuario |

Novas ferramentas = novo `.cs` implementando `IFerramenta` + registro no `FerramentaRegistry`.

### Referencias de Cliente

O sistema suporta carregar referencias de clientes (manuais de marca, exemplos, imagens) para personalizar a producao. As referencias ficam em `Assets/referencias/` com o padrao `CLIENTE_{nome}_{tipo}.ext`.

**Fluxo de referencias:**

```mermaid
flowchart LR
    subgraph "Assets/referencias/"
        JSON[CLIENTE_acme_marca.json]
        HTML[CLIENTE_acme_exemplo.html]
        IMG[CLIENTE_acme_ref-visual.png]
    end

    subgraph "Loop"
        ORQ[Orquestrador<br/>identifica cliente]
        LOOP[OrquestradorLoopService<br/>injeta no transcript]
        AG[Agentes<br/>recebem contexto]
    end

    JSON --> LOOP
    HTML --> LOOP
    IMG -->|AnalisarImagemAsync| LOOP
    LOOP -->|transcript enriquecido| AG
```

**Como funciona:**

1. **Orquestrador** identifica o cliente na mensagem (campo `cliente` no JSON)
2. **Loop** carrega as referencias de texto (JSON, HTML, MD) e analisa imagens on-demand
3. **Loop** injeta as referencias no transcript do orquestrador
4. **Orquestrador** usa o contexto enriquecido ao chamar agentes de producao

**Configuracao (appsettings.json):**

```json
{
  "Loop": {
    "MaxTurnos": 24,
    "MaxRefacoesQa": 2,
    "MaxRetriesGratis": 4,
    "MensagemForaContexto": "...",
    "MensagemFalha": "..."
  },
  "Pipeline": {
    "Referencias": {
      "MaxCharsPorArquivo": 4000
    }
  }
}
```

**Extensões suportadas:**
- Texto: `.json`, `.html`, `.htm`, `.md`, `.txt`, `.css`
- Imagem: `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`

### Fluxo do Loop

```mermaid
graph TD
    A[Mensagem do Usuario] --> B[OrquestradorLoopService]
    B --> C{Orquestrador LLM}
    C -->|responder_direto| D[Resposta Direta]
    C -->|fora_contexto| E[Mensagem Fixa]
    C -->|chamar_agente| F[Agente Especializado]
    C -->|chamar_ferramenta| G[Ferramenta]
    C -->|finalizar| H[Qualidade]
    F -->|output no transcript| C
    G -->|resultado no transcript| C
    H -->|aprovado| D
    H -->|reprovado max 2| I[Falha]
    H -->|reprovado| C
    D --> J[Resposta Final]
```

## Diagrama de Componentes

```mermaid
graph LR
    subgraph "Camada de Comunicacao"
        TS[TelegramService<br/>BackgroundService]
    end
    
    subgraph "Camada de Agentes"
        AL[AgenteLoader<br/>IHostedService]
        AD[AgenteDefinicao]
    end
    
    subgraph "Camada de IA"
        OL[OrquestradorLoopService]
        FR[FerramentaRegistry]
        OR[OpenRouterService]
        SS[StreamingService]
        HC[HistoricoChat]
    end
    
    subgraph "Camada de Observabilidade"
        LI[LangfuseInterceptor]
        LC[LangfuseClient]
        SL[Serilog]
    end
    
    TS --> AL
    TS --> OL
    TS --> SS
    TS --> HC
    AL --> AD
    OL --> FR
    OL --> OR
    OR --> LI
    LI --> LC
    TS --> SL
```

## Fluxo de Mensagem

### Fluxo Principal (Comando de Agente)

```mermaid
sequenceDiagram
    participant U as Usuario
    participant T as Telegram Bot
    participant TS as TelegramService
    participant AL as AgenteLoader
    participant OR as OpenRouterService
    participant SK as Semantic Kernel
    participant ORAPI as OpenRouter API
    participant LF as Langfuse
    
    U->>T: /redator mensagem
    T->>TS: Update (Long Polling)
    TS->>AL: ObterPorComando(/redator)
    AL-->>TS: AgenteDefinicao (persona, modelo)
    
    TS->>TS: SendChatAction (typing)
    TS->>OR: CompletarStreamingAsync(mensagem, persona, modelo)
    OR->>SK: Chat com historico
    OR->>LF: IniciarTrace
    
    loop Streaming (throttle 1s)
        SK->>ORAPI: Request
        ORAPI-->>SK: Chunk
        SK-->>OR: IAsyncEnumerable
        OR-->>TS: Chunk
        TS-->>T: EditMessageText
        T-->>U: Mensagem atualizada
    end
    
    OR->>LF: FinalizarTrace
    OR-->>TS: Resposta completa
```

### Fluxo Principal (Mensagem Livre - Loop)

```mermaid
sequenceDiagram
    participant U as Usuario
    participant T as Telegram Bot
    participant TS as TelegramService
    participant OL as OrquestradorLoopService
    participant O as Orquestrador (LLM)
    participant A as Agente Especializado
    participant F as Ferramenta (gerar_imagem)
    participant Q as Qualidade
    
    U->>T: Mensagem livre
    T->>TS: Update (Long Polling)
    TS->>OL: ExecutarAsync(chatId, mensagem)
    
    loop Max 24 turnos (retries gratis ate 4)
        OL->>O: Transcript + contexto
        O-->>OL: JSON {acao: ...}
        
        alt chamar_agente
            OL->>A: Briefing autocontido
            A-->>OL: Output (entra no transcript)
        else chamar_ferramenta
            OL->>F: Parametros
            F-->>OL: Resultado (entra no transcript)
        else finalizar
            OL->>Q: Entregavel para revisao
            Q-->>OL: {aprovado: true/false}
        end
    end
    
    OL-->>TS: ResultadoPipeline (resposta + imagem?)
    TS-->>T: SendMessage/SendPhoto
    T-->>U: Resposta final
```

### Fluxo de Imagem (Analise)

```mermaid
sequenceDiagram
    participant U as Usuario
    participant T as Telegram Bot
    participant TS as TelegramService
    participant OR as OpenRouterService
    participant SK as Semantic Kernel
    participant ORAPI as OpenRouter API
    
    U->>T: Foto + legenda
    T->>TS: Update com Photo
    TS->>TS: Download foto
    TS->>TS: SendChatAction (typing)
    TS->>OR: DescreverImagemAsync(imagem, contexto)
    OR->>SK: Chat com ImageContent
    SK->>ORAPI: qwen/qwen2.5-vl-72b-instruct (multimodal)
    ORAPI-->>SK: Descricao
    SK-->>OR: Resposta
    OR-->>TS: Descricao da imagem
    TS->>T: SendMessage
    T-->>U: Resposta
```

### Fluxo de Geracao de Imagem (via Ferramenta)

```mermaid
sequenceDiagram
    participant U as Usuario
    participant T as Telegram Bot
    participant TS as TelegramService
    participant OL as OrquestradorLoopService
    participant OR as OpenRouterService
    participant ORAPI as OpenRouter API
    
    U->>T: Mensagem com intencao de imagem
    T->>TS: Update
    TS->>OL: ExecutarAsync()
    OL->>OL: chamar_ferramenta gerar_imagem
    OL->>OR: GerarImagemAsync(prompt)
    OR->>ORAPI: POST /images/generations (qwen/qwen-image-3-pro)
    ORAPI-->>OR: b64_json
    OR-->>OL: byte[] imagem
    OL->>OL: finalizar → QA
    OL-->>TS: ResultadoPipeline (imagem + legenda)
    TS->>T: SendPhoto
    T-->>U: Imagem gerada
```

## Estrutura do Projeto

```mermaid
graph TD
    subgraph "src/DemoAgencia.Worker"
        direction TB
        PROG[Program.cs<br/>Configuracao DI + Serilog]
        SCE[ServiceCollectionExtensions.cs<br/>Composicao DI]
        
        subgraph "Telegram/"
            TS2[TelegramService.cs<br/>Long polling + handlers]
        end
        
        subgraph "Agentes/"
            AL2[AgenteLoader.cs<br/>Parser .md + cache]
            AD2[AgenteDefinicao.cs<br/>Modelo de dados]
        end

        subgraph "Referencias/"
            RCL[ReferenciaClienteLoader.cs<br/>Parser CLIENTE_ prefix]
        end

        subgraph "IA/"
            OL[OrquestradorLoopService<br/>Loop supervisor]
            FR[Ferramentas/<br/>Registry + gerar_imagem]
            OR2[OpenRouterService<br/>SK + OpenRouter]
            SS2[StreamingService.cs<br/>Throttle de edits]
            HC2[HistoricoChat.cs<br/>Contexto por chat]
        end
        
        subgraph "Seguranca/"
            AN[AnonimizadorService.cs]
            RL[RateLimiterService.cs]
        end
        
        subgraph "Observabilidade/"
            LI2[LangfuseInterceptor.cs<br/>Cria traces]
            LC2[LangfuseClient.cs<br/>HTTP client]
        end

        subgraph "Contracts/"
            LT[LangfuseTrace.cs]
            LTC[LangfuseTraceContext.cs]
        end
    end
    
    subgraph "Assets/"
        MD2[agentes/*.md<br/>Definicoes dos agentes]
        REF[referencias/CLIENTE_*<br/>Referencias de clientes]
    end
    
    subgraph "tests/"
        TEST[DemoAgencia.Worker.Tests<br/>103 testes unitarios]
    end
```

## Diagrama de Deploy

```mermaid
graph TB
    subgraph "Desenvolvimento"
        DEV[Developer Machine]
        GIT[GitHub]
        CI[GitHub Actions]
    end
    
    subgraph "Oracle Cloud - Always Free"
        subgraph "VM ARM64 (Ubuntu)"
            DOCKER[Docker Engine]
            subgraph "Container"
                APP[DemoAgencia Worker]
                LOGS[/logs/]
                ASSETS[/Assets/]
            end
        end
    end
    
    subgraph "SaaS"
        TG[Telegram API]
        OR3[OpenRouter API]
        LF2[Langfuse Cloud]
    end
    
    DEV -->|Push| GIT
    GIT -->|Trigger| CI
    CI -->|Build + Test| CI
    CI -->|Deploy automatico via SSH| DOCKER
    DOCKER -->|Run| APP
    APP -->|Long Polling| TG
    APP -->|API Calls| OR3
    APP -->|Traces| LF2
    APP -->|Write| LOGS
    APP -->|Read| ASSETS
```

## Ciclo de Vida do Agente

```mermaid
stateDiagram-v2
    [*] --> Startup: Application Start
    Startup --> Loading: AgenteLoader.StartAsync
    Loading --> Parsing: Lê /Assets/agentes/*.md
    Parsing --> Ready: Parse frontmatter + persona
    Ready --> Idle: Aguarda comandos
    
    Idle --> Selected: /redator, /dev, /estrategista, /prompt-imagem
    Selected --> Processing: Mensagem recebida
    Processing --> Streaming: LLM responde
    Streaming --> Idle: Resposta completa
    
    Idle --> Cleared: /limpar ou /reset
    Cleared --> Idle
    
    Idle --> Shutdown: CancellationToken
    Shutdown --> [*]
```

## Modelo de Dados

### AgenteDefinicao

```mermaid
classDiagram
    class AgenteDefinicao {
        +string Nome
        +string Descricao
        +string ModeloAlvo
        +List~string~ Comandos
        +string Persona
    }
    
    class ChatMessage {
        +string Role
        +string Content
    }
    
    class HistoricoChat {
        -Dictionary~long, List~ChatMessage~~ _historicos
        +AdicionarMensagem(long chatId, string role, string content)
        +List~ChatMessage~ ObterHistorico(long chatId)
        +LimparHistorico(long chatId)
    }
    
    class LangfuseTrace {
        +string Id
        +string Name
        +string UserId
        +string Model
        +object Input
        +object Output
        +int PromptTokens
        +int CompletionTokens
        +DateTime StartTime
        +DateTime EndTime
    }
    
    HistoricoChat "1" *-- "0..20" ChatMessage
```

## Fluxo de Observabilidade

```mermaid
flowchart LR
    subgraph "OpenRouterService"
        A[IniciarTrace]
        B[Executar LLM]
        C[FinalizarTrace]
    end
    
    subgraph "LangfuseInterceptor"
        D[Cria contexto]
        E[Calcula duração]
        F[Monta trace]
    end
    
    subgraph "LangfuseClient"
        G[Serializa JSON]
        H[POST /api/public/ingestion]
        I[Batch: trace + generation]
    end
    
    A --> D
    B --> E
    C --> F
    F --> G
    G --> H
    H --> I
```

## Decisões de Arquitetura

| Decisão | Justificativa | Alternativas consideradas |
|---------|---------------|---------------------------|
| Worker Service (BackgroundService) | Simplicidade, sem necessidade de web server | ASP.NET Core minimal API, Console App |
| Long Polling | Não requer webhooks, sem abertura de portas | Webhooks (requer IP público fixo) |
| OpenRouter | Acesso a múltiplos modelos com uma API | APIs diretas (mais custo, mais complexidade) |
| Semantic Kernel | Orquestração nativa Microsoft, filtros | LangChain (Python), implementação manual |
| Markdown para agentes | Legível, versionável, sem DB | YAML, JSON, banco de dados |
| In-Memory para histórico | PoC, sem estado persistente | Redis, SQLite, PostgreSQL |
| Serilog + arquivo | Simplicidade, rotação automática | ELK Stack, Seq, Application Insights |
| Langfuse Cloud (free) | LLMOps sem custo inicial | Self-hosted Langfuse, custom dashboard |
| Docker ARM64 | Aproveita Oracle Free Tier ARM | x86_64 (mais caro), bare metal |
| Streaming com throttle | UX melhorada respeitando rate limits | Resposta única, streaming sem throttle |

## Trade-offs

### Custo vs Complexidade

```mermaid
quadrantChart
    title Custo vs Complexidade
    x-axis "Baixo Custo" --> "Alto Custo"
    y-axis "Baixa Complexidade" --> "Alta Complexidade"
    quadrant-1 "Evitar"
    quadrant-2 "Considerar"
    quadrant-3 "Ideal para PoC"
    quadrant-4 "Bom para produção"
    "OpenRouter": [0.3, 0.3]
    "Langfuse Cloud": [0.2, 0.2]
    "Oracle Free Tier": [0.1, 0.4]
    "Kubernetes": [0.7, 0.8]
    "ELK Stack": [0.8, 0.7]
    "Redis Cluster": [0.6, 0.6]
```

## Evolução Futura

```mermaid
roadmap
    title Evolucao do DemoAgencia
    section PoC (Atual)
    Worker Service :done, 2025-01, 2025-03
    Telegram Bot :done, 2025-01, 2025-03
    Agentes .md :done, 2025-01, 2025-03
    Loop de Orquestracao :done, 2025-03, 2025-06
    OpenRouter :done, 2025-02, 2025-04
    Langfuse :done, 2025-02, 2025-04
    Referencias de Clientes :done, 2025-06, 2025-07
    
    section MVP
    Banco de dados :2026-01, 2026-03
    Autenticacao :2026-02, 2026-04
    Multi-tenant :2026-03, 2026-05
    
    section Producao
    Kubernetes :2026-06, 2026-08
    Monitoramento avancado :2026-06, 2026-07
    Auto-scaling :2026-07, 2026-09
```

## Referencias

- [RUNBOOK.md](../RUNBOOK.md) - Guia de deploy e operacao
- [CHECKLIST.md](../CHECKLIST.md) - Checklist de aceite E2E
