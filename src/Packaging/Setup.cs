using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class PayloadManifest
{
    public string version { get; set; }
    public string sourceCommit { get; set; }
    public Dictionary<string,string> files { get; set; }
}
internal sealed class InstallRecord
{
    public string Owner { get; set; }
    public string Version { get; set; }
    public string[] Files { get; set; }
}
internal sealed class SessionGate : IDisposable
{
    private readonly Mutex mutex=new Mutex(false,"Global\\AuraLink.ManualSession.v1");
    internal SessionGate()
    {
        bool held;
        try { held=mutex.WaitOne(0); } catch(AbandonedMutexException) { held=true; }
        if(!held){mutex.Dispose();throw new IOException("Pare a sessão AuraLink antes de instalar ou remover.");}
        string active=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"AuraLink","Runtime","active.json");
        if(File.Exists(active)){mutex.ReleaseMutex();mutex.Dispose();throw new IOException("Use Recuperar no AuraLink antes de alterar a instalação.");}
    }
    public void Dispose(){mutex.ReleaseMutex();mutex.Dispose();}
}
internal static class SetupProgram
{
    private const string Owner="AuraLink.Setup/v1";
    private const string UninstallKey="SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AuraLink";
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    private static string Version { get { return Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.Split('+')[0]; } }
    private static string Root { get { return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"AuraLink","App")); } }
    private static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
    private static string Within(string root,string relative)
    {
        if(String.IsNullOrWhiteSpace(relative) || relative.Contains(':') || Path.IsPathRooted(relative)) throw new InvalidDataException("Caminho inválido no pacote.");
        foreach(string part in relative.Replace('\\','/').Split('/'))
            if(part.Length==0 || part=="." || part==".." || part.EndsWith(".") || part.EndsWith(" ")) throw new InvalidDataException("Caminho inválido no pacote.");
        string path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
        if(!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho fora do destino.");
        NoReparseParents(Path.GetDirectoryName(path));
        return path;
    }
    private static void NoReparseParents(string path)
    {
        while(!String.IsNullOrEmpty(path))
        {
            if(Directory.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Destino com redirecionamento de diretório recusado.");
            path=Path.GetDirectoryName(path);
        }
    }
    private static string Hash(Stream stream)
    { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
    private static PayloadManifest Validate(ZipArchive zip)
    {
        var entry=zip.GetEntry("payload.manifest.json");
        if(entry==null || entry.Length>1024*1024) throw new InvalidDataException("Manifesto ausente ou inválido.");
        PayloadManifest manifest;
        using(var reader=new StreamReader(entry.Open())) manifest=Json.Deserialize<PayloadManifest>(reader.ReadToEnd());
        if(manifest==null || manifest.version!=Version || manifest.files==null || manifest.files.Count<5) throw new InvalidDataException("Versão ou manifesto divergente.");
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var file in zip.Entries)
        {
            string name=file.FullName.Replace('\\','/');
            Within(Path.Combine(Path.GetTempPath(),"AuraLink-validation"),name);
            if(!found.Add(name)) throw new InvalidDataException("Arquivo duplicado.");
            total+=file.Length;if(file.Length>32*1024*1024 || total>64*1024*1024) throw new InvalidDataException("Pacote excedeu o limite.");
            if(name=="payload.manifest.json")continue;
            string expected;
            if(!manifest.files.TryGetValue(name,out expected))throw new InvalidDataException("Arquivo inesperado: "+name);
            using(var stream=file.Open())if(!String.Equals(expected,Hash(stream),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Hash divergente: "+file.FullName);
        }
        if(found.Count!=manifest.files.Count+1) throw new InvalidDataException("Pacote incompleto.");
        return manifest;
    }
    private static ZipArchive OpenPayload()
    {
        Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("AuraLink.Payload.zip");
        if(stream==null)throw new InvalidDataException("Payload ausente.");
        return new ZipArchive(stream,ZipArchiveMode.Read,false);
    }
    private static void Extract(ZipArchive zip,string root)
    {
        foreach(var entry in zip.Entries)
        {
            string destination=Within(root,entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using(var input=entry.Open())using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write))input.CopyTo(output);
        }
    }
    private static void Protect(string root)
    {
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
        foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.SetAccessControl(root,acl);
    }
    private static void Shortcut(bool remove)
    {
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"AuraLink");
        if(remove && !Directory.Exists(directory))return;
        if(!remove)Directory.CreateDirectory(directory);
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        foreach(var pair in new[]{new[]{"AuraLink.lnk","AuraLink.exe",""},new[]{"Desinstalar AuraLink.lnk","Uninstall.exe","--uninstall"}})
        {
            string path=Path.Combine(directory,pair[0]),target=Within(Root,pair[1]);
            dynamic link=shell.CreateShortcut(path);
            if(File.Exists(path) && !String.Equals((string)link.TargetPath,target,StringComparison.OrdinalIgnoreCase)){if(remove)continue;throw new IOException("Atalho existente de outra instalação.");}
            if(remove){if(File.Exists(path))File.Delete(path);}
            else{link.TargetPath=target;link.Arguments=pair[2];link.WorkingDirectory=Root;link.Save();}
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        }
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        if(remove && Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory,false);
    }
    private static void PrepareRuntimeDirectory()
    {
        string parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"AuraLink");
        foreach(string path in new[]{parent,Path.Combine(parent,"Runtime")})
        {
            NoReparseParents(path);
            if(Directory.Exists(path))
            {
                var owner=(SecurityIdentifier)Directory.GetAccessControl(path).GetOwner(typeof(SecurityIdentifier));
                if(!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !owner.IsWellKnown(WellKnownSidType.LocalSystemSid))throw new IOException("Diretório de dados preexistente de outro proprietário. Instalação recusada.");
            }
            Directory.CreateDirectory(path);Protect(path);
        }
    }
    private static void Install()
    {
        using(var framework=Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full"))
            if(framework==null || Convert.ToInt32(framework.GetValue("Release",0))<528040)throw new IOException("Instale o .NET Framework 4.8 antes de continuar.");
        using(var gate=new SessionGate())using(var zip=OpenPayload())
        {
            PayloadManifest manifest=Validate(zip);
            NoReparseParents(Root);
            using(var existing=Registry.LocalMachine.OpenSubKey(UninstallKey))
            {if(existing!=null && Convert.ToString(existing.GetValue("AuraLinkOwner"))!=Owner)throw new IOException("Entrada de desinstalação de outro proprietário.");}
            if(Directory.Exists(Root) && Directory.GetFileSystemEntries(Root).Length>0)throw new IOException("O AuraLink já está instalado. Remova a versão anterior antes de instalar este beta.");
            PrepareRuntimeDirectory();
            var copied=new List<string>();
            Directory.CreateDirectory(Root);Protect(Root);
            try
            {
                // Journal the known file list before extraction, including partial writes.
                copied.AddRange(zip.Entries.Select(e=>e.FullName));copied.Add("Uninstall.exe");copied.Add("installation.json");
                Extract(zip,Root);
                File.Copy(Application.ExecutablePath,Within(Root,"Uninstall.exe"),false);
                File.WriteAllText(Within(Root,"installation.json"),Json.Serialize(new InstallRecord{Owner=Owner,Version=Version,Files=copied.ToArray()}));
                Shortcut(false);
                using(var key=Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    if(key.GetValue("AuraLinkOwner")!=null && (string)key.GetValue("AuraLinkOwner")!=Owner)throw new IOException("Entrada de desinstalação de outro proprietário.");
                    key.SetValue("AuraLinkOwner",Owner);key.SetValue("DisplayName","AuraLink "+Version);key.SetValue("DisplayVersion",Version);
                    key.SetValue("Publisher","AuraLink contributors (unofficial)");key.SetValue("InstallLocation",Root);
                    key.SetValue("UninstallString","\""+Within(Root,"Uninstall.exe")+"\" --uninstall");key.SetValue("DisplayIcon",Within(Root,"AuraLink.exe"));
                    key.SetValue("URLInfoAbout","https://github.com/Erhoz/auralink");key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);
                }
                MessageBox.Show("AuraLink instalado. Abra pelo menu Iniciar quando quiser sincronizar. Nenhuma inicialização automática foi configurada.","AuraLink");
            }
            catch
            {
                try{Shortcut(true);}catch(Exception){}
                foreach(string relative in copied){string path=Within(Root,relative);if(File.Exists(path))File.Delete(path);}
                using(var key=Registry.LocalMachine.OpenSubKey(UninstallKey))
                {if(key!=null && (string)key.GetValue("AuraLinkOwner")==Owner){key.Close();Registry.LocalMachine.DeleteSubKeyTree(UninstallKey,false);}}
                RemoveEmptyDirectories();throw;
            }
        }
    }
    private static void RemoveEmptyDirectories(string directory=null)
    {
        directory=directory ?? Root;NoReparseParents(directory);
        foreach(var child in Directory.GetDirectories(directory))
            if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)RemoveEmptyDirectories(child);
        if(Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory,false);
    }
    private static void Remove()
    {
        using(var gate=new SessionGate())
        {
            string marker=Within(Root,"installation.json");
            var record=Json.Deserialize<InstallRecord>(File.ReadAllText(marker));
            if(record==null || record.Owner!=Owner || record.Version!=Version || record.Files==null)throw new IOException("Propriedade ou versão da instalação divergente.");
            foreach(string path in record.Files)Within(Root,path); // validate every target before deletion
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
                using(var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
                using(var key=hive.OpenSubKey("SOFTWARE\\Classes\\CLSID\\{9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6}\\InprocServer32"))
                {if(key!=null && Convert.ToString(key.GetValue("CodeBase")).IndexOf(Root.Replace('\\','/'),StringComparison.OrdinalIgnoreCase)>=0)throw new IOException("Recupere a sessão antes de remover o HAL.");}
            Shortcut(true);
            foreach(string relative in record.Files){string path=Within(Root,relative);if(File.Exists(path))File.Delete(path);}
            using(var key=Registry.LocalMachine.OpenSubKey(UninstallKey))
            {if(key!=null && (string)key.GetValue("AuraLinkOwner")==Owner && String.Equals((string)key.GetValue("InstallLocation"),Root,StringComparison.OrdinalIgnoreCase)){key.Close();Registry.LocalMachine.DeleteSubKeyTree(UninstallKey,false);}}
            RemoveEmptyDirectories();
            MessageBox.Show("AuraLink removido. Relatórios em ProgramData foram preservados. Componentes dos fabricantes não foram removidos.","AuraLink");
        }
    }
    private static void Elevate(string arguments)
    {Process.Start(new ProcessStartInfo(Application.ExecutablePath,arguments){UseShellExecute=true,Verb="runas"});}
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool verify=args.Length>0 && (args[0]=="--verify-payload" || args[0]=="--extract");
        try
        {
            if(verify)
            {
                using(var zip=OpenPayload())
                {
                    var manifest=Validate(zip);
                    if(args[0]=="--extract"){if(args.Length!=2)throw new ArgumentException("Destino necessário.");Extract(zip,Path.GetFullPath(args[1]));}
                    string result=Json.Serialize(new{version=manifest.version,sourceCommit=manifest.sourceCommit,files=manifest.files.Count,verified=true,installed=false});
                    if(args[0]=="--verify-payload" && args.Length==2)File.WriteAllText(args[1],result);else Console.WriteLine(result);
                }
                return 0;
            }
            if(!Environment.Is64BitOperatingSystem)throw new PlatformNotSupportedException("Este beta requer Windows x64.");
            if(args.Length>0 && args[0]=="--install"){if(!Admin){Elevate("--install");return 0;}Install();return 0;}
            if(args.Length>0 && args[0]=="--uninstall")
            {
                if(!Admin){Elevate("--uninstall");return 0;}
                if(MessageBox.Show("Remover o AuraLink? Pare uma sessão ativa antes de continuar.","AuraLink",MessageBoxButtons.YesNo)!=DialogResult.Yes)return 0;
                string temporary=Path.Combine(Path.GetTempPath(),"AuraLink-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");
                File.Copy(Application.ExecutablePath,temporary,false);
                Process.Start(new ProcessStartInfo(temporary,"--remove-installed "+Process.GetCurrentProcess().Id){UseShellExecute=true});return 0;
            }
            if(args.Length==2 && args[0]=="--remove-installed")
            {try{using(var parent=Process.GetProcessById(int.Parse(args[1])))if(!parent.WaitForExit(10000))throw new IOException("Feche o desinstalador anterior.");}catch(ArgumentException){}if(!Admin)throw new UnauthorizedAccessException();Remove();return 0;}
            using(var zip=OpenPayload())Validate(zip);
            var form=new Form{Text="Instalar AuraLink "+Version,ClientSize=new Size(520,245),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,StartPosition=FormStartPosition.CenterScreen,Font=new Font("Segoe UI",10)};
            form.Controls.Add(new Label{Text="AuraLink "+Version,Font=new Font("Segoe UI",17,FontStyle.Bold),Location=new Point(22,18),AutoSize=true});
            form.Controls.Add(new Label{Text="Integração não oficial com ASUS Aura Sync.\nPara o Razer Base Station Chroma original (1532:0F08).\n\nRequer Armoury Crate, .NET Framework 4.8 e driver Razer.\nInstalação manual, sem inicialização automática.\nEste beta não possui assinatura digital.",Location=new Point(22,63),Size=new Size(478,126)});
            var install=new Button{Text="Instalar",Location=new Point(270,202),Size=new Size(105,30)};
            install.Click+=(s,e)=>{try{Elevate("--install");form.Close();}catch(Exception error){MessageBox.Show(error.Message,"AuraLink");}};
            var close=new Button{Text="Fechar",Location=new Point(390,202),Size=new Size(105,30)};close.Click+=(s,e)=>form.Close();
            form.Controls.AddRange(new Control[]{install,close});Application.Run(form);return 0;
        }
        catch(Exception error){if(verify)Console.Error.WriteLine(error.Message);else MessageBox.Show(error.Message,"AuraLink",MessageBoxButtons.OK,MessageBoxIcon.Error);return 2;}
    }
}
