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
        internal bool StopOnFrame=true;
        internal bool Win32FrameFailureOnce;
        internal bool InitModeFailureOnce;
        internal Action AfterFrame;
        internal string Directory;
        public byte[] Exchange(byte[] request,bool writeAck)
        {
            byte[] response=(byte[])request.Clone(); response[1]=2;
            if(request[7]==0 && request[8]==0x84)
            {
                if(InitModeFailureOnce) { InitModeFailureOnce=false; throw new InvalidOperationException("Injected USB initialization delay"); }
                response[9]=Mode;
            }
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
                if(Win32FrameFailureOnce) { Win32FrameFailureOnce=false; throw new System.ComponentModel.Win32Exception(1167,"Injected USB unplug"); }
                if(FailFrame) throw new IOException("Injected frame failure");
                LastFrame=(byte[])request.Clone();
                if(StopOnFrame) File.WriteAllText(Path.Combine(Directory,"stop-test"),"");
                if(AfterFrame!=null) AfterFrame();
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
        using(var publisher=new FramePipePublisher(FramePipePublisher.NameForProcess(System.Diagnostics.Process.GetCurrentProcess().Id)))
        {
            publisher.Publish(0,0xff0000ff);
            using(var source=new HalPipeFrameSource(System.Diagnostics.Process.GetCurrentProcess().Id))
            {
                var disconnected=new Fake { Directory=Path.Combine(suite,"real-pipe-disconnect"),StopOnFrame=false,AfterFrame=()=>publisher.Dispose() };
                if(RazerVolatileExperiment.RunFromRainbow(disconnected.Directory,2,()=>disconnected,true,source.Poll,"real pipe with simulated USB")!=2 ||
                    disconnected.LastFrame[14]!=255 || disconnected.LastFrame[15]!=0 || disconnected.LastFrame[16]!=0 ||
                    disconnected.RainbowWrites!=1 || !disconnected.Disposed || !(bool)Read(disconnected.Directory)["ModeRestored"])
                    throw new Exception("Real pipe disconnect did not recover simulated USB");
            }
        }
        Console.WriteLine("{\"pipeDisconnectRecoveryTestPassed\":true,\"ipc\":\"real Windows named pipe\",\"usb\":\"simulated\",\"hardwareAccess\":false}");
        string reconnectDirectory=Path.Combine(suite,"usb-reconnect");
        var unplugged=new Fake { Directory=reconnectDirectory,Win32FrameFailureOnce=true };
        var returned=new Fake { Directory=reconnectDirectory };
        var initializing=new Fake { Directory=reconnectDirectory,InitModeFailureOnce=true };
        int opened=0;
        using(var control=new ReconnectableHidControl(()=>++opened==1 ? unplugged : opened==2 ? initializing : returned,()=>false,null))
        {
            if(RazerVolatileExperiment.RunFromRainbow(reconnectDirectory,1,()=>control,true)!=0 || control.Reconnections!=1 || !unplugged.Disposed || !initializing.Disposed || opened!=3 || returned.Mode!=0 || returned.RainbowWrites!=1)
                throw new Exception("USB reopen did not resume frame and restore known baseline");
        }
        string cancelDirectory=Path.Combine(suite,"usb-reconnect-cancel");
        var canceledUsb=new Fake { Directory=cancelDirectory,Win32FrameFailureOnce=true };
        using(var control=new ReconnectableHidControl(()=>canceledUsb,()=>true,null))
        {
            if(RazerVolatileExperiment.RunFromRainbow(cancelDirectory,1,()=>control,true)!=2 || (bool)Read(cancelDirectory)["ModeRestored"] || !Read(cancelDirectory).ContainsKey("RestoreError"))
                throw new Exception("Missing USB during cancellation was falsely reported restored");
        }
        string unknownDirectory=Path.Combine(suite,"usb-reconnect-unknown-mode");
        var oldUsb=new Fake { Directory=unknownDirectory,Win32FrameFailureOnce=true };
        var unknownUsb=new Fake { Directory=unknownDirectory,Mode=2 };
        opened=0;
        using(var control=new ReconnectableHidControl(()=>++opened==1 ? oldUsb : unknownUsb,()=>false,null))
        {
            if(RazerVolatileExperiment.RunFromRainbow(unknownDirectory,1,()=>control,true)!=2 || unknownUsb.ModeWrites!=0 || !unknownUsb.Disposed)
                throw new Exception("Unknown reconnect mode was modified");
        }
        Console.WriteLine("{\"usbReconnectAdapterTestsPassed\":3,\"usb\":\"simulated\",\"hardwareAccess\":false}");
    }
}
