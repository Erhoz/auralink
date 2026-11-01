using System;
using System.IO;
using System.Web.Script.Serialization;
using AuraLink.Adapters.Razer;
using AuraLink.Adapters.Asus;

internal static class RazerHidProbe
{
    private static int Main(string[] args)
    {
        var json = new JavaScriptSerializer();
        try
        {
            BaseStationProtocol.SelfTest();
            VolatileFrameProtocol.SelfTest();
            if(args.Length==3 && args[0]=="--rgb-from-rainbow") return RazerVolatileExperiment.RunFromRainbow(args[1],int.Parse(args[2]));
            if(args.Length==5 && args[0]=="--native-from-rainbow")
            {
                using(var source=new HalLogFrameSource(args[3],int.Parse(args[4])))
                    return RazerVolatileExperiment.RunFromRainbow(args[1],int.Parse(args[2]),()=>new HidFeatureSession(),false,source.Poll);
            }
            if(args.Length==4 && args[0]=="--pipe-from-rainbow")
            {
                using(var source=new HalPipeFrameSource(int.Parse(args[3])))
                    return RazerVolatileExperiment.RunFromRainbow(args[1],int.Parse(args[2]),()=>new HidFeatureSession(),false,source.Poll,"native HAL pipe-v1",()=>new { source.Polls,source.NewFrames });
            }
            if(args.Length==4 && args[0]=="--pipe-hotplug-from-rainbow")
            {
                ReconnectableHidControl control=null;
                using(var source=new HalPipeFrameSource(int.Parse(args[3])))
                    return RazerVolatileExperiment.RunFromRainbow(args[1],int.Parse(args[2]),
                        ()=>control=new ReconnectableHidControl(()=>new HidFeatureSession(),()=>File.Exists(Path.Combine(args[1],"stop-test")),
                            state=>File.WriteAllText(Path.Combine(args[1],"usb-reconnect-state.json"),json.Serialize(new { state=state,utc=DateTime.UtcNow.ToString("o") }))),
                        false,()=>{control.CheckConnection();return source.Poll();},"native HAL pipe-v1 + bounded USB reconnect",
                        ()=>new { source.Polls,source.NewFrames,UsbLifecycle=control.Snapshot() });
            }
            if(args.Length==1 && args[0]=="--restore-normal") return RazerVolatileExperiment.RestoreNormal();
            if(args.Length==1 && args[0]=="--restore-rainbow") return RazerVolatileExperiment.RestoreRainbow();
            if(args.Length==2 && args[0]=="--lifecycle-tests") { RazerVolatileLifecycleTests.Run(args[1]); return 0; }
            if(args.Length==1 && args[0]=="--self-test") { Console.WriteLine("{\"protocolSelfTestPassed\":true,\"hardwareAccess\":false}"); return 0; }
            if(args.Length==1 && args[0]=="--state") { Console.WriteLine(json.Serialize(HidStateReader.Read())); return 0; }
            if(args.Length!=0) throw new ArgumentException("Unknown mode");
            var collections = HidMetadata.ReadBaseStation();
            Console.WriteLine(json.Serialize(new { metadataOnly=true, hardwareOutput=false,
                featureTransfers=0, collections=collections }));
            return collections.Count == 0 ? 2 : 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(json.Serialize(new { metadataOnly=args.Length==0, hardwareOutput=false, error=error.ToString(), diagnostics=error.Data }));
            return 2;
        }
    }
}
