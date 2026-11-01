# Saída física Razer e dependências observadas

Em 1 de novembro de 2026, o Base Station Chroma original `1532:0F08` recebeu uma cor estática real do protótipo AuraLink. O usuário confirmou vermelho enquanto o teste estava ativo e que o suporte voltou a apagar após a liberação. **Isso valida a saída Chroma isolada; ainda não conecta os efeitos Aura ao dispositivo físico.** A categoria [HeadphoneStand já foi validada separadamente](headphonestand-razer.md).

Atualização posterior: a [investigação USB/AppEngine](usb-appengine-investigacao.md) obteve leituras diretas de modo e firmware com os serviços SDK parados. O filtro de driver Razer ainda estava presente e o controle RGB direto permanece pendente.

O usuário perguntou se poderíamos extrair somente os componentes necessários e depois desinstalar o aplicativo Chroma. O teste de dependência mostrou que, **nesta instalação e na rota REST usada**, os serviços SDK sem AppEngine não conseguiram controlar os LEDs. Não desinstalamos produtos nem removemos manualmente arquivos Razer. A ausência de AppEngine foi medida; uma janela fechada com o processo em segundo plano não foi confundida com independência do aplicativo.

## Versões e instalação

O usuário escolheu o pacote oficial e conduziu a instalação visual/licença/UAC. O conector de controle do computador falhou ao iniciar o sandbox; o agente abriu apenas o instalador oficial já verificado e acompanhou os componentes por leitura.

| Componente | Valor observado |
|---|---|
| Bootstrapper oficial | 2.6.0.890, Authenticode válido Razer USA Ltd. |
| Produto registrado Razer Chroma / AppEngine | 4.0.896 |
| API REST core/device/version | 4.01.00 |
| Registro SDK ProductVersion | 4.1.0.10151444 |
| DLLs de compatibilidade System32/SysWOW64 | 3.8.0.238 |
| Chroma SDK Proxy carregado pelo processo principal AppEngine | RzChromaSDKProxy_v1.2.4.10151518.dll |
| Produto adicional encontrado após a instalação | Razer Axon 2.11.1.0 |

As versões têm camadas diferentes; não são intercambiáveis. O agente não selecionou extras na interface. Axon foi identificado, não utilizado nos testes nem desinstalado. Os serviços persistentes instalados pelo pacote Razer não foram desativados permanentemente; o usuário havia escolhido a instalação oficial com essa consequência informada. Não configuramos inicialização automática AuraLink.

## Particularidades confirmadas

### Hostname do serviço REST

GET em `http://127.0.0.1:54235/razer/chromasdk` devolveu HTTP 400; a mesma consulta em `http://localhost:54235/razer/chromasdk` devolveu HTTP 200 com versão 4.01.00. O cliente passou a usar exatamente `localhost`, como na [documentação Razer](https://assets.razerzone.com/dev_portal/REST/html/index.html). Não houve desativação de TLS ou mudança de firewall.

### Concessão assíncrona de acesso

Nas primeiras sessões, POST e o primeiro PUT eram aceitos, mas o log do wrapper REST mostrava inicialmente `SynapseOnline=0`, `Access=0`. Cerca de um segundo depois, com AppEngine ativo, o SDK registrava `SynapseOnline=1` e `Access state = 1`.

O cliente original enviava a cor uma única vez logo após inicializar. O teste mantido com heartbeat, mas sem reaplicar a cor, continuou apagado segundo o usuário. Ao reaplicar o mesmo vermelho uma vez por segundo, o usuário confirmou a iluminação. Esse contraste é consistente com o primeiro comando anteceder a concessão de acesso. Ainda não implementamos assinatura nativa dos eventos de acesso; a reaplicação é uma estratégia explícita e limitada do protótipo, não prova de que toda implementação deva usar um atraso fixo.

`SynapseOnline` é o nome do estado interno do SDK, não prova de instalação do produto Synapse separado. Nesta máquina instalou-se Chroma/AppEngine.

### Mapeamento do dispositivo

`Devices.xml` instalado contém `accessory pid="3848"`, módulo `599D24BA-A24B-4AAB-98F1-41264CCCDF27.dll`, `mapping="mousemat"`. 3848 decimal é `0F08`. O arquivo de metadados dinâmicos identificou `RAZER BASE STATION CHROMA`, VID 5426 (`1532`), PID 3848 e 15 posições físicas de LED. Nenhum desses arquivos foi editado.

O endpoint REST `/mousepad` passou a ter confirmação física de cor estática no modelo exato. Continua sendo um endpoint de categoria compartilhada; o protótipo recusa aplicar quando encontra outro dispositivo USB pai Razer presente. Não há seleção USB individual pelo REST implementada nem teste de LEDs individuais. O código de cores Chroma `0x00BBGGRR` é separado do formato bruto Aura, cuja conversão ainda precisa ser validada.

## Ensaios físicos e liberação

| Ensaio | Resultado da API | Observação física |
|---|---|---|
| Vermelho inicial, 3 segundos | PUT e DELETE aceitos | Usuário não observou; não comprova resposta |
| Repetição, 8 segundos | PUT e DELETE aceitos | Usuário não observou; depois relatou base apagada |
| Sessão mantida, uma única aplicação de vermelho | Heartbeat e DELETE aceitos | Usuário confirmou que continuava apagada durante o teste |
| Reaplicação estática a cada segundo, AppEngine ativo | 71 escritas, 70 heartbeats, DELETE aceito | Usuário confirmou vermelho; depois confirmou base apagada antes de fechar o app |
| Janela fechada, aplicativo ainda em segundo plano | 49 escritas, 48 heartbeats, DELETE aceito | Usuário confirmou vermelho e corrigiu que Chroma continuava em segundo plano |
| AppEngine totalmente encerrado, serviços SDK mantidos | 34 escritas, 33 heartbeats, DELETE aceito | Usuário confirmou que continuou na iluminação padrão |

As contagens são de comandos aceitos, não de mudanças físicas medidas. Todos os testes foram encerrados por duração ou arquivo de parada, com liberação confirmada. O protótipo preserva `hardwareValidated=false` em seus relatórios automáticos; a confirmação física vem das respostas do usuário, documentadas aqui e na evidência resumida. Não registrar o ensaio SDK-only como sucesso só porque seu HTTP retornou zero.

## Separação da interface e do backend

Antes do último teste, CIM retornou **zero processos RazerAppEngine.exe** e mostrou os serviços Chroma SDK Service e Server ainda Running. No fim também não havia AppEngine. O usuário relatou que fechar os aplicativos fez a base voltar à iluminação padrão.

Na sessão SDK-only, o log acompanhou o processo de teste 27860, com `ActivePID=27860`, `SynapseOnline=0`, `Access=0` e notificação `MonitorSynapseEventNotification(FALSE)`. Nenhum acesso foi concedido durante o intervalo observado; o usuário confirmou a ausência de vermelho. O encerramento registrou DELETE, `UnInit return 0` e término do wrapper.

Isso impede prometer que basta copiar DLLs ou manter somente os dois serviços SDK desta instalação. O processo principal AppEngine havia carregado o módulo Proxy Chroma, mas não foi provado que essa DLL isolada seja carregável/utilizável fora do host oficial. Não tentamos simular a presença do aplicativo, alterar sinalizadores internos ou modificar DLLs.

Foi encontrado um instalador separado **Chroma Connect SDK Core 4.0.1**, assinado, em cache. Ele não deve ser confundido com o **Chroma SDK Core 4.1.0.10151444** usado nos testes. O log da instalação deste último registra remoção de seu instalador temporário após concluir. Não foi preservado um pacote independente completo de reinstalação Core nesta rodada. O manifesto de desinstalação da interface sozinho também não comprova todos os efeitos dos helpers chamados pelo desinstalador; não o executamos.

## Código, execução e verificações

O experimento é [tools/test_razer_chroma.py](../tools/test_razer_chroma.py), somente biblioteca padrão Python, separado do HAL. O modo padrão faz inventário e GET de versão, sem sessão:

```powershell
python tools/test_razer_chroma.py
```

Teste estático com tempo para observar, heartbeat e reaplicação a cada segundo:

```powershell
python -u tools/test_razer_chroma.py --apply --color FF0000 --seconds 180 --before-note "Estado observado antes do teste" --stop-file artifacts/razer-stage/stop-example --output artifacts/razer-stage/test-example.json
# Para encerrar antes do limite, em outra sessão, criar o arquivo indicado:
New-Item -ItemType File -Path artifacts/razer-stage/stop-example
```

O arquivo de parada não deve existir no início. O script não o apaga automaticamente. Ctrl+C normal também passa por `finally`; encerrar o host à força não garante limpeza. A duração máxima é 300 segundos e a expiração do protocolo sem atividade permanece a recuperação subsidiária, sem promessa de restauração exata do perfil anterior. Não há integração ASUS, instalação de serviço AuraLink, processo permanente ou captura de teclas.

Os testes automatizados com servidor HTTP falso verificam protocolo e liberação. Os testes do SDK real, o resultado físico e a dependência do AppEngine foram verificados separadamente. Logs, instaladores e perfis ficam em `artifacts/`, ignorado pelo Git; não incorporamos binários dos fabricantes para redistribuição. Evidência resumida: [razer-saida-2026-11-01.json](evidence/razer-saida-2026-11-01.json).

## Estado final e próximo passo

O registro do HAL diagnóstico continua removido; não houve novos reinícios ASUS durante a instalação e os testes Razer. Todas as sessões de cor foram liberadas. O usuário encerrou o AppEngine; os componentes Razer instalados permanecem no disco e seus serviços continuam configurados pelo instalador. Não houve desinstalação, reboot, firmware ou desativação permanente de serviços.

Para cumprir a preferência de eliminar o aplicativo Razer, a próxima investigação é um adaptador **USB/HID direto para 1532:0F08**, começando por protocolo, interface e leitura de estado disponível. Ele substituiria somente a saída Razer; o Armoury continuaria como controlador nativo pelo HAL HeadphoneStand. Precisamos comprovar aquisição/liberação e restauração no hardware antes de desinstalar a rota funcional atual. Outra possibilidade é um modo independente oficialmente suportado do runtime, caso seja identificado; ele não foi comprovado nesta rodada.
