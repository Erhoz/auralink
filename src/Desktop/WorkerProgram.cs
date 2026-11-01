using System;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using AuraLink.Adapters.Asus;
using AuraLink.Adapters.Razer;

internal static class WorkerProgram
{
    private static int Main(string[] args)
    {
        var json=new JavaScriptSerializer();
        try
        {
            if(args.Length==1 && args[0]=="--version") { Console.WriteLine(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion); return 0; }
            if(args.Length==1 && args[0]=="--inventory") { var found=HidMetadata.ReadBaseStation(); Console.WriteLine(json.Serialize(found)); return found.Count>0 ? 0 : 2; }
            if(args.Length==1 && args[0]=="--restore-rainbow") return RazerVolatileExperiment.RestoreRainbow();
            if(args.Length!=3 || args[0]!="--run") throw new ArgumentException("Usage: --run <session-directory> <LightingService PID>, --inventory, --restore-rainbow, --version");
            ReconnectableHidControl control=null;
            using(var source=new HalPipeFrameSource(int.Parse(args[2])))
                return RazerVolatileExperiment.RunFromRainbow(args[1],0,
                    ()=>control=new ReconnectableHidControl(()=>new HidFeatureSession(),()=>File.Exists(Path.Combine(args[1],"stop-test")),
                        state=>File.WriteAllText(Path.Combine(args[1],"usb-reconnect-state.json"),json.Serialize(new { state=state,utc=DateTime.UtcNow.ToString("o") }))),
                    false,()=>{control.CheckConnection();return source.Poll();},"AuraLink native session",
                    ()=>new { source.Polls,source.NewFrames,UsbLifecycle=control.Snapshot() },true);
        }
        catch(Exception error) { Console.Error.WriteLine(json.Serialize(new { error=error.Message,details=error.ToString() })); return 2; }
    }
}
