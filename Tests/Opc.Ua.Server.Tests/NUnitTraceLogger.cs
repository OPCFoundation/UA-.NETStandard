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
using System.IO;
using NUnit.Framework;
using Opc.Ua;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// A sample serilog trace logger replacement.
    /// </summary>
    public class NUnitTraceLogger
    {
        private TextWriter m_writer;
        private int m_traceMasks;

        /// <summary>
        /// Create a serilog trace logger which replaces the default logging.
        /// </summary>
        public static NUnitTraceLogger Create(
            TextWriter writer,
            ApplicationConfiguration config,
            int traceMasks)
        {
            var traceLogger = new NUnitTraceLogger(writer, traceMasks);

            // disable the built in tracing, use nunit trace output
            Utils.SetTraceMask(Utils.TraceMask & Utils.TraceMasks.StackTrace);
            Utils.SetTraceOutput(Utils.TraceOutput.Off);
            Utils.Tracing.TraceEventHandler += traceLogger.TraceEventHandler;

            return traceLogger;
        }

        public void SetWriter(TextWriter writer)
        {
            m_writer = writer;
        }

        /// <summary>
        /// Ctor of trace logger.
        /// </summary>
        private NUnitTraceLogger(TextWriter writer, int traceMasks)
        {
            m_writer = writer;
            m_traceMasks = traceMasks;
        }

        /// <summary>
        /// Callback for logging OPC UA stack trace output
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">The trace event args.</param>
        public void TraceEventHandler(object sender, TraceEventArgs e)
        {
            if ((e.TraceMask & m_traceMasks) != 0)
            {
                if (e.Exception != null)
                {
                    m_writer.WriteLine(e.Exception);
                }
                m_writer.WriteLine(string.Format(e.Format, e.Arguments ?? new object[0]));
            }
        }
    }
}
