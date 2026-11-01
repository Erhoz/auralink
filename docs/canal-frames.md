# Canal de frames e estabilidade limitada

Atualização de 1 de novembro de 2026. A [primeira integração física](aura-usb-primeiro-ciclo.md) validou Armoury → HAL próprio → USB usando JSONL provisório. Esta etapa substitui esse transporte por um named pipe Windows. Não instala serviço AuraLink nem inicialização automática.

## Arquitetura

`LightingService → HAL HeadphoneStand → último NativeFrame → pipe local → worker → adaptador Razer USB`.

O contrato COM e o LED lógico anunciado foram preservados. Dentro do processo LightingService, SetEffect publica um objeto imutável em memória, sem arquivo, operação USB ou espera pelo consumidor. Um único thread de fundo responde às consultas do worker. A fila tem capacidade de um frame: uma rajada substitui frames intermediários pela cor mais recente. Os outros hosts COM continuam usando o sink de diagnóstico original, para manter os testes direcionados.

O formato v1 tem 32 bytes: assinatura de quatro bytes, versão uint32, sequência int64, cor uint32, efeito uint32 e timestamp int64 UTC ticks, em little endian. A conversão ASUS permanece `AABBGGRR`, validada fisicamente na etapa anterior. O núcleo de IPC transporta cor e efeito opacos; a seleção dos IDs ASUS 0/1/255 pertence aos adaptadores. Synchronize continua sem agendamento por timestamp; na rota pipe não grava um log por chamada. Metadados de diagnóstico são opcionais na rota real: erro de disco/permissão não deve impedir o dispositivo de operar.

O endpoint é `AuraLink.Frames.v1.<LightingService PID>`. O worker conecta somente na máquina local e confere o PID do servidor via GetNamedPipeServerProcessId. A ACL permite SYSTEM, administradores e a identidade criadora; nega logon de rede. Não há comandos arbitrários ou nomes de arquivos no protocolo: o cliente solicita o frame mais recente com um byte fixo. Referências primárias: [named pipes Windows](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipes), [identificação do servidor](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid) e [NamedPipeServerStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstream).

Cada consulta tem um limite total de um segundo; ruptura/timeout/versão inválida encerra o worker e passa pela recuperação do arco-íris conhecido. Um estático sem novas sequências é saudável: o pipe continua respondendo e nenhuma escrita USB duplicada é feita. Um worker novo pode reconectar e receber o último estático. O worker atual não tenta sobreviver automaticamente à troca de PID do LightingService nem à retirada física do USB; falha explicitamente e o helper tenta recuperar. Não há garantia de restauração com o dispositivo desconectado, queda de energia ou encerramento forçado do helper.

## Execução e dependências

Windows PowerShell 5.1 e .NET Framework/csc locais, System.Core/System.Web.Extensions e APIs Windows hid.dll/kernel32.dll. Nenhuma dependência NuGet nova ou binário de terceiros. Na máquina investigada, exige os componentes Razer ainda instalados e AppEngine fechado; SDK Server/Service são isolados temporariamente. O filtro RzDev_0f08 ainda não foi removido.

```powershell
powershell.exe -NoProfile -File .\Test-FramePipe.ps1
powershell.exe -NoProfile -File .\Start-RazerUsbRgbTest.ps1 -RainbowBaseline -NativeHal -Seconds 300
```

Pipe é o transporte nativo padrão. O launcher compila o novo HAL; o helper elevado copia uma versão própria para diretório protegido por hash, faz ativação COM x86/x64 e renova LightingService/ArmouryCrateService. Não sobrescreve DLL carregada ou arquivo dos fabricantes. Ao encerrar, recupera a base, restaura serviços SDK e remove o registro próprio com atualização dos consumidores ASUS. Os arquivos próprios protegidos são mantidos para análise. O limite permanece 1–300 segundos.

`-FrameTransport Log` preserva a rota anterior nesta máquina, usando o manifesto local legado guardado em `installation-log.json`. É apenas uma alternativa de investigação, não o caminho padrão.

Para parar antes do limite:

```powershell
$run = Get-Content .\artifacts\usb-rgb-test\latest-run.json -Raw | ConvertFrom-Json
New-Item -ItemType File -Path (Join-Path $run.Directory 'stop-test') -Force
```

Os relatórios incluem fonte, frames aplicados, consultas ao pipe, novas sequências, CPU do worker, pico de memória e resultados de recuperação/remoção. `Completed` continua sendo resultado técnico, separado da observação física do usuário. O ensaio de cinco minutos não equivale a teste prolongado de uso diário.

## Verificação automatizada

Oito verificações do IPC passaram em x86 e x64: frame inicial vazio, estático sem retransmissão, coalescência de 50.000 frames, reconexão do worker, rejeição de PID diferente, desconexão do publisher, pacote inválido/truncado e timeout de servidor silencioso. A corrida em que o cliente fecha antes de WaitForConnection concluir foi identificada e corrigida recriando a instância do pipe. Depois, vinte repetições x64 passaram; isso testa IPC local, sem hardware RGB.

Um teste adicional usa pipe Windows real e USB simulado: publica vermelho, derruba o publisher após o primeiro frame e exige recuperação do modo, comando arco-íris e Dispose. Os testes COM nativos e de metadados pelo SDK ASUS continuaram passando nas duas arquiteturas, com remoção do registro temporário. São evidências distintas da validação física.

`Test-HalPipeDevice.ps1` também passou em x86/x64: um sink injeta erro de disco, GetCapability continua funcionando e SetEffect/Synchronize não dependem desse sink para entregar o frame pelo pipe. Esse teste usa a DLL HAL real e IPC real, sem USB físico.

## Resultado físico

O ensaio `20261101-201251-992` executou 300 segundos no LightingService real (PID 33480), com AppEngine ausente e os dois serviços SDK isolados. O worker completou 4.846 consultas e aplicou um frame preto inicial por USB, com resposta validada do dispositivo. Nenhum novo efeito chegou durante a janela, e não houve resposta à pergunta de conferência visual enquanto o teste estava ativo.

O resultado comprova a comunicação real entre serviço e worker e sua estabilidade em estado estático por cinco minutos; **não comprova cores/animação observadas no novo transporte nem estabilidade prolongada**. A validação física anterior por JSONL continua registrada separadamente. CPU total do worker: 1.437,5 ms; pico de working set: 37.822.464 bytes. São métricas de uma execução, sem comparação de desempenho ou promessa de limite de memória.

Ao atingir a duração, o worker confirmou modo `[0,0]` e aceitação do comando arco-íris. O helper restaurou os dois serviços SDK, removeu o registro próprio e renovou os consumidores ASUS sem erro. A inspeção final encontrou LightingService/ArmouryCrateService e os dois serviços SDK Running/Auto, e CorsairDeviceControlService Running/Manual. A observação física do estado após a limpeza continua separada e pendente até o usuário responder.

Os guards de diagnóstico opcional e a supressão de log em Synchronize foram concluídos depois de iniciar essa execução; passaram nos testes da DLL real com pipe x86/x64 e na regressão COM/SDK, mas não ganharam uma segunda execução de máquina nesta janela. Hashes, métricas e distinções estão em [frame-pipe-2026-11-01.json](evidence/frame-pipe-2026-11-01.json).

Próximo teste: repetir com o usuário presente, selecionar o suporte, conferir primárias e deixar um efeito animado ativo durante toda a janela. Depois avaliar teste prolongado e recuperação por reconexão física USB; estes dois últimos ainda não foram executados. Nenhum componente Razer foi desinstalado.
