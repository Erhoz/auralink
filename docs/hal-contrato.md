# Contrato mínimo HAL e provedor de diagnóstico

Atualização de 1 de novembro de 2026, posterior à [avaliação inicial](viabilidade.md). Integração não oficial, sem vínculo com ASUS, Razer ou Corsair.

> Este documento descreve a etapa 2. A [etapa 3](armoury-etapa3.md) carregou o HAL no serviço real e corrigiu CodeBase em caminhos com espaços. Posteriormente, o [ensaio de renovação da lista](aura-cache-ensaio.md) comprovou o cartão na interface e a recepção de efeitos reais. O registro temporário foi removido; a saída física Razer continua pendente.

**Resultado:** o SDK ASUS instalado ativa e interpreta um HAL AuraLink próprio em x86 e x64. O HAL recebe chamadas sintéticas por COM nativo e grava seus dados em log. A presença na interface do Armoury Crate e a recepção dos efeitos dele continuam não comprovadas. Nesta etapa não foi feito registro de máquina, publicação na descoberta do Aura ou teste visual do Armoury.

## Contrato recuperado e validado

| Interface | IID | Ordem após IUnknown |
|---|---|---|
| HAL | `F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9` | `Enumerate`, e na variante ASUS atual `Enumerate2` |
| Dispositivo | `61711778-AB59-4026-89E8-7A63422C29C2` | `GetCapability`, `SetEffect`, `Synchronize` |

Assinaturas em [Contracts.cs](../src/DiagnosticHal/Contracts.cs), conferidas com a TLB instalada e análise estática dos componentes:

- `Enumerate(IAacLedDevice** buffer, ULONG* count)`: primeira chamada com buffer nulo devolve a contagem; a segunda usa um vetor de ponteiros alocado pelo chamador. Cada ponteiro devolvido tem uma referência que o chamador libera.
- `Enumerate2(VARIANT* value, ULONG* count)`, na variante ASUS: contagem inicial zero consulta o número de dispositivos. A segunda chamada devolve **um VARIANT com SAFEARRAY de VT_UNKNOWN**, não um vetor de VARIANTs. O tipo do VARIANT é `0x200D`; o chamador usa `VariantClear`.
- `GetCapability(BSTR* xml)`: devolve a descrição; o chamador libera o BSTR.
- `SetEffect(ULONG effectId, ULONG* colors, ULONG numberOfColors)`: recebe ID e vetor de valores de 32 bits. O diagnóstico preserva os valores brutos, sem inferir formato de cor para Razer.
- `Synchronize(ULONG effectId, ULONGLONG milliseconds)`: o diagnóstico registra o valor de 64 bits; não implementa temporização de LEDs.

O tratamento de referências do SAFEARRAY segue [SafeArrayPutElement](https://learn.microsoft.com/en-us/windows/win32/api/oleauto/nf-oleauto-safearrayputelement) e [VariantClear](https://learn.microsoft.com/en-us/windows/win32/api/oleauto/nf-oleauto-variantclear) da Microsoft. As interfaces opcionais de velocidade, direção, standby e recursos específicos não foram implementadas neste HAL mínimo.

### Correção importante da hipótese anterior

A TLB ASUS atual e a implementação Corsair **não têm a mesma extensão de vtable**, apesar do IID HAL compartilhado. Na DLL Corsair 2.2.115 x64, de SHA-256 `bba035c0c36d65ce88fee3fa12afe84a5d8999bfb298607d09f5d32efda9047a`, a análise RTTI aponta:

- vtable de `CorsairHal` no RVA `0x3F1B8`;
- slot 3 passa pelo thunk `0x25EA` para `0x1A2A0`, que implementa a enumeração e o `AddRef` de cada ponteiro;
- slot 4 passa por `0x1799` para `0x1AB10`, um destrutor C++ com teste do indicador de liberação.

Portanto, a sonda inicial que chamou esse slot como `Enumerate2` estava incorreta e podia destruir o objeto. Os números negativos obtidos não eram códigos de erro de enumeração confiáveis. A chamada foi retirada na primeira entrega; a análise atual explica a falha e impede sua reintrodução. A identificação de uma interface não autoriza presumir métodos adicionais a partir de uma TLB de outro fornecedor.

No HAL DRAM ASUS 1.1.31, `Enumerate` retorna `E_FAIL` e `Enumerate2` constrói um SAFEARRAY `VT_UNKNOWN`. O código do SDK contém fallback entre variantes. Não chamamos essa variante Corsair novamente. Uma leitura posterior, restrita ao slot 3 confirmado, retornou `S_OK` e contagem zero nesta sessão; isso não invalida a RAM instalada nem a configuração anterior do usuário.

## Schema mínimo aceito pelo SDK

[Capability.xml](../src/DiagnosticHal/Capability.xml) é uma descrição original do dispositivo virtual, baseada nos campos encontrados no construtor de capacidades Corsair e **validada pelo parser ASUS instalado**. Não é um schema oficial completo.

`version` e `type` são **elementos filhos de root**, não atributos. `device` contém nome, ID, fabricante, layout e efeitos. O dispositivo anunciado é `AuraLink Diagnostic (no hardware)`, com um LED, layout 1 × 1 e efeitos Manual/Static, IDs 0/1. Usamos tipo `0x91000` (593920), categoria mousepad listada na [documentação ASUS](https://www.asus.com/microsite/aurareadydevportal/interface_aura_service_lib_1_1_i_aura_sdk.html). Essa categoria serve ao diagnóstico; não confirma o suporte do Base Station Chroma.

O cliente direcionado usa `AuraDevelopement` (`34B707DC-1133-4EBC-B380-21387A50A89D`) e `IAuraDevelopement::EumerateDevicesFromHal`, passando **somente o CLSID AuraLink**. Não chama `GetAllDevices`, `Enumerate(0)` ou `SwitchMode`. Assim foi possível validar nome, tipo, dimensões, quantidade de LEDs, quantidade de efeitos e seus IDs sem passar pela descoberta global que bloqueava.

`SetMode`, `SetLightColor` e `Apply` também foram chamados somente no wrapper desse dispositivo virtual. Embora tenham retornado sucesso, **nenhum callback SetEffect foi observado nessa rota sem aquisição global de controle**. Não foi tentado adquirir o controle de todos os dispositivos para forçar o resultado. A entrega de frames pelo SDK permanece incerta; os frames confirmados no log vieram do cliente nativo de teste.

## Diagnóstico dos problemas Corsair

Os logs em `C:\ProgramData\Corsair\Logs\CorsairDeviceControlService` mostram que a ativação do plugin pode iniciar o serviço, inicializar/instalar seu driver LLA sob demanda e registrar um cliente com prioridade de controle. Nessa sessão, o serviço registrou uma busca de memória com timeout de dez segundos sem detectar módulos, depois uma resposta a um pipe já fechado. Há registros de `hadControl: 1` na desconexão e de reativação da iluminação de hardware ao encerrar a sessão.

Isso corrige uma limitação do relatório inicial: **não chamar SwitchMode não prova ausência de controle ou efeitos indiretos no backend Corsair**. Os campos históricos `AcquiredControl=false` eram uma suposição da sonda, não uma medição física. O inventário atual usa estado desconhecido (`null`) quando ativa caminhos de fabricante. Nenhum frame RGB foi enviado aos dispositivos físicos pelo código AuraLink, mas a inicialização e o encerramento do backend não são operações puramente passivas.

O aviso de ausência de `CorsairLLAService` não impediu o serviço moderno de iniciar sua própria camada LLA. Os registros de configuração ainda descrevem quatro módulos de dez LEDs. Não foi estabelecida a causa da detecção vazia nem uma correção para `CDCSConnect: 1`; não foram mudados serviços, drivers, arquivos ou configurações do fabricante para contornar isso nesta etapa. A enumeração global ASUS pode passar por vários provedores: o sucesso direcionado ao AuraLink isola a rota nova, mas não identifica sozinho todos os motivos do bloqueio global.

## Implementação e execução

O núcleo [DiagnosticSink.cs](../src/Core/DiagnosticSink.cs) define `IEffectSink` e grava JSON por processo. O adaptador ASUS implementa COM e enumeração. Não há dependência Razer, USB, SMBus, P/Invoke de dispositivo físico, motor de efeitos ou serviço AuraLink. Os valores recebidos são registrados em `artifacts/diagnostic-hal/logs/events-<pid>.jsonl`; cada arquivo é limitado a aproximadamente 5 MiB. Falha de gravação retorna erro ao chamador.

Compilar e executar os testes nativos:

```powershell
powershell.exe -NoProfile -File .\Test-DiagnosticHal.ps1
```

Incluir a validação direcionada do SDK ASUS instalado:

```powershell
powershell.exe -NoProfile -File .\Test-DiagnosticHal.ps1 -IncludeAsusSdk
```

O runner compila, registra temporariamente a classe apenas em HKCU nas visões x86/x64, executa cada cliente com timeout, confere os dados dos logs e remove o registro em `finally`. Código de saída 2 indica falha ou limpeza incompleta. O relatório fica em `artifacts/diagnostic-tests/report.json`. Cada cliente termina após o teste. Ctrl+C tenta executar a limpeza; encerramento abrupto do host pode exigir remoção manual.

Para um teste manual, sem publicação na descoberta do Aura:

```powershell
.\Build-DiagnosticHal.ps1
.\Register-DiagnosticHal.ps1 -Action Plan
.\Register-DiagnosticHal.ps1 -Action Install -Scope User
.\artifacts\diagnostic-hal\AuraLink.HalClient.x64.exe native
.\Register-DiagnosticHal.ps1 -Action Remove -Scope User
```

O CLSID próprio é `{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}`. O registro usa `mscoree.dll`, identidade da assembly, runtime v4 e `CodeBase`, conforme o mecanismo descrito pela [Microsoft](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/registering-assemblies-with-com). Não instala a assembly no GAC, não registra interfaces de fabricantes e não altera a associação COM Corsair. A DLL não é assinada; aceitação por serviço ou política de confiança permanece desconhecida.

O script recusa colisões; a remoção exige manifesto, marcador de propriedade e CodeBase correspondentes. Verifica todas as visões antes de excluir apenas as chaves próprias, sem apagar árvores compartilhadas. `-Discovery` prepara também a entrada de descoberta observada na instalação Corsair, mas **não foi usado nos testes desta etapa**. Há escopo Machine para a próxima etapa; seu caminho de escrita ainda não foi validado e exige administrador. Uma prévia sem alteração pode ser obtida com:

```powershell
.\Register-DiagnosticHal.ps1 -Action Plan -Scope Machine -Discovery
```

Não reinicia serviços nem configura inicialização automática. O registro por usuário não comprova visibilidade para o LightingService, que executa em outro contexto.

## Validação e dependências

Os clientes x86/x64 não referenciam a assembly do HAL: ativam COM por CLSID e fazem chamadas pelos ponteiros de vtable. Os testes nativos cobrem consulta/preenchimento de contagem, buffer insuficiente, propriedade de referências, BSTR, SAFEARRAY, rejeição de entrada inválida, captura exata de um frame sintético e preservação de um timestamp maior que 32 bits. Os testes SDK conferem o resultado interpretado pelo componente ASUS real; não simulam seu parser. A recusa a colisão e a remoção com propriedade divergente também são exercitadas.

Dependências observadas: compilador C# do .NET Framework `14.8.10001.0`, CLR v4, `System.Web.Extensions`, Windows PowerShell 5.1.26100.9818, SDK Aura 3.08.01.0 x86/x64. Não foi instalado .NET SDK. Apenas a análise estática opcional usa Python 3.14.3, pefile 2024.8.26 e capstone 5.0.9, instalados em `artifacts/analysis-deps`; não fazem parte do HAL. O arquivo [analysis-requirements.txt](../tools/analysis-requirements.txt) fixa essas versões. DLLs e binários de fabricantes não foram copiados ao repositório ou alterados.

O resumo versionável desta rodada está em [hal-diagnostico-2026-11-01.json](evidence/hal-diagnostico-2026-11-01.json). Os scripts de teste removem seu registro temporário; os serviços ArmouryCrateService e LightingService continuaram em execução e o Corsair Device Control Service estava parado no início desta etapa e na verificação posterior. Não reiniciamos o computador ou serviços.

## Próxima etapa

Publicar temporariamente a identidade própria no escopo apropriado ao LightingService, descobrir como atualizar sua lista sem reinstalar componentes oficiais e verificar a interface do Armoury Crate. Só será sucesso nativo quando o dispositivo aparecer e uma mudança feita no Armoury gerar callbacks no HAL. Se o carregamento exigir outra ABI, assinatura ou metadados adicionais, registrar essa evidência antes de alterar a arquitetura. O caminho Razer continua fora deste teste.
