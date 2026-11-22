// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#if DEBUG
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Verifies trace-file failures report the actual path through Debug output without mistaken format overloads.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [NonParallelizable]
    public sealed class TraceLoggerDebugFallbackRegressionTests
    {
        /// <summary>
        /// Installs and verifies a deterministic capture listener for Debug output.
        /// </summary>
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

        /// <summary>
        /// Removes and disposes the temporary Debug listener and its output buffer.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            Trace.Listeners.Remove(m_listener);
            m_listener.Dispose();
            m_output.Dispose();
        }

        /// <summary>
        /// Verifies an exclusively locked trace file reports both the original IO error and its literal path.
        /// </summary>
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

        /// <summary>
        /// Pins the Debug.WriteLine string overload's category semantics instead of assuming format substitution.
        /// </summary>
        [Test]
        public void DebugStringSecondArgumentIsACategoryNotAFormatArgument()
        {
            Debug.WriteLine("FilePath={1}", "category");
            m_listener.Flush();

            Assert.That(m_output.ToString(), Is.EqualTo("category: FilePath={1}" + Environment.NewLine));
        }

        /// <summary>
        /// Captures invariant-culture Debug text for exact assertions.
        /// </summary>
        private StringWriter m_output = null!;

        /// <summary>
        /// Routes Debug output to the per-test capture buffer.
        /// </summary>
        private TextWriterTraceListener m_listener = null!;
    }
}
#endif
