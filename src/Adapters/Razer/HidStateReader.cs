using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace AuraLink.Adapters.Razer
{
    public static class HidStateReader
    {
        public static object Read()
        {
            var candidates=HidMetadata.ReadBaseStation().FindAll(c => c.Opened && c.Error==null &&
                c.FeatureBytes==91 && c.UsagePage==1 && c.Usage==2 &&
                c.Path.IndexOf("&mi_00#",StringComparison.OrdinalIgnoreCase)>=0);
            if(candidates.Count!=1) throw new InvalidOperationException("Expected exactly one 0F08 MI_00 mouse collection with 91-byte features");
            var queries=new List<object>();
            byte[] mode=null, firmware=null, brightness=null;
            using(var handle=CreateFile(candidates[0].Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero))
            {
                if(handle.IsInvalid) throw new Win32Exception();
                foreach(string name in new [] {"mode","firmware","brightness","brightness-extended"})
                {
                    byte[] request=BaseStationProtocol.Query(name);
                    if(!HidD_SetFeature(handle,request,request.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"SetFeature query failed: "+name);
                    Thread.Sleep(2);
                    byte[] response=new byte[91];
                    if(!HidD_GetFeature(handle,response,response.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"GetFeature response failed: "+name);
                    try { BaseStationProtocol.ValidateEnvelope(request,response); }
                    catch(Exception error)
                    {
                        error.Data["RequestHex"]=BitConverter.ToString(request);
                        error.Data["ResponseHex"]=BitConverter.ToString(response);
                        error.Data["CompletedQueries"]=queries;
                        throw;
                    }
                    // A valid negative device reply proves transport only. It
                    // never supplies a usable state value or guessed brightness.
                    if(response[1]==5 || response[1]==3)
                    {
                        queries.Add(new { name=name, outcome=response[1]==5 ? "not-supported" : "device-failed", status=response[1], requestHex=BitConverter.ToString(request), responseHex=BitConverter.ToString(response) });
                        continue;
                    }
                    BaseStationProtocol.Validate(request,response);
                    var payload=new byte[response[6]];
                    Array.Copy(response,9,payload,0,payload.Length);
                    if(name=="mode") mode=payload;
                    if(name=="firmware") firmware=payload;
                    if(name.StartsWith("brightness")) brightness=payload;
                    queries.Add(new { name=name, outcome="read", status=response[1], arguments=payload, requestHex=BitConverter.ToString(request), responseHex=BitConverter.ToString(response) });
                }
            }
            return new { logicalReadQueries=4, transportValidated=true, snapshotComplete=mode!=null && firmware!=null && brightness!=null,
                mode=mode, firmware=firmware, brightness=brightness, lightingCommandsSent=0, hardwareOutput=false, queries=queries };
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] buffer,int length);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] buffer,int length);
    }
}
