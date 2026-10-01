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
        PS[PipelineService]
        OR[OpenRouterService]
        SS[StreamingService]
        HC[HistoricoChat]
        LI[LangfuseInterceptor]
    end
    
    subgraph "Serviços Externos"
        OR_API[OpenRouter API]
        LF[Langfuse]
    end
    
    subgraph "Armazenamento"
        MD[Agentes .md]
        LOG[Logs]
    end
    
    User -->|Mensagens| Bot
    Bot -->|Long Polling| TS
    TS -->|Carrega| AL
    TS -->|Pipeline| PS
    PS -->|Executa| OR
    OR -->|API| OR_API
    OR -->|Traces| LI
    LI -->|Envia| LF
    TS -->|Streaming| SS
    TS -->|Histórico| HC
    AL -->|Lê| MD
    TS -->|Logs| LOG
```

## Pipeline Multi-Agente

O sistema utiliza um pipeline multi-agente com orquestração inteligente:

```mermaid
sequenceDiagram
    participant U as Usuário
    participant T as TelegramService
    participant P as PipelineService
    participant O as Orquestrador
    participant E as Estrategista
    participant PR as Produção
    participant Q as Qualidade
    participant F as Formatador
    
    U->>T: Mensagem livre
    T->>P: ExecutarAsync()
    
    Note over P: 🧠 Analisando...
    P->>O: Classificar intenção
    O-->>P: fora_contexto | direta | pipeline
    
    alt fora_contexto
        P-->>T: Mensagem fixa
        T-->>U: Resposta
    else direta
        P->>O: Responder diretamente
        P->>F: Formatar resposta
        F-->>T: Resposta formatada
        T-->>U: Resposta
    else pipeline
        Note over P: 📋 Planejando...
        P->>E: Planejar execução
        E-->>P: Agente + instruções
        
        loop Máx 2 refações
            Note over P: ✍️ Produzindo...
            P->>PR: Executar tarefa
            PR-->>P: Output
            
            Note over P: 🔍 Revisando...
            P->>Q: Revisar qualidade
            Q-->>P: Aprovado | Reprovado
            
            alt Reprovado
                Note over P: 🔁 Refinando...
            else Aprovado
                Note over P: ✅ Aprovando...
                P->>E: Aprovar resultado
                E-->>P: Aprovado | Reprovado
            end
        end
        
        Note over P: 📤 Formatando...
        P->>F: Formatar resposta final
        F-->>T: Resposta formatada
        T-->>U: Resposta
    end
```

### Papéis dos Agentes

| Papel | Descrição | Exemplos |
|-------|-----------|----------|
| **orquestrador** | Classifica intenção e decide rota | Orquestrador |
| **estrategista** | Planeja execução e aprova resultados | Estrategista |
| **producao** | Executa tarefas específicas | Redator, Dev, Editor de Imagens |
| **qualidade** | Revisa output dos agentes de produção | Qualidade |
| **formatacao** | Formata resposta final para o usuário | Formatador |

### Rotas do Orquestrador

```mermaid
graph TD
    A[Mensagem do Usuário] --> B{Orquestrador}
    B -->|fora_contexto| C[Mensagem Fixa]
    B -->|direta| D[Resposta Direta]
    B -->|pipeline| E[Pipeline Completo]
    
    C --> F[Formatador]
    D --> F
    E --> G[Estrategista]
    G --> H[Produção]
    H --> I[Qualidade]
    I -->|Aprovado| J[Estrategista Aprova]
    I -->|Reprovado| H
    J -->|Aprovado| K[Formatador]
    J -->|Reprovado| H
    K --> L[Resposta Final]
```

## Diagrama de Componentes

```mermaid
graph LR
    subgraph "Camada de Comunicação"
        TS[TelegramService<br/>BackgroundService]
    end
    
    subgraph "Camada de Agentes"
        AL[AgenteLoader<br/>IHostedService]
        AD[AgenteDefinicao]
    end
    
    subgraph "Camada de IA"
        RS[RoteadorService]
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
    TS --> RS
    TS --> SS
    TS --> HC
    AL --> AD
    RS --> OR
    OR --> LI
    LI --> LC
    TS --> SL
```

## Fluxo de Mensagem

### Fluxo Principal (Texto)

```mermaid
sequenceDiagram
    participant U as Usuário
    participant T as Telegram Bot
    participant TS as TelegramService
    participant RS as RoteadorService
    participant OR as OpenRouterService
    participant SK as Semantic Kernel
    participant ORAPI as OpenRouter API
    participant LF as Langfuse
    
    U->>T: Mensagem
    T->>TS: Update (Long Polling)
    TS->>TS: Parse comando/texto
    
    alt É comando de agente
        TS->>RS: Roteear(comando)
        RS->>RS: Busca agente por comando
    else É mensagem livre
        TS->>RS: Roteear(mensagem)
        RS->>OR: ClassificarAsync(mensagem)
        OR->>SK: Classificação
        SK->>ORAPI: Gemini Flash
        ORAPI-->>SK: Categoria
        SK-->>OR: "codigo"|"estrategia"|"copy"|"geral"
        OR-->>RS: Categoria
        RS->>RS: Mapeia categoria → modelo
    end
    
    RS-->>TS: (modelo, persona)
    
    TS->>TS: SendChatAction (typing)
    TS->>OR: CompletarStreamingAsync
    OR->>SK: Chat com histórico
    OR->>LI: IniciarTrace
    
    loop Streaming
        SK->>ORAPI: Request
        ORAPI-->>SK: Chunk
        SK-->>OR: IAsyncEnumerable
        OR-->>SS: Chunk
        SS-->>TS: EditMessageText
        TS-->>T: Edit message
        T-->>U: Mensagem atualizada
    end
    
    OR->>LI: FinalizarTrace
    LI->>LF: EnviarTrace
    OR-->>TS: Resposta completa
    TS->>HC: Salvar no histórico
```

### Fluxo de Imagem (Análise)

```mermaid
sequenceDiagram
    participant U as Usuário
    participant T as Telegram Bot
    participant TS as TelegramService
    participant OR as OpenRouterService
    participant SK as Semantic Kernel
    participant ORAPI as OpenRouter API
    
    U->>T: Foto + legenda
    T->>TS: Update com Photo
    TS->>TS: Download foto
    TS->>TS: SendChatAction (typing)
    TS->>OR: AnalisarImagemAsync
    OR->>SK: Chat com ImageContent
    SK->>ORAPI: Gemini Flash (multimodal)
    ORAPI-->>SK: Descrição
    SK-->>OR: Resposta
    OR-->>TS: Descrição da imagem
    TS->>T: SendMessage
    T-->>U: Resposta
```

### Fluxo de Geração de Imagem

```mermaid
sequenceDiagram
    participant U as Usuário
    participant T as Telegram Bot
    participant TS as TelegramService
    participant OR as OpenRouterService
    participant ORAPI as OpenRouter API
    
    U->>T: /imagem prompt
    T->>TS: Update com comando
    TS->>TS: SendChatAction (upload_photo)
    TS->>OR: GerarImagemAsync(prompt)
    OR->>ORAPI: POST /images/generations
    ORAPI-->>OR: b64_json
    OR-->>TS: byte[] imagem
    TS->>T: SendPhoto
    T-->>U: Imagem gerada
```

## Roteamento de Modelos

```mermaid
flowchart TD
    START[Mensagem Recebida] --> CHECK{É comando?}
    
    CHECK -->|Sim| AGENT{Agente existe?}
    CHECK -->|Não| CLASSIFY[Classificador<br/>Gemini Flash]
    
    AGENT -->|Sim| USE_AGENT[Usa modelo do agente]
    AGENT -->|Não| CLASSIFY
    
    CLASSIFY --> CAT{Categoria}
    
    CAT -->|codigo| CLAUDE[claude-3.5-sonnet]
    CAT -->|estrategia| LLAMA[llama-3.1-70b-instruct]
    CAT -->|copy| CLAUDE
    CAT -->|geral| GEMINI[gemini-flash-1.5]
    
    USE_AGENT --> RESP[Resposta]
    CLAUDE --> RESP
    LLAMA --> RESP
    GEMINI --> RESP
    
    RESP --> HIST[Salva no Histórico]
```

## Estrutura do Projeto

```mermaid
graph TD
    subgraph "src/DemoAgencia.Worker"
        direction TB
        PROG[Program.cs<br/>Configuração DI + Serilog]
        WORK[Worker.cs<br/>Heartbeat]
        
        subgraph "Telegram/"
            TS2[TelegramService.cs<br/>Long polling + handlers]
        end
        
        subgraph "Agentes/"
            AL2[AgenteLoader.cs<br/>Parser .md + cache]
            AD2[AgenteDefinicao.cs<br/>Modelo de dados]
        end
        
        subgraph "IA/"
            RS2[RoteadorService.cs<br/>Roteamento híbrido]
            OR2[OpenRouterService.cs<br/>SK + OpenRouter]
            SS2[StreamingService.cs<br/>Throttle de edits]
            HC2[HistoricoChat.cs<br/>Contexto por chat]
        end
        
        subgraph "Observabilidade/"
            LI2[LangfuseInterceptor.cs<br/>Cria traces]
            LC2[LangfuseClient.cs<br/>HTTP client]
        end
    end
    
    subgraph "Assets/"
        MD2[agentes/*.md<br/>Definições dos agentes]
        IMG[imagens/<br/>Exemplos visuais]
    end
    
    subgraph "tests/"
        TEST[DemoAgencia.Worker.Tests<br/>46 testes unitários]
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
    CI -->|Deploy manual| DOCKER
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
    
    Idle --> Selected: /redator, /dev, /estrategista
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
    title Evolução do DemoAgencia
    section PoC (Atual)
    Worker Service :done, 2024-01, 2024-02
    Telegram Bot :done, 2024-01, 2024-02
    Agentes .md :done, 2024-01, 2024-02
    OpenRouter :done, 2024-02, 2024-03
    Langfuse :done, 2024-02, 2024-03
    
    section MVP
    Banco de dados :2024-04, 2024-05
    Autenticação :2024-04, 2024-05
    Multi-tenant :2024-05, 2024-06
    
    section Produção
    Kubernetes :2024-07, 2024-09
    Monitoramento avançado :2024-07, 2024-08
    Auto-scaling :2024-08, 2024-10
```

## Referências

- [arquitetura.json](../arquitetura.json) - Especificação original do projeto
- [RUNBOOK.md](../RUNBOOK.md) - Guia de deploy e operação
- [CHECKLIST.md](../CHECKLIST.md) - Checklist de aceite E2E
