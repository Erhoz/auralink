AuraLink @VERSION@ — beta para Windows x64
Integração não oficial, sem vínculo com ASUS, Razer ou Corsair.

DISPOSITIVO: Razer Base Station Chroma original, USB 1532:0F08.
REQUISITOS: Windows 10/11 x64, conta Windows administradora, .NET Framework 4.8, Armoury Crate instalado e funcionando e driver Razer compatível já instalado.
O driver Razer não é incluído nem instalado por este pacote. A remoção desse driver não foi validada.

INSTALAR: abra o instalador e clique em Instalar. Aceite o UAC com a própria conta administradora. O instalador configura o provedor, cria a tarefa de login e já inicia o AuraLink na bandeja. Não é necessário reiniciar o PC.
USAR: feche o Razer Chroma, inclusive na bandeja. Na primeira vez, reabra Armoury Crate → Aura Sync e selecione o suporte de fone AuraLink. Nos próximos logins, o AuraLink inicia pela tarefa AuraLink Startup, com privilégios já autorizados na instalação e sem outro UAC. Se serviços, base USB ou Razer Chroma impedirem o início, o aplicativo aguarda e informa o motivo.
BANDEJA: clique duas vezes no ícone ou abra AuraLink no menu Iniciar para mostrar a janela. Fechar a janela apenas a oculta. Parar pausa a sincronização; Sair no menu do ícone encerra o aplicativo e recupera o arco-íris conhecido. Não é possível ler/restaurar qualquer perfil anterior.
RECUPERAR: após queda ou encerramento inesperado, abra AuraLink e clique em Recuperar. Reconecte a base antes de recuperar. Não encerre o processo elevado à força durante a limpeza.
REMOVER: use Aplicativos do Windows → AuraLink → Desinstalar. A remoção encerra o aplicativo, retira a tarefa de login e o provedor próprio. Relatórios em C:\ProgramData\AuraLink são preservados. Instalações próprias anteriores podem ser atualizadas com backup dos arquivos.

O HAL próprio permanece registrado enquanto o programa está instalado, mantendo o cartão no Aura Sync. Instalar/remover o provedor renova LightingService/ArmouryCrateService; iniciar/parar normalmente a saída USB não recria o cartão. A sessão pausa os serviços Razer Chroma SDK instalados que estavam ativos e os restaura ao terminar. Não altera os tipos de inicialização dos serviços dos fabricantes. A saída replica uma cor lógica nos 15 LEDs da base.

O runtime tenta reabrir a base por até 60 segundos após perda USB. Se o LightingService reiniciar ou o canal falhar, a sessão encerra e tenta recuperar. Uma base fisicamente ausente não pode ter a iluminação restaurada até ser reconectada.

O backend foi validado no hardware com cores, animação, dez minutos de execução e um ciclo de desconexão/reconexão. A revisão com bandeja, ícone, provedor persistente e início automático foi verificada por compilação, pacote, definição em memória do Agendador e simulação, sem nova rodada de hardware, login real ou execução do novo instalador. A versão manual anterior foi retirada a pedido do usuário; baixe novamente os arquivos mesmo mantendo o número beta. Não há certificação de uso diário ou compatibilidade com todas as versões do Armoury.

Este instalador não possui assinatura Authenticode. Verifique o SHA-256 na release oficial antes de executar.
Releases e documentação: https://github.com/Erhoz/auralink/releases
