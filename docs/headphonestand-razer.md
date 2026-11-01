# HeadphoneStand e primeiro teste de saída Razer

Investigação de 1 de novembro de 2026, posterior à [prova nativa com Headset](aura-cache-ensaio.md). Integração não oficial. Há dois resultados independentes: a categoria nativa foi validada; a saída física depende da instalação e validação do runtime Razer.

Atualização: a [validação física e de dependências](razer-saida-e-dependencias.md) confirmou vermelho no Base Station via SDK 4.01.00, com AppEngine ativo, e liberação da sessão. A rota testada sem AppEngine não controlou os LEDs. Os resultados abaixo registram também a preparação anterior a esses testes.

## Categoria de suporte de fone: comprovada

A leitura estática do LightingService 3.11.08.0 x86 revelou o tipo HAL correspondente. No mapa de tipos, a referência ao nome UTF-16 `RogAudioDevices_HeadphoneStand` em RVA `0x33c7` é seguida da atribuição da chave `0x41000` em `0x33d2`. O mesmo padrão associa Headset a `0x40000`, servindo de comparação. A string HeadphoneStand está no RVA `0x3b87e8`. SHA-256 do executável inspecionado: `638D037DB804D3A60E2577E5CB9567429AACD0F167204604EB8B8988BF526139`. Nenhum arquivo ASUS foi alterado ou executado como ferramenta de análise.

Os três identificadores não devem ser confundidos:

| Camada | Identificador HeadphoneStand observado |
|---|---|
| `root/type` no XML HAL | `0x41000` = 266240 |
| Mapa interno da interface | `0x02000000` |
| Índice do dispositivo no perfil deste ensaio | 23 |

O XML próprio e as expectativas dos clientes passaram para `0x41000`; a descoberta passou a informar `HeadphoneStand`. As capacidades de LED e efeitos permaneceram iguais. A análise por candidatos de bytes x86, sozinha, não prova uma associação; a inspeção das instruções foi seguida de validação do parser e do serviço reais.

### Validação em três níveis

1. Os testes COM e metadados ASUS direcionados ao nosso CLSID passaram em x86 e x64, com remoção do registro temporário de usuário. Nenhum provedor de hardware foi enumerado globalmente.
2. Foi instalada a DLL própria protegida, hash `0BECFD6F1E28DED4BE79E0C83BBF76A389CE62149D0257A428B1A65BF0731F33`. A lista foi renovada como no ensaio anterior. O LightingService 30704 ativou e enumerou o HAL; o perfil listou `RogAudioDevices_HeadphoneStand`, índice 23. As três consultas da interface terminaram com S_OK.
3. O usuário confirmou que o cartão apareceu como suporte, selecionou-o e mudou efeitos. O sink registrou **121 SetEffect**, IDs 255/0/1, com 70 valores de cor distintos, entre 18:45:33.9446817 e 18:45:50.8928616 UTC. Não há saída física nesse HAL.

Portanto, podemos usar **uma categoria existente de suporte de fone**, sem criar categoria nova ou modificar a interface ASUS. Não foi validado nome/ícone personalizado por fabricante. A classificação foi observada nesta versão do Armoury; não é promessa de compatibilidade com outras instalações.

### Encerramento

O ensaio foi removido com `Start-ArmouryCacheTest.ps1 -Action End -Variant HeadphoneStand`. Os serviços LightingService e ArmouryCrateService foram renovados para retirar o HAL também da lista em cache. O perfil voltou a placa-mãe, GPU, memória e fita ARGB. Os três serviços ASUS/Corsair foram observados em execução após a recuperação. Binários e logs próprios foram preservados.

Evidência selecionada: [headphonestand-2026-11-01.json](evidence/headphonestand-2026-11-01.json).

`-Variant HeadphoneStand` utiliza o relatório de instalação protegido salvo em `artifacts/headphonestand-test/installation.json`, validando caminho, hash e propriedade do registro. Não usar esse ensaio sem preparar a instalação correspondente. O modo Headset permanece preso ao binário do ensaio histórico. Esses helpers são experimentos locais, não um instalador público.

## Dependência Razer

O dispositivo original `1532:0F08` estava presente e OK em PnP. Antes da instalação, não foram encontrados produtos/serviços/processos Synapse ou Chroma utilizáveis, nem as DLLs SDK nos caminhos padrão. A única pasta encontrada era `RzS3WizardPkg`.

A [lista oficial atual de Synapse 4](https://mysupport.razer.com/app/answers/detail/a_id/6120/kw/mouse%20synapse) inclui o **Base Station Chroma original, RC21-01190**, separadamente dos V2/V3. Isso confirma suporte anunciado no configurador, não a rota de efeitos deste protótipo. O [guia Razer](https://developer.razer.com/works-with-chroma/setting-up/) descreve o runtime SDK e a detecção dos periféricos. A [documentação atual WYVRN](https://doc.wyvrn.com/docs/chroma-sdk/chroma-rgb-rest-api/) remete ao protocolo REST Razer.

O usuário escolheu instalar o Chroma oficial. Foi baixado pelo link observado em [razer.com/chroma](https://www.razer.com/chroma), que redirecionou para `https://dl.razerzone.com/drivers/Chroma/RazerChromaInstaller.exe`. O bootstrapper tem versão **2.6.0.890**, 14.923.464 bytes e SHA-256 `E4EABF1A0CF030B773A1A14ED22F4CB337C5F85638863260215B6315D83EB570`; assinatura Authenticode válida de Razer USA Ltd. Essa é a versão do instalador, não a versão do aplicativo/runtime que ele instalará.

O instalador foi aberto após reconferir hash e assinatura, com a etapa visual/licença/UAC entregue ao usuário: o conector de controle do computador falhou ao iniciar (`helper_unknown_error: setup refresh had errors`). Não automatizamos a aceitação da licença ou prompts de segurança. A conclusão da instalação e suas versões devem ser registradas separadamente.

## Protótipo executável de saída, separado do HAL

[tools/test_razer_chroma.py](../tools/test_razer_chroma.py) usa somente a biblioteca padrão Python. É um experimento isolado para validar a saída, não uma ponte que lê cores da RAM, não um novo controlador ASUS e ainda não o adaptador ligado ao HAL. O padrão registra PnP/serviços e consulta a versão do SDK por GET, sem inicializar sessão ou enviar iluminação:

```powershell
python tools/test_razer_chroma.py
```

Quando o runtime estiver disponível, o modo de aplicação mantém vermelho de intensidade 128 por três segundos:

```powershell
python tools/test_razer_chroma.py --apply --color 800000 --seconds 3
```

O modo `--apply` exige que o Base Station seja o único dispositivo USB pai Razer presente. Isso evita alvos Razer conhecidos adicionais, mas **o endpoint `/mousepad` é de categoria compartilhada**, não um endereçamento USB por VID/PID. Outros dispositivos integrados nessa categoria podem responder. O mapeamento `0F08 → mousepad`, inicialmente uma pista do código RGB.NET, foi depois confirmado para cor estática nesta máquina; a categoria ASUS HeadphoneStand não determina a categoria Chroma.

O protótipo verifica o runtime em `http://localhost:54235/razer/chromasdk`, registra só a categoria mousepad, envia `CHROMA_STATIC` e encerra a sessão com DELETE em `finally`, inclusive após erro de efeito ou Ctrl+C normal. Não usa `CHROMA_NONE` para encerrar: esse comando envia preto, não libera controle. O formato Chroma é BGR/COLORREF (`0x00BBGGRR`), independente do formato bruto dos comandos Aura, ainda sem conversão validada.

O inventário, a resposta do SDK e uma observação opcional do usuário (`--before-note`) são o estado disponível registrado antes da saída. Não há API de leitura da cor/perfil anterior implementada; `LightingStateReadable=false`. DELETE confirma liberação da sessão, não restauração medida da cor anterior. Fechamento forçado do processo não garante `finally`; o [protocolo documenta expiração de sessão sem atividade em 15 segundos](https://assets.razerzone.com/dev_portal/REST/html/index.html). Uma falha de DELETE fica explícita no relatório. Após o usuário pedir observação enquanto o teste está ativo, o protótipo passou a permitir até 300 segundos, heartbeat e reaplicação estática uma vez por segundo, com parada opcional por arquivo (`--stop-file`). Não há daemon, inicialização automática AuraLink ou aquisição ASUS.

### Testes e limites

Cinco testes do protocolo usam um servidor HTTP falso local: GET sem sessão, conversão RGB/BGR e escopo, DELETE após erro de efeito, propagação de erro na liberação e recusa a URI de sessão remota. Esses testes passaram, junto dos três testes PE existentes. **Não são validação do hardware nem execução do SDK real.**

A primeira execução local em modo somente leitura encontrou o Base Station e nenhum serviço Razer; o GET da versão expirou. Não houve sessão inicializada nem tentativa de saída. O script grava o resultado em `artifacts/razer-stage/chroma-test.json`, com `outputAttempted`, `apiAccepted`, `releaseConfirmed` e `hardwareValidated` separados. Mesmo quando a API aceita um comando, o protótipo mantém `hardwareValidated=false`; a confirmação física do usuário deve ser documentada à parte.

## Próximo critério

Concluir a instalação oficial, verificar versões e serviços, confirmar que o Chroma detecta o Base Station original e executar uma cor estática limitada com liberação. Só após confirmação física, conectar a saída aos comandos recebidos pelo HAL. A primeira versão poderá replicar uma cor em todo o suporte; controle individual dos LEDs e tradução das cores Aura exigem validações próprias.
