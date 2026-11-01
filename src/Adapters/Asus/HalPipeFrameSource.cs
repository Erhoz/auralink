using System;
using AuraLink.Core;

namespace AuraLink.Adapters.Asus
{
    public sealed class HalPipeFrameSource : IDisposable
    {
        private readonly FramePipeClient client;
        public int Polls { get { return client.Polls; } }
        public int NewFrames { get { return client.NewFrames; } }
        public HalPipeFrameSource(int servicePid)
        { client=new FramePipeClient(FramePipePublisher.NameForProcess(servicePid),servicePid,1000); }
        public RgbColor? Poll()
        {
            NativeFrame frame=client.Poll();
            if(frame!=null && frame.EffectId!=0 && frame.EffectId!=1 && frame.EffectId!=255) return null;
            return frame==null ? (RgbColor?)null : DecodeColor(frame.Color);
        }
        public static RgbColor DecodeColor(uint color)
        { return new RgbColor((byte)color,(byte)(color>>8),(byte)(color>>16)); }
        public void Dispose() { client.Dispose(); }
    }
}
