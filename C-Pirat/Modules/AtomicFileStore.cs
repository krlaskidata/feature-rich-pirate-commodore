using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace PiratBotCSharp.Modules
{
    public static class AtomicFileStore
    {
        private static readonly ConcurrentDictionary<string, object> FileLocks = new();

        public static void WriteAllTextAtomic(string filePath, string content)
        {
            var lockObj = FileLocks.GetOrAdd(filePath, _ => new object());

            lock (lockObj)
            {
                var tempPath = filePath + ".tmp";
                var dir = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tempPath, content);
                File.Replace(tempPath, filePath, null);
            }
        }

        public static async Task WriteAllTextAtomicAsync(string filePath, string content)
        {
            var lockObj = FileLocks.GetOrAdd(filePath, _ => new object());

            await Task.Run(() =>
            {
                lock (lockObj)
                {
                    var tempPath = filePath + ".tmp";
                    var dir = Path.GetDirectoryName(filePath);

                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllText(tempPath, content);
                    File.Replace(tempPath, filePath, null);
                }
            });
        }
    }
}
