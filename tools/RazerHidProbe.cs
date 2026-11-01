using System;
using System.Web.Script.Serialization;
using AuraLink.Adapters.Razer;

internal static class RazerHidProbe
{
    private static int Main(string[] args)
    {
        var json = new JavaScriptSerializer();
        try
        {
            BaseStationProtocol.SelfTest();
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
