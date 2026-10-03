# Ferramentas e Servicos

Visao geral das ferramentas e servicos utilizados no projeto DemoAgencia.

## Visao Geral

```mermaid
graph TB
    subgraph "Desenvolvimento e CI/CD"
        GIT[GitHub]
        CI[GitHub Actions]
        CC[Codecov]
    end

    subgraph "Infraestrutura"
        OCI[Oracle Cloud]
    end

    subgraph "Observabilidade"
        LF[Langfuse]
        GF[Grafana Loki]
    end

    subgraph "IA"
        OR[OpenRouter]
    end

    GIT -->|Push e PRs| CI
    CI -->|Build e testes| CC
    CI -->|Deploy via SSH| OCI
    OCI -->|Executa| APP[DemoAgencia Worker]
    APP -->|Traces LLM| LF
    APP -->|Logs| GF
    APP -->|Chamadas de IA| OR
```

---

## GitHub

**Funcao:** Repositorio de codigo-fonte, controle de versao e CI/CD.

| Aspecto | Detalhe |
|---------|---------|
| Tipo | Plataforma DevOps |
| Uso no projeto | Hospedagem do repositorio, pull requests, revisao de codigo |
| CI/CD | GitHub Actions para build, testes e deploy automatizado |
| Plano | Free (repositorio publico ou privado com limites) |

**Fluxo de CI/CD:**

```mermaid
flowchart LR
    DEV[Developer] -->|git push| GH[GitHub]
    GH -->|trigger| GHA[GitHub Actions]
    GHA -->|build| BUILD[dotnet build]
    GHA -->|test| TEST[dotnet test]
    GHA -->|coverage| COV[Codecov Upload]
    GHA -->|deploy| SSH[SSH para Oracle Cloud]
    SSH -->|docker pull + run| OCI[Oracle Cloud VM]
```

**Recursos utilizados:**

- Repositorio Git com historico versionado
- Pull requests para revisao de codigo
- GitHub Actions (workflows YAML) para automatizacao
- Secrets para credenciais (tokens, chaves de API)
- Deploy automatico via SSH para a VM na Oracle Cloud

---

## OpenRouter

**Funcao:** Gateway unificado de acesso a modelos de linguagem (LLM).

| Aspecto | Detalhe |
|---------|---------|
| Tipo | API Gateway para LLMs |
| Uso no projeto | Acesso a multiplos modelos (Qwen, DeepSeek) com uma unica API |
| Integracao | Via Semantic Kernel (SK) no `OpenRouterService` |
| Plano | Pay-per-use (cobranca por tokens consumidos) |

**Modelos utilizados:**

| Modelo | Agente | Finalidade |
|--------|--------|------------|
| `qwen/qwen3.7-plus` | Redator, Prompt para Imagens | Copywriting e direcao de arte |
| `qwen/qwen-2.5-coder-32b-instruct` | Dev | Geracao de codigo HTML |
| `deepseek/deepseek-r1-0528` | Estrategista, Qualidade | Estrategia e revisao critica |
| `deepseek/deepseek-v3.2` | Orquestrador | Loop supervisor |
| `qwen/qwen2.5-vl-72b-instruct` | (visao) | Analise de imagens (multimodal) |
| `qwen/qwen-image-3-pro` | (ferramenta) | Geracao de imagens |

**Vantagens:**

- Unifica acesso a dezenas de modelos em uma so API
- Permite trocar de modelo sem alterar a integracao
- Suporte nativo a streaming e function calling
- Politica de coleta de dados configuravel (`DataCollection: deny`)

**Configuracao:**

```json
{
  "OpenRouter": {
    "ApiKey": "<chave>",
    "DataCollection": "deny"
  }
}
```

---

## Langfuse

**Funcao:** Observabilidade e rastreamento de chamadas LLM (LLMOps).

| Aspecto | Detalhe |
|---------|---------|
| Tipo | Plataforma LLMOps |
| Uso no projeto | Traces de cada interacao com LLM (input, output, tokens, duracao) |
| Integracao | `LangfuseInterceptor` + `LangfuseClient` via HTTP API |
| Plano | Cloud Free Tier (50k traces/mes) |

**Fluxo de rastreamento:**

```mermaid
flowchart LR
    A[OpenRouterService] -->|inicia trace| B[LangfuseInterceptor]
    B -->|executa LLM| C[OpenRouter API]
    C -->|resposta| B
    B -->|finaliza trace| D[LangfuseClient]
    D -->|POST /api/public/ingestion| E[Langfuse Cloud]
```

**Dados rastreados:**

- ID do trace e sessao
- Modelo utilizado
- Input (mensagem do usuario)
- Output (resposta do LLM)
- Tokens de prompt e completacao
- Duracao da chamada
- UserID (anonimizado quando configurado)

**Configuracao:**

```json
{
  "Langfuse": {
    "PublicKey": "<public-key>",
    "SecretKey": "<secret-key>",
    "Host": "https://cloud.langfuse.com"
  }
}
```

---

## Grafana Loki

**Funcao:** Agregacao e consulta de logs da aplicacao.

| Aspecto | Detalhe |
|---------|---------|
| Tipo | Sistema de logging |
| Uso no projeto | Centralizacao de logs estruturados do worker |
| Integracao | Serilog com sink para Grafana Loki |
| Plano | Grafana Cloud Free Tier |

**Fluxo de logs:**

```mermaid
flowchart LR
    APP[DemoAgencia Worker] -->|Serilog| SINK[Loki Sink]
    SINK -->|HTTP push| LOKI[Grafana Loki]
    LOKI -->|consulta| GRAFANA[Grafana Dashboard]
```

**Caracteristicas:**

- Logs estruturados com propriedades (`{Property}`)
- Rotulacao por ambiente e aplicacao
- Consulta via LogQL no Grafana
- Complementa os logs locais em arquivo

**Configuracao:**

```json
{
  "GrafanaLoki": {
    "Endpoint": "<endpoint>",
    "LoginId": "<login-id>",
    "Password": "<api-key>"
  }
}
```

---

## Codecov

**Funcao:** Relatorio e monitoramento de cobertura de testes.

| Aspecto | Detalhe |
|---------|---------|
| Tipo | Plataforma de cobertura de codigo |
| Uso no projeto | Analise de cobertura dos 103 testes unitarios |
| Integracao | Upload via GitHub Actions apos `dotnet test --collect:"XPlat Code Coverage"` |
| Plano | Free para repositorios publicos |

**Fluxo:**

```mermaid
flowchart LR
    GHA[GitHub Actions] -->|dotnet test --coverage| XML[Arquivo .xml]
    XML -->|upload| CC[Codecov]
    CC -->|badge e relatorio| PR[Pull Request]
```

**Recursos utilizados:**

- Badge de cobertura no README
- Comentario automatico em PRs com diff de cobertura
- Historico de evolucao da cobertura ao longo do tempo
- Validacao de minimo de cobertura (quando configurado)

---

## Oracle Cloud

**Funcao:** Infraestrutura de hospedagem do worker em producao.

| Aspecto | Detalhe |
|---------|---------|
| Tipo | Cloud IaaS (Infraestrutura como Servico) |
| Uso no projeto | VM ARM64 para execucao do container Docker |
| Integracao | Deploy via SSH a partir do GitHub Actions |
| Plano | Always Free Tier (gratuito permanentemente) |

**Recursos utilizados:**

| Recurso | Especificacao |
|---------|---------------|
| Compute | VM ARM64 Ampere A1 (4 OCPUs, 24 GB RAM) |
| SO | Ubuntu Server |
| Rede | VPC com IP publico |
| Armazenamento | Block Volume 50 GB |

**Arquitetura de deploy:**

```mermaid
graph TB
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

    subgraph "Externos"
        TG[Telegram API]
        OR[OpenRouter API]
        LF[Langfuse]
    end

    DOCKER -->|run| APP
    APP -->|Long Polling| TG
    APP -->|API calls| OR
    APP -->|Traces| LF
    APP -->|Write| LOGS
    APP -->|Read| ASSETS
```

**Vantagens do Always Free Tier:**

- VM ARM64 generosa (4 CPUs, 24 GB RAM) gratuitamente
- Sem custo para PoC e ambientes de desenvolvimento
- Armazenamento em bloco incluso
- Suficiente para rodar o worker + Docker

---

## Resumo da Integracao

```mermaid
graph LR
    subgraph "Desenvolvimento"
        GH[GitHub] -->|CI/CD| GHA[GitHub Actions]
        GHA -->|coverage| CC[Codecov]
    end

    subgraph "Producao"
        GHA -->|SSH deploy| OCI[Oracle Cloud]
        OCI --> APP[DemoAgencia]
    end

    subgraph "Servicos"
        APP -->|LLM| OR[OpenRouter]
        APP -->|Traces| LF[Langfuse]
        APP -->|Logs| GF[Grafana Loki]
    end
```

| Ferramenta | Categoria | Custo | Critico? |
|------------|-----------|-------|----------|
| GitHub | CI/CD e codigo | Free | Sim |
| OpenRouter | IA (LLMs) | Pay-per-use | Sim |
| Langfuse | Observabilidade LLM | Free (50k traces/mes) | Nao |
| Grafana Loki | Logs | Free Tier | Nao |
| Codecov | Cobertura de testes | Free (repos publicos) | Nao |
| Oracle Cloud | Infraestrutura | Always Free | Sim |

## Referencias

- [ARCHITECTURE.md](./ARCHITECTURE.md) - Arquitetura detalhada do sistema
- [DEVELOPMENT.md](./DEVELOPMENT.md) - Guia de desenvolvimento e variaveis de ambiente
- [RUNBOOK.md](../RUNBOOK.md) - Guia de deploy e operacao
