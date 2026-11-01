# Ensaio de renovação da lista do Armoury

Em 1 de novembro de 2026, foi iniciado um ensaio para isolar a hipótese de cache identificada na [análise da interface](aura-ui-investigacao.md). O binário protegido é exatamente o do ensaio Headset anterior, SHA-256 `DECDD07B43EAF69785A0199BE9712D76E44A7C3215F0E5CD224C72B356ACE41B`. Não se alterou categoria, capacidades ou saída de hardware.

Atualização posterior: o [ensaio HeadphoneStand](headphonestand-razer.md) validou o tipo HAL `0x41000` e o cartão de suporte de fone. Este documento preserva o ensaio anterior que isolou a renovação da lista.

## Procedimento executado

Os serviços ArmouryCrateService e LightingService estavam em execução e não tinham dependentes em execução. O helper guardou o perfil disponível e os estados/PIDs/tipos de inicialização antes de agir. Parou normalmente o ArmouryCrateService, depois o LightingService, registrou somente o HAL próprio e iniciou LightingService seguido de ArmouryCrateService. Não usou parada forçada, não alterou tipos de inicialização nem arquivos ASUS.

O novo [Start-ArmouryCacheTest.ps1](../Start-ArmouryCacheTest.ps1) solicita a elevação normal do Windows para seu helper. `Begin` recusa sessão/registro existentes e exige o hash do binário anterior; `End` exige a sessão correspondente e remove o registro próprio, renovando os dois serviços na mesma ordem. Há recuperação em caso de erro, com os resultados registrados; se uma recuperação falhar, o relatório preserva o erro em vez de declarar sucesso.

```powershell
powershell.exe -NoProfile -File .\Start-ArmouryCacheTest.ps1 -Action Begin
# Conferir Aura Sync e, se houver cartão novo, selecionar e mudar o efeito.
powershell.exe -NoProfile -File .\Start-ArmouryCacheTest.ps1 -Action End
```

Não há watchdog ou remoção automática por tempo: o operador deve executar `End` ao terminar a conferência. Os arquivos e logs próprios protegidos são mantidos. Este roteiro é um experimento desta máquina, não um instalador de uso geral. Não executar novamente durante uma sessão ativa.

## Evidência inicial

Às 15:25:52 locais, o ArmouryCrateService iniciou no PID 33732 e o LightingService no PID 35484. A nova sequência completa de consultas foi registrada:

| Consulta | Início local | Resultado |
|---|---|---|
| QueryAllDeviceStatus | 15:25:52.860 | S_OK, terminou 15:25:55.906 |
| QueryAllDeviceCap | 15:25:55.910 | S_OK, terminou 15:25:55.922 |
| QueryAllDevice | 15:25:55.928 | S_OK, terminou 15:25:55.930 |

O helper `GetDevList` confirmou sucesso. O sink próprio registrou ativação, duas chamadas Enumerate e GetCapability do processo real LightingService. O perfil durante o ensaio continha placa-mãe, GPU, memória, fita ARGB e `RogAudioDevices_Headset` (índice 9). Não interpretar o índice 9 como código de tipo HAL ou categoria.

O serviço Corsair ficou parado no snapshot imediatamente após a partida dos outros serviços, mas voltou a executar no PID 14936 na verificação seguinte, sem uma ordem explícita de start/restart Corsair pelo AuraLink. Isso evidencia a necessidade de aguardar a inicialização, não prova física de sincronização da RAM.

Na coleta inicial ainda não havia SetEffect. Depois, o usuário reabriu a interface, confirmou o cartão novo, selecionou-o e trocou efeitos. A seleção também ficou registrada como `ingroupdevice` para `RogAudioDevices_Headset`, índice 9.

## Resultado confirmado

O processo real LightingService 35484 enviou **202 chamadas SetEffect**, entre 18:29:29.6338677 e 18:30:29.3300921 UTC. Foram observados os IDs de efeito 255, 0 e 1 e 124 valores de cor distintos. A primeira captura parcial continha 64 chamadas e 61 cores; o log continuou recebendo comandos até a coleta final. Todos os eventos têm `hardwareOutput=false`: o sink apenas registra os valores brutos, sem Razer, USB ou outro backend físico. Não inferimos ordem de bytes ou canais a partir desses inteiros nesta etapa.

O usuário relatou que o cartão apareceu e que os demais dispositivos acompanhavam os efeitos, mas o suporte não. Esse último resultado é esperado: **não existe adaptador de saída Razer no diagnóstico atual**. A prova obtida é de inclusão de um provedor virtual e recepção dos comandos configurados no Armoury, não de iluminação do Base Station físico.

Isso supera o bloqueio principal da etapa 3. O êxito com a mesma DLL após renovar os serviços é consistente com a hipótese de lista antiga no ArmouryCrateService. Não foi isolado se reiniciar somente esse serviço bastaria: o ensaio renovou também o LightingService. Não se estabelece uma regra universal para outras versões do Armoury nem para todas as categorias.

## Encerramento e estado final

`End` concluiu normalmente às 18:30:38 UTC. Parou ArmouryCrateService e LightingService, removeu somente o registro próprio e iniciou os dois novamente. Na conferência seguinte:

- ArmouryCrateService: Running, PID 16476, inicialização Auto.
- LightingService: Running, PID 8720, inicialização Auto.
- CorsairDeviceControlService: Running, PID 47688, inicialização Manual; retornou pela inicialização normal do backend, sem comando explícito de reinício Corsair.
- Perfil: somente `Mainboard_Master`, `Vga`, `GSkillDram` e `AddressableStrip`, todos no grupo.
- Registro AuraLink: removido; binários e logs próprios preservados.

A confirmação física do usuário ocorreu durante o ensaio; não se apresenta o perfil final como nova confirmação visual após a remoção.

Os dois scripts PowerShell passaram pela análise sintática. As ações Begin e End foram executadas de verdade nesta máquina, com relatórios de sucesso; os caminhos de recuperação de falhas não foram provocados. Artefatos locais em `artifacts/armoury-cache-test/`: relatórios Begin/End, perfis antes/durante/depois, sink final e trechos de consultas e efeitos da interface. Evidência selecionada: [aura-cache-ensaio-2026-11-01.json](evidence/aura-cache-ensaio-2026-11-01.json).

O próximo passo técnico é o adaptador físico Razer: confirmar as dependências atuais, validar uma saída limitada no modelo 1532:0F08 e implementar aquisição/liberação de controle antes de conectar a sequência do Aura ao dispositivo. O tipo interno HeadphoneStand continua uma investigação separada; não é necessário inventar uma categoria nova para provar a recepção nativa já observada.
