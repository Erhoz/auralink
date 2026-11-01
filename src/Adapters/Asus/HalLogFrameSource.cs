using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using AuraLink.Core;

namespace AuraLink.Adapters.Asus
{
    // Temporary IPC using our own HAL log, never colors read from another device.
    // Only new, fresh frames from the selected real LightingService PID qualify.
    public sealed class HalLogFrameSource : IDisposable
    {
        private readonly FileStream stream;
        private readonly int processId;
        private string pending="";
        private readonly Decoder decoder=new UTF8Encoding(false,true).GetDecoder();
        public int AcceptedFrames { get; private set; }
        public HalLogFrameSource(string path,int processId)
        {
            this.processId=processId;
            stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
            stream.Seek(0,SeekOrigin.End); // no replay of an earlier lighting state
        }
        public static RgbColor? Parse(string line,int processId,DateTime now)
        {
            var obj=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);
            if((string)obj["process"]!="LightingService" || Convert.ToInt32(obj["processId"])!=processId || (string)obj["operation"]!="SetEffect") return null;
            DateTime stamp=DateTime.Parse((string)obj["timestampUtc"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
            if(stamp>now.AddSeconds(1) || stamp<now.AddSeconds(-2)) return null;
            var data=(Dictionary<string,object>)obj["data"];
            uint effect=Convert.ToUInt32(data["effectId"]);
            if(effect!=0 && effect!=1 && effect!=255) return null;
            var colors=(IList)data["colors"];
            if(colors.Count!=1) return null; // current native HAL advertises one LED
            uint raw=Convert.ToUInt32(colors[0]);
            // HAL low byte is red (AABBGGRR). Initial AARRGGBB interpretation
            // made Armoury red appear physically blue; keep conversion here.
            return new RgbColor((byte)raw,(byte)(raw>>8),(byte)(raw>>16));
        }
        public RgbColor? Poll()
        {
            if(stream.Length<stream.Position) throw new IOException("HAL log was truncated");
            long available=stream.Length-stream.Position;
            if(available>65536) throw new IOException("HAL log backlog exceeds safety bound");
            var bytes=new byte[(int)available];
            int count=stream.Read(bytes,0,bytes.Length);
            var chars=new char[count+2];
            int decoded=decoder.GetChars(bytes,0,count,chars,0,false);
            pending+=new string(chars,0,decoded);
            if(pending.Length>65536) throw new IOException("HAL log line exceeds safety bound");
            RgbColor? latest=null;
            int newline;
            while((newline=pending.IndexOf('\n'))>=0)
            {
                string line=pending.Substring(0,newline).TrimEnd('\r');
                pending=pending.Substring(newline+1);
                if(line.Length==0) continue;
                RgbColor? color=Parse(line,processId,DateTime.UtcNow);
                if(color.HasValue) { AcceptedFrames++; latest=color; }
            }
            return latest; // coalesce to newest; no delayed effect playback
        }
        public void Dispose() { stream.Dispose(); }
    }
}
