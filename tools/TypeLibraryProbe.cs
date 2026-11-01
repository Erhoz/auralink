using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

// REGKIND_NONE reads metadata without registering or activating COM classes.
public static class TypeLibraryProbe
{
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void LoadTypeLibEx(string path, int regkind, out ITypeLib library);
    public static string[] Inspect(string path)
    {
        ITypeLib library;
        LoadTypeLibEx(path, 2, out library);
        var lines = new List<string>();
        try
        {
            for (int i = 0; i < library.GetTypeInfoCount(); i++)
            {
                ITypeInfo info;
                library.GetTypeInfo(i, out info);
                IntPtr attr = IntPtr.Zero;
                try
                {
                    info.GetTypeAttr(out attr);
                    var type = (TYPEATTR)Marshal.PtrToStructure(attr, typeof(TYPEATTR));
                    string name, doc, help; int context;
                    info.GetDocumentation(-1, out name, out doc, out context, out help);
                    lines.Add(type.typekind + " " + name + " " + type.guid + " flags=" + type.wTypeFlags);
                    for (int b = 0; b < type.cImplTypes; b++)
                    {
                        int href; ITypeInfo parent;
                        info.GetRefTypeOfImplType(b, out href);
                        info.GetRefTypeInfo(href, out parent);
                        try
                        {
                            parent.GetDocumentation(-1, out name, out doc, out context, out help);
                            lines.Add("  implements " + name);
                        }
                        finally { Marshal.ReleaseComObject(parent); }
                    }
                    for (int j = 0; j < type.cFuncs; j++)
                    {
                        IntPtr ptr;
                        info.GetFuncDesc(j, out ptr);
                        try
                        {
                            var fn = (FUNCDESC)Marshal.PtrToStructure(ptr, typeof(FUNCDESC));
                            var names = new string[fn.cParams + 1]; int count;
                            info.GetNames(fn.memid, names, names.Length, out count);
                            var args = new List<string>();
                            int size = Marshal.SizeOf(typeof(ELEMDESC));
                            for (int p = 0; p < fn.cParams; p++)
                            {
                                var el = (ELEMDESC)Marshal.PtrToStructure(IntPtr.Add(fn.lprgelemdescParam, p * size), typeof(ELEMDESC));
                                args.Add(el.desc.paramdesc.wParamFlags + " " + Describe(info, el.tdesc) + " " + names[p + 1]);
                            }
                            lines.Add("  slot=" + fn.oVft + " " + fn.invkind + " " + Describe(info, fn.elemdescFunc.tdesc) + " " + names[0] + "(" + string.Join(", ", args.ToArray()) + ")");
                        }
                        finally { info.ReleaseFuncDesc(ptr); }
                    }
                }
                finally
                {
                    if (attr != IntPtr.Zero) info.ReleaseTypeAttr(attr);
                    Marshal.ReleaseComObject(info);
                }
            }
            return lines.ToArray();
        }
        finally { Marshal.ReleaseComObject(library); }
    }
    private static string Describe(ITypeInfo info, TYPEDESC desc)
    {
        var vt = (VarEnum)desc.vt;
        if (vt == VarEnum.VT_PTR || vt == VarEnum.VT_SAFEARRAY)
            return Describe(info, (TYPEDESC)Marshal.PtrToStructure(desc.lpValue, typeof(TYPEDESC))) + (vt == VarEnum.VT_PTR ? "*" : "[]");
        if (vt == VarEnum.VT_USERDEFINED)
        {
            ITypeInfo referenced;
            info.GetRefTypeInfo(unchecked((int)desc.lpValue.ToInt64()), out referenced);
            try
            {
                string name, doc, help; int context;
                referenced.GetDocumentation(-1, out name, out doc, out context, out help);
                return name;
            }
            finally { Marshal.ReleaseComObject(referenced); }
        }
        return vt.ToString();
    }
}
