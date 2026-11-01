using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AuraLink.Core
{
    // Versioned local IPC. No vendor SDK, lighting device or filesystem dependency.
    public sealed class NativeFrame
    {
        public readonly long Sequence, TimestampTicks;
        public readonly uint EffectId, Color;
        public NativeFrame(long sequence,uint effectId,uint color,long timestampTicks)
        { Sequence=sequence; EffectId=effectId; Color=color; TimestampTicks=timestampTicks; }
        public byte[] Encode()
        {
            var bytes=new byte[32];
            using(var writer=new BinaryWriter(new MemoryStream(bytes)))
            { writer.Write(0x414c4e4bU); writer.Write(1U); writer.Write(Sequence); writer.Write(Color); writer.Write(EffectId); writer.Write(TimestampTicks); }
            return bytes;
        }
        public static NativeFrame Decode(byte[] bytes)
        {
            if(bytes==null || bytes.Length!=32) throw new InvalidDataException("Invalid frame length");
            using(var reader=new BinaryReader(new MemoryStream(bytes)))
            {
                if(reader.ReadUInt32()!=0x414c4e4bU || reader.ReadUInt32()!=1) throw new InvalidDataException("Unsupported frame protocol");
                long sequence=reader.ReadInt64(); uint color=reader.ReadUInt32(),effect=reader.ReadUInt32(); long ticks=reader.ReadInt64();
                if(sequence<0 || ticks<0 || ticks>DateTime.MaxValue.Ticks) throw new InvalidDataException("Invalid frame fields");
                return new NativeFrame(sequence,effect,color,ticks);
            }
        }
    }

    public sealed class FramePipePublisher : IDisposable
    {
        private NamedPipeServerStream pipe;
        private readonly string name;
        private readonly PipeSecurity security;
        private readonly object lifetimeGate=new object();
        private readonly Thread thread;
        private readonly object publishGate=new object();
        private NativeFrame latest=new NativeFrame(0,0,0,0);
        private volatile bool stopped;
        public static string NameForProcess(int pid) { return "AuraLink.Frames.v1."+pid; }
        public FramePipePublisher(string name)
        {
            this.name=name;
            security=new PipeSecurity();
            security.SetAccessRuleProtection(true,false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
            foreach(var sid in new[]{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),WindowsIdentity.GetCurrent().User})
                security.AddAccessRule(new PipeAccessRule(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
            pipe=CreatePipe();
            thread=new Thread(Serve); thread.IsBackground=true; thread.Name="AuraLink frame channel"; thread.Start();
        }
        private NamedPipeServerStream CreatePipe()
        { return new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,1024,1024,security); }
        public bool Publish(uint effectId,uint color)
        {
            if(stopped || !Monitor.TryEnter(publishGate)) return false;
            try
            {
                var before=Interlocked.CompareExchange(ref latest,null,null);
                Interlocked.Exchange(ref latest,new NativeFrame(before.Sequence+1,effectId,color,DateTime.UtcNow.Ticks));
                return true;
            }
            finally { Monitor.Exit(publishGate); }
        }
        private void Serve()
        {
            while(!stopped)
            {
                var active=pipe;
                try
                {
                    active.WaitForConnection();
                    while(!stopped)
                    {
                        int request=active.ReadByte();
                        if(request<0) break;
                        if(request!=1) throw new InvalidDataException("Invalid frame request");
                        byte[] bytes=Interlocked.CompareExchange(ref latest,null,null).Encode();
                        active.Write(bytes,0,bytes.Length);
                    }
                }
                catch(IOException) { }
                catch(ObjectDisposedException) { }
                catch(Exception) { stopped=true; } // never propagate on a service thread
                finally
                {
                    lock(lifetimeGate)
                    {
                        active.Dispose();
                        // A client can close before WaitForConnection finishes. That
                        // instance is broken even when IsConnected was never set.
                        if(!stopped) { try { pipe=CreatePipe(); } catch(Exception) { stopped=true; } }
                    }
                }
            }
        }
        public void Dispose()
        {
            stopped=true;
            lock(lifetimeGate) { pipe.Dispose(); }
            if(Thread.CurrentThread!=thread) thread.Join(1000);
        }
    }

    public sealed class FramePipeClient : IDisposable
    {
        private readonly NamedPipeClientStream pipe;
        private readonly int timeoutMilliseconds;
        private long lastSequence;
        public int Polls { get; private set; }
        public int NewFrames { get; private set; }
        public FramePipeClient(string name,int expectedServerPid,int timeoutMilliseconds)
        {
            if(expectedServerPid<=0 || timeoutMilliseconds<1 || timeoutMilliseconds>5000) throw new ArgumentOutOfRangeException();
            this.timeoutMilliseconds=timeoutMilliseconds;
            pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Anonymous);
            try
            {
                pipe.Connect(timeoutMilliseconds);
                uint actual;
                if(!GetNamedPipeServerProcessId(pipe.SafePipeHandle,out actual) || actual!=(uint)expectedServerPid)
                    throw new IOException("Frame server process identity mismatch");
            }
            catch { pipe.Dispose(); throw; }
        }
        private void Finish(Task operation,int remaining)
        {
            if(remaining<=0 || !operation.Wait(remaining)) { pipe.Dispose(); throw new TimeoutException("Frame channel stopped responding"); }
            operation.GetAwaiter().GetResult();
        }
        public NativeFrame Poll()
        {
            var timer=Stopwatch.StartNew();
            Finish(pipe.WriteAsync(new byte[]{1},0,1),timeoutMilliseconds);
            var bytes=new byte[32]; int received=0;
            while(received<bytes.Length)
            {
                var read=pipe.ReadAsync(bytes,received,bytes.Length-received);
                Finish(read,timeoutMilliseconds-(int)timer.ElapsedMilliseconds);
                if(read.Result==0) throw new EndOfStreamException("Frame publisher disconnected");
                received+=read.Result;
            }
            Polls++;
            NativeFrame frame=NativeFrame.Decode(bytes);
            if(frame.Sequence<lastSequence) throw new InvalidDataException("Frame sequence moved backwards");
            if(frame.Sequence==lastSequence) return null; // healthy static effect, no duplicate writes
            lastSequence=frame.Sequence; NewFrames++; return frame;
        }
        public void Dispose() { pipe.Dispose(); }
        [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
    }
}
