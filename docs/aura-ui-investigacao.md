# Comparação Corsair e lista da interface Aura

Investigação de 1 de novembro de 2026, após a remoção do protótipo da etapa 3. Integração não oficial. Esta rodada fez somente leituras de arquivos, registro, estado de serviços e logs; não ativou COM de fabricantes, registrou HAL, enviou efeitos ou reiniciou serviços.

Atualização posterior: o [ensaio controlado de renovação](aura-cache-ensaio.md) apresentou o cartão e entregou efeitos reais ao mesmo HAL. As hipóteses e os limites abaixo registram o estado anterior a esse ensaio.

O [ensaio seguinte HeadphoneStand](headphonestand-razer.md) identificou e validou o tipo HAL `0x41000` como suporte de fone. A incerteza do mapeamento registrada abaixo também foi superada nessa versão instalada.

## Resultado que muda o próximo teste

**O cache do serviço da interface é uma hipótese forte e ainda não testada.** Nos ensaios anteriores, reiniciamos o LightingService e reabrimos o aplicativo, mas o processo do ArmouryCrateService que guarda a lista continuou o mesmo. Não podemos tratar a ausência do cartão como prova de rejeição definitiva do HAL ou de exigência de parceria ASUS.

Há também uma categoria interna de **suporte de fone**, `RogAudioDevices_HeadphoneStand`. Ela é mais coerente com o Base Station que Mousepad ou Headset. Sua existência no código não prova que um HAL genérico possa usá-la: o identificador encontrado pertence à representação interna da interface, não foi confirmado como `root/type` do XML HAL.

## 1. Corsair × AuraLink

A comparação reutilizou as quatro capacidades Corsair salvas na recuperação anterior, sem reativar o plugin. O registro de descoberta foi lido novamente nas visões 32 e 64 bits. O XML AuraLink é o arquivo-fonte atual, correspondente ao último ensaio Headset com sincronização habilitada.

| Campo | Corsair instalado | AuraLink ensaiado |
|---|---|---|
| CLSID | `099B9EEB-5BBA-435C-A89E-212FEEEFA394` | `9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6` |
| Servidor COM | DLL nativa, `ThreadingModel=Both` | Assembly .NET Framework via `mscoree.dll`, `Both` |
| Descoberta | Mesma árvore de componentes HAL, ambas as visões | Mesmo mecanismo; retirado ao final da etapa 3 |
| `Pluging` / `SpecVersion` no registro | `0` / `1.0.0` | `0` / `1.0.0` |
| `DeviceType` no registro | `DRAM` | `Headset` |
| Versão publicada no registro | `2.0.0` | `0.1.0` |
| Versão da capacidade | `1` | `1` |
| Tipo HAL | `458752` (`0x70000`) | `262144` (`0x40000`) |
| Dispositivos / LEDs | Quatro dispositivos de 10 LEDs | Um dispositivo de 1 LED |
| Efeitos | Manual 0, Static 1, Default 255, Breathing 2, Color cycle 4 | Manual 0, Static 1 |
| Manual e Static | `synchronizable=1`, `customized_color=1` | Mesmos valores |
| Desinstalador no registro HAL | Entradas MSI presentes | Ausentes |

O plugin tem versão de arquivo 2.2.115, distinta de sua versão publicada `2.0.0` na descoberta. Não há motivo demonstrado para copiar essa versão nem suas entradas MSI. As strings de desinstalação foram apenas lidas, nunca executadas.

O conjunto de elementos XML Corsair só acrescenta `levels` e `default`, usados nas velocidades de Breathing/Color cycle. Não foi identificado um campo básico obrigatório ausente no AuraLink. Isso não equivale à validação de um schema completo: os requisitos internos da interface permanecem parcialmente desconhecidos. A falta dos efeitos 2, 4 e 255 é uma diferença a registrar, não uma causa comprovada nem autorização para anunciar efeitos não implementados.

A Corsair usa o tipo DRAM existente; a designação interna `GSkillDram` do serviço não implica que o hardware seja G.Skill. Na máquina, esse cartão corresponde aos módulos Corsair confirmados pelo usuário. O registro comparável e a aceitação anterior do HAL próprio pelo LightingService também não demonstram equivalência de todos os contratos.

A [documentação oficial ASUS para Corsair](https://rog.asus.com/uk/support/faq/1055748/) confirma a integração pelo pacote dedicado, mas não publica o contrato de provedor. O [SDK público](https://www.asus.com/microsite/aurareadydevportal/interface_aura_service_lib_1_1_i_aura_sdk.html) documenta enumeração e aquisição de controle de dispositivos existentes; não fornece um mecanismo de criação de categorias da interface.

## 2. Caminho da lista e cache

Módulo analisado: `C:\Program Files\ASUS\Armoury Crate Service\AuraPlugin\ArmouryCrate.AuraPlugin.dll`, x64 nativo, versão 6.7.3.0, assinatura ASUSTeK válida. SHA-256: `bade6b564acad07658292faec320f7acd267784529ac0dadda0bcb9f1ee33033`. A análise leu o PE com pefile/capstone; não carregou a DLL.

Referências e fluxo observados nessa versão específica:

- A entrada de status da interface, RVA `0xb63c0`, chama a rotina da lista em `0xb649f` e depois produz a resposta `GetDeviceStatusNewXML`.
- A rotina da lista, RVA `0x121bb0`, testa um byte de estado do objeto em `0x121cf4` e outro estado global em `0x121cfe`. O desvio leva a `0x12200b`, com mensagem de lista já obtida ou suspensão. Nesse ramo não é criado o worker de consulta completa. Há outras atualizações nesse caminho; não se afirma que toda a resposta de status fique congelada.
- O caminho de consulta completa cria um worker; a chamada em `0x123d33` chega ao helper `GetDevList`, RVA `0x23dd90`.
- Os logs identificam as três consultas desse helper: `QueryAllDeviceStatus`, `QueryAllDeviceCap` e `QueryAllDevice`. A interface tem, portanto, outra representação além dos objetos enumerados pelo SDK.
- A construção da resposta também referencia um arquivo `GetDeviceStatusNew.xml` e campos como `DeviceCount`, `HALIsInstalled` e `Name`. O arquivo não foi localizado nos diretórios ASUS examinados; nenhum arquivo foi criado ou editado para influenciar esse caminho.

Os RVAs são pontos para reproduzir a inspeção, não interfaces estáveis para chamar ou alterar. A desmontagem linear pode atravessar dados embutidos; as conclusões acima usam instruções e referências específicas, conferidas com o fluxo e os logs, sem assumir que todo byte das funções seja código.

### Correlação temporal

Horários locais, UTC−3, no log `ArmouryCrate.Service_2026-11-01_01.log`:

| Evento | Horário |
|---|---|
| Consulta completa, processo 5008, três etapas concluídas | 12:39:31.993–12:39:40.072 |
| LightingService carrega o AuraLink corrigido, versão Mousepad | Aproximadamente 14:36 |
| Reabertura da interface e pedido `GetDeviceStatusNew` | 14:40:31.124 |
| Nova reabertura e pedido de status | 14:44:25.713 |
| Pedido de status após o ensaio Headset | 14:59:46.686 |

Não há nova sequência completa `GetDevList` registrada depois de 12:39 no arquivo examinado, inclusive durante as reaberturas posteriores. O ArmouryCrateService continuou no PID 5008; os reinícios anteriores afetaram apenas o LightingService. A ausência de log, isoladamente, não provaria ausência de consulta, mas a combinação com o ramo de cache torna essa hipótese concreta e prioritária.

**Limite:** não lemos a lista em memória nem obtivemos a resposta real enviada à UWP enquanto o AuraLink estava registrado. Ainda podem existir filtros por identidade, suporte ou categoria após a atualização da lista. Não foi demonstrada uma whitelist de CLSID/fabricante que rejeite o AuraLink.

### Categoria de suporte de fone

A rotina em RVA `0x2b0370` constrói um mapa de nomes internos. Em `0x2b0993` referencia `RogAudioDevices_HeadphoneStand` e em `0x2b09a3` associa o valor `0x02000000`. Outra rotina de informações padrão contém a mesma associação.

**Esse valor não deve ser copiado para o XML HAL.** Os espaços de identificadores diferem: por exemplo, o mesmo mapa usa `4` para Headset, enquanto o SDK/HAL usa `0x40000`; Mousepad e DRAM também passam por outra representação. É necessário rastrear a conversão no LightingService para estabelecer um tipo HAL correspondente. A descoberta corrige a impressão anterior de que só havia Mousepad ou Headset entre as opções plausíveis, sem comprovar apresentação ou controle dessa categoria pelo AuraLink. Não criamos uma categoria nova.

## Reprodução e verificações

Ferramentas adicionadas:

- `tools/compare_aura_metadata.py`: biblioteca padrão Python; lê apenas o resultado JSON Corsair já salvo, o XML próprio e um snapshot de registro. Não coleta capacidades novas.
- `tools/inspect_aura_bridge.py`: inspeção PE x64, referências RIP relativas dentro das entradas de funções de runtime; requer as dependências opcionais de `tools/analysis-requirements.txt` em `artifacts/analysis-deps`. Não executa o alvo.

Exemplos, a partir da raiz do repositório, com os artefatos locais desta máquina:

```powershell
python tools/compare_aura_metadata.py --corsair-saved artifacts/armoury-stage3/corsair-recovery-read.json --auralink-xml src/DiagnosticHal/Capability.xml --registry-snapshot artifacts/armoury-stage3/discovery-registry-readonly.json --output artifacts/armoury-stage3/metadata-comparison.json
python tools/inspect_aura_bridge.py 'C:\Program Files\ASUS\Armoury Crate Service\AuraPlugin\ArmouryCrate.AuraPlugin.dll' --output artifacts/armoury-stage3/ui-bridge-xrefs.json
```

Os comandos terminam ao concluir a leitura; não deixam registro, servidor ou controle de iluminação para liberar. Os caminhos de entrada são específicos da máquina e o snapshot Corsair é histórico. Saídas e desmontagens completas ficam em `artifacts/`, ignorado pelo Git; não redistribuímos as DLLs dos fabricantes. Evidências selecionadas: [aura-ui-investigacao-2026-11-01.json](evidence/aura-ui-investigacao-2026-11-01.json).

## Próximo teste proposto, ainda não executado

Manter exatamente o HAL diagnóstico anterior, com zero saída física, e testar uma **atualização real da lista do serviço da interface**, além do LightingService. Primeiro preparar o procedimento de parada/partida e recuperação do ArmouryCrateService, sem alterar seu tipo de inicialização; depois instalar o mesmo HAL protegido, renovar os serviços na ordem controlada e verificar uma nova sequência completa das três consultas nos logs. Só então conferir o cartão e `SetEffect`. Ao terminar, retirar o registro próprio, renovar a lista novamente e confirmar RAM e ARGB.

Esse ensaio altera serviços temporariamente e fica fora dos itens 1 e 2, que nesta rodada foram de leitura. Não mudar categoria e cache no mesmo ensaio: se o cartão continuar ausente com lista comprovadamente renovada, comparar as respostas de status/capacidades/dispositivos antes de atribuir a falha a filtros. A categoria HeadphoneStand exige uma investigação separada do mapeamento HAL.

Ao encerrar esta rodada, ArmouryCrateService (5008), LightingService (14580) e CorsairDeviceControlService (45720) continuavam em execução. O AuraLink permaneceu sem registro de máquina. Não houve nova validação física, alteração de iluminação ou comprovação de integração nativa.
