using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;
using System.Xml;

internal static class StartupTask
{
    internal const string Name="AuraLink Startup";
    private const string Owner="AuraLink.Setup/v1";
    internal static string UserSid { get { return WindowsIdentity.GetCurrent().User.Value; } }
    internal static string ShowEvent { get { return "Local\\AuraLink.Show."+UserSid; } }
    internal static string ExitEvent { get { return "Local\\AuraLink.Exit."+UserSid; } }
    internal static bool Signal(string name)
    {
        try { using(var handle=EventWaitHandle.OpenExisting(name,System.Security.AccessControl.EventWaitHandleRights.Modify)){handle.Set();return true;} }
        catch(WaitHandleCannotBeOpenedException){return false;}
    }
    internal static string Xml(string root,string sid,bool enabled=true)
    {
        string user=SecurityElement.Escape(sid),exe=SecurityElement.Escape(Path.Combine(root,"AuraLink.exe")),directory=SecurityElement.Escape(root);
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"+
            "<RegistrationInfo><Author>AuraLink</Author><Source>"+Owner+"</Source><Description>AuraLink automatic synchronization at user logon</Description></RegistrationInfo>"+
            "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>"+user+"</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>"+
            "<Principals><Principal id=\"Author\"><UserId>"+user+"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>"+
            "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>"+
            "<AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><AllowStartOnDemand>true</AllowStartOnDemand>"+
            "<Enabled>"+(enabled ? "true" : "false")+"</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>"+
            "<Actions Context=\"Author\"><Exec><Command>"+exe+"</Command><Arguments>--autostart</Arguments><WorkingDirectory>"+directory+"</WorkingDirectory></Exec></Actions></Task>";
    }
    private static dynamic Service()
    {dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();return service;}
    private static dynamic Find(dynamic folder)
    {
        try{return folder.GetTask(Name);}
        // COM maps ERROR_FILE_NOT_FOUND to FileNotFoundException on this runtime.
        // Preserve all other failures, including access denied and scheduler errors.
        catch(Exception error){if(error.HResult==unchecked((int)0x80070002))return null;throw;}
    }
    private static void Validate(dynamic task,string root,bool requireUser)
    {
        var xml=new XmlDocument();xml.LoadXml((string)task.Xml);
        var ns=new XmlNamespaceManager(xml.NameTable);ns.AddNamespace("t","http://schemas.microsoft.com/windows/2004/02/mit/task");
        var source=xml.SelectSingleNode("/t:Task/t:RegistrationInfo/t:Source",ns);
        var command=xml.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command",ns);
        var user=xml.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:UserId",ns);
        if(source==null || source.InnerText!=Owner || command==null || !String.Equals(Path.GetFullPath(command.InnerText),Path.Combine(root,"AuraLink.exe"),StringComparison.OrdinalIgnoreCase) || xml.SelectNodes("/t:Task/t:Actions/*",ns).Count!=1)
            throw new IOException("Já existe uma tarefa AuraLink de outro proprietário.");
        if(requireUser)
        {
            if(user==null)throw new IOException("A tarefa não identifica o usuário.");
            string sid=user.InnerText;
            if(!sid.StartsWith("S-1-",StringComparison.OrdinalIgnoreCase))sid=((SecurityIdentifier)new NTAccount(sid).Translate(typeof(SecurityIdentifier))).Value;
            if(sid!=UserSid)throw new IOException("A tarefa pertence a outro usuário Windows.");
        }
    }
    internal static void Register(string root,bool enabled=false)
    {
        dynamic service=Service();
        try{dynamic folder=service.GetFolder("\\");dynamic old=Find(folder);if(old!=null)Validate(old,root,true);folder.RegisterTask(Name,Xml(root,UserSid,enabled),6,UserSid,null,3,null);}
        finally{Marshal.FinalReleaseComObject(service);}
    }
    internal static bool TryRun(string root)
    {
        dynamic service=Service();
        try{dynamic task=Find(service.GetFolder("\\"));if(task==null)return false;Validate(task,root,true);task.Run(null);return true;}
        finally{Marshal.FinalReleaseComObject(service);}
    }
    internal static void Remove(string root)
    {
        dynamic service=Service();
        try{dynamic folder=service.GetFolder("\\");dynamic task=Find(folder);if(task!=null){Validate(task,root,false);folder.DeleteTask(Name,0);}}
        finally{Marshal.FinalReleaseComObject(service);}
    }
    internal static bool? SetEnabled(string root,bool enabled)
    {
        dynamic service=Service();
        try{dynamic task=Find(service.GetFolder("\\"));if(task==null)return null;Validate(task,root,true);bool before=(bool)task.Enabled;task.Enabled=enabled;return before;}
        finally{Marshal.FinalReleaseComObject(service);}
    }
}
