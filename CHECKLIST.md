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
- [ ] `/help` - Lista comandos disponíveis (/start, /help)
- [ ] `/comando_inexistente` - Bot responde com erro amigável
- [ ] Mensagem livre com intenção de email marketing - Pipeline de email executada
- [ ] Enviar foto com legenda - Bot analisa a imagem com contexto

### Router - Classificação de Mensagens

#### Conversa (resposta direta)
- [ ] Mensagem "O que é marketing de conteúdo?" - Router classifica como `conversa`, responde diretamente
- [ ] Mensagem "Qual a capital do Brasil?" - Router classifica como `fora_contexto`, recusa

#### Esclarecimento (perguntas)
- [ ] Mensagem "Quero criar um email" (sem detalhes) - Router pede esclarecimentos (máx 2 rodadas)
- [ ] Responder às perguntas - Pipeline continua com informações fornecidas
- [ ] Não responder - Timeout após 15 minutos (configurável em PreFlight__TimeoutMinutosPendencia)

#### Fora de Contexto
- [ ] Mensagem "Qual a previsão do tempo?" - Router classifica como `fora_contexto`, mensagem específica (assunto_fora_escopo)
- [ ] Mensagem "Crie um email para Acme" (cliente não permitido) - Router classifica como `fora_contexto` (cliente_nao_permitido)
- [ ] Mensagem "Crie um post para Instagram para MRV" (canal não permitido) - Router classifica como `fora_contexto` (canal_nao_permitido)
- [ ] Mensagem "Crie um email de boas-vindas" (sem cliente) - Router pede esclarecimento sobre qual cliente

#### Produção (pipeline)
- [ ] Mensagem "Crie um email sobre Black Friday" - Pipeline email executada
- [ ] Mensagem "Crie um post para Instagram" - Router classifica como `fora_contexto` (canal_nao_permitido)

### Pipeline de Email

#### Estruturação do Brief
- [ ] Mensagem "Email sobre Black Friday com 50% off" - Router extrai: canal=email, objetivo=vender, oferta=Black Friday
- [ ] Mensagem "Email urgente sobre lançamento" - Router extrai: tom=urgente, objetivo=informar
- [ ] Mensagem "Email para jovens sobre app" - Router extrai: público=jovens

#### Steps da Pipeline
- [ ] StepEstrategiaEmail - Carrega fase, paleta, temas, sub-jornada, mapa emocional e satisfações
- [ ] StepMarcaEmail - Carrega logo, cores, tom de voz do cliente (se referências existem)
- [ ] StepCopyEmail - Gera: assunto, preheader, título, saudação, corpo, CTA, rodapé
- [ ] StepImagemHero - Seleciona banner por afinidade (etapa/sub-jornada/mensagem); se banner afim, pula IA; senão gera prompt + imagem hero
- [ ] StepTemplateEmail - HTML table-based, CSS inline, ghost tables, max-width 600px, {{banner_section}} e {{hero_section}} condicionais
- [ ] StepAssetsEmail - Resolve referências assets/ no HTML e empacota assets correspondentes
- [ ] StepQaEmail - Avalia entregável, aprova ou reprova (max 2 refações)

#### QA com Retry
- [ ] QA aprova na primeira tentativa - Pipeline completa normalmente
- [ ] QA reprova com feedback "copy muito longa" - Volta ao StepCopyEmail, refaz com feedback
- [ ] QA reprova 2x - Retorna mensagem de falha com feedback do QA

#### Entrega
- [ ] Pipeline completa - Zip enviado com HTML + imagens + assets
- [ ] Zip contém `entregavel.html` com template table-based
- [ ] Zip contém `imagens/gerada_1.png` (se hero gerado via IA)
- [ ] Zip contém `assets/banner_{nome}.png` (se banner afim à etapa selecionado)
- [ ] Zip contém `assets/logo.png` (se cliente tem logo)
- [ ] Zip contém `assets/logoMRVCO.png`, ícones sociais (se referenciados no template)

### Multimodal (Análise de Fotos)
- [ ] Enviar foto sem legenda - Bot descreve a imagem
- [ ] Enviar foto com legenda "O que tem nesta imagem?" - Bot analisa com contexto

### Rate Limiting
- [ ] Enviar muitas mensagens rápido - Bot responde "Você está enviando mensagens muito rápido"
- [ ] Aguardar - Bot volta a responder normalmente

## Observabilidade

### Langfuse
- [ ] Acessar dashboard Langfuse
- [ ] Verificar trace `router` (etapaNome="router")
- [ ] Verificar trace `email_copy` (etapaNome="email_copy")
- [ ] Verificar trace `email_hero_prompt` (etapaNome="email_hero_prompt")
- [ ] Verificar trace `email_hero_imagem` (etapaNome="email_hero_imagem")
- [ ] Verificar trace `email_qa` (etapaNome="email_qa")
- [ ] Verificar métricas: tokens, latência, modelo usado por step
- [ ] Verificar traces de análise de imagem (etapaNome="image-analysis")

### Logs Locais
- [ ] `docker-compose logs -f` mostra logs em tempo real
- [ ] Logs contêm informações de cada step (StepMarcaEmail, StepCopyEmail, etc.)
- [ ] Arquivo `logs/demo-log-YYYY-MM-DD.txt` é criado
- [ ] Logs são rotacionados diariamente

### Grafana Loki
- [ ] Acessar Grafana
- [ ] Verificar logs estruturados
- [ ] Filtrar por step (e.g., `step="StepTemplateEmail"`)
- [ ] Verificar métricas de duração por step

## Tratamento de Erros

### Router
- [ ] JSON inválido do router - Sistema faz 1 retry automático
- [ ] JSON inválido após retry - Retorna "Não consegui entender seu pedido"
- [ ] Cliente não encontrado - Pipeline continua sem marca (usa defaults)

### Pipeline
- [ ] StepCopyEmail falha - Pipeline aborta, mensagem de erro retornada
- [ ] StepImagemHero falha com banner selecionado - Pipeline continua com banner (sem IA)
- [ ] StepImagemHero falha sem banner - Pipeline continua sem imagem hero (degradado)
- [ ] StepTemplateEmail falha - Pipeline aborta, mensagem de erro retornada
- [ ] StepQaEmail falha - Pipeline retorna HTML sem QA (degradado)

### Reconexão Telegram
- [ ] Simular perda de conexão (parar/rede)
- [ ] Verificar se bot reconecta automaticamente
- [ ] Verificar backoff exponencial nos logs

### Fallback de Modelos (429)
- [ ] 429 do OpenRouter - Sistema faz fallback para próximo modelo (verificar logs com "Fallback para")
- [ ] 429 em todos os modelos - Pipeline retorna erro amigável ao usuário
- [ ] 429 na geração de imagem - Pipeline continua sem imagem hero (degradado)
- [ ] Trace Langfuse registra modelo efetivamente usado após fallback

### Mensagens Inválidas
- [ ] Enviar comando inexistente - Bot responde com erro amigável
- [ ] Enviar foto muito grande - Bot trata erro graciosamente

## Performance

- [ ] Router (classificação): < 5 segundos
- [ ] StepCopyEmail (LLM): < 15 segundos
- [ ] StepImagemHero (LLM + API): < 30 segundos
- [ ] StepTemplateEmail (determinístico): < 1 segundo
- [ ] StepQaEmail (LLM): < 10 segundos
- [ ] Pipeline completa (sem hero): < 45 segundos
- [ ] Pipeline completa (com hero): < 90 segundos
- [ ] Análise de imagem: < 15 segundos
- [ ] Uso de memória: < 512 MB

## Segurança

- [ ] Arquivo `.env` não está no repositório
- [ ] Credenciais não aparecem nos logs (AnonimizadorService)
- [ ] VM com firewall configurado (apenas SSH liberado)
- [ ] Rate limiting funcionando (max mensagens por minuto)

## Documentação

- [ ] README.md atualizado (arquitetura router + pipelines)
- [ ] ARCHITECTURE.md atualizado (fluxo da pipeline email)
- [ ] CHECKLIST.md atualizado (testes funcionais)
- [ ] RUNBOOK.md completo
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
