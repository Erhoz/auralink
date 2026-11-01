# USB direto e componentes AppEngine

Este documento registra a etapa de consultas e análise estática. A etapa seguinte já confirmou vermelho físico por USB e recuperação explícita do arco-íris; veja o [ensaio RGB volátil](usb-rgb-volatil.md), suas limitações e a falha do primeiro encerramento.

Investigação em 1 de novembro de 2026, após a [validação do Chroma e de suas dependências](razer-saida-e-dependencias.md). O objetivo continua sendo receber efeitos nativos do Armoury por um HAL HeadphoneStand; as rotas abaixo substituem somente a saída para o Razer.

## Resultado

**O protocolo USB respondeu neste hardware sem AppEngine e com os dois serviços SDK temporariamente parados.** A sonda própria abriu a coleção HID do modelo original, consultou modo e firmware e validou as respostas. Nenhum comando de iluminação ou mudança de modo foi enviado.

| Critério | Resultado |
|---|---|
| Enumerar interfaces HID `1532:0F08` | Sete coleções presentes e abertas para metadados |
| Encontrar feature report adequado | MI_00, UsagePage 1 / Usage 2, 91 bytes |
| Enviar consulta e obter resposta direta | Comprovado por HidD_SetFeature / HidD_GetFeature |
| Validar modo | Status 2, argumentos `[0,0]` |
| Validar firmware | Status 2, argumentos `[1,0]` |
| Ler brilho padrão | Status 5: não suportado |
| Ler brilho estendido | Status 3: falha reportada pelo dispositivo |
| Controlar LED por USB próprio | Ainda não implementado/testado |
| Desinstalar Chroma ou driver | Não executado |

Os resultados de brilho são negativos, não valores zero. `brightness=null` e `snapshotComplete=false` são intencionais. `Completed=true` no relatório final significa que o procedimento de consultas e restauração dos serviços terminou, não que todas as propriedades foram lidas ou que a saída RGB está pronta.

## Fonte e protocolo

Foi consultado somente código-fonte, sem executar binários GitHub. Revisão OpenRazer fixada: `477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d`, versão declarada 3.12.1. Fontes principais:

- [razeraccessory_driver.h](https://github.com/openrazer/openrazer/blob/477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d/driver/razeraccessory_driver.h): original `USB_DEVICE_ID_RAZER_CHROMA_BASE = 0x0F08`; V2 separado como `0x0F20`.
- [razeraccessory_driver.c](https://github.com/openrazer/openrazer/blob/477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d/driver/razeraccessory_driver.c): seleção dos comandos/transações por modelo.
- [razercommon.c](https://github.com/openrazer/openrazer/blob/477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d/driver/razercommon.c) e [razercommon.h](https://github.com/openrazer/openrazer/blob/477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d/driver/razercommon.h): transporte, pacote e checksum.
- [razerchromacommon.c](https://github.com/openrazer/openrazer/blob/477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d/driver/razerchromacommon.c): significado dos comandos de consulta.

Os drivers consultados declaram GPL-2.0-or-later; os fontes e a licença foram guardados somente como referência em `artifacts/`. Não incorporamos o driver Linux. O codificador C# é original, restrito aos campos e comandos necessários para a experiência; não é uma portabilidade completa desse driver. A licença global do AuraLink não foi escolhida nesta etapa.

O controle usa um pacote Razer de 90 bytes. A API HID Windows acrescenta o byte de report ID, resultando em buffer de 91 bytes. Foram usados report ID 0, pacote único e checksum XOR dos offsets 2–87 do pacote Razer. O leitor confere tamanho, ID, classe/comando, contagem restante, comprimento e checksum antes de interpretar status ou dados.

| Consulta lógica | Transação | Classe / comando | Argumentos do pedido |
|---|---|---|---|
| Modo | FF | 00 / 84 | 00 00 |
| Firmware | FF | 00 / 81 | 00 00 |
| Brilho padrão | FF | 03 / 83 | 01 05 00 |
| Brilho estendido, alternativa identificada no código | 3F | 0F / 84 | 01 00 00 |

O pedido de consulta é enviado com SET_FEATURE e a resposta lida com GET_FEATURE. Isso é uma escrita de transporte que solicita um estado; não é um comando que define cor, brilho ou modo. Não foram usados ReadFile ou relatórios de entrada de mouse/teclado. Consulte também [HIDP_CAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps) e [obtenção de relatórios HID](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports), da Microsoft.

## Experiência na máquina e isolamento

Na coleção MI_00, o Windows informou 9 bytes de entrada, zero de saída e 91 de feature. A sonda abriu o handle com acesso desejado zero e compartilhamento de leitura/escrita; esse handle permitiu as consultas feature. Não foi necessário instalar outro driver ou pacote para executar a sonda.

O helper exigiu AppEngine ausente e parou normalmente somente `Razer Chroma SDK Server` e `Razer Chroma SDK Service`, sem dependentes em execução. Após as consultas, confirmou ambos ainda parados e restaurou os que estavam Running antes do teste, sem alterar seus tipos de inicialização. Esse caminho foi executado três vezes ao refinar o tratamento das respostas negativas de brilho; os relatórios anteriores foram preservados. Não houve reinício ASUS, firmware, reboot ou desativação permanente.

A primeira execução interrompeu a leitura ao receber status 5 no brilho padrão. A segunda preservou as leituras anteriores e tentou o brilho estendido, recebendo status 3. O leitor final distingue pacotes válidos com rejeição de erros de transporte, preservando todas as respostas e deixando o snapshot parcial. Não se tentou adivinhar um brilho ou gravar um valor para contornar a falta de leitura.

### Limite importante: filtro Razer presente

A interface USB usa `HidUsb`, provedor Microsoft, `input.inf`, sem filtros listados nesse nó. Entretanto, a coleção de mouse usa `mouhid` com INF Razer `oem121.inf` e **UpperFilters = RzDev_0f08**. O log SetupAPI registra a instalação desse filtro às 15:54:54–15:54:55 locais, durante a instalação Razer anterior.

Portanto, esta prova não demonstra funcionamento sem todos os drivers Razer. Ela demonstra ausência de AppEngine e dos dois serviços SDK ativos durante a consulta. O filtro não foi removido nem desativado. A viabilidade de manter apenas o driver ou usar exclusivamente o HID padrão depois da desinstalação ainda precisa de um teste separado e recuperável.

## Alternativa: hospedar componentes do AppEngine

Foram examinados PE, imports, exports e wrappers JavaScript de arquivos instalados, sem carregar as DLLs nem executar o JavaScript. A leitura do ASAR acessou somente membros de texto específicos; cópias de análise permanecem em `artifacts/`, sem redistribuição dos módulos do fabricante.

| Componente | Observações estáticas |
|---|---|
| RzChromaSDKProxy_v1.2.4.10151518.dll | x64, 364.232 bytes, assinatura válida; exports Init/UnInit, AddDevice/RemoveDevice, Invoke, PollEvents, SetNodeFFIEvent, SetAllocator e SetOperatingMode |
| chroma_native_sdk_v1.6.0.0.dll | x64, 917.128 bytes; exports Init, Terminate, JsonApi/JsonApiNoReturn, FreeMalloc e funções de versão |
| AppEngine app.asar | Contém host genérico `ffi-napi-rz`, registro de DLLs, callbacks, eventos, mutexes e ações de encerramento |
| FFIPreloadMain | Recebe dinamicamente `dllPath` e `apiObj` para montar a biblioteca FFI; as assinaturas específicas não são dedutíveis somente pelos exports |
| FFILightingDriver | Interface separada Startup/Shutdown, Configure(JSON), callbacks de escrita, registro de dispositivos e liberação das strings |

Os imports PE observados desses dois módulos são Windows/Visual C++ runtime; isso **não prova autonomia**, pois há dependências dinâmicas, objetos de configuração e callbacks. O papel do host é mais amplo que mostrar a janela. O Proxy tinha sido observado carregado no processo principal AppEngine quando o SDK passou a conceder acesso à iluminação.

Não foi confirmado um contrato público para hospedar esses módulos, nem um programa mínimo que os inicialize, receba frames e controle o hardware. Não chamamos Init com assinaturas presumidas, não sinalizamos artificialmente presença do aplicativo e não modificamos arquivos ou flags do SDK. Extrair arquivos ainda não equivale a extrair um backend funcional. A investigação dessa rota segue possível, mas permanece em análise estática.

## Protótipo e execução

Arquivos próprios:

- [HidMetadata.cs](../src/Adapters/Razer/HidMetadata.cs): enumeração e capacidades, restritas ao VID/PID alvo.
- [BaseStationProtocol.cs](../src/Adapters/Razer/BaseStationProtocol.cs): somente quatro pedidos de consulta nomeados e validação das respostas; não implementa escrita de iluminação.
- [HidStateReader.cs](../src/Adapters/Razer/HidStateReader.cs): escolha única de MI_00 e leitura por feature reports.
- [Read-RazerHid.ps1](../Read-RazerHid.ps1): compila a sonda com .NET Framework e executa em filho com timeout.
- [Invoke-RazerUsbRead.ps1](../tools/Invoke-RazerUsbRead.ps1): isolamento temporário e restauração dos dois serviços SDK, com relatório.
- [read_asar_member.py](../tools/read_asar_member.py): leitura de texto de um membro ASAR, sem executar o conteúdo.

Somente metadados, sem transferência de feature reports:

```powershell
powershell.exe -NoProfile -File .\Read-RazerHid.ps1
```

Consultas ao hardware, com elevação normal para isolar serviços:

```powershell
powershell.exe -NoProfile -File .\Read-RazerHid.ps1 -QueryState
```

Resultados em `artifacts/usb-investigation/`, ignorado pelo Git. A compilação usa o csc .NET Framework já instalado, sem SDK .NET novo. Os leitores terminam sozinhos e fecham seus handles; o helper restaura serviços em `finally`. Há limites de tempo e recuperação registrada, sem garantia de executar `finally` se o host elevado for encerrado à força. Não interromper o helper durante uma experiência; se houver falha, conferir `state-operation.json` e os estados reais antes de repetir.

O codificador executa testes sem hardware: checksums conhecidos dos quatro pedidos, aceitação de resposta válida, recusa a comando divergente, checksum corrompido, estado negativo e comando de iluminação não implementado. A leitura física é uma validação distinta desses testes. Evidência selecionada: [usb-appengine-2026-11-01.json](evidence/usb-appengine-2026-11-01.json).

## Próximo passo

Priorizar um teste USB de **frame volátil**, sem gravar um preset permanente nem mudar brilho. Precisamos validar o comando de frame do modelo, a transição controlada a modo de software e a volta ao modo `[0,0]`, com leitura de confirmação, tratamento de falhas e observação física enquanto o teste está ativo. O efeito/perfil completo anterior não foi lido, então a restauração exata de configurações arbitrárias ainda não está garantida.

Somente após controlar e liberar a iluminação por USB devemos ligar esse adaptador ao HAL e avaliar a remoção oficial dos componentes Razer, preservando um caminho de recuperação do driver. O controle continuará Armoury → HAL HeadphoneStand → adaptador Razer; não será substituído por leitura de RAM ou aplicativo gerador de efeitos.
