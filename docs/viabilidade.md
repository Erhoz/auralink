# Avaliação inicial — 1 de novembro de 2026

## Conclusão

A integração nativa é **plausível pelo mecanismo HAL, mas ainda não demonstrada para um provedor independente**. Há evidência local de registro e ativação COM do plugin Corsair. Falta validar o contrato do provedor, o formato de capacidades e a aceitação de um HAL AuraLink pelo LightingService e pela interface do Armoury Crate. O SDK público de controle não resolve essas etapas.

| Caminho | Resultado necessário | Situação |
|---|---|---|
| (a) Nativo, prioridade | AuraLink aparece no Armoury Crate e recebe seus efeitos | Não comprovado; investigar HAL |
| (b) Ponte externa | Ler cores produzidas pelo Aura e repassá-las ao Razer | Leitura durante controle do Armoury não comprovada; não adiciona dispositivo à lista |
| (c) Aplicativo próprio | Assumir os dispositivos ASUS e Razer e produzir efeitos | Outro objetivo; não implementado |

A [documentação ASUS](https://www.asus.com/microsite/aurareadydevportal/) distingue API COM e bibliotecas HAL. A interface pública descrita no [tutorial](https://www.asus.com/microsite/aurareadydevportal/tutorial_cpp.html) enumera dispositivos e assume seu controle com `SwitchMode`; não apresenta uma API de cadastro de hardware. Nos materiais públicos consultados não foi encontrado um contrato completo para implementar e distribuir HALs independentes. Isso não prova que tal contrato não exista em programa de parceiros.

A existência de integração oficial é confirmada pela [ASUS](https://rog.asus.com/uk/support/faq/1055748/) e pela [Corsair](https://help.corsair.com/hc/en-us/articles/31086864483729-Memory-Enable-RGB-control-through-third-party-software-Armoury-Crate). Essa última descreve o serviço de controle e instalação sem iCUE. O usuário confirmou anteriormente o controle de sua RAM pelo Armoury; não repetimos um teste visual nesta entrega.

## Evidência local nova

Pasta inicialmente vazia, sem `AGENTS.md` encontrado na pasta ou nos ancestrais consultados. Git inicializado em `main`; `origin` vinculado a `https://github.com/Erhoz/auralink.git`, encontrado pela conexão GitHub. `ls-remote` retornou vazio, compatível com remoto sem commits. Nenhum push realizado.

O plugin oficial tem classe COM `{099B9EEB-5BBA-435C-A89E-212FEEEFA394}`. As visões x64 e x86 do registro apontam para DLLs próprias do pacote. Seu arquivo `x86\createpluginregistry.reg`, apenas lido, declara uma entrada nesta árvore:

```text
HKCR\CLSID\{109DC3E4-B9FF-4AF3-9008-AB13705D4E5F}
  \Instance\{E9BBD754-6CF4-492E-BA89-782177A2771B}\Instance\<CLSID do provedor>
```

Ela contém nome, fabricante, tipo DRAM, `SpecVersion`, `Version` e `Pluging` (grafia original). A árvore também contém outros fabricantes. Isso mostra como provedores instalados são descritos, mas não demonstra que criar uma chave seja suficiente para o serviço aceitar qualquer provedor.

A DLL Corsair x64 exporta `DllGetClassObject`, `DllCanUnloadNow`, `DllInstall`, `DllRegisterServer` e `DllUnregisterServer`. Nenhum export de instalação foi chamado. Strings e imports indicam uso de `CorsairDeviceControlServiceAPI.dll` e operações CDCS de descoberta, geometria e envio de cores. Essas pistas são análise estática, não um protocolo completo.

A biblioteca local `C:\Program Files\ASUS\ASUS_Aac_DRAM\Aac3572DramHal.tlb` foi inspecionada com `LoadTypeLibEx(REGKIND_NONE)`, sem registro nem ativação do HAL. Ela expõe:

- `IAacLedDeviceHal`, IID `F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9`: `Enumerate`, `Enumerate2`.
- `IAacLedDevice`, IID `61711778-AB59-4026-89E8-7A63422C29C2`: `GetCapability`, `SetEffect`, `Synchronize`.
- Interfaces adicionais de velocidade, direção e transporte VARIANT.

O SDK local também contém interfaces de desenvolvimento para descobrir HALs. São metadados locais; estabilidade e suporte público dessas interfaces não foram estabelecidos. Não foi inferida uma ABI Corsair completa apenas pela coincidência do IID.

## Hipótese e resultados executáveis

Hipótese inicial reduzida: **o plugin oficial instalado participa do mecanismo COM HAL identificável nos metadados locais**. `CoCreateInstance` solicitando diretamente o IID HAL retornou `S_OK` e uma interface válida. Essa etapa foi comprovada em execução real, sem métodos de efeito. O resultado não comprova descoberta de um HAL novo pelo Armoury.

Um experimento inicial tentou `Enumerate2` usando a assinatura da TLB ASUS contra Corsair. Retornou valores negativos diferentes entre execuções, nenhuma lista e contagem zero. Não há base para interpretar esses números como erros documentados do fabricante; pode haver incompatibilidade de ABI ou implementação. Essa chamada foi retirada do protótipo entregue. A versão atual limita Corsair à ativação/consulta do IID e liberação da referência.

A sonda `IAuraSdk.Enumerate(0)`, sem `SwitchMode`, não terminou: primeiro processo encerrado especificamente; depois o wrapper comprovou timeout de 15 e 10 segundos. Não foi obtida lista de dispositivos ASUS nem leitura de cores. Os logs incluem aviso de ausência do serviço legado `CorsairLLAService` e, em outra execução, `CDCSConnect: 1`, apesar de existir `CorsairDeviceControlService`. Não se determinou a causa; não se presume defeito do plugin ou ausência da RAM.

A ativação de COM não é isenta de efeitos indiretos: o plugin tentou iniciar/conectar seu serviço. Ele foi observado parado durante a investigação, embora estivesse em execução inicialmente. Restabelecemos sua execução com `Start-Service`, sem mudar o tipo de inicialização manual. Na execução final ele estava ativo antes e imediatamente depois, mas foi observado parado novamente numa consulta posterior. Não se atribui causalidade ao teste, nem se conclui falha ou parada por ociosidade, sem evidência adicional. ArmouryCrateService e LightingService permaneceram em execução nas verificações. Nenhum comando nosso parou serviços de fabricante.

Não foram enviados efeitos, alterados perfis, registradas DLLs, instalados pacotes, reiniciado o Windows ou executados binários baixados do GitHub. Não foi capturado o estado visual dos LEDs. Como nenhum controle RGB foi adquirido, não há efeito anterior a restaurar; as referências COM são liberadas. Antes de qualquer teste futuro de escrita será necessário registrar o estado disponível, implementar liberação explícita em `finally` e validar a devolução do controle no hardware.

## Dependências observadas

| Componente | Versão / estado |
|---|---|
| Armoury Crate Service, cadastro | 6.7.6.0 |
| `ArmouryCrate.Service.exe`, arquivo | 6.7.4.0; diferença registrada, sem causa determinada |
| AURA Service / LightingService | 3.11.08 / arquivo 3.11.08.0 |
| ASUS Aura SDK | cadastro 3.08.01 / DLL 3.08.01.0 |
| ASUS AURA DRAM Component | 1.1.31 |
| Plugin Corsair | instalação e DLL 2.2.115; registro HAL ainda declara 2.0.0 |
| Corsair Device Control Service | 1.9.18 |
| API CDCS junto ao plugin | 1.7.4 |
| RAM via CIM | 4 × 17.179.869.184 bytes, CMW32GX4M2E3200C16: 64 GiB |
| Base Station original | presente em PnP, `1532:0F08`, interfaces com estado OK |
| Synapse/Chroma | sem produto ou serviço correspondente encontrado nas consultas; apenas pasta `RzS3WizardPkg` |
| DLLs SDK Chroma | ausentes nos caminhos padrão consultados em System32/SysWOW64 |
| Windows PowerShell x64 | 5.1.26100.9818, utilizado para filhos COM e execução principal validada |
| PowerShell x64 | 7.6.9, também utilizado no inventário |
| Python | 3.14.3; biblioteca padrão, sem pacotes adicionais |
| .NET | runtimes 6.0.16, 8.0.21, 10.0.13; nenhum .NET SDK instalado |

A ausência de entradas e arquivos padrão não exclui instalações portáteis ou residuais em outros locais. Não há versão de Synapse/Chroma utilizável confirmada.

O [guia Razer](https://developer.razer.com/works-with-chroma/setting-up/) orienta instalar Synapse e os componentes SDK. O [portal atual WYVRN](https://doc.wyvrn.com/) mantém documentação Chroma, inclusive REST. Isso não confirma o modelo original com a pilha atual.

No [RGB.NET, commit 3c5af72](https://github.com/DarthAffe/RGB.NET/blob/3c5af72b25c77971c21ccf96c13c41c62c8212f6/RGB.NET.Devices.Razer/RazerDeviceProvider.cs), `0F08` está associado a mousepad e o comentário continua declarando que o modelo original não foi testado pelo autor. O resultado indexado da [lista oficial de Synapse 4](https://mysupport.razer.com/app/answers/detail/a_id/6120) aponta o V2; a página completa redirecionou para uma verificação sem conteúdo legível. Não obtivemos confirmação do original. Não houve teste Chroma no hardware nem instalação de Synapse.

## Reuso, arquitetura e próximos critérios

O repositório contém um núcleo de resultados independente e adaptadores de descoberta por fabricante. Não há motor de efeitos, interface gráfica ou daemon. O dispositivo Razer é tratado separadamente de sua possível rota SDK. Registro antigo não é considerado presença física: o adaptador usa PnP `PresentOnly`.

[JackNet](https://github.com/Fanman03/JackNetRGBSync) permanece arquivado, licença GPL-2.0. Sua cópia local foi consultada, sem executar binários. RGB.NET possui LGPL-2.1 em seu arquivo LICENSE. Nenhum código desses projetos ou DLL proprietária foi incorporado. As interfaces das sondas foram escritas a partir da inspeção local. Não foi escolhida uma licença global para AuraLink em nome do proprietário.

[ChromaControl](https://github.com/ChromaControl/ChromaControl) e [AuraConnect](https://github.com/jjairocj/AuraConnect) continuam referências no sentido Synapse → ASUS. Não resolvem o objetivo prioritário. Uma ponte por leitura de RAM permanece hipótese separada; suporte a leitura em biblioteca não demonstra leitura de efeitos enquanto o Armoury controla o hardware.

Próximo passo prioritário: obter/validar o contrato HAL, especialmente a ABI de enumeração, XML de capacidades, IDs e semântica dos efeitos, critérios de confiança e atualização da descoberta no serviço. Usar documentação de parceiros ou análise estática adicional de componentes instalados, sem substituir DLLs oficiais. Em seguida, construir um HAL mínimo com CLSID próprio e destino de diagnóstico, registro e remoção restritos ao AuraLink. Critério de sucesso: aparecer na interface do Armoury e registrar efeitos recebidos ao mudar um efeito nele. Um teste apenas no SDK ou em simulação não atende esse critério.

Somente após essa etapa, validar a saída Chroma para `0F08` e a devolução de controle. Se a rota de parceiros exigir acesso indisponível ou assinatura restrita, documentar esse bloqueio antes de considerar (b) ou (c).

## Validação desta entrega

Três testes automatizados do leitor PE passaram: tabela de nomes de exports, arquivos truncados/inválidos e RVA fora do arquivo. Eles verificam o parser, não a integração RGB. A compilação `Add-Type` e as sondas COM foram executadas neste Windows. A descoberta PnP é validação real de presença; nenhum teste visual de iluminação foi realizado. Evidências brutas ficam em `artifacts/`; o resumo versionável está em `docs/evidence/2026-11-01.json`.
