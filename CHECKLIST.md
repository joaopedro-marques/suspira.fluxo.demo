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
- [ ] `/agentes` - Lista os 3 agentes (Redator, Dev, Estrategista)
- [ ] `/limpar` - Limpa histórico do chat
- [ ] `/reset` - Deseleciona agente e limpa histórico

### Interação com Agentes
- [ ] `/redator` - Seleciona agente redator
- [ ] `/redator Escreva um slogan para uma cafeteria` - Responde com copy criativa
- [ ] `/dev` - Seleciona agente dev
- [ ] `/dev Crie uma função em Python que soma dois números` - Responde com código
- [ ] `/estrategista` - Seleciona agente estrategista
- [ ] `/estrategista Como aumentar vendas de um e-commerce?` - Responde com estratégia

### Roteamento Automático
- [ ] Mensagem "Como faço um loop em JavaScript?" - Classifica como "codigo" e usa Claude
- [ ] Mensagem "Me ajude a planejar um lançamento" - Classifica como "estrategia" e usa Llama
- [ ] Mensagem "Escreva um texto persuasivo" - Classifica como "copy" e usa Claude
- [ ] Mensagem "Qual a capital do Brasil?" - Classifica como "geral" e usa Gemini Flash

### Streaming
- [ ] Mensagens de texto aparecem progressivamente (edição da mensagem)
- [ ] Respostas longas são atualizadas em tempo real
- [ ] Não há erros de rate limit do Telegram

### Multimodal
- [ ] Enviar foto sem legenda - Bot descreve a imagem
- [ ] Enviar foto com legenda "O que tem nesta imagem?" - Bot analisa com contexto
- [ ] `/imagem um gato azul em estilo cyberpunk` - Gera e envia imagem

### Histórico
- [ ] Enviar múltiplas mensagens - Bot mantém contexto da conversa
- [ ] `/limpar` seguido de nova mensagem - Histórico resetado
- [ ] `/reset` - Agente deselecionado e histórico limpo

## Observabilidade

### Langfuse
- [ ] Acessar dashboard Langfuse
- [ ] Verificar traces das interações
- [ ] Verificar métricas: tokens, latência, modelo usado
- [ ] Verificar traces de classificação (roteamento automático)
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
- [ ] Enviar `/imagem` sem prompt - Bot responde com uso correto
- [ ] Enviar foto muito grande - Bot trata erro graciosamente

## Performance

- [ ] Respostas de texto: < 10 segundos (depende do modelo)
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
