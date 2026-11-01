AuraLink @VERSION@ — beta para Windows x64
Integração não oficial, sem vínculo com ASUS, Razer ou Corsair.

DISPOSITIVO: Razer Base Station Chroma original, USB 1532:0F08.
REQUISITOS: Windows 10/11 x64, .NET Framework 4.8, Armoury Crate instalado e funcionando e driver Razer compatível já instalado.
O driver Razer não é incluído nem instalado por este pacote. A remoção desse driver não foi validada.

INSTALAR: abra o instalador e clique em Instalar. O Windows pede UAC. Não há inicialização automática nem serviço AuraLink instalado.
USAR: feche o Razer Chroma, inclusive na bandeja. Abra AuraLink no menu Iniciar e clique em Iniciar, aceitando o UAC da sessão. Reabra Armoury Crate → Aura Sync e selecione o suporte de fone AuraLink.
PARAR: clique em Parar ou feche a janela do AuraLink. Aguarde a recuperação antes de encerrar. A base volta a um arco-íris conhecido; não é possível ler/restaurar qualquer perfil anterior.
RECUPERAR: após queda ou encerramento inesperado, abra AuraLink e clique em Recuperar. Reconecte a base antes de recuperar. Não encerre o processo elevado à força durante a limpeza.
REMOVER: pare a sessão e use Aplicativos do Windows → AuraLink → Desinstalar. Relatórios em C:\ProgramData\AuraLink\Runtime são preservados. Remova a versão anterior antes de instalar outra versão deste beta.

Uma sessão mantém temporariamente o HAL próprio registrado, renova os serviços LightingService/ArmouryCrateService e pausa os serviços Razer Chroma SDK instalados que estavam ativos, restaurando-os ao terminar. Tipos de inicialização não são alterados. Os dispositivos ASUS continuam controlados pelo Armoury. A saída replica uma cor lógica nos 15 LEDs da base.

O runtime tenta reabrir a base por até 60 segundos após perda USB. Se o LightingService reiniciar ou o canal falhar, a sessão encerra e tenta recuperar. Uma base fisicamente ausente não pode ter a iluminação restaurada até ser reconectada.

O backend foi validado no hardware com cores, animação, dez minutos de execução e um ciclo de desconexão/reconexão. A interface, o empacotamento e o modo contínuo são novos neste beta: não foi feita outra rodada de hardware nem instalação/desinstalação real da versão distribuída. Não há certificação de uso diário ou compatibilidade com todas as versões do Armoury.

Este instalador não possui assinatura Authenticode. Verifique o SHA-256 na release oficial antes de executar.
Releases e documentação: https://github.com/Erhoz/auralink/releases
