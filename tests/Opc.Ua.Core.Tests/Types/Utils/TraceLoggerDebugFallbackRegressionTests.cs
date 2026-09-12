/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

#if DEBUG
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [NonParallelizable]
    public sealed class TraceLoggerDebugFallbackRegressionTests
    {
        [SetUp]
        public void SetUp()
        {
            m_output = new StringWriter(CultureInfo.InvariantCulture);
            m_listener = new TextWriterTraceListener(m_output);
            Trace.Listeners.Add(m_listener);
            Debug.WriteLine("Debug capture is active.");
            m_listener.Flush();
            Assert.That(m_output.ToString(), Is.EqualTo("Debug capture is active." + Environment.NewLine));
            m_output.GetStringBuilder().Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Trace.Listeners.Remove(m_listener);
            m_listener.Dispose();
            m_output.Dispose();
        }

        [Test]
        public void TraceFileFailureReportsActualPathInDebugOutput()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(TraceLoggerDebugFallbackRegressionTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, "trace {literal}.log");
            using var provider = new TraceLoggerProvider();
            provider.SetTraceOutput(Utils.TraceOutput.FileOnly);

            try
            {
                provider.SetTraceLog(filePath, deleteExisting: true);
                using FileStream held = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                IOException failure = Assert.Throws<IOException>(() =>
                {
                    using FileStream denied = File.Open(
                        filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                })!;

                provider.TraceWriteLine("write denied");
                m_listener.Flush();
                string expected =
                    "Could not write to trace file. Error=" + failure.Message + Environment.NewLine +
                    "FilePath=" + filePath + Environment.NewLine;
                Assert.That(m_output.ToString(), Is.EqualTo(expected));
            }
            finally
            {
                provider.SetTraceLog(string.Empty, deleteExisting: false);
                File.Delete(filePath);
                Directory.Delete(directory);
            }
        }

        [Test]
        public void DebugStringSecondArgumentIsACategoryNotAFormatArgument()
        {
            Debug.WriteLine("FilePath={1}", "category");
            m_listener.Flush();

            Assert.That(m_output.ToString(), Is.EqualTo("category: FilePath={1}" + Environment.NewLine));
        }

        private StringWriter m_output = null!;
        private TextWriterTraceListener m_listener = null!;
    }
}
#endif
