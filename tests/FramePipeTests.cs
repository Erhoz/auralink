using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using AuraLink.Core;
using AuraLink.Adapters.Asus;

internal static class FramePipeTests
{
    private static string phase="start";
    private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void MustFail(Action action,string message)
    { bool failed=false; try { action(); } catch(Exception) { failed=true; } Require(failed,message); }
    private static int Main()
    {
        try
        {
            int pid=Process.GetCurrentProcess().Id;
            string name="AuraLink.Tests."+Guid.NewGuid().ToString("N");
            long bytesBefore=GC.GetTotalMemory(true);
            var timer=Stopwatch.StartNew();
            using(var publisher=new FramePipePublisher(name))
            {
                phase="first connection";
                using(var client=new FramePipeClient(name,pid,1000))
                {
                    Require(client.Poll()==null,"An empty publisher emitted a frame");
                    Require(publisher.Publish(0,0xff0000ff),"Primary frame not published");
                    NativeFrame red=client.Poll();
                    Require(red!=null && HalPipeFrameSource.DecodeColor(red.Color).ToString()=="FF0000","Red conversion failed");
                    for(int i=0;i<100;i++) Require(client.Poll()==null,"Static effect was retransmitted");
                    for(int i=0;i<50000;i++) publisher.Publish(0,(uint)i);
                    NativeFrame latest=client.Poll();
                    Require(latest.Sequence==50001 && latest.Color==49999,"A burst did not coalesce to its newest frame");
                    Require(publisher.Publish(123,0),"Core transport rejected an opaque vendor effect");
                    Require(client.Poll().EffectId==123,"Core transport changed the vendor effect");
                }
                phase="worker reconnect";
                using(var reconnected=new FramePipeClient(name,pid,1000))
                    Require(reconnected.Poll().Sequence==50002,"Worker reconnect lost static state");
                phase="PID refusal";
                bool refused=false;
                try { using(var wrong=new FramePipeClient(name,pid+1,1000)){} }
                catch(IOException error) { if(error.Message.IndexOf("identity mismatch",StringComparison.Ordinal)<0) throw; refused=true; }
                Require(refused,"Wrong publisher PID accepted");
                phase="publisher disconnect";
                using(var disconnected=new FramePipeClient(name,pid,1000))
                {
                    publisher.Dispose();
                    MustFail(()=>disconnected.Poll(),"Publisher disconnect was ignored");
                }
            }
            byte[] invalid=new NativeFrame(1,0,0,DateTime.UtcNow.Ticks).Encode(); invalid[0]^=1;
            MustFail(()=>NativeFrame.Decode(invalid),"Corrupt protocol header accepted");
            MustFail(()=>NativeFrame.Decode(new byte[31]),"Truncated packet accepted");
            phase="silent server timeout";
            using(var silent=new NamedPipeServerStream(name+".silent",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
            {
                var accept=silent.BeginWaitForConnection(null,null);
                using(var client=new FramePipeClient(name+".silent",pid,250))
                {
                    silent.EndWaitForConnection(accept);
                    var timeout=Stopwatch.StartNew();
                    MustFail(()=>client.Poll(),"Silent server did not time out");
                    Require(timeout.ElapsedMilliseconds<2000,"Channel timeout exceeded its bound");
                }
            }
            Console.WriteLine("{\"passed\":true,\"processBits\":"+(IntPtr.Size*8)+",\"tests\":8,\"burstFrames\":50000,\"elapsedMs\":"+timer.ElapsedMilliseconds+",\"retainedManagedBytesDelta\":"+(GC.GetTotalMemory(true)-bytesBefore)+",\"hardwareAccess\":false}");
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(phase+": "+error); return 2; }
    }
}
