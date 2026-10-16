# Guia: Como Adicionar um Novo Cliente

Este guia explica como adicionar um novo cliente ao sistema **sem alterar código**.

## Arquitetura de 2 Camadas

O sistema segue uma arquitetura de **2 camadas** que separa claramente:

### Camada 1: Agente do Step (Genérica)
- **Onde está**: `src/DemoAgencia.Worker/IA/Pipelines/Email/`
- **O que faz**: Comportamento genérico dos steps (StepCopyEmail, StepImagemHero, etc.)
- **Característica**: Não conhece nenhum cliente específico
- **Arquivos de agente**: `Assets/agentes/*.md` (redator.md, hero.md, etc.)

### Camada 2: Informação do Cliente (Configuração)
- **Onde está**: `Assets/referencias/`
- **O que faz**: Dados específicos de cada cliente
- **Característica**: 100% configurável via arquivos JSON/HTML
- **Estrutura**:
  ```
  Assets/referencias/
  ├── estrategia/
  │   ├── {CLIENTE}_paleta_de_cores.json
  │   ├── {CLIENTE}_temas_jornadas.json
  │   ├── {CLIENTE}_jornada_cliente.json (opcional)
  │   ├── {CLIENTE}_mapa_emocional_jornada.json (opcional)
  │   ├── {CLIENTE}_fases.json
  │   └── {CLIENTE}_regua_jornada.json (opcional)
  ├── footer/
  │   └── {CLIENTE}_footer.html
  ├── templates/
  │   ├── {CLIENTE}_html_*.html
  │   └── {CLIENTE}_html_*.json (sidecars)
  ├── components/
  │   ├── hero_section.html
  │   └── banner_section.html
  └── imagens/
      ├── banners/
      ├── icons/
      └── logos/
  ```

## Checklist: Adicionando um Novo Cliente

### ✅ Passo 1: Configurar no appsettings.json

Edite `src/DemoAgencia.Worker/appsettings.json`:

```json
{
  "PreFlight": {
    "ClientesPermitidos": ["mrv", "novo_cliente"]
  }
}
```

### ✅ Passo 2: Criar Configuração de Fases (OBRIGATÓRIO)

Crie `Assets/referencias/estrategia/{CLIENTE}_fases.json`:

```json
{
  "fases": {
    "fase-1": {
      "sinonimos": ["fase 1", "primeira fase", "variação"]
    },
    "fase-2": {
      "sinonimos": ["fase 2", "segunda fase"]
    }
  }
}
```

**Por que é obrigatório?**
- Define as fases da jornada do seu cliente
- Permite normalização de variações (acentos, espaços, sinônimos)
- Usado pelo router, brief extractor e todos os steps

### ✅ Passo 3: Criar Paleta de Cores (OBRIGATÓRIO)

Crie `Assets/referencias/estrategia/{CLIENTE}_paleta_de_cores.json`:

```json
{
  "paleta_de_cores": [
    {
      "cor_principal": "Nome da Cor",
      "etapa": "fase-1",
      "descricao": "Descrição emocional da cor",
      "cores_hex_aproximadas": [["#FF0000"], ["#FF5555"]]
    }
  ]
}
```

**Importante**: O campo `etapa` deve corresponder a uma fase definida no arquivo de fases.

### ✅ Passo 4: Criar Temas (OBRIGATÓRIO)

Crie `Assets/referencias/estrategia/{CLIENTE}_temas_jornadas.json`:

```json
{
  "titulo": "Jornada do Cliente",
  "fases": [
    {
      "nome": "fase-1",
      "temas": ["Tema 1", "Tema 2", "Tema 3"]
    }
  ]
}
```

### ✅ Passo 5: Criar Footer (OBRIGATÓRIO)

Crie `Assets/referencias/footer/{CLIENTE}_footer.html`:

```html
<table>
  <tr>
    <td style="padding: 20px; background-color: #YOUR_COLOR; color: white; text-align: center;">
      <p style="margin: 0; font-family: Arial, sans-serif; font-size: 14px;">
        <strong>Nome da Empresa</strong><br>
        www.empresa.com.br | contato@empresa.com.br<br>
        © 2024 Empresa. Todos os direitos reservados.
      </p>
    </td>
  </tr>
</table>
```

**Nota**: Se precisar de múltiplos footers, use `{CLIENTE}_footer_{nome}.html` e referencie no template como `{{footer:nome}}`.

### ✅ Passo 6: Criar Templates HTML (OBRIGATÓRIO)

Crie pelo menos um template padrão:

**Template HTML**: `Assets/referencias/templates/{CLIENTE}_html_default.html`
```html
<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <title>{{assunto}}</title>
</head>
<body>
  <!-- Seu template aqui -->
  {{banner_section}}
  {{hero_section}}
  {{corpo}}
  {{footer}}
</body>
</html>
```

**Sidecar JSON**: `Assets/referencias/templates/{CLIENTE}_html_default.json`
```json
{
  "cliente": "novo_cliente",
  "fase": "fase-1",
  "sub_jornadas": ["sub1", "sub2"],
  "palavras_chave": ["palavra1", "palavra2"],
  "padrao": true
}
```

### ✅ Passo 7: Adicionar Assets Visuais (OPCIONAL)

Adicione imagens em `Assets/referencias/imagens/`:

- **Logos**: `{CLIENTE}_logo_nome.png`
- **Ícones**: `{CLIENTE}_icon_nome.png`
- **Banners**: `{CLIENTE}_banner_nome.png`

### ✅ Passo 8: Configurar Régua de Jornada (OPCIONAL)

Se o cliente precisa de uma régua de progresso no hero, crie `Assets/referencias/estrategia/{CLIENTE}_regua_jornada.json`:

```json
{
  "regua": {
    "etapas": [
      "Etapa 1",
      "Etapa 2",
      "Etapa 3"
    ],
    "etapa_destacada_por_fase": {
      "fase-1": "Etapa 1",
      "fase-2": "Etapa 2"
    },
    "estilo": {
      "concluida": "line WHITE fill on the progress line segment",
      "atual": "solid YELLOW highlight",
      "futura": "outline only, no fill",
      "diretrizes": [
        "Render labels in Portuguese",
        "Use clean sans-serif typeface"
      ]
    }
  }
}
```

## Anatomia do Email Gerado

O email gerado segue esta anatomia (todas as partes são configuráveis):

```
┌─────────────────────────────────┐
│  HEADER (componentes genéricos) │
│  ├── Logo (StepMarcaEmail)      │
│  ├── Banner/Hero (StepImagemHero)│
│  └── Régua (se configurada)     │
├─────────────────────────────────┤
│  CONTEÚDO (gerado por LLM)      │
│  ├── Assunto (StepCopyEmail)    │
│  ├── Preheader (StepCopyEmail)  │
│  ├── Título (StepCopyEmail)     │
│  ├── Corpo (StepCopyEmail)      │
│  │   └── Diagramação            │
│  │       (StepDiagramacaoEmail) │
│  └── CTA (StepCopyEmail)        │
├─────────────────────────────────┤
│  FOOTER (config do cliente)     │
│  └── {{footer}} ou              │
│      {{footer:nome}}            │
└─────────────────────────────────┘
```

## Exemplo: Cliente ACME

Veja o cliente de teste ACME como referência completa:

- **Fases**: `pos-compra`, `pre-chaves` (apenas 2 fases, diferente da MRV)
- **Footer**: Corporativo com cor azul (#1E3A8A)
- **Paleta**: Azul Corporativo + Verde Esperança
- **Sem régua**: ACME não usa régua de jornada

Arquivos:
- `ACME_fases.json`
- `ACME_paleta_de_cores.json`
- `ACME_temas_jornadas.json`
- `ACME_footer.html`
- `ACME_html_default.html` + `.json`

## Validação

Após adicionar todos os arquivos:

1. **Compile o projeto**: `dotnet build`
2. **Execute os testes**: `dotnet test`
3. **Inicie o bot**: O cliente novo aparecerá automaticamente na lista de clientes permitidos
4. **Teste via Telegram**: Envie uma mensagem mencionando o novo cliente

## Solução de Problemas

### Cliente não aparece na lista
- Verifique se adicionou em `appsettings.json` → `PreFlight.ClientesPermitidos`
- Reinicie o bot

### Fases não são reconhecidas
- Verifique se o nome da fase no arquivo de paleta/temas corresponde exatamente ao nome no arquivo de fases
- Verifique se os sinônimos estão configurados corretamente

### Footer não aparece
- Verifique se o arquivo está em `Assets/referencias/footer/{CLIENTE}_footer.html`
- Verifique se o template HTML contém o placeholder `{{footer}}`

### Erro "Template não encontrado"
- Verifique se existe pelo menos um template marcado como `"padrao": true` no sidecar JSON
- Verifique se o nome do template segue o padrão `{CLIENTE}_html_*.html`

## Princípios de Design

1. **Zero código**: Nunca altere código C# para adicionar um cliente
2. **Configuração explícita**: Cada aspecto do email é configurável
3. **Separação de responsabilidades**: Steps são genéricos, dados são específicos
4. **Fallback gracioso**: Se uma config não existe, o sistema usa valores padrão ou omite o componente
5. **Coexistência pacífica**: Múltiplos clientes podem existir sem interferência

## Próximos Passos

Após adicionar um novo cliente:

1. Teste com mensagens reais
2. Ajuste os templates HTML conforme necessário
3. Adicione mais templates para diferentes fases/sub-jornadas
4. Otimize a paleta de cores com feedback visual
5. Ajuste os temas conforme o comportamento do cliente

---

**Dúvidas?** Consulte os testes de integração em `ClienteAcmeIntegrationTests.cs` para exemplos completos de configuração.
