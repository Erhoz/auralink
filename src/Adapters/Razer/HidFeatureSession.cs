using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace AuraLink.Adapters.Razer
{
    public interface IHidControl : IDisposable
    {
        byte[] Exchange(byte[] request,bool writeAck);
    }
    public sealed class HidFeatureSession : IHidControl
    {
        private readonly SafeFileHandle handle;
        public HidFeatureSession()
        {
            var candidates=HidMetadata.ReadBaseStation().FindAll(c=>c.Opened && c.Error==null &&
                c.FeatureBytes==91 && c.UsagePage==1 && c.Usage==2 &&
                c.Path.IndexOf("&mi_00#",StringComparison.OrdinalIgnoreCase)>=0);
            if(candidates.Count!=1) throw new InvalidOperationException("Expected one 0F08 MI_00 91-byte feature collection");
            handle=CreateFile(candidates[0].Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle.IsInvalid) { int error=Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        }
        public byte[] Exchange(byte[] request,bool writeAck)
        {
            if(request==null || request.Length!=91 || request[0]!=0) throw new ArgumentException("Invalid report");
            if(!HidD_SetFeature(handle,request,request.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"HID request failed");
            Thread.Sleep(2);
            var response=new byte[91];
            if(!HidD_GetFeature(handle,response,response.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(),"HID response failed");
            try
            {
                BaseStationProtocol.ValidateEnvelope(request,response,writeAck ? 0 : request[6]);
                if(response[1]!=2) throw new InvalidOperationException("Device rejected command; status="+response[1]);
            }
            catch(Exception error)
            {
                error.Data["RequestHex"]=BitConverter.ToString(request);
                error.Data["ResponseHex"]=BitConverter.ToString(response);
                throw;
            }
            return response;
        }
        public void Dispose() { handle.Dispose(); }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] buffer,int length);
        [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle handle,[In,Out] byte[] buffer,int length);
    }
}
