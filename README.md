# AuraLink

Investigação de uma integração **não oficial**, sem vínculo com ASUS, Razer ou Corsair, para apresentar dispositivos RGB externos ao Aura Sync do Armoury Crate.

**Prova de conceito nativa validada com um dispositivo virtual:** o usuário confirmou o novo cartão no Armoury Crate e o HAL recebeu 202 chamadas reais de `SetEffect` do LightingService após a seleção e troca de efeitos. O dispositivo físico Razer ainda não recebe comandos AuraLink. O registro temporário foi removido após o ensaio.

Leia o [resultado da etapa 3](docs/armoury-etapa3.md), incluindo correção de ativação nativa, regressões e recuperação. A criação de uma categoria própria na interface também permanece não comprovada.

A [investigação da lista da interface](docs/aura-ui-investigacao.md) encontrou uma hipótese concreta de cache no ArmouryCrateService. O [ensaio de renovação da lista](docs/aura-cache-ensaio.md) apresentou o cartão e entregou os efeitos ao HAL. Depois, a [categoria HeadphoneStand foi validada](docs/headphonestand-razer.md): tipo HAL `0x41000`, cartão confirmado pelo usuário e 121 chamadas de efeito recebidas. O código atual anuncia suporte de fone.

Para a implementação atual, consulte o [contrato HAL e os testes](docs/hal-contrato.md). Execute `powershell.exe -NoProfile -File .\Test-DiagnosticHal.ps1 -IncludeAsusSdk` para compilar, testar e remover automaticamente o registro de usuário. Esse teste direcionado é separado da comprovação de recepção de efeitos do Armoury no ensaio de máquina.

Leia a [avaliação de viabilidade](docs/viabilidade.md) e as [evidências da primeira execução](docs/evidence/2026-11-01.json).

## Executar

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

O único dispositivo Razer reconhecido pelo adaptador é o Base Station Chroma original `1532:0F08`. As várias interfaces PnP pertencem ao dispositivo composto; não representam vários suportes físicos. O HAL em `src/DiagnosticHal/` anuncia apenas um dispositivo virtual de diagnóstico, já observado como suporte na interface e recebendo efeitos. O [teste isolado Chroma](tools/test_razer_chroma.py) já produziu vermelho físico confirmado pelo usuário, com liberação de sessão. A [avaliação de dependências](docs/razer-saida-e-dependencias.md) mostra que a rota testada funciona com AppEngine em execução e falhou somente com os serviços SDK. O teste Razer ainda não está ligado ao HAL; a sincronização física completa permanece pendente.

A [investigação USB/AppEngine](docs/usb-appengine-investigacao.md) já consultou modo e firmware diretamente por HID, sem AppEngine e com serviços SDK parados durante as consultas. Brilho permaneceu desconhecido; RGB direto ainda não foi testado. O filtro de driver Razer permanece instalado, portanto não há prova de independência completa de seus drivers. Execute `powershell.exe -NoProfile -File .\Read-RazerHid.ps1` para somente metadados ou acrescente `-QueryState` para consultas isoladas com restauração dos serviços.

Teste de regressão do registro COM em caminho com espaços, usando host Python sem CLR previamente carregado:

```powershell
powershell.exe -NoProfile -File .\Test-NativeHostRegistration.ps1
```
