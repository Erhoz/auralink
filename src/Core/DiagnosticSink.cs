using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AuraLink.Core
{
    public interface IEffectSink
    {
        void Record(string operation, object data);
    }

    // No ASUS, Razer, USB or SDK dependency. One file per process avoids cross-
    // process interleaving. A full/unwritable log fails the call, never claims capture.
    public sealed class JsonLineSink : IEffectSink
    {
        private static readonly object Gate = new object();
        private readonly string directory;
        public JsonLineSink(string directory) { this.directory = directory; }
        public void Record(string operation, object data)
        {
            var serializer = new JavaScriptSerializer();
            int pid = Process.GetCurrentProcess().Id;
            string line = serializer.Serialize(new {
                timestampUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                processId = pid, process = Process.GetCurrentProcess().ProcessName,
                operation = operation, data = data, hardwareOutput = false
            });
            lock (Gate)
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "events-" + pid + ".jsonl");
                using (var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    if (file.Length > 5 * 1024 * 1024) throw new IOException("Diagnostic log limit reached");
                    file.Seek(0, SeekOrigin.End);
                    byte[] bytes = new UTF8Encoding(false).GetBytes(line + "\n");
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush();
                }
            }
        }
    }
}
