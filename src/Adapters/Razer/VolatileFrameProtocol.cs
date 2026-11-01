using System;

namespace AuraLink.Adapters.Razer
{
    // Model-specific volatile operations. No stored preset/brightness commands.
    public static class VolatileFrameProtocol
    {
        private static byte[] Packet(byte transaction,byte size,byte commandClass,byte command)
        {
            var bytes=new byte[91];
            bytes[2]=transaction; bytes[6]=size; bytes[7]=commandClass; bytes[8]=command;
            return bytes;
        }
        private static byte[] Finish(byte[] bytes)
        {
            byte checksum=0;
            for(int i=3;i<89;i++) checksum^=bytes[i];
            bytes[89]=checksum;
            return bytes;
        }
        public static byte[] Mode(byte mode)
        {
            if(mode!=0 && mode!=3) throw new ArgumentException("Only normal and software mode are permitted");
            var bytes=Packet(0xff,2,0,4); bytes[9]=mode;
            return Finish(bytes);
        }
        public static byte[] SelectCustom()
        {
            var bytes=Packet(0x3f,0x0c,0x0f,2);
            bytes[9]=0; // NOSTORE, not a hardware preset
            bytes[11]=8; // custom frame
            return Finish(bytes);
        }
        public static byte[] SolidFrame(byte red,byte green,byte blue)
        {
            var bytes=Packet(0x3f,0x47,0x0f,3);
            bytes[13]=14; // row 0, inclusive LED range 0..14
            for(int led=0;led<15;led++)
            {
                bytes[14+led*3]=red; bytes[15+led*3]=green; bytes[16+led*3]=blue;
            }
            return Finish(bytes);
        }
        public static byte[] Rainbow()
        {
            var bytes=Packet(0x3f,6,0x0f,2);
            bytes[9]=0; // volatile, no preset storage
            bytes[11]=4; bytes[12]=1; bytes[13]=0x28; // rainbow wave, direction/speed
            return Finish(bytes);
        }
        public static void SelfTest()
        {
            if(Mode(3)[89]!=5 || Mode(0)[89]!=6 || SelectCustom()[89]!=9 || SolidFrame(255,0,0)[89]!=0xba || Rainbow()[89]!=0x26)
                throw new Exception("Volatile packet golden checksum mismatch");
            var frame=SolidFrame(0x12,0x34,0x56);
            if(frame[13]!=14 || frame[14]!=0x12 || frame[15]!=0x34 || frame[16]!=0x56 || frame[58]!=0x56 || frame[59]!=0)
                throw new Exception("15 LED frame bounds/channel order mismatch");
            if(SelectCustom()[9]!=0 || SelectCustom()[10]!=0 || SelectCustom()[11]!=8)
                throw new Exception("Custom effect must use volatile storage");
            try { Mode(2); throw new Exception("Factory mode accepted"); } catch(ArgumentException) { }
        }
    }
}
