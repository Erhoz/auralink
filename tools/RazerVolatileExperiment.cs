using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using AuraLink.Adapters.Razer;
using AuraLink.Core;

internal static class RazerVolatileExperiment
{
    internal static int RunFromRainbow(string directory,int seconds)
    {
        return RunFromRainbow(directory,seconds,()=>new HidFeatureSession(),false);
    }
    internal static int RunFromRainbow(string directory,int seconds,Func<IHidControl> factory,bool simulation)
    {
        return RunFromRainbow(directory,seconds,factory,simulation,null);
    }
    internal static int RunFromRainbow(string directory,int seconds,Func<IHidControl> factory,bool simulation,Func<RgbColor?> nextFrame,string sourceName="native HAL log",Func<object> sourceMetrics=null,bool continuous=false)
    {
        if(continuous ? seconds!=0 : seconds<1 || seconds>900) throw new ArgumentOutOfRangeException("seconds");
        Directory.CreateDirectory(directory);
        string journal=Path.Combine(directory,"device-operation.json");
        string stop=Path.Combine(directory,"stop-test");
        var json=new JavaScriptSerializer();
        var events=new List<object>();
        var samples=new List<object>();
        var report=new Dictionary<string,object> {
            {"StartedUtc",DateTime.UtcNow.ToString("o")},{"Completed",false},{"ColorRgb",nextFrame==null ? "FF0000" : null},
            {"MaximumSeconds",seconds},{"ModeChangeAttempted",false},{"FrameAccepted",false},
            {"Simulation",simulation},
            {"Source",nextFrame==null ? "red diagnostic" : sourceName},{"FramesWritten",0},
            {"RecoveryEffect","rainbow-wave"},{"RecoveryEffectAccepted",false},
            {"BaselineSource","explicit caller declaration; effect is not readable"},
            {"PhysicalRestorationValidated",false},
            {"ModeRestored",false},{"PhysicalRgbValidated",false},{"Events",events}
        };
        report["StabilitySamples"]=samples;
        report["ContinuousSession"]=continuous;
        Action save=()=>File.WriteAllText(journal,json.Serialize(report),new UTF8Encoding(false));
        Action<string,byte[]> note=(name,response)=> { events.Add(new { utc=DateTime.UtcNow.ToString("o"), operation=name, responseHex=BitConverter.ToString(response) }); save(); };
        bool attempted=false;
        bool restored=false;
        string failure=null;
        TimeSpan cpuBefore=Process.GetCurrentProcess().TotalProcessorTime;
        using(var session=factory())
        {
            try
            {
                if(File.Exists(stop)) throw new InvalidOperationException("Stop file already exists");
                byte[] mode=session.Exchange(BaseStationProtocol.Query("mode"),false);
                report["OriginalMode"]=new int[]{mode[9],mode[10]};
                note("ReadOriginalMode",mode);
                if(mode[9]!=0 || mode[10]!=0) throw new InvalidOperationException("Test requires original normal mode [0,0]");
                report["ModeChangeAttempted"]=true;
                save(); // recovery is armed before sending the mode command
                attempted=true;
                note("EnterSoftwareMode",session.Exchange(VolatileFrameProtocol.Mode(3),true));
                byte[] software=session.Exchange(BaseStationProtocol.Query("mode"),false);
                note("VerifySoftwareMode",software);
                if(software[9]!=3 || software[10]!=0) throw new InvalidOperationException("Software mode was not confirmed");
                note("SelectVolatileCustom",session.Exchange(VolatileFrameProtocol.SelectCustom(),true));
                if(nextFrame==null)
                {
                    note("Write15LedRedFrame",session.Exchange(VolatileFrameProtocol.SolidFrame(255,0,0),true));
                    report["FrameAccepted"]=true; report["FramesWritten"]=1;
                }
                report["Active"]=true;
                save();
                if(!simulation)
                {
                    Console.WriteLine(json.Serialize(new { status="active",transport="USB HID",source=report["Source"] }));
                    Console.Out.Flush();
                }
                var clock=Stopwatch.StartNew();
                double nextSample=30;
                double nextJournal=0;
                while((continuous || clock.Elapsed.TotalSeconds<seconds) && !File.Exists(stop))
                {
                    if(nextFrame!=null)
                    {
                        RgbColor? color=nextFrame();
                        if(color.HasValue)
                        {
                            var rgb=color.Value;
                            session.Exchange(VolatileFrameProtocol.SolidFrame(rgb.Red,rgb.Green,rgb.Blue),true);
                            report["FrameAccepted"]=true;
                            report["FramesWritten"]=(int)report["FramesWritten"]+1;
                            report["LastColorRgb"]=rgb.ToString();
                            report["LastFrameUtc"]=DateTime.UtcNow.ToString("o");
                            if(clock.Elapsed.TotalSeconds>=nextJournal) { save(); nextJournal=clock.Elapsed.TotalSeconds+1; }
                        }
                    }
                    if(clock.Elapsed.TotalSeconds>=nextSample)
                    {
                        using(var process=Process.GetCurrentProcess())
                            samples.Add(new { utc=DateTime.UtcNow.ToString("o"),elapsedSeconds=clock.Elapsed.TotalSeconds,
                                framesWritten=(int)report["FramesWritten"],workingSetBytes=process.WorkingSet64,
                                cpuMilliseconds=(process.TotalProcessorTime-cpuBefore).TotalMilliseconds });
                        nextSample+=30;
                        if(samples.Count>120) samples.RemoveAt(0);
                        save();
                    }
                    Thread.Sleep(50);
                }
                if(!continuous && nextFrame!=null && !(bool)report["FrameAccepted"]) throw new IOException("No native HAL frame was applied");
                report["StopReason"]=File.Exists(stop) ? "stop-file" : "duration";
            }
            catch(Exception error)
            {
                failure=error.ToString(); report["Error"]=failure; report["Diagnostics"]=error.Data;
            }
            finally
            {
                report["Active"]=false;
                if(attempted)
                {
                    try
                    {
                        note("RestoreNormalMode",session.Exchange(VolatileFrameProtocol.Mode(0),true));
                        note("RestoreVolatileRainbow",session.Exchange(VolatileFrameProtocol.Rainbow(),true));
                        report["RecoveryEffectAccepted"]=true;
                        byte[] mode=session.Exchange(BaseStationProtocol.Query("mode"),false);
                        note("VerifyRestoredMode",mode);
                        restored=mode[9]==0 && mode[10]==0;
                        report["ModeRestored"]=restored;
                        if(!restored) throw new InvalidOperationException("Normal mode not confirmed after restore");
                    }
                    catch(Exception error) { report["RestoreError"]=error.ToString(); }
                }
                report["Completed"]=failure==null && restored;
                if(sourceMetrics!=null) report["SourceMetrics"]=sourceMetrics();
                using(var process=Process.GetCurrentProcess())
                {
                    report["WorkerCpuMilliseconds"]=(process.TotalProcessorTime-cpuBefore).TotalMilliseconds;
                    report["WorkerPeakWorkingSetBytes"]=process.PeakWorkingSet64;
                }
                report["FinishedUtc"]=DateTime.UtcNow.ToString("o");
                save();
            }
        }
        if(!simulation) Console.WriteLine(json.Serialize(report));
        return (bool)report["Completed"] ? 0 : 2;
    }
    internal static int RestoreNormal()
    {
        using(var session=new HidFeatureSession())
        {
            session.Exchange(VolatileFrameProtocol.Mode(0),true);
            byte[] mode=session.Exchange(BaseStationProtocol.Query("mode"),false);
            bool restored=mode[9]==0 && mode[10]==0;
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { restored=restored, mode=new int[]{mode[9],mode[10]} }));
            return restored ? 0 : 2;
        }
    }
    internal static int RestoreRainbow()
    {
        using(var session=new HidFeatureSession())
        {
            session.Exchange(VolatileFrameProtocol.Mode(0),true);
            byte[] effect=session.Exchange(VolatileFrameProtocol.Rainbow(),true);
            byte[] mode=session.Exchange(BaseStationProtocol.Query("mode"),false);
            bool restored=mode[9]==0 && mode[10]==0;
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { normalModeConfirmed=restored,
                rainbowCommandAccepted=true, volatileStorage=true, physicalRestorationValidated=false,
                mode=new int[]{mode[9],mode[10]}, effectResponseHex=BitConverter.ToString(effect) }));
            return restored ? 0 : 2;
        }
    }
}
