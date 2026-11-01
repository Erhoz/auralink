# Construção e publicação

`version.json` define o SemVer, a versão numérica de assembly Windows e o canal. O primeiro beta é `1.0.0-beta.1`; a tag Git correspondente é `v1.0.0-beta.1`. Conteúdo publicado não deve ser substituído silenciosamente: correções devem receber nova versão.

Exceção desta revisão: o usuário solicitou explicitamente retirar a publicação manual e republicar `1.0.0-beta.1` com autostart e ícone. `tools/Replace-Release.ps1 -ExpectedOldCommit <sha>` verifica o commit/tag remotos antigos, salva metadados, remove somente aquela release/tag e publica os artefatos do novo commit. O histórico de commits permanece. Não usar esse fluxo para substituições silenciosas.

Requisitos de build: Windows x64, Git, Windows PowerShell 5.1 e compilador .NET Framework local. Não requer baixar compiladores de terceiros, NuGet, Python, SDKs RGB ou binários de fabricantes. O instalador é compilado de `src/Packaging/Setup.cs` e contém um ZIP próprio com manifesto SHA-256. Não é um MSI e não possui assinatura Authenticode.

Durante o desenvolvimento:

```powershell
powershell.exe -NoProfile -File .\Build-Release.ps1 -AllowDirty
powershell.exe -NoProfile -File .\Verify-Release.ps1
```

Esses comandos compilam, verificam e extraem em `artifacts/`. Não instalam, não registram COM e não alteram iluminação. Um build com `-AllowDirty` nunca deve ser publicado. As verificações de pacote não equivalem a instalar/remover no Windows ou a validar hardware.

Publicação autorizada, após revisar e commitar todos os arquivos:

```powershell
powershell.exe -NoProfile -File .\Build-Release.ps1
powershell.exe -NoProfile -File .\Verify-Release.ps1
git tag -a v1.0.0-beta.1 -m "AuraLink 1.0.0-beta.1"
git push origin main
git push origin v1.0.0-beta.1
powershell.exe -NoProfile -File .\tools\Publish-Release.ps1 -Publish
```

O publicador usa a credencial do Git em memória, pela API oficial do GitHub; não grava tokens em argumentos, arquivos ou logs. Exige tag, HEAD e commit dos artefatos iguais. Cria um rascunho, sobe todos os quatro arquivos, confere tamanhos/digests e só então publica como prerelease. Se houver erro no upload, o rascunho permanece para inspeção; uma release existente não é sobrescrita.

O ZIP de download contém o instalador e as instruções, não um modo portátil. Código-fonte também fica acessível pela tag no GitHub. A publicação não acrescenta uma licença de código aberto ao repositório.

O ícone é próprio, com fonte SVG e ICO em `assets/`; a geometria foi aprovada no Superdesign. Pode ser reproduzido compilando `tools/BuildIcon.cs` com referência a System.Drawing. O pacote verifica os ícones extraídos dos três executáveis e carrega a definição de login no Agendador em memória, sem registrar tarefa nem iniciar hardware. A instalação real registra `AuraLink Startup` para o usuário administrador atual, configura o provedor persistente e inicia o aplicativo na bandeja.
