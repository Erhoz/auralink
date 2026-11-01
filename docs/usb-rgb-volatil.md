# Saída RGB USB volátil — 1 de novembro de 2026

O Base Station Chroma original `1532:0F08` ficou vermelho por comando HID próprio, confirmado visualmente pelo usuário. AppEngine estava ausente e os serviços Razer Chroma SDK Server e Service ficaram temporariamente parados. Os dois serviços voltaram a Running, com StartMode Auto preservado. O filtro Razer `RzDev_0f08` permaneceu instalado: ainda não comprovamos funcionamento após desinstalar o Chroma/driver.

Esta é uma prova física de saída, separada do HAL que já recebeu efeitos reais do LightingService. **A ligação Armoury → HAL → USB ainda não foi implementada nem validada.**

## Protocolo e dependências

Windows PowerShell 5.1, compilador .NET Framework `Framework64/v4.0.30319/csc.exe`, System.Web.Extensions e APIs Windows hid.dll/kernel32.dll. Sem biblioteca USB adicional. Modelo consultado: firmware `[1,0]`, coleção MI_00 UsagePage 1 / Usage 2, feature report de 91 bytes. A arquitetura mantém o transporte e o codificador específicos em `src/Adapters/Razer/`; o runner experimental fica em `tools/`.

As referências de protocolo são os fontes OpenRazer já analisados, revisão `477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d` (3.12.1, GPL-2.0-or-later), conforme a [investigação anterior](usb-appengine-investigacao.md). O codificador C# é próprio, sem copiar o driver Linux ou redistribuir binários Razer.

O ensaio lê modo `[0,0]`, entra em modo software `[3,0]`, seleciona custom volátil (classe 0F/comando 02, efeito 08, NOSTORE=0) e transmite um frame vermelho para 15 LEDs (0F/03). Cada resposta exige envelope, checksum e status 2 válidos. O encerramento agora envia modo normal **e** efeito arco-íris volátil (0F/02, efeito 04, direção 1, velocidade 28 hexadecimal), depois confirma modo `[0,0]`.

## Falha observada e recuperação

No primeiro ensaio (`20261101-194223-970`), o usuário confirmou vermelho. O retorno ao modo `[0,0]` teve ACK e leitura corretos, mas a base **continuou vermelha**. Portanto, esse encerramento não restaurou fisicamente a iluminação, apesar do sucesso do transporte.

O usuário informou que usava arco-íris. A recuperação explícita (`20261101-194615-568`) enviou modo normal e arco-íris volátil; os comandos foram aceitos e o usuário confirmou: “arcoires voltou”. Os serviços SDK também foram restaurados. A confirmação visual fica separada do JSON automático, que não consegue observar LEDs.

O runner foi corrigido para repetir essa sequência no finally e na recuperação independente do helper. Cinco testes simulados passaram: encerramento normal, falha no frame, falha no modo de recuperação, modo inicial ocupado e falha no comando arco-íris. A sequência corrigida completa ainda não foi repetida em um novo ensaio físico; seus comandos de recuperação foram validados fisicamente no ensaio separado. Não há promessa de recuperar velocidade, direção, fase ou perfis arbitrários anteriores: o efeito completo não é legível pela sonda atual.

## Executar e encerrar

Feche o AppEngine. Com a base em arco-íris conhecido, execute:

```powershell
powershell.exe -NoProfile -File .\Start-RazerUsbRgbTest.ps1 -RainbowBaseline -Seconds 180
```

O parâmetro declara explicitamente o efeito de recuperação; sem ele o teste é recusado. Há compilação, autotestes e UAC para isolar temporariamente somente os dois serviços SDK. Nenhum serviço ASUS é reiniciado. Não há gravação de preset, mudança de brilho, firmware ou configuração de inicialização.

Para encerrar antes do limite, em outro terminal na raiz do repo:

```powershell
$run = Get-Content .\artifacts\usb-rgb-test\latest-run.json -Raw | ConvertFrom-Json
New-Item -ItemType File -Path (Join-Path $run.Directory 'stop-test') -Force
```

O helper fecha o processo e restaura serviços em finally; se o filho falhar, tenta recuperação própria antes de religá-los. Não encerre o helper elevado à força: queda do host/energia pode impedir essa limpeza. Relatórios e stdout ficam no diretório indicado por `latest-run.json`. `Completed` significa conclusão técnica, não observação física. A recuperação manual conhecida é:

```powershell
powershell.exe -NoProfile -File .\Start-RazerUsbRgbTest.ps1 -RestoreRainbow
```

Somente testes de protocolo e inventário, sem alterar luz:

```powershell
powershell.exe -NoProfile -File .\Read-RazerHid.ps1
.\artifacts\usb-investigation\AuraLink.RazerHidProbe.exe --lifecycle-tests .\artifacts\usb-rgb-tests
```

## Próximo passo

Repetir o ciclo corrigido e confirmar visualmente sua recuperação automática. Depois conectar os frames recebidos pelo HAL à saída USB, mantendo adaptadores separados e tratamento de concorrência/encerramento. Remover oficialmente componentes Razer exige outro teste recuperável; não é necessário extrair DLLs AppEngine para a rota USB que acabou de funcionar.
