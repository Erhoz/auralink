# Primeiro ciclo Armoury → HAL → USB

**Caminho nativo Armoury → HAL → USB validado fisicamente**, em 1 de novembro de 2026: o usuário confirmou vermelho, verde e azul corretos após corrigir a conversão dos canais. Em ensaio anterior confirmou animação recebida do Armoury e os demais dispositivos funcionando, embora vermelho/azul ainda estivessem invertidos naquele momento. O ciclo USB vermelho → recuperação automática do arco-íris também já foi confirmado fisicamente em teste separado. A conclusão técnica do runner continua distinta da observação física.

O HAL HeadphoneStand protegido anteriormente validado continua recebendo os comandos reais do LightingService e gravando seu próprio log. Um processo separado lê somente novos SetEffect desse PID e envia a cor aos 15 LEDs. Este é um backend do nosso provedor nativo; não lê cores da RAM Corsair ou de outro dispositivo existente, nem adquire o controle do SDK ASUS. Não há geração própria de efeitos nesta rota.

O uso de JSONL é IPC provisório para a prova de viabilidade. Evita USB bloqueante dentro do LightingService sem exigir substituir o HAL já validado. O núcleo `RgbColor` é independente; `HalLogFrameSource` interpreta ASUS, e `HidFeatureSession`/`VolatileFrameProtocol` implementam a saída Razer. Uma implementação posterior deve substituir o log diagnóstico limitado por um canal de frames apropriado, com propriedade exclusiva do dispositivo e watchdog de processo.

## Execução limitada

Pré-requisitos desta máquina: instalação protegida HeadphoneStand em `artifacts/headphonestand-test/installation.json`, cópia e hash correspondentes em Program Files, serviços ASUS em execução, nenhum registro/sessão AuraLink ativo, AppEngine fechado e base em arco-íris conhecido.

```powershell
powershell.exe -NoProfile -File .\Start-RazerUsbRgbTest.ps1 -RainbowBaseline -NativeHal -Seconds 180
```

O helper registra somente nosso HAL, usa o procedimento já validado para atualizar LightingService/ArmouryCrateService e isola temporariamente os serviços Razer SDK. Registra o PID real do LightingService, abre seu log no final e descarta eventos antigos, de outros processos ou com mais de dois segundos. Coalesce frames novos a cada 50 ms; não reproduz uma fila atrasada. Efeitos estáticos podem ficar sem enviar novos frames: o último frame é mantido até outro comando ou o fim do teste. O limite total permanece 1–300 segundos. A conversão atual interpreta o SetEffect HAL como `0xAABBGGRR`, ignorando o byte alto e extraindo vermelho do byte baixo. A interpretação inicial `0xAARRGGBB` foi refutada pela observação física: vermelho no Armoury apareceu azul. Não inferimos o contrato HAL somente do campo Color do SDK público.

Na interface, reabrir Aura Sync, selecionar o suporte e aplicar vermelho, verde, azul e depois um efeito animado. Conferir também RAM, coolers e dispositivos ASUS. Atualmente um único LED lógico anunciado pelo HAL é replicado nos 15 LEDs físicos. Synchronize é registrado pelo HAL, mas o worker não implementa agendamento por esse timestamp; a prova aplica frames conforme sua chegada.

Para terminar antecipadamente, usar o arquivo `stop-test` descrito no [ensaio USB](usb-rgb-volatil.md). O finally do filho devolve modo normal e arco-íris conhecido. O helper tenta recuperação independente se necessário, restaura os serviços SDK e remove o registro próprio com nova atualização dos dois consumidores ASUS. Não altera tipos de inicialização nem configura autostart AuraLink. Encerrar o helper elevado à força pode impedir sua limpeza.

Relatórios ficam em `artifacts/usb-rgb-test/`, indicados por `latest-run.json`. `FramesWritten`, `LastColorRgb`, `NativeSource`, `NativeEnd` e `NativeRegistrationRemoved` distinguem envio, fonte e limpeza. São dados de transporte, não sensores de cor. Os cinco testes de ligação adicionais usam transporte falso e cobrem aplicação/restauração, falha da fonte, interpretação de canais, rejeição de eventos antigos/estranhos e leitura de linhas parciais sem replay.

## Primeiro ensaio e correção

O ensaio `20261101-195549-927` abriu o HAL real e enviou um frame preto. Um timeout inicial de 30 segundos sem novos frames encerrou a experiência antes da conferência. Esse timeout foi removido por ser incompatível com efeitos estáticos que enviam uma única cor. O teste restaurou modo/arco-íris e serviços e removeu seu registro; não conta como sucesso físico da sincronização.

O segundo ensaio (`20261101-195652-291`, LightingService PID 50948) aplicou 376 frames. O usuário inicialmente informou primárias corretas, mas corrigiu em seguida: “na verdade no vermelho ficou azul”. Essa correção prevalece; o ensaio não prova cores corretas. Em pergunta separada sobre animação e outros dispositivos, confirmou “Base acompanha e os demais continuam funcionando”. A parada por arquivo concluiu recuperação do modo e arco-íris, restaurou os serviços SDK e removeu o registro próprio. O resultado é sincronização física com canais invertidos, não sincronização fiel de cores.

Após trocar a extração dos canais vermelho/azul, o terceiro ensaio (`20261101-195944-626`, LightingService PID 49184) aplicou 357 frames. O usuário confirmou: “Agora vermelho, verde e azul estão corretos”. A parada por arquivo retornou modo `[0,0]`, aceitou o comando arco-íris, restaurou os serviços SDK e removeu o registro próprio, sem erro de limpeza. O usuário confirmou a recuperação e a ausência de regressão: “Sim, arco-íris voltou e os demais funcionam”. Na conferência final, LightingService e ArmouryCrateService estavam Running/Auto, CorsairDeviceControlService Running/Manual e ambos os serviços Razer SDK Running/Auto.

Evidência selecionada: [aura-usb-2026-11-01.json](evidence/aura-usb-2026-11-01.json). O filtro Razer continua instalado; nenhuma independência completa de drivers foi comprovada. A prova não constitui integração pronta para uso contínuo: o próximo passo é um canal de frames próprio com ciclo de vida e reconexão, seguido de um teste de estabilidade e recuperação; nenhuma inicialização automática foi configurada.
