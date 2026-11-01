using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class DesktopProgram
{
    internal static string Version { get { return Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.Split('+')[0]; } }
    internal static string Root { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar); } }
    internal static string Data { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"AuraLink","Runtime"); } }
    internal static bool IsAdmin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--version") { Console.WriteLine(Version); return 0; }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string action=args.Length==1 && args[0]=="--recover-session" ? "Recover" : "Run";
        bool auto=args.Length==1 && (args[0]=="--session" || args[0]=="--recover-session");
        if(auto && !IsAdmin) { MessageBox.Show("A confirmação de administrador é necessária.","AuraLink"); return 2; }
        Application.Run(new SessionForm(auto,action)); return 0;
    }
}

internal sealed class SessionForm : Form
{
    private readonly Label status=new Label(),details=new Label();
    private readonly Button start=new Button(),stop=new Button(),recover=new Button();
    private readonly Timer timer=new Timer();
    private Process session;
    private string stderrPath;
    private bool closing,stopping;
    private DateTime started;
    internal SessionForm(bool auto,string action)
    {
        Text="AuraLink "+DesktopProgram.Version; ClientSize=new Size(550,265); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false;
        Font=new Font("Segoe UI",10); StartPosition=FormStartPosition.CenterScreen;
        var heading=new Label { Text="Razer Base Station Chroma",Location=new Point(22,18),AutoSize=true,Font=new Font("Segoe UI",15,FontStyle.Bold) };
        var info=new Label { Text="Integração não oficial com o ASUS Aura Sync.\nFeche o Razer Chroma antes de iniciar. Ao encerrar, a base volta ao arco-íris.",Location=new Point(22,58),Size=new Size(506,47) };
        status.Location=new Point(22,117);status.Size=new Size(506,27);status.Text="Pronto para iniciar.";
        details.Location=new Point(22,147);details.Size=new Size(506,47);details.ForeColor=SystemColors.GrayText;details.Text="Depois de iniciar, reabra o Armoury Crate e selecione o suporte em Aura Sync.";
        start.Text="Iniciar";start.Location=new Point(22,211);start.Size=new Size(110,32);start.Click+=(s,e)=>Begin("Run");
        stop.Text="Parar";stop.Location=new Point(144,211);stop.Size=new Size(110,32);stop.Enabled=false;stop.Click+=(s,e)=>StopSession();
        recover.Text="Recuperar";recover.Location=new Point(378,211);recover.Size=new Size(150,32);recover.Click+=(s,e)=>Begin("Recover");
        Controls.AddRange(new Control[]{heading,info,status,details,start,stop,recover});
        timer.Interval=1000;timer.Tick+=(s,e)=>RefreshSession();
        FormClosing+=(s,e)=>{if(session!=null && !session.HasExited){closing=true;e.Cancel=true;StopSession();}};
        Shown+=(s,e)=>{if(auto) Begin(action);};
        FormClosed+=(s,e)=>{timer.Dispose();if(session!=null)session.Dispose();};
    }
    private void Begin(string action)
    {
        if(!DesktopProgram.IsAdmin)
        {
            try{Process.Start(new ProcessStartInfo(Application.ExecutablePath,action=="Recover" ? "--recover-session" : "--session") { UseShellExecute=true,Verb="runas" });Close();}
            catch(Exception error){status.Text="A confirmação de administrador não foi concluída.";details.Text=error.Message;}
            return;
        }
        try
        {
            if(session!=null){session.Dispose();session=null;}
            started=DateTime.UtcNow;stopping=false;
            stderrPath=Path.Combine(Path.GetTempPath(),"AuraLink-"+Guid.NewGuid().ToString("N")+".log");
            string engine=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
            string script=Path.Combine(DesktopProgram.Root,"scripts","Session.ps1");
            session=new Process { StartInfo=new ProcessStartInfo(engine,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -Action "+action+" -OwnerPid "+Process.GetCurrentProcess().Id) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true } };
            session.ErrorDataReceived+=(s,e)=>{if(e.Data!=null){try{lock(this)File.AppendAllText(stderrPath,e.Data+Environment.NewLine);}catch(IOException){}}};
            session.OutputDataReceived+=(s,e)=>{};
            session.Start();session.BeginErrorReadLine();session.BeginOutputReadLine();
            start.Enabled=false;recover.Enabled=false;stop.Enabled=true;status.Text=action=="Run" ? "Iniciando…" : "Recuperando sessão…";
            details.Text="Aguarde a preparação. Não é necessário reiniciar o computador.";timer.Start();
        }
        catch(Exception error){status.Text="Não foi possível iniciar.";details.Text=error.Message;start.Enabled=true;recover.Enabled=true;}
    }
    private static Dictionary<string,object> Read(string path)
    {var value=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));if(value==null)throw new IOException("Estado em gravacao.");return value;}
    private static string RunDirectory(Dictionary<string,object> state)
    {
        if((string)state["Owner"]!="AuraLink.ReleaseSession/v1" || !String.Equals((string)state["AppRoot"],DesktopProgram.Root,StringComparison.OrdinalIgnoreCase)) throw new IOException("Estado de outra instalação.");
        string run=Path.GetFullPath((string)state["RunDirectory"]);
        string root=Path.GetFullPath(Path.Combine(DesktopProgram.Data,"sessions"))+Path.DirectorySeparatorChar;
        if(!run.StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new IOException("Diretório de sessão inválido.");
        return run;
    }
    private void StopSession()
    {
        stopping=true;stop.Enabled=false;status.Text="Encerrando e recuperando a iluminação…";
        try{var state=Read(Path.Combine(DesktopProgram.Data,"active.json"));File.WriteAllText(Path.Combine(RunDirectory(state),"stop-test"),"");}
        catch(IOException){ } catch(ArgumentException){ } catch(InvalidOperationException){ }
    }
    private void RefreshSession()
    {
        if(session==null) return;
        if(session.HasExited)
        {
            timer.Stop();start.Enabled=true;recover.Enabled=true;stop.Enabled=false;
            status.Text=session.ExitCode==0 ? "Sessão encerrada." : "A sessão precisa de atenção.";
            details.Text="Relatórios: "+DesktopProgram.Data;
            if(session.ExitCode!=0)
            {
                try{var last=Read(Path.Combine(DesktopProgram.Data,"last-session.json"));if(DateTime.Parse((string)last["StartedUtc"]).ToUniversalTime()>=started.AddSeconds(-2) && last["Error"]!=null)details.Text=(string)last["Error"];else if(File.Exists(stderrPath))details.Text=File.ReadAllText(stderrPath);}
                catch(Exception){if(File.Exists(stderrPath))details.Text=File.ReadAllText(stderrPath);}
            }
            if(closing)Close();return;
        }
        try
        {
            var state=Read(Path.Combine(DesktopProgram.Data,"active.json"));
            string run=RunDirectory(state);
            if(stopping){File.WriteAllText(Path.Combine(run,"stop-test"),"");return;}
            string path=Path.Combine(run,"device-operation.json");
            if(File.Exists(path))
            {
                var device=Read(path);bool active=Convert.ToBoolean(device["Active"]);status.Text=active ? "Sincronização ativa." : "Finalizando sessão…";
                details.Text="Frames aplicados: "+device["FramesWritten"]+". Selecione o suporte no Aura Sync.";
                string usb=Path.Combine(run,"usb-reconnect-state.json");
                if(active && File.Exists(usb) && (string)Read(usb)["state"]=="disconnected")status.Text="Aguardando reconexão USB (até 60 segundos)…";
            }
        }
        catch(IOException) { } catch(ArgumentException) { } catch(KeyNotFoundException) { } catch(InvalidOperationException) { }
    }
}
