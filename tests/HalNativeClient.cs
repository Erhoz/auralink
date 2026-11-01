using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Xml.Linq;

// No reference to AuraLink.DiagnosticHal.dll. Every call crosses its native COM
// vtable, allowing x86/x64 tests to catch layout and ownership errors.
internal static class HalNativeClient
{
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr value);
    [DllImport("oleaut32.dll")] private static extern void VariantInit(IntPtr value);
    [DllImport("oleaut32.dll")] private static extern int VariantClear(IntPtr value);
    [DllImport("oleaut32.dll")] private static extern int SafeArrayGetElement(IntPtr array, ref int index, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr self, IntPtr buffer, ref uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Capability(IntPtr self, out IntPtr text);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Effect(IntPtr self, uint id, IntPtr colors, uint count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Synchronize(IntPtr self, uint id, ulong tick);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDevices(IntPtr self, ref Guid id, uint count, out IntPtr collection);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Count(IntPtr self, out int count);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Item(IntPtr self, int index, out IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetName(IntPtr self, out IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetInterface(IntPtr self, out IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetUnsigned(IntPtr self, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetMode(IntPtr self, int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetColor(IntPtr self, uint index, uint color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Apply(IntPtr self);
    private static T Method<T>(IntPtr instance, int slot) where T : class
    {
        return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T));
    }
    private static void Check(bool condition, string message) { if(!condition) throw new Exception(message); }
    private static void Hr(int value) { Marshal.ThrowExceptionForHR(value); }
    private static string ReadCapability(IntPtr device)
    {
        IntPtr text; Hr(Method<Capability>(device,3)(device,out text));
        try { return Marshal.PtrToStringBSTR(text); } finally { Marshal.FreeBSTR(text); }
    }
    private static object NativeTest()
    {
        IntPtr hal=IntPtr.Zero,device=IntPtr.Zero,buffer=Marshal.AllocCoTaskMem(IntPtr.Size),variant=Marshal.AllocCoTaskMem(IntPtr.Size==8?24:16),color=Marshal.AllocCoTaskMem(4);
        VariantInit(variant);
        try
        {
            var clsid=new Guid("9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6");var iid=new Guid("F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9");
            Hr(CoCreateInstance(ref clsid,IntPtr.Zero,1,ref iid,out hal));
            var enumerate=Method<Enumerate>(hal,3);uint count=0;
            Hr(enumerate(hal,IntPtr.Zero,ref count));Check(count==1,"Count query did not return one device");
            Marshal.WriteIntPtr(buffer,IntPtr.Zero);count=0;
            Check(enumerate(hal,buffer,ref count)==unchecked((int)0x8000000B),"Small buffer accepted");
            Check(Marshal.ReadIntPtr(buffer)==IntPtr.Zero,"Small buffer was written");
            count=1;Hr(enumerate(hal,buffer,ref count));device=Marshal.ReadIntPtr(buffer);Check(device!=IntPtr.Zero,"Missing device pointer");
            var xml=XDocument.Parse(ReadCapability(device));
            Check(xml.Root.Name=="root" && (int)xml.Root.Element("type")==0x41000,"Unexpected root/type");
            Check((string)xml.Root.Element("device").Element("name")=="AuraLink Diagnostic (no hardware)","Unexpected name");
            Check((int)xml.Root.Element("device").Element("layout").Element("led_count")==1,"Unexpected LED count");
            var effect=Method<Effect>(device,4);Marshal.WriteInt32(color,0x00112233);
            Check(effect(device,0,IntPtr.Zero,1)==unchecked((int)0x80070057),"Null color buffer accepted");
            Check(effect(device,0,color,257)==unchecked((int)0x80070057),"Oversized frame accepted");
            Hr(effect(device,0,color,1));Hr(Method<Synchronize>(device,5)(device,0,0x100000001UL));
            count=0;Hr(Method<Enumerate>(hal,4)(hal,IntPtr.Zero,ref count));Check(count==1,"Enumerate2 count failed");
            count=1;Hr(Method<Enumerate>(hal,4)(hal,variant,ref count));Check(Marshal.ReadInt16(variant)==0x200D,"Expected VT_ARRAY|VT_UNKNOWN");
            IntPtr array=Marshal.ReadIntPtr(variant,8),second;int index=0;
            Hr(SafeArrayGetElement(array,ref index,out second));
            try { Check(ReadCapability(second)==ReadCapability(device),"Variant device mismatch"); } finally {Marshal.Release(second);}
            return new { passed=true, processBits=IntPtr.Size*8, nativeVtable=true, registryActivation=true, syntheticEffect=true, hardwareOutput=false, nativeIntegrationProven=false };
        }
        finally
        {
            VariantClear(variant);Marshal.FreeCoTaskMem(variant);Marshal.FreeCoTaskMem(buffer);Marshal.FreeCoTaskMem(color);
            if(device!=IntPtr.Zero)Marshal.Release(device);if(hal!=IntPtr.Zero)Marshal.Release(hal);
        }
    }
    private static object SdkTest()
    {
        IntPtr development=IntPtr.Zero,collection=IntPtr.Zero,device=IntPtr.Zero,lights=IntPtr.Zero,effects=IntPtr.Zero;
        try
        {
            Console.Error.WriteLine("SDK: activating AuraDevelopement; no SwitchMode");
            var clsid=new Guid("34B707DC-1133-4EBC-B380-21387A50A89D");var iid=new Guid("9FCF439B-F9ED-4F93-B775-13305BE23E55");
            Hr(CoCreateInstance(ref clsid,IntPtr.Zero,1,ref iid,out development));
            Console.Error.WriteLine("SDK: enumerating only AuraLink CLSID");
            var ours=new Guid("9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6");
            Hr(Method<GetDevices>(development,9)(development,ref ours,1,out collection));
            int count;Hr(Method<Count>(collection,8)(collection,out count));Check(count==1,"ASUS SDK did not parse exactly one diagnostic device");
            Hr(Method<Item>(collection,9)(collection,0,out device));IntPtr name;
            Hr(Method<GetName>(device,9)(device,out name));string text;
            try{text=Marshal.PtrToStringBSTR(name);}finally{Marshal.FreeBSTR(name);}
            Check(text=="AuraLink Diagnostic (no hardware)","SDK device name mismatch");
            uint type,width,height;
            Hr(Method<GetUnsigned>(device,8)(device,out type));Console.Error.WriteLine("SDK actual type: 0x"+type.ToString("X"));Check(type==0x41000,"SDK type mismatch");
            Hr(Method<GetUnsigned>(device,10)(device,out width));Hr(Method<GetUnsigned>(device,11)(device,out height));
            Check(width==1 && height==1,"SDK layout mismatch");
            Hr(Method<GetInterface>(device,7)(device,out lights));int ledCount;
            Hr(Method<Count>(lights,8)(lights,out ledCount));Check(ledCount==1,"SDK LED count mismatch");
            Hr(Method<GetInterface>(device,17)(device,out effects));int effectCount;
            Hr(Method<Count>(effects,8)(effects,out effectCount));Check(effectCount==2,"SDK effect count mismatch");
            int[] effectIds=new int[effectCount];
            for(int i=0;i<effectCount;i++)
            {
                IntPtr entry;Hr(Method<Item>(effects,9)(effects,i,out entry));
                try{Hr(Method<Count>(entry,7)(entry,out effectIds[i]));Check(effectIds[i]==i,"SDK effect ID mismatch");int sync;Hr(Method<Count>(entry,9)(entry,out sync));Check(sync==1,"Effect is not advertised as synchronizable");}
                finally{Marshal.Release(entry);}
            }
            // Synthetic effect through ASUS' device wrapper, targeting the sole
            // diagnostic device. No global SwitchMode or physical provider call.
            Hr(Method<SetMode>(device,13)(device,0));
            Hr(Method<SetColor>(device,14)(device,0,0x00112233));
            Hr(Method<Apply>(device,12)(device));
            return new { metadataPassed=true,processBits=IntPtr.Size*8, sdkParsedDevice=text, type=type, ledCount=ledCount, effectCount=effectCount,effectIds=effectIds,allEffectsSynchronizable=true, width=width,height=height,applyCallReturnedSuccess=true,effectDeliveryVerified=false,targetOnly=true, acquiredControl=false, armouryUiValidated=false, nativeIntegrationProven=false };
        }
        finally{if(effects!=IntPtr.Zero)Marshal.Release(effects);if(lights!=IntPtr.Zero)Marshal.Release(lights);if(device!=IntPtr.Zero)Marshal.Release(device);if(collection!=IntPtr.Zero)Marshal.Release(collection);if(development!=IntPtr.Zero)Marshal.Release(development);}
    }
    private static object DiscoverHal(bool create)
    {
        IntPtr development=IntPtr.Zero,collection=IntPtr.Zero;
        try
        {
            var clsid=new Guid("34B707DC-1133-4EBC-B380-21387A50A89D");var iid=new Guid("9FCF439B-F9ED-4F93-B775-13305BE23E55");
            Hr(CoCreateInstance(ref clsid,IntPtr.Zero,1,ref iid,out development));
            // Read HAL registrations only; never CreateHal or enumerate devices.
            Hr(Method<GetInterface>(development,8)(development,out collection));
            int count;Hr(Method<Count>(collection,8)(collection,out count));Check(count>=0 && count<1024,"Unexpected HAL count");
            string[] ids=new string[count];bool found=false;int? createResult=null;bool created=false;uint? plugin=null;
            for(int i=0;i<count;i++)
            {
                IntPtr entry;Hr(Method<Item>(collection,9)(collection,i,out entry));
                try
                {
                    IntPtr name;Hr(Method<GetName>(entry,7)(entry,out name));
                    try{ids[i]=Marshal.PtrToStringBSTR(name);}finally{Marshal.FreeBSTR(name);}
                    Guid id;if(Guid.TryParse(ids[i],out id) && id==new Guid("9FE19BA9-9EF2-4BF2-96AC-20983C2EB1A6"))
                    {
                        found=true;uint value;Hr(Method<GetUnsigned>(entry,8)(entry,out value));plugin=value;
                        if(create)
                        {
                            IntPtr hal=IntPtr.Zero;
                            try{createResult=Method<GetInterface>(entry,9)(entry,out hal);created=hal!=IntPtr.Zero;}
                            finally{if(hal!=IntPtr.Zero)Marshal.Release(hal);}
                        }
                    }
                }finally{Marshal.Release(entry);}
            }
            return new {processBits=IntPtr.Size*8,halCount=count,auraLinkDiscovered=found,halIds=ids,plugin=plugin,factoryAttempted=create,factoryHResult=createResult,halCreated=created,deviceEnumerationAttempted=false,acquiredControl=false};
        }
        finally{if(collection!=IntPtr.Zero)Marshal.Release(collection);if(development!=IntPtr.Zero)Marshal.Release(development);}
    }
    private static object ReadService()
    {
        IntPtr service=IntPtr.Zero;
        try
        {
            var clsid=new Guid("95775DC4-77AA-4E94-8CF6-68267EEF1856");var iid=new Guid("76C92A6B-B131-4DF9-9D24-CCD6014EFB5F");
            Hr(CoCreateInstance(ref clsid,IntPtr.Zero,4,ref iid,out service));
            string[] values=new string[3];int[] slots={9,10,26};
            for(int i=0;i<slots.Length;i++)
            {
                IntPtr text;Hr(Method<GetName>(service,slots[i])(service,out text));
                try{values[i]=text==IntPtr.Zero?null:Marshal.PtrToStringBSTR(text);}finally{if(text!=IntPtr.Zero)Marshal.FreeBSTR(text);}
            }
            return new{processBits=IntPtr.Size*8,statusXml=values[0],capabilityXml=values[1],devicesXml=values[2],controlMethodsCalled=false};
        }
        finally{if(service!=IntPtr.Zero)Marshal.Release(service);}
    }
    [MTAThread] private static int Main(string[] args)
    {
        int initialized=CoInitializeEx(IntPtr.Zero,0);
        try
        {
            Hr(initialized);
            object result=args.Length==1 && args[0]=="service" ? ReadService() : args.Length==1 && (args[0]=="discover" || args[0]=="factory") ? DiscoverHal(args[0]=="factory") : args.Length==1 && args[0]=="sdk" ? SdkTest() : NativeTest();
            Console.WriteLine(new JavaScriptSerializer().Serialize(result));return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 2;}
        finally{if(initialized>=0)CoUninitialize();}
    }
}
