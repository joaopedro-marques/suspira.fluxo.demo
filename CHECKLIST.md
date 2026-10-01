# Checklist de Aceite E2E - DemoAgencia

## Pré-deploy

- [ ] VM Oracle criada e acessível via SSH
- [ ] Docker instalado na VM
- [ ] Token do Telegram obtido (BotFather)
- [ ] API Key do OpenRouter obtida
- [ ] Conta Langfuse criada (chaves obtidas)
- [ ] Arquivo `.env` configurado com todas as credenciais

## Deploy

- [ ] Código transferido para a VM
- [ ] Imagem Docker construída com sucesso
- [ ] Container iniciado (`docker-compose up -d`)
- [ ] Container com status "Up"
- [ ] Logs mostram "Bot conectado"

## Testes Funcionais

### Comandos Básicos
- [ ] `/start` - Bot responde com mensagem de boas-vindas
- [ ] `/help` - Lista todos os comandos disponíveis
- [ ] `/agentes` - Lista os agentes públicos (Redator, Dev, Estrategista)
- [ ] `/limpar` - Limpa histórico do chat
- [ ] `/reset` - Deseleciona agente e limpa histórico

### Interação Direta com Agentes (Bypass)
- [ ] `/redator` - Seleciona agente redator
- [ ] `/redator Escreva um slogan para uma cafeteria` - Responde com copy criativa (streaming)
- [ ] `/dev` - Seleciona agente dev
- [ ] `/dev Crie uma função em Python que soma dois números` - Responde com código (streaming)
- [ ] `/estrategista` - Seleciona agente estrategista
- [ ] `/estrategista Como aumentar vendas de um e-commerce?` - Responde com estratégia (streaming)

### Pipeline Multi-Agente
- [ ] Mensagem "Crie um post para Instagram" - Pipeline completo com progresso visível
- [ ] Mensagem "O que é marketing de conteúdo?" - Rota direta (resposta simples)
- [ ] Mensagem "Qual a capital do Brasil?" - Fora do contexto (mensagem fixa)
- [ ] Pipeline mostra progresso: 🧠 → 📋 → ✍️ → 🔍 → ✅ → 📤
- [ ] Qualidade reprova e pipeline refaz (máx 2 refações)
- [ ] Pipeline excede refações - Mensagem de falha retornada

### Geração de Imagens (via Pipeline)
- [ ] Mensagem "Crie uma imagem de um gato azul" - Pipeline roteia para Editor de Imagens
- [ ] Editor enriquece prompt com detalhes de direção de arte
- [ ] Imagem gerada e enviada com legenda (pedido original)
- [ ] QA revisa o prompt otimizado antes da geração

### Streaming (Comandos Diretos)
- [ ] Mensagens via comando aparecem progressivamente (edição da mensagem)
- [ ] Respostas longas são atualizadas em tempo real
- [ ] Não há erros de rate limit do Telegram

### Multimodal (Análise de Fotos)
- [ ] Enviar foto sem legenda - Bot descreve a imagem
- [ ] Enviar foto com legenda "O que tem nesta imagem?" - Bot analisa com contexto

### Histórico
- [ ] Enviar múltiplas mensagens - Bot mantém contexto da conversa
- [ ] `/limpar` seguido de nova mensagem - Histórico resetado
- [ ] `/reset` - Agente deselecionado e histórico limpo

## Observabilidade

### Langfuse
- [ ] Acessar dashboard Langfuse
- [ ] Verificar traces das interações
- [ ] Verificar métricas: tokens, latência, modelo usado
- [ ] Verificar traces do pipeline (orquestrador, estrategista, produção, qualidade, formatador)
- [ ] Verificar traces de análise de imagem
- [ ] Verificar traces de geração de imagem

### Logs Locais
- [ ] `docker-compose logs -f` mostra logs em tempo real
- [ ] Logs contêm informações de roteamento
- [ ] Logs contêm informações de streaming
- [ ] Arquivo `logs/demo-log-YYYY-MM-DD.txt` é criado
- [ ] Logs são rotacionados diariamente

## Tratamento de Erros

### Fallback de Modelos
- [ ] Simular erro em um modelo (ex: modelo indisponível)
- [ ] Verificar se fallback funciona (string "modelo1,modelo2")
- [ ] Verificar se try-catch captura erros e retorna mensagem amigável

### Reconexão Telegram
- [ ] Simular perda de conexão (parar/rede)
- [ ] Verificar se bot reconecta automaticamente
- [ ] Verificar backoff exponencial nos logs

### Mensagens Inválidas
- [ ] Enviar comando inexistente - Bot responde com erro amigável
- [ ] Enviar foto muito grande - Bot trata erro graciosamente
- [ ] Pipeline com JSON inválido do orquestrador - Fallback para rota direta

## Performance

- [ ] Respostas de texto (comando direto): < 10 segundos (depende do modelo)
- [ ] Pipeline completo: < 60 segundos (múltiplas chamadas LLM)
- [ ] Streaming: primeira edição em < 2 segundos
- [ ] Análise de imagem: < 15 segundos
- [ ] Geração de imagem: < 30 segundos
- [ ] Uso de memória: < 512 MB

## Segurança

- [ ] Arquivo `.env` não está no repositório
- [ ] Credenciais não aparecem nos logs
- [ ] VM com firewall configurado (apenas SSH liberado)
- [ ] Docker rodando como usuário não-root

## Documentação

- [ ] README.md atualizado
- [ ] RUNBOOK.md completo
- [ ] Checklist de aceite preenchido
- [ ] Script `deploy.sh` funcional
- [ ] Script `validate.sh` funcional

## Aceite Final

- [ ] Todos os testes funcionais passaram
- [ ] Observabilidade configurada e funcionando
- [ ] Tratamento de erros validado
- [ ] Performance dentro do esperado
- [ ] Segurança verificada
- [ ] Documentação completa

**Status**: [ ] APROVADO / [ ] REPROVADO

**Observações**:
```
[Adicionar observações aqui]
```

**Data**: ____/____/________

**Responsável**: _________________________________
