# Rodada final: estabilidade e USB

Solicitada pelo usuário como as duas últimas etapas de testes. Não iniciar novas rodadas visuais após estas sem novo pedido. O controle continua sendo Armoury → HAL HeadphoneStand → pipe local → Base Station Chroma original `1532:0F08`.

## Estabilidade

Execução limitada a 600 segundos com um efeito animado escolhido no Aura Sync. O worker registra amostras a cada 30 segundos: frames escritos, CPU e working set. A limitação do runner foi ampliada de 300 para 900 segundos, mantendo o padrão de 180 segundos e a limpeza automática. Dez minutos são uma janela limitada; não equivalem a estabilidade diária ou a certificação.

```powershell
powershell.exe -NoProfile -File .\Start-RazerUsbRgbTest.ps1 -RainbowBaseline -NativeHal -Seconds 600
```

A primeira tentativa não começou porque o Windows informou cancelamento do UAC. Não houve registro/sessão ativos nessa tentativa. A execução seguinte (`20261101-222029-698`) completou os 600 segundos, com 7.668 frames escritos, 7.782 consultas ao pipe e nenhuma falha registrada. As 19 amostras de 30 segundos tiveram frames novos em todos os intervalos; working set amostrado de 37.044.224 a 38.043.648 bytes e pico do processo de 38.146.048 bytes. CPU total do worker: 7.421,875 ms. Essas métricas descrevem somente essa execução e não provam ausência de vazamento em uso prolongado diário.

O usuário confirmou a animação no início e, ao final: “Sim, permaneceu normal e voltou ao arco-íris”. O modo normal foi confirmado por leitura; serviços SDK restaurados e registro próprio removido sem erro. O efeito animado específico não foi identificado: foi solicitado Ciclo de cores ou Respiração, à escolha do usuário.

## Desconexão e reconexão USB

O modo explícito `-UsbReconnectTest` acrescenta monitoramento de modo por HID uma vez por segundo e reabertura limitada a 60 segundos após uma falha de transporte. Fecha o handle antigo, espera a coleção exclusiva do modelo voltar, aceita somente os modos normal/software `[0,0]` ou `[3,0]`, reentra em modo software e seleciona custom volátil. A escrita que falhou é repetida; na próxima consulta chegam os frames atuais do Aura, sem reproduzir uma fila antiga de efeitos. Nenhum preset ou brilho é gravado.

```powershell
powershell.exe -NoProfile -File .\Start-RazerUsbRgbTest.ps1 -RainbowBaseline -NativeHal -UsbReconnectTest -Seconds 180
```

Essa recuperação é opt-in, específica do primeiro dispositivo e sem instalação permanente. O mesmo pipe/PID do LightingService deve continuar ativo durante o ciclo USB. Não há reinício de serviço ASUS para recuperar a base no meio do ensaio. O helper reserva até 90 segundos além da duração do filho para encerramento/reconexão limitada; não espera indefinidamente.

Com a base já animando, o operador retira o cabo USB da base por cerca de cinco segundos e o recoloca na mesma porta. Não é necessário responder enquanto está desconectada. O status fica em `usb-reconnect-state.json`; o relatório final inclui contagem e horários dos eventos de reconexão. O arquivo é um registro do último evento, não uma indicação de que o processo permanece ativo após o fim.

Falha, cancelamento ou modo desconhecido ficam explícitos. Se a base estiver ausente, o código não declara que restaurou sua iluminação. Ao reconectar dentro da janela e concluir, a limpeza devolve modo normal e arco-íris conhecido e remove o registro próprio. Estado completo de perfis arbitrários continua não legível, e o filtro Razer permanece instalado.

Três verificações com USB simulado passaram na preparação: desconexão com retomada e recuperação (incluindo uma falha transitória de inicialização), cancelamento enquanto ausente sem falso sucesso de restauração e recusa a modificar um modo desconhecido. São verificações do código, separadas do resultado físico abaixo.

O ensaio físico `20261101-223125-218` registrou desconexão às 22:31:49.4966415 UTC e reconexão às 22:31:55.8789934 UTC: intervalo de 6,38 segundos entre esses eventos, sem promessa de latência exata até a primeira cor visível. Houve uma reconexão e 484 frames aplicados no total. O mesmo LightingService PID 52180 continuou ativo durante a retomada. O usuário confirmou: “Reconectei e voltou a acompanhar”.

Ao parar por arquivo, o worker confirmou modo normal e aceitação do comando arco-íris. O helper restaurou os serviços SDK e removeu seu registro, sem erro de limpeza. O usuário confirmou a recuperação e os demais dispositivos funcionando: “Sim, tudo normal”. Nenhum reinício de serviço ASUS foi usado para retomar a iluminação durante a desconexão/reconexão; as atualizações normais da lista ocorreram antes e depois do ensaio.

## Resultados

**As duas últimas etapas passaram e a rodada de testes foi encerrada.** Evidência selecionada, hashes, amostras e observações em [final-tests-2026-11-01.json](evidence/final-tests-2026-11-01.json). Na inspeção final, LightingService, ArmouryCrateService e os dois serviços SDK estavam Running/Auto; CorsairDeviceControlService estava Running/Manual. Registro de máquina e sessão temporária AuraLink ausentes.

A conclusão vale para este modelo, uma janela de dez minutos e um ciclo USB físico na mesma porta. Não houve teste diário, de troca de porta ou de remoção do driver. Não há inicialização automática, daemon AuraLink ou instalação para uso contínuo habilitados. Não contar compilação, simulação ou a tentativa de UAC cancelada como teste físico concluído. Não iniciar novas rodadas visuais sem novo pedido do usuário.
