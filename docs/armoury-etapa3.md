# Etapa 3 — descoberta pelo LightingService e interface do Armoury

Investigação em 1 de novembro de 2026. O teste terminou com o **registro AuraLink removido**. O usuário confirmou que a memória Corsair e os coolers ARGB voltaram a acompanhar os efeitos. Os arquivos próprios protegidos e os logs foram preservados para análise.

Atualização posterior: o [ensaio de renovação dos serviços](aura-cache-ensaio.md) comprovou a apresentação do cartão e a recepção de efeitos reais no HAL. Este documento preserva os resultados anteriores, quando somente o LightingService havia sido recarregado.

## Resultado

| Critério | Evidência | Estado |
|---|---|---|
| Registro próprio em máquina, x86/x64 | Entradas próprias de COM e descoberta; ativação de teste | Comprovado, depois removido |
| SDK descobre o novo HAL | `EumerateHalInfo` listou o CLSID em processos novos de ambas as arquiteturas | Comprovado |
| LightingService carrega e enumera o HAL | Eventos do processo real do serviço: `Activated`, `Enumerate`, `GetCapability` | Comprovado |
| Armoury Crate exibe o dispositivo | Usuário reabriu o aplicativo; não encontrou item novo | Não comprovado |
| Mudança de efeito no Armoury chega ao HAL | Nenhum `SetEffect` proveniente do LightingService foi registrado | Não comprovado |
| Razer acompanha iluminação | Não existe adaptador de saída Razer nesta etapa | Não implementado |

Aceitação pelo SDK, carregamento pelo serviço e inclusão na interface são critérios diferentes. **Ainda não há sucesso de integração nativa com o Armoury Crate.** A aceitação do HAL próprio pelo serviço reduz a lacuna técnica, mas não resolve a apresentação nem a seleção na interface.

## Instalação controlada e correção de ativação

O serviço executa como LocalSystem. A operação elevou um helper próprio pelo UAC, copiou a assembly e clientes de teste para `C:\Program Files\AuraLink\DiagnosticHal\v0.1-<hash>` e restringiu os arquivos executáveis a escrita por Administradores/SYSTEM. Usuários comuns mantêm leitura/execução. Não foram substituídos arquivos de fabricantes.

O registro foi feito em HKLM, nas visões x86 e x64, com o CLSID `{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}` e a entrada de descoberta já identificada. A pasta dos arquivos não é o workspace gravável pelo usuário. A remoção usa o manifesto e as verificações de identidade do registro próprio.

O primeiro carregamento do serviço falhou. Foi possível reproduzir o erro em um host Python nativo, sem CLR previamente carregado: `CoCreateInstance` retornava `0x80070002` tanto para IUnknown quanto para o IID HAL. Os clientes .NET anteriores funcionavam, inclusive porque estavam em um contexto que já conseguia resolver a assembly.

A comparação com `RegAsm /regfile /codebase` identificou a diferença no CodeBase: nossa entrada continha espaços codificados como `%20`; a referência usava espaços literais no URI. No **mesmo caminho com espaços e com a mesma DLL**, o teste passou de `0x80070002` para `S_OK` após corrigir apenas essa representação. Foi adicionado [Test-NativeHostRegistration.ps1](../Test-NativeHostRegistration.ps1). Os testes por vtable em clientes .NET não substituem esse teste de host nativo.

Depois da correção, o processo LightingService 43108 ativou e enumerou a versão mousepad. O processo 26132 fez o mesmo com a versão headset. Os registros do nosso sink e do scanner ASUS concordam sobre o CLSID e o tipo. A mensagem de validação de assinatura encontrada na análise estática do serviço se refere ao caminho **do SDK oficial AuraSdk_x86.dll**; não foi usada como evidência de rejeição da nossa DLL por assinatura.

## Categorias, sincronização e interface

Foram ensaiados os tipos públicos Mousepad (`0x91000`) e Headset (`0x40000`). O segundo foi uma aproximação temporária ao suporte de fone, não uma escolha final de representação. O usuário considerou ambos inadequados. A categoria final permanece em aberto.

A primeira capacidade também declarava `synchronizable=0`; a segunda passou a declarar `1` e os testes verificam o valor interpretado pelo SDK. Mesmo assim a interface não mostrou o item novo e o serviço não enviou efeitos. Portanto, a hipótese de que essa flag, isoladamente, explicava a ausência na interface **não foi confirmada**. O contador `_unsyncDeviceCount` também não basta para concluir falta de suporte: o novo dispositivo estava fora do grupo selecionado no perfil.

O perfil interno registrou `MousePad` e depois `RogAudioDevices_Headset`. Isso não equivale a um cartão visível no Armoury. As capturas e a confirmação do usuário mostraram apenas os dispositivos reais já existentes. **Memória** era a integração Corsair; **Addressable LED Strip** eram os coolers no ARGB. Nenhum desses cartões representava o AuraLink.

O [SDK público ASUS](https://www.asus.com/microsite/aurareadydevportal/interface_aura_service_lib_1_1_i_aura_sdk.html) lista tipos existentes, incluindo placa-mãe, fita da placa-mãe, GPU, monitor, headset, microfone, discos externos, RAM, teclados, mouse, mousepad, chassi, projetor e water cooler. Não documenta categoria de hub USB, suporte de fone ou extensão da taxonomia da interface. Declarar um novo número no XML não comprova que a interface crie uma categoria correspondente. Nenhuma categoria personalizada foi criada nesta etapa.

## Regressões e recuperação

Durante a reenumeração, a RAM deixou de acompanhar os efeitos. Foram encontrados três servidores COM auxiliares na sessão do usuário, criados exatamente às 13:23:34 local durante a sonda global da primeira entrega: `AacKingstonDramHal_x64.exe`, `Aac3572DramHal_x64.exe` e uma instância adicional de `Aac3572MbHal_x86.exe`. O término do cliente daquela sonda não havia encerrado esses processos separados.

Após conferir PID, nome, sessão e horário, encerramos somente essas instâncias. As instâncias de placa-mãe iniciadas no boot foram preservadas. A chamada confirmada `Enumerate` v1 Corsair voltou a retornar quatro módulos; o serviço Corsair também voltou a detectar os quatro módulos e o usuário confirmou a recuperação visual. A evidência é compatível com interferência dos auxiliares na descoberta; não foi identificado qual dos três, isoladamente, era responsável.

A varredura global foi retirada do modo runtime do inventário, e o script legado exige opt-in explícito. Os testes direcionados ao nosso CLSID continuam disponíveis. Não é adequado repetir a varredura global como verificação rotineira.

Depois, o usuário relatou que a fita dos coolers não acompanhava. Retiramos o registro AuraLink e reiniciamos o LightingService. O inventário voltou a conter somente `Mainboard_Master`, `Vga`, `GSkillDram` e `AddressableStrip`, todos no grupo. O usuário confirmou que os coolers voltaram a acompanhar. A causa específica dessa segunda regressão não foi estabelecida; não se atribui automaticamente ao formato de cor ou ao dispositivo Razer, que não recebeu comandos.

Foram feitas **quatro reinicializações pontuais do LightingService** ao longo do diagnóstico: carregamento inicial, correção do CodeBase, atualização de capacidades e remoção final. O Windows não foi reiniciado. Não houve alteração de firmware, tipo de inicialização de serviços ou instalação de inicialização automática. Os perfis disponíveis foram copiados antes dos testes; não sobrescrevemos arquivos de configuração ASUS para restaurá-los. As mudanças de efeito foram feitas pelo usuário na interface.

## Ferramentas e encerramento

- [Start-ArmouryDiagnostic.ps1](../Start-ArmouryDiagnostic.ps1): operações elevadas sobre arquivos e registro próprios. Não é necessário para os testes de usuário.
- `Install`: registra a descoberta, sem reiniciar o serviço automaticamente.
- `UpdateAndRestart`: instala uma nova versão própria e recarrega o serviço.
- `RemoveAndRestart`: remove o registro próprio e recarrega o serviço; foi a operação final aplicada.
- `Inspect` e `RepairRegistration`: ferramentas usadas nesta investigação.
- [Capture-Desktop.ps1](../tools/Capture-Desktop.ps1): captura local, somente leitura, usada a pedido do usuário após falha do conector de captura. Imagens estão em `artifacts/`, ignoradas pelo Git.

Os binários/logs em Program Files permanecem, mas não há registro COM ou de descoberta que os publique no Aura. A remoção não apaga binários possivelmente carregados por um processo. Não executar uma nova instalação de máquina apenas para testar categorias enquanto a regra de apresentação/seleção da interface continuar desconhecida.

Uma sonda de leitura do `IServiceMediator` foi encerrada por timeout sem obter resposta; não foi usada para enviar perfis. As rotas de atualização manual e os dados que o plugin de interface usa para montar os cartões ainda precisam de investigação. O conector de automação visual falhou ao iniciar seu sandbox; a validação visual ficou com o usuário e com as capturas locais solicitadas.

## Próximo passo técnico

Investigar em leitura a ponte `ArmouryCrate.AuraPlugin` e sua construção da lista de dispositivos, comparando os dados esperados com as capacidades e identidades do HAL. Precisamos descobrir o que falta para um dispositivo independente ser apresentado e selecionado, antes de novos ensaios de registro ou de integrar o Razer. Se isso exigir contrato ou metadados de parceiros indisponíveis, registrar o bloqueio. Não substituir silenciosamente esse objetivo por controle externo ou espelhamento de cores.

A inspeção inicial dessa ponte encontrou mensagens de cache da lista e separação entre a sessão do usuário e a sessão 0. O módulo instalado tem versão de arquivo 6.7.3.0 e assinatura Authenticode válida da ASUSTeK. Isso é uma pista para investigar a atualização da interface, não prova de que apenas limpar cache ou reiniciar outro serviço resolveria a inclusão. Nenhum arquivo desse módulo foi alterado.

Evidências resumidas: [armoury-etapa3-2026-11-01.json](evidence/armoury-etapa3-2026-11-01.json). A validação física registrada é a confirmação do usuário da recuperação da RAM e dos coolers; não há validação física de saída AuraLink/Razer.

Investigação posterior: [comparação Corsair e cache da interface](aura-ui-investigacao.md). Os logs não registram uma nova consulta completa da lista após o carregamento do HAL. Também foi encontrada a categoria interna HeadphoneStand, cujo mapeamento HAL continua desconhecido. Esses achados ainda não foram validados com um novo ensaio de serviços.
