using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AuraLink.Adapters.Razer
{
    // Explicit hotplug experiment only. Reopens this model, never switches a
    // factory/unknown mode, never retries indefinitely or writes stored presets.
    public sealed class ReconnectableHidControl : IHidControl
    {
        private readonly Func<IHidControl> factory;
        private readonly Func<bool> canceled;
        private readonly Action<string> notify;
        private readonly List<object> events=new List<object>();
        private readonly Stopwatch health=Stopwatch.StartNew();
        private long lastHealth;
        private IHidControl inner;
        private bool armed,restoring;
        public int Reconnections { get; private set; }
        public ReconnectableHidControl(Func<IHidControl> factory,Func<bool> canceled,Action<string> notify)
        { this.factory=factory; this.canceled=canceled; this.notify=notify; inner=factory(); }
        private void Note(string state)
        {
            events.Add(new { state=state,utc=DateTime.UtcNow.ToString("o") });
            if(notify!=null) notify(state);
        }
        public object Snapshot() { return new { Reconnections, Events=events.ToArray() }; }
        public void CheckConnection()
        {
            if(armed && health.ElapsedMilliseconds-lastHealth>=1000)
            {
                lastHealth=health.ElapsedMilliseconds;
                byte[] mode=Exchange(BaseStationProtocol.Query("mode"),false);
                if(mode[9]!=3 || mode[10]!=0) throw new IOException("Software mode lost during USB test");
            }
        }
        public byte[] Exchange(byte[] request,bool writeAck)
        {
            if(request[7]==0 && request[8]==4 && request[9]==0) { restoring=true; armed=false; }
            if(inner==null) throw new IOException("USB unavailable; reconnect did not complete");
            byte[] response;
            try { response=inner.Exchange(request,writeAck); }
            catch(Win32Exception)
            {
                if(!armed || restoring) throw;
                Reconnect();
                response=inner.Exchange(request,writeAck);
            }
            if(request[7]==0 && request[8]==4 && request[9]==3) armed=true;
            return response;
        }
        private void Reconnect()
        {
            inner.Dispose(); inner=null;
            Note("disconnected");
            var wait=Stopwatch.StartNew();
            Exception last=null;
            while(wait.Elapsed.TotalSeconds<60)
            {
                if(canceled()) throw new OperationCanceledException("USB reconnection canceled by stop file");
                IHidControl candidate=null;
                try { candidate=factory(); }
                catch(Win32Exception error) { last=error; }
                catch(InvalidOperationException error) { last=error; } // no unique target collection yet
                if(candidate!=null)
                {
                    try
                    {
                        byte[] before=candidate.Exchange(BaseStationProtocol.Query("mode"),false);
                        if((before[9]!=0 && before[9]!=3) || before[10]!=0) throw new IOException("Reconnected USB mode is not normal/software");
                        candidate.Exchange(VolatileFrameProtocol.Mode(3),true);
                        byte[] mode=candidate.Exchange(BaseStationProtocol.Query("mode"),false);
                        if(mode[9]!=3 || mode[10]!=0) throw new IOException("Reconnected USB software mode was not confirmed");
                        candidate.Exchange(VolatileFrameProtocol.SelectCustom(),true);
                        inner=candidate; candidate=null;
                        Reconnections++;
                        Note("reconnected");
                        return;
                    }
                    catch(Win32Exception error) { last=error; }
                    catch(InvalidOperationException error) { last=error; } // device may still be initializing
                    finally { if(candidate!=null) candidate.Dispose(); }
                }
                Thread.Sleep(250);
            }
            Note("reconnect-timeout");
            throw new IOException("USB was not recovered within 60 seconds",last);
        }
        public void Dispose() { if(inner!=null) { inner.Dispose(); inner=null; } }
    }
}
