# AuraLink

Investigação de uma integração **não oficial**, sem vínculo com ASUS, Razer ou Corsair, para apresentar dispositivos RGB externos ao Aura Sync do Armoury Crate.

**Estado: integração nativa ainda não comprovada.** A primeira entrega contém sondas executáveis de descoberta, interfaces COM e dependências. Nenhum dispositivo AuraLink foi registrado no Windows ou apareceu no Armoury Crate. Nenhum efeito RGB foi enviado.

Leia a [avaliação de viabilidade](docs/viabilidade.md) e as [evidências da primeira execução](docs/evidence/2026-11-01.json).

## Executar

Windows, PowerShell x64. O modo padrão consulta registro, arquivos, serviços e dispositivos presentes:

```powershell
powershell.exe -NoProfile -File .\Invoke-AuraLinkProbe.ps1
```

Modo experimental: ativa a classe COM Corsair instalada e tenta enumerar pelo SDK ASUS em processos separados, com limite de tempo por processo:

```powershell
powershell.exe -NoProfile -Mta -File .\Invoke-AuraLinkProbe.ps1 -Runtime -TimeoutSeconds 30
```

A ativação pode inicializar dependências do fabricante mesmo sem escrever cores. Nesta máquina houve erro de conexão Corsair e timeout ASUS. Não use esse modo como ferramenta de sincronização. Os resultados ficam em `artifacts/`, ignorado pelo Git. Timeout ou falha de sonda runtime produz código de saída 2. Um comando concluído não significa integração nativa validada; veja `Runtime`, `Evidence` e `NativeIntegrationProven` no JSON.

Os scripts terminam sozinhos; os filhos COM têm timeout e liberação das referências. Não há serviço AuraLink, instalação, inicialização automática ou alteração persistente do registro. Ctrl+C interrompe a execução; o bloco `finally` tenta encerrar o filho ativo. Encerrar abruptamente o host não garante executar esse bloco.

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

O único dispositivo Razer reconhecido pelo adaptador é o Base Station Chroma original `1532:0F08`. As várias interfaces PnP pertencem ao dispositivo composto; não representam vários suportes físicos. Adaptadores de saída RGB e um HAL AuraLink ficam pendentes da validação do contrato nativo.
