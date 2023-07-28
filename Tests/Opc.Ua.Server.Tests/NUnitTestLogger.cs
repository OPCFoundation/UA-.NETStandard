/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Tests
{
    public class NUnitTestLogger<T> : ILogger<T>
    {
        /// <summary>
        /// Create a nunit trace logger which replaces the default logging.
        /// </summary>
        public static NUnitTestLogger<T> Create(
            TextWriter writer,
            ApplicationConfiguration config,
            int traceMasks)
        {
            var traceLogger = new NUnitTestLogger<T>(writer);

            // disable the built in tracing, use nunit trace output
            Utils.SetTraceMask(Utils.TraceMask & Utils.TraceMasks.StackTrace);
            Utils.SetTraceOutput(Utils.TraceOutput.Off);
            Utils.SetLogger(traceLogger);

            return traceLogger;
        }

        private NUnitTestLogger(TextWriter outputWriter)
        {
            m_outputWriter = outputWriter;
        }

        public LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= MinimumLogLevel;
        }

        public void SetWriter(TextWriter outputWriter)
        {
            Interlocked.Exchange(ref m_outputWriter, outputWriter);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (logLevel < MinimumLogLevel)
            {
                return;
            }

            try
            {
                var sb = new StringBuilder();
                sb.AppendFormat("{0:yy-MM-dd HH:mm:ss.fff}: ", DateTime.UtcNow);
                sb.Append(formatter(state, exception));

                var logEntry = sb.ToString();

                m_outputWriter.WriteLine(logEntry);
            }
            catch
            {
                // intentionally ignored
            }
        }

        private TextWriter m_outputWriter;
    }


}
