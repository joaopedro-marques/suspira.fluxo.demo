#!/bin/bash

set -e

echo "=== DemoAgencia - Script de Deploy ==="
echo ""

if [ ! -f ".env" ]; then
    echo "ERRO: Arquivo .env nao encontrado!"
    echo "Copie .env.example para .env e preencha as variaveis:"
    echo "  cp .env.example .env"
    echo "  nano .env"
    exit 1
fi

echo "Verificando Docker..."
if ! command -v docker &> /dev/null; then
    echo "Docker nao encontrado. Instalando..."
    curl -fsSL https://get.docker.com -o get-docker.sh
    sudo sh get-docker.sh
    sudo usermod -aG docker $USER
    echo "Docker instalado. Faca logout e login novamente."
    exit 1
fi

echo "Docker encontrado: $(docker --version)"
echo ""

echo "Verificando Docker Compose..."
if ! command -v docker-compose &> /dev/null; then
    echo "Docker Compose nao encontrado. Instalando..."
    sudo curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
    sudo chmod +x /usr/local/bin/docker-compose
fi

echo "Docker Compose encontrado: $(docker-compose --version)"
echo ""

echo "Parando container existente (se houver)..."
docker-compose down || true
echo ""

echo "Construindo imagem..."
docker-compose build --no-cache
echo ""

echo "Iniciando container..."
docker-compose up -d
echo ""

echo "Aguardando inicializacao..."
sleep 5

echo "Verificando status..."
docker-compose ps
echo ""

echo "Verificando logs..."
docker-compose logs --tail=20
echo ""

echo "=== Deploy concluido ==="
echo ""
echo "Comandos utiles:"
echo "  Ver logs em tempo real:  docker-compose logs -f"
echo "  Reiniciar container:     docker-compose restart"
echo "  Parar container:         docker-compose down"
echo "  Status do container:     docker-compose ps"
echo ""
