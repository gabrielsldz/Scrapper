# Scrapper DATASUS Oncologia (.NET 8)

![.NET 8](https://img.shields.io/badge/.NET-8.0-purple?logo=dotnet)
![Status](https://img.shields.io/badge/status-alpha-orange)

Automatizador em **C# (.NET 8)** que coleta, em lote, os dados do **Painel Oncológico do DATASUS (2013-2025)** e grava tudo num único arquivo **JSON** pronto para análise ou carga em banco de dados.

---

## ✨ Principais recursos

| 💡                          | Funcionalidade                                                                      |
| --------------------------- | ----------------------------------------------------------------------------------- |
| 🚀 CLI interativo           | Pergunta anos, faixas etárias, CIDs, nº de threads, timeout etc.                    |
| 🧵 Multithreading real      | Usa `SemaphoreSlim`; até 24 threads simultâneas por padrão                          |
| 🔁 Retentativa automática   | Any timeout ou erro de rede é repetido (n vezes configuráveis)                      |
| ⏱ Indicador de progresso    | Mostra total de consultas e % concluída em tempo real                               |
| 📄 Saída unificada          | Gera `total_onco.json` consolidando todos os recortes (região × sexo × faixa × CID) |
| 🛠 100 % código gerenciável | Somente **System.\***; sem libs externas – fácil de auditar e portar                |

---

## 🚀 Instalação e execução

Pré-requisito: **.NET SDK 8.0** ([https://dotnet.microsoft.com/](https://dotnet.microsoft.com/))

```bash
git clone https://github.com/gabrielsldz/Scrapper.git
cd Scrapper

# compilar em modo Release
dotnet build -c Release

# rodar
dotnet run -c Release
```

Na primeira execução o programa pergunta:

```
Ano inicial (2013-2025): 2019
Ano final   (2013-2025): 2023
Threads [24]:
Timeout (s) [45]:
Retries [3]:
Faixas (, ou * ) [*]:     ← * = todas as faixas
CIDs   (, ou * ) [*]:     ← * = todos os CIDs detalhados
Arquivo JSON [total_onco.json]:
```

Pressione **Enter** para aceitar o valor padrão.

---

## 🗂 Estrutura do JSON gerado

```jsonc
{
  "2023": {
    "Norte": {
      "totais": { "ALL": 2345, "M": 1120, "F": 1225 },
      "0 a 19 anos": {
        "C00": { "ALL": 12, "M": 8, "F": 4 },
        "C50": { "ALL": 3,  "M": 0, "F": 3 }
      },
      "25 a 29 anos": { … }
    },
    "Sudeste": { … }
  },
  "2022": { … }
}
```

*Obs.: valores fictícios; números reais dependem do DATASUS.*

---

## ⚙️ Ajustes rápidos

* **Aumentar threads** – aceito no prompt ou edite o valor padrão (`AskUser()`).
* **Aumentar/reduzir timeout**(Se o site ficar instável, aumente timeout ou reduza threads.)
* **Filtrar faixas ou CIDs** – informe lista separada por vírgulas (`50 a 54 anos, 55 a 59 anos`, `C50,C34` etc.).

---

## 🛣 Roadmap

* [ ] Pool de **proxies** para aumentar concorrência sem bloquear o IP
* [ ] Persistência opcional em **SQLite/PostgreSQL** (migrations automáticas)
* [ ] Exportação nativa **CSV** / **Parquet**
* [ ] Dockerfile + workflow GitHub Actions
* [ ] Integração com **ASP.NET API** para servir os dados como REST

---

## 🤝 Como contribuir

1. Fork → 2. branch `feature/…` → 3. Pull Request
   • Siga `dotnet format` / PEP-8 equivalentes para C#.
   • Comente com `///` XML.
   • Adicione testes unitários quando possível.

Bug ou sugestão? Abra uma **issue**!

---


