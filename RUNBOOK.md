# Runbook de Deploy - DemoAgencia

Runbook completo para configuração do GitHub, GitHub Actions, VM Oracle Cloud e deploy do DemoAgencia.

---

## Sumário

1. [Configuração do GitHub](#1-configuração-do-github)
2. [Configuração do GitHub Actions (CI/CD)](#2-configuração-do-github-actions-cicd)
3. [Configuração da VM Oracle Cloud](#3-configuração-da-vm-oracle-cloud)
4. [Preparação do Ambiente Local](#4-preparação-do-ambiente-local)
5. [Deploy Automatizado via GitHub Actions](#5-deploy-automatizado-via-github-actions)
6. [Deploy Manual na VM](#6-deploy-manual-na-vm)
7. [Verificação e Testes](#7-verificação-e-testes)
8. [Monitoramento e Manutenção](#8-monitoramento-e-manutenção)
9. [Troubleshooting](#9-troubleshooting)
10. [Segurança](#10-segurança)
11. [Custos](#11-custos)

---

## 1. Configuração do GitHub

### 1.1 Criar o Repositório

1. Acesse [github.com](https://github.com) e faça login
2. Clique no botão **+** → **New repository**
3. Preencha:
   - **Repository name**: `suspira.fluxo.demo` (ou `DemoAgencia`)
   - **Description**: `PoC Telegram + IA - Agentes multi-agente com orquestração inteligente`
   - **Visibility**: Private (recomendado) ou Public
   - **Initialize**: NÃO marque nenhuma opção (README, .gitignore, etc.)
4. Clique em **Create repository**

### 1.2 Conectar Repositório Local

```bash
# Navegar até a pasta do projeto
cd D:\Suspira\repos\suspira.fluxo.demo

# Inicializar git (se ainda não feito)
git init

# Adicionar remote
git remote add origin https://github.com/SEU_USUARIO/suspira.fluxo.demo.git

# Adicionar todos os arquivos
git add .

# Primeiro commit
git commit -m "Initial commit: DemoAgencia PoC"

# Push para o GitHub
git branch -M main
git push -u origin main
```

### 1.3 Configurar Branch Protection (Opcional)

Para garantir qualidade no código:

1. Vá em **Settings** → **Branches** → **Add branch protection rule**
2. Configure:
   - **Branch name pattern**: `main`
   - ✅ **Require a pull request before merging**
   - ✅ **Require status checks to pass before merging**
   - ✅ **Require branches to be up to date before merging**
3. Clique em **Create**

### 1.4 Adicionar Status Badge ao README

Após configurar o GitHub Actions, adicione o badge ao `README.md`:

```markdown
![CI/CD](https://github.com/SEU_USUARIO/suspira.fluxo.demo/actions/workflows/ci.yml/badge.svg)
```

---

## 2. Configuração do GitHub Actions (CI/CD)

### 2.1 Entender o Pipeline

O projeto já possui um workflow configurado em `.github/workflows/ci.yml` com 5 jobs:

| Job | Descrição | Gatilho |
|-----|-----------|---------|
| **build-and-test** | Build .NET 10 + 85 testes unitários com coverage | Push/PR |
| **docker-build** | Build da imagem Docker ARM64 | Após build-and-test |
| **validate-structure** | Valida arquivos obrigatórios e .env.example | Após build-and-test |
| **security-scan** | Verifica secrets e pacotes vulneráveis | Após build-and-test |
| **summary** | Resumo do pipeline com status | Após todos |

**Fluxo:**
```
Push/PR → Build + Test → [Docker Build + Validate + Security] → Summary
```

### 2.2 Verificar o Workflow

O arquivo `.github/workflows/ci.yml` já está configurado. Para verificar:

```bash
# Ver conteúdo do workflow
cat .github/workflows/ci.yml
```

### 2.3 Testar o Pipeline Localmente

Antes de fazer push, teste localmente:

```bash
# Build
dotnet build --configuration Release

# Testes
dotnet test --configuration Release

# Com coverage
dotnet test --collect:"XPlat Code Coverage"

# Docker build
docker build -t demoagencia:test .
```

### 2.4 Trigger do Pipeline

O pipeline é executado automaticamente em:
- **Push** para branches `main` ou `develop`
- **Pull Request** para branch `main`

### 2.5 Verificar Execução do Pipeline

1. Acesse o repositório no GitHub
2. Vá em **Actions**
3. Você verá a lista de execuções
4. Clique em uma execução para ver detalhes de cada job

### 2.6 Secrets no CI

O pipeline **NÃO precisa** de secrets reais para rodar:
- Testes usam mocks (100% isolados)
- Docker build não precisa de credenciais
- Security scan verifica ausência de secrets

**Importante**: Nunca commite o arquivo `.env` ou credenciais reais.

### 2.7 Adicionar Novos Testes

1. Crie o arquivo em `tests/DemoAgencia.Worker.Tests/<Camada>/`
2. Use xUnit + Moq + FluentAssertions
3. Métodos testáveis devem ser `virtual`
4. Execute `dotnet test` antes do commit

```csharp
public class MeuServiceTests
{
    private readonly Mock<ILogger<MeuService>> _loggerMock;
    private readonly MeuService _service;

    public MeuServiceTests()
    {
        _loggerMock = new Mock<ILogger<MeuService>>();
        _service = new MeuService(_loggerMock.Object);
    }

    [Fact]
    public async Task Metodo_Cenario_DeveResultado()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

### 2.8 Configurar Codecov (Opcional)

O pipeline já envia coverage para Codecov. Para ver relatórios visuais:

1. Acesse [codecov.io](https://codecov.io)
2. Faça login com GitHub
3. Adicione o repositório
4. Copie o token e adicione em **Settings** → **Secrets** → **Actions** → **New repository secret**
   - Name: `CODECOV_TOKEN`
   - Value: token copiado

---

## 3. Configuração da VM Oracle Cloud

### 3.1 Criar Instância VM

1. Acesse [cloud.oracle.com](https://cloud.oracle.com)
2. Vá em **Compute** → **Instances** → **Create Instance**
3. Configure:
   - **Name**: `demoagencia-vm`
   - **Compartment**: escolha o compartment
   - **Image**: Ubuntu 22.04 (ou superior)
   - **Shape**: `VM.Standard.A1.Flex` (ARM Ampere A1)
   - **OCPU**: 2
   - **Memory**: 12 GB
   - **Networking**: VCN com subnet pública
   - **Assign a public IP address**: ✅ Sim
4. **Add SSH keys**:
   - Generate a key pair (download a private key) OU
   - Upload a public key (.pub)
5. Clique em **Create**

### 3.2 Acessar a VM via SSH

```bash
# Dar permissão à chave privada
chmod 400 ~/.ssh/oracle_key.pem

# Conectar via SSH
ssh -i ~/.ssh/oracle_key.pem ubuntu@SEU_IP_PUBLICO
```

**Nota**: O usuário padrão é `ubuntu`.

### 3.3 Atualizar Sistema

```bash
# Atualizar pacotes
sudo apt update && sudo apt upgrade -y

# Instalar ferramentas básicas
sudo apt install -y curl wget git vim htop
```

### 3.4 Instalar Docker

```bash
# Instalar Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh

# Adicionar usuário ao grupo docker
sudo usermod -aG docker $USER

# Logout e login para aplicar permissões
exit
ssh -i ~/.ssh/oracle_key.pem ubuntu@SEU_IP_PUBLICO

# Verificar instalação
docker --version
docker compose version
docker run hello-world
```

### 3.5 Instalar Docker Compose (se necessário)

```bash
# Verificar se já está instalado
docker compose version

# Se não estiver, instalar
sudo curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
sudo chmod +x /usr/local/bin/docker-compose

# Criar symlink
sudo ln -s /usr/local/bin/docker-compose /usr/bin/docker-compose

# Verificar
docker-compose --version
```

### 3.6 Configurar Firewall

```bash
# Permitir apenas SSH (porta 22)
sudo ufw allow 22/tcp
sudo ufw enable

# Verificar status
sudo ufw status

# NÃO abrir outras portas (Telegram usa long polling, não precisa de portas abertas)
```

### 3.7 Configurar Swap (Opcional)

Para evitar problemas de memória:

```bash
# Criar arquivo de swap (2GB)
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile

# Verificar
sudo swapon --show

# Tornar permanente
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

### 3.8 Preparar Diretório de Deploy

```bash
# Criar diretório
mkdir -p ~/demoagencia
cd ~/demoagencia
```

---

## 4. Preparação do Ambiente Local

### 4.1 Obter Credenciais

#### Telegram Bot Token
1. Abra o Telegram e procure por [@BotFather](https://t.me/BotFather)
2. Envie `/newbot`
3. Siga as instruções (nome e username)
4. Copie o token gerado (formato: `1234567890:ABCdefGHIjklMNOpqrsTUVwxyz`)

#### OpenRouter API Key
1. Acesse [openrouter.ai](https://openrouter.ai)
2. Crie uma conta ou faça login
3. Vá em **Keys** → **Create Key**
4. Copie a chave (formato: `sk-or-v1-...`)

#### Langfuse (Observabilidade)
1. Acesse [cloud.langfuse.com](https://cloud.langfuse.com)
2. Crie uma conta gratuita
3. Crie um novo projeto
4. Vá em **Settings** → **API Keys**
5. Copie **Public Key** e **Secret Key**

### 4.2 Preparar Arquivo .env

Na raiz do projeto local:

```bash
# Copiar template
cp .env.example .env

# Editar com suas credenciais
# Windows: notepad .env
# Linux/Mac: nano .env
```

Preencha o arquivo `.env`:

```env
Telegram__BotToken="token"
OpenRouter__ApiKey="apikey"
OpenRouter__BaseUrl=https://openrouter.ai/api/v1
OpenRouter__DataCollection=deny
Langfuse__PublicKey="privatekey"
Langfuse__SecretKey="secret-key"
Langfuse__Host=https://cloud.langfuse.com
Seguranca__AnonimizarDados=true
Seguranca__MaxMensagensPorMinuto=5
```

**Importante**:
- Nunca commite o arquivo `.env` (já está no `.gitignore`)

### 4.3 Testar Localmente (Opcional)

```bash
# Restaurar dependências
dotnet restore

# Executar localmente
dotnet run --project src/DemoAgencia.Worker

# Ou com Docker
docker build -t demoagencia:test .
docker run -d --name test -e Telegram__BotToken="seu-token" -e OpenRouter__ApiKey="sua-chave" demoagencia:test
```

---

## 5. Deploy Automatizado via GitHub Actions

Deploy automático: push na `main` → CI valida → GitHub Actions faz SSH na VM → deploy. **Sem precisar entrar na VM.**

### 5.1 Preparar a VM (Primeira vez)

```bash
# Acessar a VM via SSH
ssh -i ~/.ssh/oracle_key.pem ubuntu@SEU_IP_PUBLICO

# Executar script de setup
# (copie scripts/setup-vm.sh para a VM ou cole o conteúdo)
chmod +x setup-vm.sh
./setup-vm.sh

# Logout e login para aplicar permissões
exit
ssh -i ~/.ssh/oracle_key.pem ubuntu@SEU_IP_PUBLICO

# Criar diretório de deploy
mkdir -p ~/demoagencia
```

### 5.2 Gerar Chave SSH Dedicada para o GitHub Actions

```bash
# Na sua máquina local (NÃO na VM)
ssh-keygen -t ed25519 -C "github-actions-deploy" -f ~/.ssh/github_deploy_key -N ""

# Copiar a chave pública para a VM
ssh-copy-id -i ~/.ssh/github_deploy_key.pub ubuntu@SEU_IP_PUBLICO

# Testar conexão
ssh -i ~/.ssh/github_deploy_key ubuntu@SEU_IP_PUBLICO

# Copiar a chave privada (conteudo) para usar como secret
cat ~/.ssh/github_deploy_key
```

### 5.3 Configurar Secrets no GitHub

1. Acesse o repositório no GitHub
2. Vá em **Settings** → **Secrets and variables** → **Actions**
3. Clique em **New repository secret** para cada um:

| Secret | Valor | Descrição |
|--------|-------|-----------|
| `SSH_HOST` | `SEU_IP_PUBLICO` | IP público da VM Oracle |
| `SSH_USER` | `ubuntu` | Usuário SSH |
| `SSH_KEY` | *(conteúdo de `~/.ssh/github_deploy_key`)* | Chave privada SSH (copiar tudo, incluindo `-----BEGIN` e `-----END`) |
| `SSH_PORT` | `22` | Porta SSH (opcional, default 22) |
| `TELEGRAM_BOT_TOKEN` | `.` | Token do BotFather |
| `TELEGRAM_CHAT_IDS` | *(vazio ou IDs separados por vírgula)* | Chat IDs permitidos |
| `OPENROUTER_API_KEY` | `` | Chave da OpenRouter |
| `LANGFUSE_PUBLIC_KEY` | `` | Public key do Langfuse |
| `LANGFUSE_SECRET_KEY` | `` | Secret key do Langfuse |

### 5.4 Como Funciona o Deploy Automático

```
Push na main → CI (build + test) → Deploy via SSH → VM pull + build + run
```

O workflow `.github/workflows/deploy.yml`:
1. **CI Validation**: Build .NET + testes unitários
2. **Deploy to Oracle VM**: Via SSH, executa na VM:
   - Clona o repo (primeira vez) ou faz `git pull`
   - Gera o arquivo `.env` com os secrets
   - Para container existente
   - Constrói nova imagem Docker
   - Inicia container
   - Verifica status

### 5.5 Trigger do Deploy

**Automático**: Push na branch `main`

**Manual**: 
1. Vá em **Actions** → **Deploy to VM** → **Run workflow**
2. Selecione a branch `main`
3. Clique em **Run workflow**

### 5.6 Verificar Deploy

1. Vá em **Actions** no GitHub
2. Clique na execução mais recente do **Deploy to VM**
3. Veja os logs de cada step
4. Verifique se o step "Verify deployment" passou

### 5.7 Fluxo Completo de Desenvolvimento

```bash
# 1. Desenvolver localmente
git checkout -b feature/nova-funcionalidade
# ... fazer alterações ...
git commit -m "feat: nova funcionalidade"
git push -u origin feature/nova-funcionalidade

# 2. Criar Pull Request no GitHub
# → CI roda automaticamente (build + test + docker + security)

# 3. Merge na main
# → Deploy automático na VM via SSH

# 4. Verificar
# → Telegram bot já está rodando com a nova versão
```

---

## 6. Deploy Manual na VM

Use esta opção se não quiser configurar o deploy automático.

### 6.1 Opção A: Script Automatizado

```bash
# Na VM
cd ~/demoagencia

# Transferir código (da sua máquina)
scp -r -i ~/.ssh/oracle_key.pem . ubuntu@SEU_IP_PUBLICO:~/demoagencia

# Ou clonar via git
git clone https://github.com/SEU_USUARIO/suspira.fluxo.demo.git .

# Criar .env
nano .env
# (preencha com as credenciais)

# Executar deploy
chmod +x deploy.sh
./deploy.sh
```

### 6.2 Opção B: Deploy Manual Passo a Passo

```bash
# Na VM
cd ~/demoagencia

# Criar .env
nano .env

# Build e run
docker compose build --no-cache
docker compose up -d

# Verificar
docker compose ps
docker compose logs -f
```

### 6.3 Verificar Deploy Manual

```bash
# Ver status do container
docker compose ps

# Ver logs em tempo real
docker compose logs -f

# Ver últimas 50 linhas
docker compose logs --tail=50

# Verificar healthcheck
docker inspect --format='{{.State.Health.Status}}' demoagencia
```

---

## 7. Verificação e Testes

### 7.1 Testar Bot no Telegram

1. Abra o Telegram
2. Procure pelo seu bot (use o username criado)
3. Envie `/start`
4. Teste os comandos:

| Comando | Teste |
|---------|-------|
| `/help` | Lista de comandos |
| `/agentes` | Lista agentes disponíveis |
| `/redator` | Seleciona agente redator |
| `/redator Escreva um slogan` | Testa streaming |
| `/dev` | Seleciona agente dev |
| `/estrategista` | Seleciona agente estrategista |
| `/imagem um gato azul` | Gera imagem |
| `/limpar` | Limpa histórico |
| `/reset` | Deseleciona agente |
| Mensagem livre | Testa pipeline completo |
| Enviar foto | Testa análise multimodal |

### 7.2 Verificar Langfuse

1. Acesse [cloud.langfuse.com](https://cloud.langfuse.com)
2. Abra seu projeto
3. Vá em **Traces**
4. Verifique:
   - Traces das interações
   - Tokens consumidos
   - Latência
   - Modelo usado

### 7.3 Verificar Logs

```bash
# Logs em tempo real
docker compose logs -f

# Últimas 50 linhas
docker compose logs --tail=50

# Logs do arquivo (dentro do container)
docker compose exec demoagencia cat /app/logs/demo-log-$(date +%Y-%m-%d).txt

# Ver uso de recursos
docker stats demoagencia --no-stream
```

### 7.4 Executar Checklist de Validação

```bash
# Tornar script executável
chmod +x validate.sh

# Executar validação
./validate.sh
```

O script verifica:
- Container rodando
- Bot conectado ao Telegram
- Variáveis de ambiente configuradas
- Arquivos de agentes presentes
- Diretório de logs
- Healthcheck

---

## 8. Monitoramento e Manutenção

### 8.1 Comandos Úteis

```bash
# Ver status do container
docker compose ps

# Reiniciar container
docker compose restart

# Parar container
docker compose down

# Atualizar código e redeploy
git pull
docker compose down
docker compose build --no-cache
docker compose up -d

# Limpar imagens antigas
docker image prune -a

# Ver uso de recursos
docker stats demoagencia

# Espaço em disco
docker system df
```

### 8.2 Backup

```bash
# Backup dos logs
docker compose exec demoagencia tar czf /tmp/logs-backup.tar.gz /app/logs
docker cp demoagencia:/tmp/logs-backup.tar.gz ./logs-backup-$(date +%Y-%m-%d).tar.gz

# Backup do .env (IMPORTANTE)
cp .env .env.backup-$(date +%Y-%m-%d)
```

### 8.3 Atualizações

```bash
# Atualizar sistema da VM
sudo apt update && sudo apt upgrade -y

# Atualizar Docker
sudo apt install --only-upgrade docker-ce docker-ce-cli containerd.io

# Atualizar aplicação
cd ~/demoagencia
git pull
docker compose down
docker compose build --no-cache
docker compose up -d
```

### 8.4 Alertas (Opcional)

Configure monitoramento com:
- **UptimeRobot**: Monitorar health endpoint
- **Langfuse Dashboard**: Monitorar traces e custos
- **Oracle Cloud Monitoring**: Métricas da VM

---

## 9. Troubleshooting

### 9.1 Bot não responde

```bash
# Verificar se container está rodando
docker compose ps

# Verificar logs
docker compose logs --tail=50

# Verificar se token está correto
docker compose exec demoagencia env | grep TELEGRAM

# Reiniciar container
docker compose restart

# Verificar conexão com Telegram
docker compose exec demoagencia curl -I https://api.telegram.org
```

### 9.2 Erro de autenticação OpenRouter

```bash
# Verificar API key
docker compose exec demoagencia env | grep OpenRouter

# Testar conexão
docker compose exec demoagencia curl -H "Authorization: Bearer $OpenRouter__ApiKey" https://openrouter.ai/api/v1/models

# Verificar logs de erro
docker compose logs | grep -i "openrouter"
```

### 9.3 Container reiniciando constantemente

```bash
# Ver logs completos
docker compose logs --tail=100

# Verificar healthcheck
docker inspect demoagencia | grep -A 10 Health

# Verificar uso de memória
docker stats demoagencia

# Reiniciar manualmente
docker compose restart

# Se necessário, recriar container
docker compose down
docker compose up -d
```

### 9.4 Problemas de memória

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

# Reiniciar
docker compose down
docker compose up -d
```

### 9.5 Problemas de disco

```bash
# Ver espaço em disco
df -h

# Limpar imagens antigas
docker image prune -a

# Limpar volumes não utilizados
docker volume prune

# Limpar cache do Docker
docker builder prune
```

### 9.6 Problemas de rede

```bash
# Verificar conectividade
ping -c 4 api.telegram.org
ping -c 4 openrouter.ai

# Verificar DNS
nslookup api.telegram.org

# Verificar firewall
sudo ufw status
```

### 9.7 Erro ao construir imagem Docker

```bash
# Limpar cache do Docker
docker builder prune -a

# Verificar espaço em disco
df -h

# Tentar build sem cache
docker compose build --no-cache

# Ver logs detalhados
docker compose build --progress=plain
```

### 9.8 Deploy via GitHub Actions falha

**Erro: SSH connection failed**
```bash
# Verificar se a chave SSH está correta
ssh -i ~/.ssh/github_deploy_key ubuntu@SEU_IP

# Verificar se o secret SSH_KEY está correto no GitHub
# Settings → Secrets → SSH_KEY (deve incluir -----BEGIN e -----END)

# Verificar se o SSH_USER está correto (ubuntu)
```

**Erro: git pull failed**
```bash
# Na VM, verificar permissões
cd ~/demoagencia
ls -la .git

# Se necessário, re-clone
cd ~
rm -rf demoagencia
mkdir demoagencia
cd demoagencia
git clone https://github.com/SEU_USUARIO/suspira.fluxo.demo.git .
```

**Erro: docker compose build failed**
```bash
# Verificar espaço em disco na VM
df -h

# Limpar imagens antigas
docker image prune -a

# Verificar logs do workflow no GitHub
# Actions → Deploy to VM → ver logs do step "Deploy via SSH"
```

**Erro: Container não inicia**
```bash
# Verificar logs na VM
ssh -i ~/.ssh/oracle_key.pem ubuntu@SEU_IP
cd ~/demoagencia
docker compose logs --tail=100

# Verificar se .env foi criado corretamente
cat .env

# Verificar se as variáveis estão corretas
docker compose config
```

---

## 10. Segurança

### 10.1 Boas Práticas

1. **Nunca commite o arquivo .env**
   - Arquivo já está no `.gitignore`
   - Use `.env.example` como template

2. **Use secrets do Docker** para produção (futuro)

3. **Configure firewall** na VM Oracle:
   ```bash
   sudo ufw allow 22/tcp
   sudo ufw enable
   ```

4. **Atualize regularmente**:
   ```bash
   sudo apt update && sudo apt upgrade -y
   ```

5. **Use chaves SSH** em vez de senhas

6. **Desabilite login root via SSH**:
   ```bash
   sudo nano /etc/ssh/sshd_config
   # Altere: PermitRootLogin no
   sudo systemctl restart sshd
   ```

### 10.2 Rotação de Logs

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

### 10.3 Verificação de Secrets no CI

O pipeline verifica automaticamente:
- Chaves OpenRouter (`sk-or-`)
- Chaves Langfuse (`pk-lf-`, `sk-lf-`)
- Outras possíveis secrets

Se o security scan falhar:
1. Verifique os logs do pipeline
2. Remova qualquer secret do código
3. Use variáveis de ambiente

---

## 11. Custos

### 11.1 Oracle Cloud (Always Free)

| Recurso | Custo |
|---------|-------|
| VM ARM (2 OCPU + 12 GB RAM) | Grátis |
| Storage (200 GB) | Grátis |
| Network (10 TB/mês) | Grátis |
| **Total** | **$0/mês** |

### 11.2 OpenRouter

| Uso | Custo |
|-----|-------|
| Modelo | Pay-per-use |
| Estimativa PoC | $5-20/mês |
| Estimativa Produção | $50-200/mês |

### 11.3 Langfuse (Free Tier)

| Recurso | Limite |
|---------|--------|
| Traces | 50.000/mês |
| Armazenamento | 30 dias |
| Usuários | Ilimitado |
| **Custo** | **$0/mês** |

### 11.4 Telegram

| Recurso | Custo |
|---------|-------|
| Bot API | Grátis |
| Long Polling | Grátis |
| **Total** | **$0/mês** |

### 11.5 GitHub

| Recurso | Custo |
|---------|-------|
| Repositório privado | Grátis |
| GitHub Actions | 2.000 min/mês |
| **Total** | **$0/mês** (para PoC) |

---

## 12. Próximos Passos

Após deploy bem-sucedido:

1. **Testar todos os fluxos** (texto, imagem, agentes)
2. **Monitorar Langfuse** (verificar traces e custos)
3. **Coletar feedback** de usuários beta
4. **Ajustar prompts** dos agentes conforme necessário
5. **Planejar evolução** para produção (se aprovado)

---

## 13. Suporte

1. **Verifique os logs primeiro**: `docker compose logs -f`
2. **Consulte a seção de Troubleshooting**
3. **Verifique o CHECKLIST.md** para testes E2E
4. **Consulte a documentação**:
   - [README.md](README.md) - Visão geral
   - [CHECKLIST.md](CHECKLIST.md) - Checklist de aceite
   - [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) - Arquitetura
   - [docs/API.md](docs/API.md) - API Reference
   - [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) - Desenvolvimento

---

**Última atualização**: 2026-02-25
