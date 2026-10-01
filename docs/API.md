# API Reference - Telegram Bot

## Comandos

### `/start`

Mensagem de boas-vindas.

**Exemplo:**
```
/start
```

**Resposta:**
```
Bem-vindo! Sou o DemoAgencia Bot. Use /help para ver os comandos disponíveis.
```

---

### `/help`

Lista todos os comandos disponíveis e agentes.

**Exemplo:**
```
/help
```

**Resposta:**
```
Comandos:
/start - Inicia o bot
/help - Mostra esta ajuda
/agentes - Lista agentes disponíveis
/limpar - Limpa histórico do chat
/reset - Deseleciona agente e limpa histórico

Mensagens livres são processadas pelo pipeline multi-agente.

Agentes (atalhos diretos):
/redator - Redator: Especialista em copywriting
/dev - Dev: Especialista em desenvolvimento
/estrategista - Estrategista: Especialista em estratégia
```

---

### `/agentes`

Lista todos os agentes disponíveis com descrição e comandos.

**Exemplo:**
```
/agentes
```

**Resposta:**
```
Agentes disponíveis:

• Redator - Especialista em copywriting e criação de conteúdo persuasivo
  Comandos: /redator

• Dev - Especialista em desenvolvimento de software e arquitetura de código
  Comandos: /dev

• Estrategista - Especialista em estratégia de negócios e planejamento
  Comandos: /estrategista
```

---

### `/redator`

Seleciona o agente Redator (copywriting).

**Uso sem args:**
```
/redator
```
**Resposta:** `Agente Redator selecionado. Envie sua mensagem para interagir.`

**Uso com args:**
```
/redator Escreva um slogan para uma cafeteria
```
**Resposta:** [Resposta gerada pelo LLM com persona do redator]

**Modelo:** `anthropic/claude-3.5-sonnet`

---

### `/dev`

Seleciona o agente Dev (desenvolvimento).

**Uso sem args:**
```
/dev
```
**Resposta:** `Agente Dev selecionado. Envie sua mensagem para interagir.`

**Uso com args:**
```
/dev Crie uma função em Python que soma dois números
```
**Resposta:** [Código gerado pelo LLM com persona do dev]

**Modelo:** `anthropic/claude-3.5-sonnet`

---

### `/estrategista`

Seleciona o agente Estrategista (planejamento).

**Uso sem args:**
```
/estrategista
```
**Resposta:** `Agente Estrategista selecionado. Envie sua mensagem para interagir.`

**Uso com args:**
```
/estrategista Como aumentar vendas de um e-commerce?
```
**Resposta:** [Estratégia gerada pelo LLM com persona do estrategista]

**Modelo:** `meta-llama/llama-3.1-70b-instruct`

---

### `/limpar`

Limpa o histórico de conversas do chat atual.

**Exemplo:**
```
/limpar
```

**Resposta:** `Historico limpo.`

---

### `/reset`

Deseleciona o agente ativo e limpa o histórico.

**Exemplo:**
```
/reset
```

**Resposta:** `Agente deselecionado e historico limpo.`

---

## Mensagens Livres

Mensagens livres (sem comando) são processadas pelo pipeline multi-agente:

```mermaid
flowchart TD
    MSG[Mensagem Livre] --> ORQ[Orquestrador]
    ORQ --> DEC{Decisão}
    DEC -->|fora_contexto| FIX[Mensagem Fixa]
    DEC -->|direta| DIR[Resposta Direta]
    DEC -->|pipeline| PIPE[Pipeline Completo]
    
    PIPE --> EST[Estrategista Planeja]
    EST --> PROD[Produção]
    PROD --> QA[Qualidade]
    QA -->|Aprovado| APROV[Estrategista Aprova]
    QA -->|Reprovado| PROD
    APROV -->|Aprovado| FORM[Formatador]
    APROV -->|Reprovado| PROD
    FORM --> RESP[Resposta Final]
```

### Rotas do Orquestrador

| Rota | Descrição | Exemplo |
|------|-----------|---------|
| `fora_contexto` | Fora do escopo da Suspira (Marketing) | "Qual a capital do Brasil?" |
| `direta` | Pergunta simples dentro do contexto | "O que é marketing de conteúdo?" |
| `pipeline` | Tarefa de produção complexa | "Crie um post para Instagram" |

### Pipeline Completo

O pipeline executa as seguintes etapas:

1. **🧠 Orquestrador**: Analisa intenção e decide rota
2. **📋 Estrategista**: Planeja execução e seleciona agente de produção
3. **✍️ Produção**: Agente especializado executa a tarefa
4. **🔍 Qualidade**: Revisa o output (aprovado/reprovado)
5. **✅ Estrategista**: Aprova o resultado final
6. **📤 Formatador**: Formata resposta para o Telegram

**Máximo de refações:** 2 (configurável)

**Progresso:** O bot atualiza a mensagem com o progresso de cada etapa.

### Agentes de Produção

| Agente | Especialidade | Modelo |
|--------|---------------|--------|
| Redator | Copywriting e conteúdo | Claude 3.5 Sonnet |
| Dev | Desenvolvimento e código | Claude 3.5 Sonnet |
| Editor de Imagens | Direção de arte e geração | Claude 3.5 Sonnet + GPT Image |

### Geração de Imagens

Para solicitar imagens, use mensagens naturais:

**Exemplo:**
```
Crie uma imagem de um gato azul em estilo cyberpunk
```

**Fluxo:**
1. Orquestrador detecta intenção de imagem
2. Pipeline roteia para Editor de Imagens
3. Editor enriquece o prompt com detalhes de direção de arte
4. Imagem é gerada via GPT Image
5. QA revisa o prompt otimizado
6. Estrategista aprova
7. Imagem enviada com legenda (pedido original)

**Resposta:** [Imagem gerada enviada como foto]

---

## Fotos

Envie uma foto para análise multimodal.

**Sem legenda:**
```
[Envia foto]
```
**Resposta:** Descrição da imagem pelo Gemini Flash.

**Com legenda:**
```
[Envia foto] O que tem nesta imagem?
```
**Resposta:** Análise contextual da imagem.

---

## Streaming

Todas as respostas de texto são enviadas com streaming (edição progressiva):

1. Mensagem inicial enviada imediatamente
2. Mensagem editada a cada ~1 segundo
3. Resposta final quando completa

**Indicador:** "typing..." enquanto processa.

---

## Histórico

- **Limite:** 20 mensagens por chat
- **Isolamento:** Cada chat tem histórico independente
- **Persistência:** In-memory (perdido ao reiniciar)
- **Limpar:** `/limpar` ou `/reset`

---

## Códigos de Erro

| Situação | Resposta |
|----------|----------|
| Comando desconhecido | "Comando não reconhecido. Use /help..." |
| Erro no LLM | "Desculpe, ocorreu um erro..." |
| Erro na imagem | "Erro ao gerar imagem. Tente novamente." |
| Erro na análise | "Erro ao analisar a imagem..." |
| Token não configurado | Log de erro (bot não inicia) |

---

## Rate Limits

- **Telegram:** 30 msgs/seg para o bot
- **Streaming:** Edição throttled a 1s
- **OpenRouter:** Depende do plano (pay-per-use)
- **Langfuse:** 50k traces/mês (free tier)
