# AuraLink

<img src="assets/AuraLink.svg" alt="AuraLink" width="72" />

Integração **não oficial**, sem vínculo com ASUS, Razer ou Corsair, para apresentar dispositivos RGB externos ao Aura Sync do Armoury Crate.

## 1.0.0-beta.1 para Windows x64

[Baixar o beta](https://github.com/Erhoz/auralink/releases/tag/v1.0.0-beta.1) · [Notas da versão](docs/releases/1.0.0-beta.1.md) · [Changelog](CHANGELOG.md)

O primeiro dispositivo é o **Razer Base Station Chroma original `1532:0F08`**. Requer conta Windows administradora, Armoury Crate funcionando, .NET Framework 4.8 e driver Razer já instalado. O pacote não contém componentes dos fabricantes.

Feche o Razer Chroma e instale: o AuraLink já inicia na bandeja e volta a iniciar automaticamente no login, sem novo UAC. Na primeira vez, selecione o suporte no Aura Sync. Clique duas vezes no ícone para abrir a janela; fechar a janela só oculta, **Parar** pausa a sincronização e **Sair** encerra e recupera um arco-íris conhecido. A desinstalação remove a tarefa de login e o provedor próprio.

Esta é a republicação de `1.0.0-beta.1` com autostart e ícone, solicitada pelo usuário. A publicação manual anterior foi retirada; baixe novamente o instalador. O histórico de commits foi preservado.

O instalador não tem assinatura digital; compare seu SHA-256 com a release. O backend foi validado no hardware antes do empacotamento. A revisão de autostart, bandeja e provedor persistente passou por verificação de pacote/definição do Agendador e simulações; o novo instalador e um login real não foram executados, nem outra rodada de hardware. Veja [construção e publicação](docs/publicacao.md).

## Evidências e investigação

**Prova de conceito nativa validada no hardware:** o suporte aparece no Aura Sync como HeadphoneStand e a [ligação Armoury → HAL → USB](docs/aura-usb-primeiro-ciclo.md) já reproduziu vermelho, verde e azul corretos no Razer, confirmados pelo usuário após corrigir a ordem dos canais. A animação também acompanhou em ensaio anterior, com os demais dispositivos funcionando. O teste é temporário e limitado; seu registro foi removido ao terminar. Ainda não é uma integração para uso contínuo.

A etapa atual substitui o JSONL por um [canal local de frames](docs/canal-frames.md), com coalescência, identidade do processo servidor e timeout. **Cores, animação e recuperação foram confirmadas visualmente pelo novo canal.** A [rodada final](docs/ensaios-finais.md) também passou: dez minutos com 7.668 frames e uma desconexão/reconexão USB com retomada, mantendo os demais dispositivos funcionando. A fase de testes físicos solicitada foi encerrada. O runner de investigação continua limitado a 900 segundos; o aplicativo beta usa sessão contínua manual com parada e recuperação. O driver Razer permanece instalado.

Leia o [resultado da etapa 3](docs/armoury-etapa3.md), incluindo correção de ativação nativa, regressões e recuperação. A criação de uma categoria própria na interface também permanece não comprovada.

A [investigação da lista da interface](docs/aura-ui-investigacao.md) encontrou uma hipótese concreta de cache no ArmouryCrateService. O [ensaio de renovação da lista](docs/aura-cache-ensaio.md) apresentou o cartão e entregou os efeitos ao HAL. Depois, a [categoria HeadphoneStand foi validada](docs/headphonestand-razer.md): tipo HAL `0x41000`, cartão confirmado pelo usuário e 121 chamadas de efeito recebidas. O código atual anuncia suporte de fone.

Para a implementação atual, consulte o [contrato HAL e os testes](docs/hal-contrato.md). Execute `powershell.exe -NoProfile -File .\Test-DiagnosticHal.ps1 -IncludeAsusSdk` para compilar, testar e remover automaticamente o registro de usuário. Esse teste direcionado é separado da comprovação de recepção de efeitos do Armoury no ensaio de máquina.

Leia a [avaliação de viabilidade](docs/viabilidade.md) e as [evidências da primeira execução](docs/evidence/2026-11-01.json).

## Ferramentas de investigação

Windows, PowerShell x64. O modo padrão consulta registro, arquivos, serviços e dispositivos presentes:

```powershell
powershell.exe -NoProfile -File .\Invoke-AuraLinkProbe.ps1
```

Modo experimental: ativa a classe COM Corsair instalada em processo separado, com limite de tempo. A varredura global de dispositivos do SDK foi retirada desse runner por ter deixado auxiliares de fabricantes vivos e interferido na descoberta de memória:

```powershell
powershell.exe -NoProfile -Mta -File .\Invoke-AuraLinkProbe.ps1 -Runtime -TimeoutSeconds 30
```

A ativação pode inicializar o serviço/driver Corsair e assumir prioridade no backend mesmo sem chamar `SwitchMode`. Isso foi observado nos logs; o estado de controle é desconhecido nesse modo. Houve erro de conexão Corsair e timeout ASUS. Prefira os testes direcionados de `Test-DiagnosticHal.ps1`. Não use a sonda global como ferramenta de sincronização. Os resultados ficam em `artifacts/`, ignorado pelo Git. Timeout ou falha de sonda runtime produz código de saída 2. Um comando concluído não significa integração nativa validada; veja `Runtime`, `Evidence` e `NativeIntegrationProven` no JSON.

As sondas de inventário terminam sozinhas; os filhos COM têm timeout e liberação das referências. Elas não instalam serviço AuraLink, inicialização automática ou registro de provedor. O novo teste de HAL cria e remove registro próprio em HKCU; também há scripts para instalação/remoção explícitas. Ctrl+C tenta executar a limpeza; encerrar abruptamente o host não garante executar `finally`.

Inspeção estática de interfaces e exports, sem ativar classes COM:

```powershell
.\tools\Inspect-TypeLibrary.ps1 -Path 'C:\Program Files\ASUS\ASUS_Aac_DRAM\Aac3572DramHal.tlb'
python .\tools\inspect_pe.py 'C:\Program Files (x86)\Corsair\Corsair Plugin for Asus Aura Sync\amd64\corsairasusplugin64.dll'
python -m unittest discover -s tests -v
```

Python é necessário apenas para o leitor PE e seus testes. A sonda COM usa `Add-Type` do Windows PowerShell 5.1 e não exige instalar .NET SDK. Execute as sondas de runtime pelo comando principal, que aplica o timeout; os scripts internos não o aplicam isoladamente.

## Estrutura

- `src/Core.psm1`: formato de evidência, sem dependência de fabricante.
- `src/Adapters/`: descoberta ASUS, Corsair e Razer isoladas.
- `tools/`: sondas COM e leitores de metadados.
- `tests/`: testes do leitor PE, sem hardware.

O único dispositivo Razer reconhecido pelo adaptador é o Base Station Chroma original `1532:0F08`. As várias interfaces PnP pertencem ao dispositivo composto; não representam vários suportes físicos. O HAL em `src/DiagnosticHal/` anuncia um LED lógico, visto como suporte na interface. No ensaio nativo, seu processo de saída recebe os frames registrados pelo HAL e replica uma cor nos 15 LEDs físicos. O [teste isolado Chroma](tools/test_razer_chroma.py) também produziu vermelho físico, mas a [avaliação de dependências](docs/razer-saida-e-dependencias.md) encontrou dependência do AppEngine nessa rota. A saída USB usada no teste nativo funciona com AppEngine ausente e serviços SDK temporariamente parados; o driver Razer permanece instalado.

A [investigação USB/AppEngine](docs/usb-appengine-investigacao.md) consultou modo e firmware diretamente por HID. O [ensaio RGB volátil](docs/usb-rgb-volatil.md) depois produziu vermelho físico sem AppEngine e com os serviços SDK parados; a recuperação automática do arco-íris também foi confirmada pelo usuário no ciclo corrigido. O primeiro retorno somente ao modo normal deixou a base vermelha, e o encerramento foi corrigido para enviar o efeito conhecido. O filtro de driver Razer permanece instalado, portanto não há prova de independência completa de seus drivers. Execute `powershell.exe -NoProfile -File .\Read-RazerHid.ps1` para somente metadados ou acrescente `-QueryState` para consultas isoladas com restauração dos serviços.

Teste de regressão do registro COM em caminho com espaços, usando host Python sem CLR previamente carregado:

```powershell
powershell.exe -NoProfile -File .\Test-NativeHostRegistration.ps1
```
