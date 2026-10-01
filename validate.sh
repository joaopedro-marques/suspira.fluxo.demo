#!/bin/bash

echo "=== DemoAgencia - Checklist de Validação E2E ==="
echo ""

PASS=0
FAIL=0

check() {
    if [ $? -eq 0 ]; then
        echo "✓ $1"
        ((PASS++))
    else
        echo "✗ $1"
        ((FAIL++))
    fi
}

echo "1. Verificando container..."
docker-compose ps | grep -q "Up"
check "Container está rodando"

echo ""
echo "2. Verificando logs..."
docker-compose logs --tail=5 | grep -q "Bot conectado"
check "Bot conectado ao Telegram"

echo ""
echo "3. Verificando variáveis de ambiente..."
docker-compose exec demoagencia env | grep -q "Telegram__BotToken"
check "Telegram__BotToken configurado"

docker-compose exec demoagencia env | grep -q "OpenRouter__ApiKey"
check "OpenRouter__ApiKey configurado"

echo ""
echo "4. Verificando arquivos..."
docker-compose exec demoagencia ls /app/Assets/agentes/redator.md > /dev/null 2>&1
check "Agente redator.md existe"

docker-compose exec demoagencia ls /app/Assets/agentes/dev.md > /dev/null 2>&1
check "Agente dev.md existe"

docker-compose exec demoagencia ls /app/Assets/agentes/estrategista.md > /dev/null 2>&1
check "Agente estrategista.md existe"

echo ""
echo "5. Verificando logs da aplicação..."
docker-compose exec demoagencia ls /app/logs/ > /dev/null 2>&1
check "Diretório de logs existe"

echo ""
echo "6. Verificando healthcheck..."
HEALTH=$(docker inspect --format='{{.State.Health.Status}}' demoagencia 2>/dev/null || echo "unknown")
if [ "$HEALTH" = "healthy" ]; then
    echo "✓ Healthcheck: healthy"
    ((PASS++))
else
    echo "? Healthcheck: $HEALTH (aguardando ou não configurado)"
fi

echo ""
echo "=== Resultado ==="
echo "Passou: $PASS"
echo "Falhou: $FAIL"
echo ""

if [ $FAIL -eq 0 ]; then
    echo "✓ Todos os checks passaram!"
    echo ""
    echo "Próximos passos:"
    echo "  1. Teste o bot no Telegram: /start, /help, /agentes"
    echo "  2. Teste uma mensagem de texto"
    echo "  3. Teste /imagem <prompt>"
    echo "  4. Verifique os traces no Langfuse"
    exit 0
else
    echo "✗ Alguns checks falharam. Verifique os logs:"
    echo "  docker-compose logs -f"
    exit 1
fi
