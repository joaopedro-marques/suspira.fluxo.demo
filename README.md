# DemoAgencia - PoC Telegram + IA

![CI/CD](https://github.com/SEU_USUARIO/DemoAgencia/actions/workflows/ci.yml/badge.svg)
![Tests](https://img.shields.io/badge/tests-517-green)
![.NET](https://img.shields.io/badge/.NET-10-purple)

Sistema de automação de marketing via Telegram com pipeline de email marketing gerada por IA. **Arquitetura agnóstica de cliente**: adicione novos clientes apenas com configuração, sem alterar código.

## Stack

- .NET 10 (Worker Service)
- Telegram.Bot (Long Polling)
- Microsoft Semantic Kernel + OpenRouter
- Serilog (logs locais)
- Langfuse (observabilidade LLM)
- Grafana Loki (observabilidade de logs)

## Arquitetura

### Arquitetura de 2 Camadas

O sistema segue uma arquitetura de **2 camadas** que separa claramente:

**Camada 1 — Agente do Step (Genérica)**
- Comportamento genérico dos steps (StepCopyEmail, StepImagemHero, etc.)
- Arquivos de agente: `Assets/agentes/*.md`
- Não conhece nenhum cliente específico
- Lógica de orquestração, parsing, validação

**Camada 2 — Informação do Cliente (Configuração)**
- Dados específicos por cliente em `Assets/referencias/`
- 100% configurável via arquivos JSON/HTML
- Estrutura:
  ```
  Assets/referencias/
  ├── estrategia/           # Config de jornada por cliente
  │   ├── {CLIENTE}_fases.json              # Fases + sinônimos
  │   ├── {CLIENTE}_paleta_de_cores.json    # Paleta por fase
  │   ├── {CLIENTE}_temas_jornadas.json     # Temas por fase
  │   ├── {CLIENTE}_regua_jornada.json      # Régua (opcional)
  │   └── {CLIENTE}_mapa_emocional.json     # Mapa emocional (opcional)
  ├── footer/               # Footer por cliente
  │   ├── {CLIENTE}_footer.html             # Footer padrão
  │   └── {CLIENTE}_footer_{nome}.html      # Footers nomeados
  ├── templates/            # Templates HTML por cliente
  │   ├── {CLIENTE}_html_*.html
  │   └── {CLIENTE}_html_*.json             # Sidecars
  ├── components/           # Componentes genéricos
  │   ├── hero_section.html
  │   └── banner_section.html
  └── imagens/              # Assets visuais
      ├── logos/
      ├── icons/
      └── banners/
  ```

**Princípio**: Código não sabe o que um email tem — sabe renderizar **slots**. O que o email tem (header, footer, fases, temas, cores) é declarado no perfil do cliente.

**👉 [Guia: Como Adicionar um Novo Cliente](docs/COMO_ADICIONAR_NOVO_CLIENTE.md)**

```
Mensagem → TelegramService (long polling não-bloqueante):
  ├── UpdateDispatcher (modelo ator por chat):
  │     Chats diferentes processam em paralelo
  │     Mesmo chat: fila FIFO (garante ordem no PreFlight)
  │     Aviso "⏳ Aguarde..." se chat já tem mensagem em processamento
  │
  ├── /start, /help → respostas fixas
  └── Mensagem livre → RouterService (intake):
        ├── conversa → resposta direta
        ├── esclarecimento → perguntas (store pendente)
        ├── fora_contexto → recusa
        └── produção:
              ├── Limite global: SemaphoreSlim (MaxPipelinesSimultaneos=3)
              ├── email → PipelineEmail
              │     ├── StepEstrategiaEmail (retrieval: fase, paleta, temas, mapa, satisfações)
              │     ├── StepMapaEmocionalEmail (LLM: associação etapa emocional → fator E/R)
              │     ├── StepMarcaEmail (retrieval: logo, cores, tom)
              │     ├── StepCopyEmail (LLM: assunto, título, corpo, CTA)
              │     ├── StepDiagramacaoEmail (LLM: seções HTML visuais + guarda determinística)
              │     ├── StepImagemHero (retrieval banner + LLM visão + API: imagem opcional)
              │     ├── StepTemplateEmail (HTML table-based com slots)
              │     ├── StepAssetsEmail (resolução de assets referenciados)
              │     └── StepQaEmail (LLM: aprovado/reprovado, max 4 refações)
              └── fora_contexto (canal/cliente fora do escopo configurável)
  → Entrega: zip com HTML + assets + imagens
```

- **Router**: Classifica mensagens (conversa/esclarecimento/produção/fora_contexto), estrutura brief com campos (objetivo, público, canal, oferta, etc.)
- **PipelineEmail**: Steps ordenados deterministicamente, cada um recebe contexto mínimo
- **Steps**: Retrieval (estratégia, marca), LLM (copy, QA, prompt imagem), Template (HTML slots), API (gerar imagem)
- **QA com retry**: StepQaEmail avalia entregável; se reprovar, volta ao step alvo (copy/diagramacao/hero), max 4 refações. Entrega só ocorre após aprovação; caso contrário, feedback é enviado como mensagem
- **Fallback de modelos em 429**: Polly re-tenta no mesmo modelo respeitando Retry-After; se 429 persiste, troca para próximo modelo da cadeia configurável com backoff exponencial
- **Template HTML**: Table-based, CSS inline, ghost tables para Outlook, max-width 600px, CTA bulletproof
- **Descrições de ícones**: Ícones são descritos via visão (IconDescricaoCache com sidecar JSON) e injetados no prompt do diagramador para escolha semântica
- **Observabilidade**: Langfuse traces por step (router, email_estrategia, email_mapa_emocional, email_marca, email_copy, email_diagramacao, email_hero_prompt, email_hero_imagem, email_template, email_assets, email_qa)

## Estrutura

```
src/DemoAgencia.Worker/
  ├── Telegram/              # Cliente Telegram (ITelegramGateway) e handlers
  ├── IA/                    # Router + Pipelines + OpenRouterService
  │   ├── Router/            # RouterService, RouterParser, Brief
  │   ├── PreFlight/         # ConversaPendenteStore, EstadoPreFlight
  │   └── Pipelines/         # PipelineRunner, IPipelineStep
  │   │       └── Email/         # StepEstrategiaEmail, StepMapaEmocionalEmail, StepMarcaEmail, StepCopyEmail,
    │                          # StepDiagramacaoEmail, StepImagemHero, StepTemplateEmail,
    │                          # StepAssetsEmail, StepQaEmail
  │   ├── BannerDescricao.cs     # Record estruturado para análise visual de banners
  │   ├── BannerDescricaoCache.cs # Cache com sidecar JSON para descrições
  │   ├── IconDescricao.cs        # Record estruturado para análise visual de ícones
  │   ├── IconDescricaoCache.cs   # Cache com sidecar JSON para descrições de ícones
  ├── Agentes/               # IAgentesCatalogo + loader de agentes .md
  ├── Referencias/           # IReferenciasCliente (texto + imagens + estratégia + fases)
  │   ├── EstrategiaCliente.cs      # Perfil completo do cliente (fases, paleta, temas, régua)
  │   ├── FasesConfig.cs            # Configuração de fases com sinônimos
  │   ├── ReguaJornada.cs           # Configuração de régua de jornada
  │   └── ReferenciaClienteLoader.cs # Loader que carrega tudo de Assets/referencias/
  ├── Configuracoes/         # Options pattern (PreFlightOptions, OpenRouterOptions, etc.)
  ├── Seguranca/             # AnonimizadorService, RateLimiterService
  ├── Observabilidade/       # Serilog e Langfuse
  └── Contracts/             # LangfuseTrace, LangfuseTraceContext

Assets/
  ├── agentes/               # Agentes em markdown (router.md, redator.md, diagramador.md, hero.md, qa.md, curador.md)
  └── referencias/
      ├── estrategia/        # {cliente}_{tipo}.json (fases, paleta, temas, jornada, régua, mapa)
      ├── footer/            # {cliente}_footer.html (footer padrão)
      │                      # {cliente}_footer_{nome}.html (footers nomeados)
      ├── templates/         # Modelos base de email (com slots {{header}}, {{footer}}, etc.)
      ├── components/        # Componentes genéricos (hero_section.html, banner_section.html)
      └── imagens/           # {cliente}_{tipo}_{nome}.ext (logos/, icons/, banners/)
                             # Banners: classificados pelo diretório banners/, nome descritivo
                             # da etapa da jornada para seleção por afinidade
```

## Execução Local

```bash
# Configurar variáveis de ambiente
export Telegram__BotToken="seu-token"
export OpenRouter__ApiKey="sua-chave"

# Executar
dotnet run --project src/DemoAgencia.Worker/DemoAgencia.Worker.csproj
```

## Deploy

### Preparação

1. Copie `.env.example` para `.env` e preencha as credenciais:
```bash
cp .env.example .env
nano .env
```

2. Configure:
    - `Telegram__BotToken` - Token do BotFather
    - `OpenRouter__ApiKey` - Chave da OpenRouter
    - `Langfuse__PublicKey` / `Langfuse__SecretKey` - Chaves do Langfuse

### Deploy na VM Oracle

```bash
# Opção A: Script automatizado
chmod +x deploy.sh
./deploy.sh

# Opção B: Manual
docker-compose build --no-cache
docker-compose up -d
```

### Validação

```bash
# Executar checklist de validação
chmod +x validate.sh
./validate.sh
```

## Documentação

- **[RUNBOOK.md](RUNBOOK.md)** - Guia completo de deploy e manutenção
- **[CHECKLIST.md](CHECKLIST.md)** - Checklist de aceite E2E
- **[ARCHITECTURE.md](docs/ARCHITECTURE.md)** - Arquitetura detalhada
- **[DEVELOPMENT.md](docs/DEVELOPMENT.md)** - Guia de desenvolvimento
- **[FERRAMENTAS.md](docs/FERRAMENTAS.md)** - Ferramentas e serviços
- **[COMO_ADICIONAR_NOVO_CLIENTE.md](docs/COMO_ADICIONAR_NOVO_CLIENTE.md)** - Guia para adicionar novos clientes sem alterar código

## Docker

```bash
# Build
docker build -t demoagencia .

# Run
docker run -d \
  --name demoagencia \
  -e Telegram__BotToken="seu-token" \
  -e OpenRouter__ApiKey="sua-chave" \
  --restart unless-stopped \
  demoagencia
```

## Testes

```bash
# Rodar todos os testes
dotnet test

# Com coverage
dotnet test --collect:"XPlat Code Coverage"
```

**515 testes** cobrindo: RouterParser, RouterService, StepEstrategiaEmail, StepMapaEmocionalEmail, StepCopyEmail, StepImagemHero, StepTemplateEmail, StepQaEmail, OpenRouterService, OpenRouterPrivacyHandler, OpenRouterReasoningHandler, LangfuseInterceptor, TelegramService, UpdateDispatcher, AnonimizadorService, RateLimiterService, ReferenciaClienteLoader, ConversaPendenteStore, TelegramMessageSplitter, TelegramTextFormatter, AgentesLoader, TemplateCatalogo, BannerDescricaoCache, IconDescricaoCache, TomEmocional.

## CI/CD

Pipeline GitHub Actions com 5 jobs: build+test, docker build, validate structure, security scan, summary.

Ver [RUNBOOK.md](RUNBOOK.md) para detalhes.
