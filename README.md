# Tugboats

Plugin para servidores Rust utilizando uMod/Oxide que permite aos jogadores comprar Tugboats diretamente nas Vilas de Pescadores.

Desenvolvido por MrMadara.

![Banner](https://i.imgur.com/QRQNwsJ.png)

---

# ✨ Recursos

* Compra de Tugboats via NPC
* Interface CUI integrada
* Sistema de permissões
* Limite de compras por wipe
* Combustível inicial configurável
* Proteção temporária do Tugboats
* Pontos de spawn personalizados
* Compatível com uMod/Oxide

---

# 📦 Instalação

Coloque o arquivo:

```bash
Tugboats.cs
```

em:

```bash
oxide/plugins/
```

Depois recarregue o plugin:

```bash
o.reload Tugboats
```

---

# 🔑 Permissões

```text
Tugboats.tug.use
Tugboats.tug.vip
Tugboats.tug.admin
```

## Descrição

| Permissão              | Função                      |
| ---------------------- | --------------------------- |
| `Tugboats.tug.use`   | Permite comprar Tugboats    |
| `Tugboats.tug.vip`   | Compra gratuita             |
| `Tugboats.tug.admin` | Funções administrativas     |

---

# ⚙️ Configuração

Arquivo:

```bash
oxide/config/Tugboats.json
```

## Exemplo

```json
{
  "Quanto combustível o Tugboats deve possuir ao nascer?": 100,
  "Mostrar no HUD do jogador a localização do Tugboats após a compra?": true,
  "Limitar quantos Tugboats um jogador pode comprar por wipe? [0 = sem limite]": 0
}
```

---

# 🛠️ Comandos

## Administrador

```bash
/btshowspawnpoints
```

Mostra os pontos de spawn.

```bash
/btaddspawnpoint
```

Adiciona um novo ponto de spawn.

---

# 🚤 Funcionamento

1. O jogador conversa com o NPC Boat Vendor
2. A interface de compra é aberta
3. O jogador confirma a compra
4. O Tugboats é spawnado automaticamente
5. O plugin adiciona combustível inicial
6. O Tugboats fica protegido temporariamente

---

# 📁 Arquivos

## Plugin

```bash
oxide/plugins/Tugboats.cs
```

## Configuração

```bash
oxide/config/Tugboats.json
```

## Dados

```bash
oxide/data/Tugboats.json
```

---

# ⚠️ Observações

* Compatível com Rust atual
* Compatível com uMod/Oxide
* Recomendado realizar backup antes de atualizar

---

# 👨‍💻 Desenvolvedor

```text
MrMadara
```
