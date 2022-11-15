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

namespace Opc.Ua.Core.Tests.Types.LogTests
{
    /// <summary>
    /// Tests for the BuiltIn Types.
    /// </summary>
    [TestFixture, Category("Utils")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    public class LogTests
    {
        /// <summary>
        /// A trace event callback for tests.
        /// </summary>
        public class StringArrayTraceLogger : IDisposable
        {
            private bool m_disposed;
            private TextWriter m_writer;
            private int m_traceMasks;
            private List<string> m_traceList;
            public List<string> TraceList => m_traceList;
            public TraceEventArgs LastTraceEventArgs { get; set; }

            /// <summary>
            /// Create a serilog trace logger which replaces the default logging.
            /// </summary>
            public static StringArrayTraceLogger Create(TextWriter writer, int traceMasks)
            {
                var traceLogger = new StringArrayTraceLogger(writer, traceMasks);

                // disable the built in tracing, use nunit trace output
                Utils.SetTraceMask(Utils.TraceMask & Utils.TraceMasks.StackTrace);
                Utils.SetTraceOutput(Utils.TraceOutput.Off);
                Utils.Tracing.TraceEventHandler += traceLogger.TraceEventHandler;

                return traceLogger;
            }

            /// <summary>
            /// Ctor of trace logger.
            /// </summary>
            private StringArrayTraceLogger(TextWriter writer, int traceMasks)
            {
                m_traceList = new List<string>();
                m_writer = writer;
                m_traceMasks = traceMasks;
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
                    Utils.Tracing.TraceEventHandler -= this.TraceEventHandler;
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
                    m_traceList.Add(e.Exception.Message);
                }
                string message = string.Format(e.Format, e.Arguments);
                m_writer.WriteLine(message);
                m_traceList.Add(message);
            }
        }

        /// <summary>
        /// Test that all messages are propagated to the TraceEvent callback.
        /// </summary>
        /// <param name="logLevel">The level to test.</param>
        [Test]
        [TestCase(LogLevel.Trace)]
        [TestCase(LogLevel.Debug)]
        [TestCase(LogLevel.Information)]
        [TestCase(LogLevel.Warning)]
        [TestCase(LogLevel.Error)]
        [TestCase(LogLevel.Critical)]
        [NonParallelizable]
        public void LogTraceEventMessages(LogLevel logLevel)
        {
            Utils.SetLogLevel(logLevel);

            using (var logger = StringArrayTraceLogger.Create(TestContext.Out, Utils.TraceMasks.All))
            {
                Assert.NotNull(logger);

                logger.LastTraceEventArgs = null;

                // test the legacy log mapping to TraceEventArgs
                Utils.Trace(Utils.TraceMasks.None, "This is a None message: {0}", Utils.TraceMasks.None);
                if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Assert.AreEqual(Utils.TraceMasks.Operation, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.Error, "This is an Error message: {0}", Utils.TraceMasks.Error);
                if (Utils.Logger.IsEnabled(LogLevel.Error))
                {
                    Assert.AreEqual(Utils.TraceMasks.Error, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.Information, "This is a Information message: {0}", Utils.TraceMasks.Information);
                if (Utils.Logger.IsEnabled(LogLevel.Information))
                {
                    Assert.AreEqual(Utils.TraceMasks.Information, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.StackTrace, "This is a StackTrace message: {0}", Utils.TraceMasks.StackTrace);
                if (Utils.Logger.IsEnabled(LogLevel.Error))
                {
                    Assert.AreEqual(Utils.TraceMasks.StackTrace, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.Service, "This is a Service message: {0}", Utils.TraceMasks.Service);
                if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Assert.AreEqual(Utils.TraceMasks.Service, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.ServiceDetail, "This is a ServiceDetail message: {0}", Utils.TraceMasks.ServiceDetail);
                if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Assert.AreEqual(Utils.TraceMasks.ServiceDetail, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.Operation, "This is a Operation message: {0}", Utils.TraceMasks.Operation);
                if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Assert.AreEqual(Utils.TraceMasks.Operation, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.OperationDetail, "This is a OperationDetail message: {0}", Utils.TraceMasks.OperationDetail);
                if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Assert.AreEqual(Utils.TraceMasks.OperationDetail, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.StartStop, "This is a StartStop message: {0}", Utils.TraceMasks.StartStop);
                if (Utils.Logger.IsEnabled(LogLevel.Information))
                {
                    Assert.AreEqual(Utils.TraceMasks.StartStop, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.ExternalSystem, "This is a ExternalSystem message: {0}", Utils.TraceMasks.ExternalSystem);
                if (Utils.Logger.IsEnabled(LogLevel.Trace))
                {
                    Assert.AreEqual(Utils.TraceMasks.ExternalSystem, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                Utils.Trace(Utils.TraceMasks.Security, "This is a Security message: {0}", Utils.TraceMasks.Security);
                if (Utils.Logger.IsEnabled(LogLevel.Information))
                {
                    Assert.AreEqual(Utils.TraceMasks.Security, logger.LastTraceEventArgs.TraceMask);
                    logger.LastTraceEventArgs = null;
                }
                else
                {
                    Assert.IsNull(logger.LastTraceEventArgs);
                }

                var sre = new ServiceResultException(StatusCodes.BadServiceUnsupported, "service unsupported");
                Utils.Trace(sre, "This is a ServiceResultException");

                TestContext.Out.WriteLine("Logged {0} messages.", logger.TraceList.Count);

                Utils.LogTrace("This is a Trace message: {0}", LogLevel.Trace);
                Utils.LogDebug("This is a Debug message: {0}", LogLevel.Debug);
                Utils.LogInfo("This is a Info message: {0}", LogLevel.Information);
                Utils.LogWarning("This is a Warning message: {0}", LogLevel.Warning);
                Utils.LogError("This is a Error message: {0}", LogLevel.Error);
                Utils.LogCritical("This is a Critical message: {0}", LogLevel.Critical);

                TestContext.Out.WriteLine("Logged {0} messages.", logger.TraceList.Count);

#if DEBUG
                switch (logLevel)
                {
                    case LogLevel.Trace: Assert.AreEqual(20, logger.TraceList.Count); break;
                    case LogLevel.Debug: Assert.AreEqual(13, logger.TraceList.Count); break;
                    case LogLevel.Information: Assert.AreEqual(12, logger.TraceList.Count); break;
                    case LogLevel.Warning: Assert.AreEqual(8, logger.TraceList.Count); break;
                    case LogLevel.Error: Assert.AreEqual(7, logger.TraceList.Count); break;
                    case LogLevel.Critical: Assert.AreEqual(1, logger.TraceList.Count); break;
                }
#else
                switch (logLevel)
                {
                    case LogLevel.Trace: Assert.AreEqual(18, logger.TraceList.Count); break;
                    case LogLevel.Debug: Assert.AreEqual(11, logger.TraceList.Count); break;
                    case LogLevel.Information: Assert.AreEqual(11, logger.TraceList.Count); break;
                    case LogLevel.Warning: Assert.AreEqual(7, logger.TraceList.Count); break;
                    case LogLevel.Error: Assert.AreEqual(6, logger.TraceList.Count); break;
                    case LogLevel.Critical: Assert.AreEqual(1, logger.TraceList.Count); break;
                }
#endif
            }
        }
    }
}
