# Arquitetura - DemoAgencia

Documentação técnica da arquitetura do sistema DemoAgencia.

## Visão Geral

O DemoAgencia é uma Prova de Conceito (PoC) de um sistema de automação de marketing via Telegram, com pipeline de email marketing gerada por IA. A arquitetura evoluiu de um loop multi-agente para uma abordagem mais simples e determinística baseada em **Router + Pipelines**.

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
        RS[RouterService]
        PS[ConversaPendenteStore]
    PR[PipelineRunner]
    PE[PipelineEmail]
    SE[StepEstrategiaEmail]
    OR[OpenRouterService]
    LI[LangfuseInterceptor]
    RC[ReferenciaClienteLoader]
}
    
    subgraph "Servicos Externos"
        OR_API[OpenRouter API]
        LF[Langfuse]
        GL[Grafana Loki]
    end
    
    subgraph "Armazenamento"
        TPL[referencias/templates/*.html]
        REF[Referencias {cliente}_*]
        EST[Referencias/estrategia/*.json]
        LOG[Logs]
    end
    
    User -->|Mensagens| Bot
    Bot -->|Long Polling| TS
    TS -->|Router| RS
    RS -->|Pendencias| PS
    RS -->|Dispatch email| PR
    PR -->|Executa steps| PE
    PE -->|Estrategia| SE
    PE -->|Marca| RC
    PE -->|LLM| OR
    OR -->|API| OR_API
    OR -->|Traces| LI
    LI -->|Envia| LF
    SE -->|Le| RC
    RC -->|Le| REF
    RC -->|Le| EST
    TS -->|Logs| LOG
    LOG --> GL
```

## Router (Intake Único)

O RouterService substitui o antigo PipelinePreFlightService. Sua função é classificar a mensagem e estruturar o brief para pipelines de produção.

```mermaid
sequenceDiagram
    participant U as Usuario
    participant T as TelegramService
    participant R as RouterService
    participant PS as ConversaPendenteStore
    
    U->>T: Mensagem livre
    T->>R: IniciarAsync(chatId, mensagem)
    
    R->>R: 1. Detectar cliente (match deterministico)
    R->>R: 2. Chamar LLM (deepseek-v3.2, temp 0.2)
    R-->>T: RouterResultado
    
    alt tipo = conversa
        T-->>U: Resposta direta
    else tipo = esclarecimento
        R->>PS: Guardar estado (perguntas)
        T-->>U: Perguntas
        U->>T: Respostas
        T->>R: ResumirAsync(chatId, respostas)
        R->>R: Re-processar com respostas acumuladas
        R-->>T: RouterResultado (producao)
    else tipo = fora_contexto
        T-->>U: Mensagem fixa (fora do escopo)
    else tipo = producao
        T->>T: Dispatch por canal
    end
```

### Schema do Brief

```json
{
  "tipo": "producao",
  "cliente": "acme",
  "brief": {
    "canal": "email | instagram | landing",
    "objetivo": "vender | engajar | informar",
    "publico": "empresarios",
    "oferta": "Black Friday 50% off",
    "tom": "urgente",
    "link": "https://acme.com/promo",
    "etapa_jornada": "pos-compra | pre-chaves | pos-chaves",
    "sub_jornada": "Pos Financiamento",
    "restricoes": ["sem emojis", "max 200 palavras"],
    "imagens": [{"papel": "hero", "descricao": "Banner com produto"}]
  }
}
```

### Dispatch por Canal

| Canal | Handler | Status |
|-------|---------|--------|
| `email` | `PipelineEmail` | Implementado |
| `instagram` | PipelineInstagram | Planejado |
| `landing` | PipelineLanding | Planejado |

Canais não implementados retornam mensagem "canal não suportado".

## Pipeline de Email

A PipelineEmail é uma lista ordenada de steps executados deterministicamente pelo PipelineRunner. Cada step recebe apenas o contexto necessário.

```mermaid
sequenceDiagram
    participant PR as PipelineRunner
    participant SE as StepEstrategiaEmail
    participant SM as StepMarcaEmail
    participant SC as StepCopyEmail
    participant SH as StepImagemHero
    participant ST as StepTemplateEmail
    participant SQ as StepQaEmail
    participant OR as OpenRouterService
    participant GI as GeradorImagem
    participant RC as ReferenciasCliente
    
    PR->>SE: ExecutarAsync(context)
    SE->>RC: ObterEstrategia(cliente)
    RC-->>SE: Fase, paleta, temas, mapa, satisfacoes
    SE-->>PR: context.Estrategia
    
    PR->>SM: ExecutarAsync(context)
    SM->>RC: ListarAssets(cliente)
    RC-->>SM: Assets (logo, cores, tom)
    SM-->>PR: context.Marca
    
    PR->>SC: ExecutarAsync(context)
    SC->>OR: ChamarAgenteAsync(email_copy)
    OR-->>SC: JSON com slots
    SC-->>PR: context.Copy
    
    PR->>SH: ExecutarAsync(context)
    SH->>OR: ChamarAgenteAsync(email_hero_prompt)
    OR-->>SH: Prompt otimizado
    SH->>GI: GerarImagemAsync(prompt)
    GI-->>SH: byte[] imagem
    SH-->>PR: context.HeroSrc
    
    PR->>ST: ExecutarAsync(context)
    ST->>ST: Preencher slots no template
    ST-->>PR: context.Html
    
    PR->>SQ: ExecutarAsync(context)
    SQ->>OR: ChamarAgenteAsync(email_qa)
    OR-->>SQ: {aprovado, feedback, step_alvo}
    
    alt aprovado
        SQ-->>PR: context.QaAprovado = true
    else reprovado (max 2 refações)
        SQ-->>PR: context.QaAprovado = false
        PR->>PR: Voltar ao step alvo (copy/hero)
        Note over PR,SQ: Loop até aprovação ou max refações
    end
    
    PR-->>PR: ResultadoPipeline (HTML + imagens + assets)
```

### Steps da Pipeline Email

| Step | Tipo | Modelo | Descrição |
|------|------|--------|-----------|
| `StepEstrategiaEmail` | Retrieval determinístico | N/A | Carrega fase da jornada, paleta, temas, sub-jornada, mapa emocional e satisfações/insatisfações |
| `StepMarcaEmail` | Retrieval determinístico | N/A | Carrega logo, cores, tom de voz do cliente |
| `StepCopyEmail` | LLM (few-shot) | `qwen/qwen3.7-plus` | Gera assunto, preheader, título, saudação, corpo, CTA, rodapé |
| `StepImagemHero` | LLM + API | `qwen/qwen3.7-plus` | Gera prompt otimizado + chama API de imagem |
| `StepTemplateEmail` | Template + slots | N/A | Preenche HTML table-based com slots |
| `StepQaEmail` | LLM (branch explícito) | `deepseek/deepseek-r1-0528` | Avalia entregável; retorna `step_alvo` se reprovar |

### Contexto por Step

Cada step recebe apenas o contexto necessário (princípio do mínimo privilégio):

| Step | Recebe | Não recebe |
|------|--------|------------|
| `StepEstrategiaEmail` | Cliente, Brief.etapa_jornada, banco de estratégias | Brief restante, outros steps |
| `StepMarcaEmail` | Cliente, banco de referências | Brief, Estrategia, outros steps |
| `StepCopyEmail` | Brief fields + Marca + Estrategia | Outros steps, histórico |
| `StepImagemHero` | Brief.imagens + Marca + Estrategia (paleta) | Copy, outros steps |
| `StepTemplateEmail` | Copy slots + Hero src + Marca | Brief, outros steps |
| `StepQaEmail` | Brief original + HTML final + Estrategia | Few-shots, outros steps |

## Template HTML

O template HTML base (`Assets/referencias/templates/email.html`) é table-based para compatibilidade com clientes de email (Outlook, Gmail, etc.):

```html
<!-- Estrutura principal -->
<table role="presentation" width="100%" style="max-width: 600px;">
  <tr>
    <td>
      <img src="{{logo_src}}" alt="Logo" border="0" style="display: block;">
    </td>
  </tr>
  <tr>
    <td>
      <h1>{{titulo}}</h1>
      <p>{{saudacao}}</p>
      {{corpo}}
    </td>
  </tr>
  {{hero_section}}
  <tr>
    <td>
      <table role="presentation">
        <tr>
          <td style="background-color: #0070f3;">
            <a href="{{cta_link}}">{{cta_texto}}</a>
          </td>
        </tr>
      </table>
    </td>
  </tr>
  <tr>
    <td>{{rodape}}</td>
  </tr>
</table>
```

### Características do Template

- **Table-based layout**: Compatível com Outlook, Gmail, Apple Mail
- **CSS inline**: Cada tag tem `style=""` explícito
- **Ghost tables**: `<!--[if mso]>` para Outlook
- **Imagens**: `<img>` com `alt`, `border="0"`, `style="display: block;"`
- **Max-width 600px**: Padrão de email marketing
- **CTA bulletproof**: Botão como tabela, não `<a>` com background
- **Hero section**: Condicional (`{{hero_section}}` preenchido ou vazio)
- **HTML escaping**: Slots de texto escapados via `WebUtility.HtmlEncode`

### Testes HTML (TDD)

O `StepTemplateEmail` tem 11 testes unitários cobrindo:

1. **Preenchimento de slots**: Todos os placeholders substituídos
2. **Estrutura table**: Zero `<div>`, apenas `<table>`
3. **Ghost tables**: Presença de `<!--[if mso]>`
4. **CSS inline**: Presença de `style=""`
5. **Imagens**: `alt`, `border="0"`, `display: block`
6. **Max-width**: `max-width: 600px`
7. **CTA bulletproof**: Tabela ao redor do link
8. **Hero condicional**: Presente quando `hero_src` definido, ausente quando nulo
9. **HTML escaping**: Caracteres especiais escapados (`&lt;`, `&amp;`, `&quot;`)
10. **Resultado final**: `ResultadoPipeline.RespostaFinal` = HTML completo
11. **Logo**: `logo_src` preenchido corretamente

## PipelineRunner

O PipelineRunner executa uma lista ordenada de steps com suporte a QA retry loop:

```csharp
public class PipelineRunner
{
    public async Task<ResultadoPipeline> ExecutarAsync(
        PipelineContext context,
        IReadOnlyList<IPipelineStep> steps,
        int maxRefacoesQa,
        Func<string, Task>? onProgresso,
        CancellationToken ct)
    {
        // Executa steps em ordem
        // Se QA reprova, volta ao step alvo (copy/hero)
        // Max refações = maxRefacoesQa (default 2)
        // Retorna ResultadoPipeline (HTML + imagens + assets)
    }
}
```

### QA Retry Loop

```mermaid
graph TD
    A[Iniciar pipeline] --> B[Executar step 1]
    B --> C[Executar step 2]
    C --> D[Executar step 3]
    D --> E[Executar QA]
    E --> F{Aprovado?}
    F -->|Sim| G[Retornar resultado]
    F -->|Não| H{Refações < max?}
    H -->|Sim| I{step_alvo = copy?}
    I -->|Sim| C[Re-executar copy]
    I -->|Não| D[Re-executar hero]
    H -->|Não| J[Retornar com feedback]
```

## Observabilidade

### Langfuse Traces

Cada step LLM gera um trace no Langfuse com etapa nomeada:

| Etapa | Descrição | Modelo |
|-------|-----------|--------|
| `router` | Classificação + estruturação do brief | `deepseek/deepseek-v3.2` |
| `router_retry` | Retry do router (JSON inválido) | `deepseek/deepseek-v3.2` |
| `email_copy` | Geração de copy (assunto, corpo, CTA) | `qwen/qwen3.7-plus` |
| `email_hero_prompt` | Geração de prompt para imagem hero | `qwen/qwen3.7-plus` |
| `email_hero_imagem` | Chamada de API de geração de imagem | (API call) |
| `email_qa` | Avaliação de qualidade | `deepseek/deepseek-r1-0528` |
| `image-analysis` | Análise de imagem enviada pelo usuário | `qwen/qwen2.5-vl-72b-instruct` |
| `image-generation` | Geração de imagem via API | `qwen/qwen-image-3-pro` |

Steps determinísticos (estrategia, marca, template) não geram traces LLM, apenas logs Serilog.

### Grafana Loki

Todos os steps logam via Serilog para Grafana Loki:

```
[StepMarcaEmail] Cliente=acme, Assets=3, Logo=true
[StepCopyEmail] Modelo=qwen3.7-plus, Tokens=1200
[StepTemplateEmail] Slots=11, HeroIncluded=true
[StepQaEmail] Aprovado=true, Feedback="Copy clara e persuasiva"
```

### Reasoning Disabling

Etapas com prefixo `router` desabilitam reasoning do modelo (otimização de custo/latência):

```csharp
var desativarRaciocinio = etapaNome.StartsWith("router", StringComparison.OrdinalIgnoreCase);
ReasoningDisablingHandler.IsActive = desativarRaciocinio;
```

## Estrutura do Projeto

```
src/DemoAgencia.Worker/
├── Program.cs                          # Configuração DI + Serilog
├── ServiceCollectionExtensions.cs      # Composição DI
│
├── Telegram/
│   ├── TelegramService.cs              # Long polling + handlers
│   ├── ITelegramGateway.cs             # Interface para Telegram Bot
│   ├── TelegramBotGateway.cs           # Implementacao real + TelegramGatewayFactory
│   ├── TelegramMessageSplitter.cs      # Divisão de mensagens longas
│   └── TelegramTextFormatter.cs        # Formatação de texto
│
├── IA/
    │   ├── Router/
    │   │   ├── RouterService.cs            # Intake + classificação
    │   │   ├── RouterParser.cs             # Parse JSON do router
    │   │   └── Brief.cs                    # Modelo do brief estruturado
    │   │
    │   ├── Pipelines/
    │   │   ├── IPipelineStep.cs            # Interface de step
    │   │   ├── PipelineContext.cs          # Contexto compartilhado
    │   │   ├── PipelineRunner.cs           # Executor de pipeline
    │   │   ├── StepRecords.cs              # Records (MarcaEmail, CopyEmailSlots)
    │   │   ├── EstrategiaEmail.cs          # Contexto de estratégia por fase
    │   │   │
    │   │   └── Email/
    │   │       ├── PipelineEmail.cs        # Composição da pipeline email
    │   │       ├── StepEstrategiaEmail.cs  # Retrieval de estratégia
    │   │       ├── StepMarcaEmail.cs       # Retrieval de marca
    │   │       ├── StepCopyEmail.cs        # LLM copy
    │   │       ├── StepImagemHero.cs       # LLM prompt + API imagem
    │   │       ├── StepTemplateEmail.cs    # Template HTML slots
    │   │       └── StepQaEmail.cs          # QA com branch explícito
│   │
│   ├── OpenRouterService.cs            # LLM + API imagem via OpenRouter
│   ├── IServicoChat.cs                 # Interface para LLM
│   ├── IGeradorImagem.cs               # Interface para geração de imagem
│   ├── IAnalisadorImagem.cs            # Interface para análise de imagem
│   ├── JsonHelper.cs                   # Utilitário para extração de JSON
│   └── ResultadoPipeline.cs            # Resultado final (HTML + imagens + assets)
│
├── Referencias/
│   ├── IReferenciasCliente.cs          # Interface para referências
│   ├── ReferenciaClienteLoader.cs      # Loader de referências + estratégia
│   ├── AssetVisual.cs                  # Modelo de asset visual
│   ├── EstrategiaCliente.cs            # Modelos de estratégia de jornada
│   └── FaseJornada.cs                  # Chaves canônicas de fase + normalizador
│
├── Configuracoes/
│   ├── OpenRouterOptions.cs            # Config OpenRouter
│   ├── TelegramOptions.cs              # Config Telegram
│   ├── PreFlightOptions.cs             # Config router (max rodadas)
│   └── SegurancaOptions.cs             # Config segurança
│
├── Seguranca/
│   ├── AnonimizadorService.cs          # Anonimização de dados sensíveis
│   └── RateLimiterService.cs           # Rate limiting
│
├── Observabilidade/
│   ├── LangfuseInterceptor.cs          # Interceptor para Langfuse
│   └── LangfuseClient.cs               # HTTP client para Langfuse
│
├── Contracts/
│   ├── LangfuseTrace.cs                # Modelo de trace
│   └── LangfuseTraceContext.cs         # Contexto de trace
```

## Modelo de Dados

### Brief

```csharp
public record Brief(
    string Canal,           // email | instagram | landing
    string? Objetivo,       // vender | engajar | informar
    string? Publico,        // Descrição do público-alvo
    string? Oferta,         // Produto/serviço em oferta
    string? Tom,            // Tom de voz desejado
    string? Link,           // URL do CTA
    List<string> Restricoes,// Restrições adicionais
    List<ImagemBrief> Imagens, // Imagens a gerar (papel + descrição)
    string? EtapaJornada,   // pos-compra | pre-chaves | pos-chaves (quando cliente tem estratégia)
    string? SubJornada      // Sub-jornada específica (quando aplicável)
);

public record ImagemBrief(string Papel, string Descricao);
```

### RouterResultado

```csharp
public record RouterResultado(
    string Tipo,            // conversa | esclarecimento | producao | fora_contexto
    string? Resposta,       // Resposta direta (conversa)
    List<string> Perguntas, // Perguntas de esclarecimento
    string? Cliente,        // Cliente identificado
    Brief? Brief            // Brief estruturado (producao)
);
```

### PipelineContext

```csharp
public class PipelineContext
{
    public long ChatId { get; init; }
    public Brief Brief { get; init; } = null!;
    public string MensagemOriginal { get; init; } = string.Empty;
    public string? Cliente { get; init; }
    
    public EstrategiaEmail? Estrategia { get; set; }
    public MarcaEmail? Marca { get; set; }
    public string? LogoSrc { get; set; }
    public CopyEmailSlots? Copy { get; set; }
    public string? HeroSrc { get; set; }
    public string? Html { get; set; }
    
    public string? QaFeedback { get; set; }
    public string? QaStepAlvo { get; set; }
    public bool QaAprovado { get; set; }
    public int Refacoes { get; set; }
    
    public ResultadoPipeline Resultado { get; } = new();
}
```

## Decisões de Arquitetura

| Decisão | Justificativa | Alternativas consideradas |
|---------|---------------|---------------------------|
| Router + Pipelines (ao invés de loop multi-agente) | Elimina "telefone sem fio", reduz custo de coordenação, contexto mínimo por step | Loop de orquestração (8 agentes conversando) |
| Steps determinísticos (ao invés de agentes decidindo ordem) | Ordem conhecida em design-time, previsível, testável | Agentes decidindo ordem em runtime |
| Template HTML table-based (ao invés de HTML livre) | Compatibilidade com Outlook/Gmail, testável, previsível | LLM gerando HTML livre (16k tokens, bugs de layout) |
| QA com branch explícito (ao invés de loop de conversa) | Retry determinístico ao step alvo, max refações | QA conversando com outros agentes |
| Contexto mínimo por step | Cada step recebe só o necessário (marca, brief, step anterior) | Contexto compartilhado gigante (todos os steps) |
| Single Router (ao invés de Refinador + Montador) | Uma única LLM call classifica + estrutura brief | Duas LLM calls (refinador + montador) |

## Trade-offs

### Simplicidade vs Flexibilidade

```mermaid
quadrantChart
    title Simplicidade vs Flexibilidade
    x-axis "Simples" --> "Flexível"
    y-axis "Baixo Custo" --> "Alto Custo"
    quadrant-1 "Evitar"
    quadrant-2 "Complexo mas flexível"
    quadrant-3 "Ideal para PoC"
    quadrant-4 "Simples e eficiente"
    "Router + Pipelines": [0.3, 0.2]
    "Loop multi-agente": [0.8, 0.8]
    "Template HTML": [0.2, 0.1]
    "HTML livre (LLM)": [0.9, 0.7]
```

## Evolução Futura

```mermaid
roadmap
    title Evolução do DemoAgencia
    section PoC (Atual)
    Router + Pipeline Email :done, 2026-10, 2026-10
    Template HTML table-based :done, 2026-10, 2026-10
    QA com retry :done, 2026-10, 2026-10
    
    section Próximos Passos
    Pipeline Instagram :2026-11, 2026-12
    Pipeline Landing :2027-01, 2027-02
    A/B testing de copy :2027-02, 2027-03
    
    section Produção
    Integração com CRM :2027-03, 2027-04
    Analytics de conversão :2027-04, 2027-05
    Multi-tenant :2027-05, 2027-06
```

## Referências

- [RUNBOOK.md](../RUNBOOK.md) - Guia de deploy e operação
- [CHECKLIST.md](../CHECKLIST.md) - Checklist de aceite E2E
- [README.md](../README.md) - Visão geral do projeto
