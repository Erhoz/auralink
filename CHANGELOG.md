# Changelog

- Correção da primeira instalação: tarefa ainda ausente retorna `FileNotFoundException` (`0x80070002`) neste Windows. O instalador agora aceita essa ausência; falhas de permissão continuam sendo propagadas. Regressão automatizada adicionada.
- Ícone também exibido em destaque no cabeçalho do instalador, além dos recursos dos executáveis, bandeja e atalhos.

## 1.0.0-beta.1 — republicação solicitada pelo usuário

- Retirada da primeira publicação manual; mesma versão/tag republicadas com novo commit e novos hashes por solicitação explícita. Histórico Git preservado.
- Inicialização após instalar e no login pela tarefa `AuraLink Startup`, sem UAC a cada login.
- Bandeja com Abrir, Iniciar, Parar e Sair; fechar a janela passa a ocultar.
- Ícone próprio em SVG/ICO multirresolução, executáveis, instalador e atalhos.
- Provedor persistente instalado/removido junto do aplicativo, sem recriar o cartão a cada sessão.
- Atualização de instalação própria com backup, encerramento gracioso e remoção da tarefa no uninstall.
- Correções para cancelamento antes do controle, erros antigos de recuperação e parada transitória de serviços. A instalação antiga deste PC foi recuperada e removida a pedido.
- Verificações de compilação, pacote, ícones, tarefa em memória e simulações; nenhum novo login ou ensaio físico executado.
- Publicador compatível com a serialização de strings do Windows PowerShell 5.1; links definitivos dos assets conferidos depois de publicar.

## 1.0.0-beta.1 — primeira publicação, retirada

- Primeiro instalador Windows x64 e pacote ZIP com hashes SHA-256.
- Aplicativo desktop com Iniciar, Parar e Recuperar; sessão contínua manual, sem autostart.
- HAL HeadphoneStand, canal de frames local e adaptador USB para o Razer Base Station Chroma original `1532:0F08`.
- Recuperação USB limitada a 60 segundos e retorno a arco-íris conhecido ao encerrar.
- Manifesto do pacote, versões dos binários e metadados do commit de origem.

O backend foi validado no hardware antes do empacotamento, incluindo dez minutos e um ciclo USB. Interface, instalador e modo contínuo são novos e foram verificados sem nova rodada de hardware. Veja as [notas da versão](docs/releases/1.0.0-beta.1.md).
