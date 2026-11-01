using System;

namespace AuraLink.Adapters.Razer
{
    // Original packet encoder, restricted to named logical read commands.
    // Protocol facts: OpenRazer 477f4d6d, razercommon/razerchromacommon/accessory.
    // Windows prepends report ID 0 to the 90-byte Razer control report.
    public static class BaseStationProtocol
    {
        public static byte[] Query(string name)
        {
            var report = new byte[91];
            report[2] = 0xff;
            switch (name)
            {
                case "mode": report[6]=2; report[7]=0; report[8]=0x84; break;
                case "firmware": report[6]=2; report[7]=0; report[8]=0x81; break;
                case "brightness": report[6]=3; report[7]=3; report[8]=0x83; report[9]=1; report[10]=5; break;
                case "brightness-extended": report[2]=0x3f; report[6]=3; report[7]=0x0f; report[8]=0x84; report[9]=1; break;
                default: throw new ArgumentException("Only mode, firmware and brightness queries are implemented");
            }
            report[89] = Checksum(report);
            return report;
        }
        private static byte Checksum(byte[] report)
        {
            byte crc = 0;
            for (int i=3; i<89; i++) crc ^= report[i];
            return crc;
        }
        public static void ValidateEnvelope(byte[] request, byte[] response)
        {
            ValidateEnvelope(request,response,request[6]);
        }
        public static void ValidateEnvelope(byte[] request, byte[] response, int minimumPayload)
        {
            if(request.Length!=91 || response.Length!=91 || response[0]!=0) throw new InvalidOperationException("Invalid HID report size/ID");
            if(response[3]!=request[3] || response[4]!=request[4] || response[7]!=request[7] || response[8]!=request[8])
                throw new InvalidOperationException("Response does not match query");
            if(response[6]>80 || response[6]<minimumPayload) throw new InvalidOperationException("Invalid response payload length");
            if(response[89]!=Checksum(response)) throw new InvalidOperationException("Response checksum mismatch");
        }
        public static void Validate(byte[] request, byte[] response)
        {
            ValidateEnvelope(request,response);
            if(response[1]!=2) throw new InvalidOperationException("Device response status="+response[1]);
        }
        public static void SelfTest()
        {
            if(Query("mode")[89]!=0x86 || Query("firmware")[89]!=0x83 || Query("brightness")[89]!=0x87 || Query("brightness-extended")[89]!=0x89)
                throw new Exception("Known query checksum mismatch");
            byte[] request=Query("mode"), response=(byte[])request.Clone();
            response[1]=2;
            Validate(request,response);
            response[8]=0x81;
            try { Validate(request,response); throw new Exception("Mismatched command accepted"); }
            catch(InvalidOperationException) { }
            response=(byte[])request.Clone(); response[1]=2; response[9]=3;
            try { Validate(request,response); throw new Exception("Corrupt response accepted"); }
            catch(InvalidOperationException) { }
            response=(byte[])request.Clone(); response[1]=5;
            ValidateEnvelope(request,response);
            try { Validate(request,response); throw new Exception("Negative device status accepted as state"); }
            catch(InvalidOperationException) { }
            try { Query("static"); throw new Exception("Unimplemented lighting write accepted"); }
            catch(ArgumentException) { }
        }
    }
}
