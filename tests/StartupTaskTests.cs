using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

public sealed class MissingTaskFolder
{
    public Exception Error;
    public object GetTask(string name) { throw Error; }
}

internal static class StartupTaskTests
{
    private static int Main()
    {
        var find=typeof(StartupTask).GetMethod("Find",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(Exception error in new Exception[]{new FileNotFoundException(),new COMException("Missing",unchecked((int)0x80070002))})
            if(find.Invoke(null,new object[]{new MissingTaskFolder{Error=error}})!=null)
                throw new Exception("Missing task must return null.");
        var denied=new COMException("Access denied",unchecked((int)0x80070005));
        try { find.Invoke(null,new object[]{new MissingTaskFolder{Error=denied}});throw new Exception("Access denied was swallowed."); }
        catch(TargetInvocationException error) { if(!Object.ReferenceEquals(error.InnerException,denied))throw; }
        Console.WriteLine("PASS: missing task mappings accepted; access denied preserved. No task registered or started.");
        return 0;
    }
}
