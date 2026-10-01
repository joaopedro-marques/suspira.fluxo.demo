# Runbook de Deploy - DemoAgencia

## Pré-requisitos

### 1. VM Oracle Cloud (Ubuntu ARM64)

Crie uma instância no Oracle Cloud:
- **Shape**: VM.Standard.A1.Flex (ARM Ampere A1)
- **OS**: Ubuntu 22.04 ou superior
- **Configuração**: 2 OCPU + 12 GB RAM (suficiente para PoC)
- **Rede**: Subnet pública com IP público

### 2. Acessar a VM via SSH

```bash
ssh -i ~/.ssh/oracle_key ubuntu@SEU_IP_PUBLICO
```

### 3. Instalar Docker na VM

```bash
# Atualizar sistema
sudo apt update && sudo apt upgrade -y

# Instalar Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh

# Adicionar usuário ao grupo docker
sudo usermod -aG docker $USER

# Logout e login novamente para aplicar permissões
exit
ssh -i ~/.ssh/oracle_key ubuntu@SEU_IP_PUBLICO

# Verificar instalação
docker --version
docker run hello-world
```

## Configuração Local (Sua Máquina)

### 1. Obter Credenciais

#### Telegram Bot Token
1. Abra o Telegram e procure por [@BotFather](https://t.me/BotFather)
2. Envie `/newbot` e siga as instruções
3. Copie o token gerado

#### OpenRouter API Key
1. Acesse [openrouter.ai](https://openrouter.ai)
2. Crie uma conta e faça login
3. Vá em "Keys" e crie uma nova API key
4. Copie a chave

#### Langfuse (Observabilidade)
1. Acesse [cloud.langfuse.com](https://cloud.langfuse.com)
2. Crie uma conta gratuita
3. Crie um novo projeto
4. Vá em "Settings" → "API Keys"
5. Copie a Public Key e Secret Key

### 2. Preparar Arquivo .env

Na raiz do projeto, crie o arquivo `.env`:

```bash
cp .env.example .env
nano .env
```

Preencha com suas credenciais:

```env
Telegram__BotToken=1234567890:ABCdefGHIjklMNOpqrsTUVwxyz
Telegram__ChatIdsPermitidos=
OpenRouter__ApiKey=sk-or-v1-abc123...
OpenRouter__DataCollection=deny
Langfuse__PublicKey=pk-lf-abc123...
Langfuse__SecretKey=sk-lf-abc123...
Langfuse__Host=https://cloud.langfuse.com
Seguranca__AnonimizarDados=true
Seguranca__MaxMensagensPorMinuto=5
```

## Deploy

### Opção A: Deploy Automatizado (Recomendado)

```bash
# Tornar script executável
chmod +x deploy.sh

# Executar deploy
./deploy.sh
```

### Opção B: Deploy Manual

```bash
# 1. Transferir código para a VM
scp -r -i ~/.ssh/oracle_key . ubuntu@SEU_IP_PUBLICO:~/demoagencia

# 2. Acessar a VM
ssh -i ~/.ssh/oracle_key ubuntu@SEU_IP_PUBLICO

# 3. Navegar até o diretório
cd ~/demoagencia

# 4. Criar arquivo .env
nano .env
# (preencha com as credenciais)

# 5. Construir imagem
docker-compose build --no-cache

# 6. Iniciar container
docker-compose up -d

# 7. Verificar logs
docker-compose logs -f
```

## Verificação

### 1. Testar Bot no Telegram

1. Abra o Telegram
2. Procure pelo seu bot (use o username que você criou)
3. Envie `/start`
4. Teste os comandos:
   - `/help` - Lista de comandos
   - `/agentes` - Lista agentes disponíveis
   - `/redator` - Seleciona agente redator
   - Envie uma mensagem qualquer para testar o LLM
   - `/imagem um gato azul` - Gera imagem

### 2. Verificar Langfuse

1. Acesse [cloud.langfuse.com](https://cloud.langfuse.com)
2. Abra seu projeto
3. Vá em "Traces"
4. Você deve ver os traces das interações

### 3. Verificar Logs

```bash
# Logs em tempo real
docker-compose logs -f

# Últimas 50 linhas
docker-compose logs --tail=50

# Logs do arquivo (dentro do container)
docker-compose exec demoagencia cat /app/logs/demo-log-$(date +%Y-%m-%d).txt
```

## Manutenção

### Comandos Úteis

```bash
# Ver status do container
docker-compose ps

# Reiniciar container
docker-compose restart

# Parar container
docker-compose down

# Atualizar código e redeploy
git pull
docker-compose down
docker-compose build --no-cache
docker-compose up -d

# Limpar imagens antigas
docker image prune -a

# Ver uso de recursos
docker stats demoagencia
```

### Backup

```bash
# Backup dos logs
docker-compose exec demoagencia tar czf /tmp/logs-backup.tar.gz /app/logs
docker cp demoagencia:/tmp/logs-backup.tar.gz ./logs-backup.tar.gz

# Backup do banco de dados (se houver no futuro)
# docker exec demoagencia sqlite3 /app/data/db.sqlite .dump > db-backup.sql
```

## Troubleshooting

### Bot não responde

```bash
# Verificar se container está rodando
docker-compose ps

# Verificar logs
docker-compose logs --tail=50

# Verificar se token está correto
docker-compose exec demoagencia env | grep TELEGRAM
```

### Erro de autenticação OpenRouter

```bash
# Verificar API key
docker-compose exec demoagencia env | grep OpenRouter

# Testar conexão
docker-compose exec demoagencia curl -H "Authorization: Bearer $OpenRouter__ApiKey" https://openrouter.ai/api/v1/models
```

### Container reiniciando constantemente

```bash
# Ver logs completos
docker-compose logs --tail=100

# Verificar healthcheck
docker inspect demoagencia | grep -A 10 Health

# Reiniciar manualmente
docker-compose restart
```

### Problemas de memória

```bash
# Ver uso de memória
docker stats demoagencia

# Ajustar limites no docker-compose.yml (adicionar):
# deploy:
#   resources:
#     limits:
#       memory: 512M
#     reservations:
#       memory: 256M
```

## Monitoramento

### Healthcheck

O container possui healthcheck configurado. Verifique:

```bash
docker inspect --format='{{.State.Health.Status}}' demoagencia
```

### Métricas

```bash
# Uso de CPU/Memória
docker stats demoagencia --no-stream

# Espaço em disco
docker system df
```

### Alertas (Opcional)

Configure monitoramento com:
- **UptimeRobot**: Monitorar health endpoint
- **Langfuse Dashboard**: Monitorar traces e custos
- **Oracle Cloud Monitoring**: Métricas da VM

## Segurança

### Boas Práticas

1. **Nunca commite o arquivo .env**
2. **Use secrets do Docker** para produção (futuro)
3. **Configure firewall** na VM Oracle:
   ```bash
   # Permitir apenas SSH
   sudo ufw allow 22
   sudo ufw enable
   ```
4. **Atualize regularmente**:
   ```bash
   sudo apt update && sudo apt upgrade -y
   docker-compose pull
   docker-compose up -d
   ```

### Rotação de Logs

Os logs são rotacionados automaticamente (7 dias de retenção). Para ajustar:

Edite `appsettings.json`:
```json
"WriteTo": [
  {
    "Name": "File",
    "Args": {
      "retainedFileCountLimit": 7
    }
  }
]
```

## CI/CD (GitHub Actions)

### Configuração

O projeto inclui um pipeline CI/CD automatizado via GitHub Actions (`.github/workflows/ci.yml`).

### Jobs do Pipeline

| Job | Descrição | Gatilho |
|-----|-----------|---------|
| **build-and-test** | Build .NET + testes unitários (46 testes) | Push/PR |
| **docker-build** | Build da imagem Docker ARM64 | Após build-and-test |
| **validate-structure** | Valida arquivos obrigatórios e .env.example | Após build-and-test |
| **security-scan** | Verifica secrets e pacotes vulneráveis | Após build-and-test |
| **summary** | Resumo do pipeline | Após todos |

### Como Funciona

```
Push/PR → Build + Test → Docker Build + Validate + Security → Summary
```

### Status Badges

Adicione ao README.md após configurar:

```markdown
![CI/CD](https://github.com/SEU_USUARIO/DemoAgencia/actions/workflows/ci.yml/badge.svg)
```

### Testes Unitários

O projeto possui **46 testes unitários** cobrindo:

| Componente | Testes | O que testa |
|------------|--------|-------------|
| AgenteLoader | 8 | Parser .md, cache, comandos, persona |
| HistoricoChat | 10 | Add, limite 20 msgs, clear, thread-safety |
| RoteadorService | 8 | Roteamento por comando, classificação, fallback |
| StreamingService | 4 | Throttle 1s, envio inicial, edição progressiva |
| LangfuseInterceptor | 6 | Traces, timestamps, metadata, tags |
| TelegramService | 10 | Parsing comandos, seleção de agente |

### Rodar Testes Localmente

```bash
# Todos os testes
dotnet test

# Com coverage
dotnet test --collect:"XPlat Code Coverage"

# Testes específicos
dotnet test --filter "AgenteLoaderTests"
dotnet test --filter "HistoricoChatTests"
```

### Secrets no CI

O pipeline **não precisa** de secrets reais para rodar:
- Testes usam mocks (100% isolados)
- Docker build não precisa de credenciais
- Security scan verifica ausência de secrets

### Adicionar Novos Testes

1. Crie o arquivo em `tests/DemoAgencia.Worker.Tests/`
2. Use xUnit + Moq + FluentAssertions
3. Métodos testáveis devem ser `virtual` (para mocking)
4. Execute `dotnet test` antes do commit

## Custo Estimado

### Oracle Cloud (Always Free)
- **VM ARM**: Grátis (2 OCPU + 12 GB RAM)
- **Storage**: Grátis (200 GB)
- **Network**: Grátis (10 TB/mês)

### OpenRouter
- **Uso**: Pay-per-use
- **Estimativa PoC**: $5-20/mês (dependendo do uso)

### Langfuse (Free Tier)
- **Traces**: 50.000/mês grátis
- **Armazenamento**: 30 dias

### Telegram
- **Bot API**: Grátis

## Próximos Passos

Após deploy bem-sucedido:

1. **Testar todos os fluxos** (texto, imagem, agentes)
2. **Monitorar Langfuse** (verificar traces)
3. **Coletar feedback** de usuários beta
4. **Ajustar prompts** dos agentes conforme necessário
5. **Planejar evolução** para produção (se aprovado)

---

**Suporte**: Verifique os logs primeiro, depois consulte a seção de Troubleshooting.
