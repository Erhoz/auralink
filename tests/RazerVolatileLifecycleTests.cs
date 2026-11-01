using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using AuraLink.Adapters.Razer;
using AuraLink.Adapters.Asus;
using AuraLink.Core;

internal static class RazerVolatileLifecycleTests
{
    private sealed class Fake : IHidControl
    {
        internal byte Mode;
        internal bool FailFrame,FailRestore,FailRainbow,Disposed;
        internal int ModeWrites,RestoreAttempts,RainbowWrites;
        internal byte[] LastFrame;
        internal string Directory;
        public byte[] Exchange(byte[] request,bool writeAck)
        {
            byte[] response=(byte[])request.Clone(); response[1]=2;
            if(request[7]==0 && request[8]==0x84) response[9]=Mode;
            if(request[7]==0 && request[8]==4)
            {
                ModeWrites++;
                if(request[9]==0)
                {
                    RestoreAttempts++;
                    if(FailRestore) throw new IOException("Injected restore failure");
                }
                Mode=request[9];
            }
            if(request[7]==0x0f && request[8]==3)
            {
                if(FailFrame) throw new IOException("Injected frame failure");
                LastFrame=(byte[])request.Clone();
                File.WriteAllText(Path.Combine(Directory,"stop-test"),"");
            }
            if(request[7]==0x0f && request[8]==2 && request[11]==4)
            {
                RainbowWrites++;
                if(FailRainbow) throw new IOException("Injected rainbow failure");
            }
            return response;
        }
        public void Dispose() { Disposed=true; }
    }
    private static Dictionary<string,object> Read(string directory)
    {
        return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(directory,"device-operation.json")));
    }
    internal static void Run(string root)
    {
        string suite=Path.Combine(root,DateTime.UtcNow.Ticks.ToString());
        var normal=new Fake { Directory=Path.Combine(suite,"normal") };
        if(RazerVolatileExperiment.RunFromRainbow(normal.Directory,1,()=>normal,true)!=0 || !normal.Disposed || normal.Mode!=0 || normal.RestoreAttempts!=1 || normal.RainbowWrites!=1 || !(bool)Read(normal.Directory)["RecoveryEffectAccepted"])
            throw new Exception("Normal stop did not restore/dispose");
        var frameFailure=new Fake { Directory=Path.Combine(suite,"frame-failure"),FailFrame=true };
        if(RazerVolatileExperiment.RunFromRainbow(frameFailure.Directory,1,()=>frameFailure,true)!=2 || frameFailure.Mode!=0 || !frameFailure.Disposed || frameFailure.RestoreAttempts!=1 || frameFailure.RainbowWrites!=1)
            throw new Exception("Frame failure did not restore/dispose");
        var restoreFailure=new Fake { Directory=Path.Combine(suite,"restore-failure"),FailRestore=true };
        if(RazerVolatileExperiment.RunFromRainbow(restoreFailure.Directory,1,()=>restoreFailure,true)!=2 || (bool)Read(restoreFailure.Directory)["ModeRestored"])
            throw new Exception("Restore failure was hidden");
        var occupied=new Fake { Directory=Path.Combine(suite,"occupied"),Mode=3 };
        if(RazerVolatileExperiment.RunFromRainbow(occupied.Directory,1,()=>occupied,true)!=2 || occupied.ModeWrites!=0 || occupied.RestoreAttempts!=0 || occupied.RainbowWrites!=0)
            throw new Exception("An occupied device mode was modified");
        var rainbowFailure=new Fake { Directory=Path.Combine(suite,"rainbow-failure"),FailRainbow=true };
        if(RazerVolatileExperiment.RunFromRainbow(rainbowFailure.Directory,1,()=>rainbowFailure,true)!=2 || (bool)Read(rainbowFailure.Directory)["RecoveryEffectAccepted"] || !rainbowFailure.Disposed)
            throw new Exception("Rainbow recovery failure was hidden");
        Console.WriteLine("{\"lifecycleTestsPassed\":5,\"simulation\":true,\"hardwareAccess\":false}");
        var bridge=new Fake { Directory=Path.Combine(suite,"native-frame") };
        if(RazerVolatileExperiment.RunFromRainbow(bridge.Directory,1,()=>bridge,true,()=>new RgbColor(0x12,0x34,0x56))!=0 || bridge.LastFrame[14]!=0x12 || bridge.LastFrame[15]!=0x34 || bridge.LastFrame[16]!=0x56 || bridge.RainbowWrites!=1)
            throw new Exception("Native frame did not reach USB encoder and restore");
        var sourceFailure=new Fake { Directory=Path.Combine(suite,"source-failure") };
        if(RazerVolatileExperiment.RunFromRainbow(sourceFailure.Directory,1,()=>sourceFailure,true,()=>{throw new IOException("Injected source failure");})!=2 || sourceFailure.RainbowWrites!=1 || !sourceFailure.Disposed)
            throw new Exception("Source failure did not restore/dispose");
        var serializer=new JavaScriptSerializer();
        DateTime now=DateTime.UtcNow;
        Func<string,int,DateTime,string> line=(process,pid,stamp)=>serializer.Serialize(new { process=process,processId=pid,timestampUtc=stamp.ToString("o"),operation="SetEffect",data=new { effectId=0, colors=new uint[]{0xff563412} } });
        var parsed=HalLogFrameSource.Parse(line("LightingService",123,now),123,now);
        if(!parsed.HasValue || parsed.Value.ToString()!="123456") throw new Exception("HAL channel hypothesis encoding changed");
        if(HalLogFrameSource.Parse(line("LightingService",123,now.AddSeconds(-10)),123,now).HasValue ||
            HalLogFrameSource.Parse(line("LightingService",999,now),123,now).HasValue ||
            HalLogFrameSource.Parse(line("SyntheticClient",123,now),123,now).HasValue)
            throw new Exception("Stale or foreign HAL frames accepted");
        string path=Path.Combine(suite,"tail.jsonl");
        File.WriteAllText(path,line("LightingService",123,now)+"\n");
        using(var reader=new HalLogFrameSource(path,123))
        {
            if(reader.Poll().HasValue) throw new Exception("Old log was replayed");
            string next=line("LightingService",123,DateTime.UtcNow);
            File.AppendAllText(path,next.Substring(0,next.Length/2));
            if(reader.Poll().HasValue) throw new Exception("Partial log line was consumed");
            File.AppendAllText(path,next.Substring(next.Length/2)+"\n");
            if(!reader.Poll().HasValue || reader.AcceptedFrames!=1) throw new Exception("Completed log line was lost");
        }
        Console.WriteLine("{\"nativeBridgeTestsPassed\":5,\"simulation\":true,\"hardwareAccess\":false,\"colorOrderPhysicallyValidated\":false}");
    }
}
