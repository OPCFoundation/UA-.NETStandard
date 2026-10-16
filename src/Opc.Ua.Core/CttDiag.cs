// DIAGNOSTIC ONLY - not for merge. Writes timestamped lines to stdout (the CTT CI server log),
// or to the file named by CTT_DIAG_LOG.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Diagnostic trace sink.
    /// </summary>
    public static class CttDiag
    {
        private static readonly BlockingCollection<string> s_queue = new(new ConcurrentQueue<string>());

        static CttDiag()
        {
            var t = new Thread(Writer) { IsBackground = true, Name = "CttDiag" };
            t.Start();
        }

        /// <summary>
        /// Whether tracing is on.
        /// </summary>
        public static bool Enabled => true;

        /// <summary>
        /// Writes a line.
        /// </summary>
        public static void Log(string message)
        {
            s_queue.Add(
                "DIAG " + DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                " [" + Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture) + "] " + message);
        }

        private static void Writer()
        {
            string? path = Environment.GetEnvironmentVariable("CTT_DIAG_LOG");
            using TextWriter w = path != null
                ? new StreamWriter(path, append: true)
                : new StreamWriter(Console.OpenStandardOutput());
            foreach (string line in s_queue.GetConsumingEnumerable())
            {
                w.WriteLine(line);
                if (s_queue.Count == 0)
                {
                    w.Flush();
                }
            }
        }
    }
}
