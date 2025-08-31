/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.LogTests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class LogTests
    {
        /// <summary>
        /// A trace event callback for tests.
        /// </summary>
        public sealed class StringArrayTraceLogger : IDisposable
        {
            private bool m_disposed;
            private readonly TextWriter m_writer;
            public List<string> TraceList { get; }
            public TraceEventArgs LastTraceEventArgs { get; set; }

            /// <summary>
            /// Create a serilog trace logger which replaces the default logging.
            /// </summary>
            public static StringArrayTraceLogger Create(TextWriter writer)
            {
                var traceLogger = new StringArrayTraceLogger(writer);

                // disable the built in tracing, use nunit trace output
                Utils.SetTraceMask(Utils.TraceMask & Utils.TraceMasks.StackTrace);
                Utils.SetTraceOutput(Utils.TraceOutput.Off);
                Utils.Tracing.TraceEventHandler += traceLogger.TraceEventHandler;

                return traceLogger;
            }

            /// <summary>
            /// Ctor of trace logger.
            /// </summary>
            private StringArrayTraceLogger(TextWriter writer)
            {
                TraceList = [];
                m_writer = writer;
            }

            /// <summary>
            /// clean up event handler.
            /// </summary>
            ~StringArrayTraceLogger()
            {
                Dispose();
            }

            /// <summary>
            /// IDisposable to clean up event handler.
            /// </summary>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    Utils.Tracing.TraceEventHandler -= TraceEventHandler;
                    m_disposed = true;
                }
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Callback for logging OPC UA stack trace output.
            /// </summary>
            /// <param name="sender">Sender object</param>
            /// <param name="e">The trace event args.</param>
            public void TraceEventHandler(object sender, TraceEventArgs e)
            {
                LastTraceEventArgs = e;
                if (e.Exception != null)
                {
                    m_writer.WriteLine(e.Exception);
                    TraceList.Add(e.Exception.Message);
                }
                string message = Utils.Format(e.Format, e.Arguments);
                m_writer.WriteLine(message);
                TraceList.Add(message);
            }
        }
    }
}
