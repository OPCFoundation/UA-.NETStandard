/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using FastSerialization;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderBenchmarks
    {
        const int kBufferSize = 4096;

        [Params(1, 2, 8, 64)]
        public int PayLoadSize { get; set; } = 64;

        [Params(128, 512, 1024, 4096, 8192, 65536)]
        public int StreamSize { get; set; } = 1024;

        /// <summary>
        /// Benchmark overhead to create StreamWriter, MemoryStream is kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void StreamWriter()
        {
            using (var test = new StreamWriter(m_memoryStream, Encoding.UTF8, StreamSize, true))
                test.Flush();
        }

        /// <summary>
        /// Benchmark overhead to create StreamWriter and MemoryStream.
        /// </summary>
        [Benchmark]
        [Test]
        public void StreamWriterRecyclableMemoryStream()
        {
            using (var memoryStream = new Microsoft.IO.RecyclableMemoryStream(m_memoryManager))
            using (var test = new StreamWriter(memoryStream, Encoding.UTF8, StreamSize))
                test.Flush();
        }

        /// <summary>
        /// Benchmark overhead to create StreamWriter and MemoryStream.
        /// </summary>
        [Benchmark]
        [Test]
        public void StreamWriterMemoryStream()
        {
            using (var memoryStream = new MemoryStream())
            using (var test = new StreamWriter(memoryStream, Encoding.UTF8, StreamSize))
                test.Flush();
        }

        /// <summary>
        /// Benchmark encoding with internal memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderConstructor2()
        {
            using (var jsonEncoder = new JsonEncoder(m_context, false))
            {
                TestEncoding(jsonEncoder);
                _ = jsonEncoder.CloseAndReturnText();
            }
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderStreamLeaveOpenMemoryStream()
        {
            JsonEncoder_StreamLeaveOpen(m_memoryStream);
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderStreamLeaveOpenRecyclableMemoryStream()
        {
            JsonEncoder_StreamLeaveOpen(m_recyclableMemoryStream);
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderStreamLeaveOpenArraySegmentStream()
        {
            JsonEncoder_StreamLeaveOpen(m_arraySegmentStream);
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open,
        /// use internal reflection to get string from memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderConstructorStreamwriterReflection2()
        {
            using (var jsonEncoder = new JsonEncoder(m_context, false, false, m_memoryStream, true, StreamSize))
            {
                TestEncoding(jsonEncoder);
                var result = jsonEncoder.CloseAndReturnText();
            }
        }

        #region Private Methods
        private void TestEncoding(IEncoder encoder)
        {
            int payLoadSize = PayLoadSize;
            encoder.WriteByte("Byte", 0);
            while (--payLoadSize > 0)
            {
                encoder.WriteBoolean("Boolean", true);
                encoder.WriteUInt64("UInt64", 1234566890);
                encoder.WriteString("String", "The quick brown fox...");
                encoder.WriteNodeId("NodeId", s_nodeId);
                encoder.WriteInt32Array("Array", s_list);
            }
        }

        private void JsonEncoder_StreamLeaveOpen(MemoryStream stream)
        {
            int length1;
            int length2;
            stream.Position = 0;
            using (var jsonEncoder = new JsonEncoder(m_context, false, false, stream, true, StreamSize))
            {
                TestEncoding(jsonEncoder);
                length1 = jsonEncoder.Close();
            }
            using (var jsonEncoder = new JsonEncoder(m_context, false, false, stream, true, StreamSize))
            {
                TestEncoding(jsonEncoder);
                length2 = jsonEncoder.Close();
            }
            var result = Encoding.UTF8.GetString(stream.ToArray());
            Assert.NotNull(result);
            Assert.AreEqual(length1 * 2, length2);
            Assert.AreEqual(length2, result.Length);
        }
        #endregion

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // for validating benchmark tests
            m_context = new ServiceMessageContext();
            m_memoryStream = new MemoryStream();
            m_memoryManager = new Microsoft.IO.RecyclableMemoryStreamManager();
            m_recyclableMemoryStream = new Microsoft.IO.RecyclableMemoryStream(m_memoryManager);
            m_bufferManager = new BufferManager(nameof(BinaryEncoder), kBufferSize);
            m_arraySegmentStream = new ArraySegmentStream(m_bufferManager, kBufferSize, 0, kBufferSize);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_context = null;
            m_memoryStream.Dispose();
            m_memoryStream = null;
            m_recyclableMemoryStream.Dispose();
            m_recyclableMemoryStream = null;
            m_memoryManager = null;
            m_bufferManager = null;
            m_arraySegmentStream.Dispose();
            m_arraySegmentStream = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            // for validating benchmark tests
            m_context = new ServiceMessageContext();
            m_memoryStream = new MemoryStream();
            m_memoryManager = new Microsoft.IO.RecyclableMemoryStreamManager();
            m_recyclableMemoryStream = new Microsoft.IO.RecyclableMemoryStream(m_memoryManager);
            m_bufferManager = new BufferManager(nameof(BinaryEncoder), kBufferSize);
            m_arraySegmentStream = new ArraySegmentStream(m_bufferManager, kBufferSize, 0, kBufferSize);
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_context = null;
            m_memoryStream.Dispose();
            m_memoryStream = null;
            m_recyclableMemoryStream.Dispose();
            m_recyclableMemoryStream = null;
            m_memoryManager = null;
            m_bufferManager = null;
            m_arraySegmentStream.Dispose();
            m_arraySegmentStream = null;
        }
        #endregion

        #region Private Fields
        private static NodeId s_nodeId = new NodeId(1234);
        private static IList<Int32> s_list = new List<Int32>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private IServiceMessageContext m_context;
        private MemoryStream m_memoryStream;
        private Microsoft.IO.RecyclableMemoryStreamManager m_memoryManager;
        private Microsoft.IO.RecyclableMemoryStream m_recyclableMemoryStream;
        private BufferManager m_bufferManager;
        private ArraySegmentStream m_arraySegmentStream;
        #endregion
    }

    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderDateTimeBenchmark
    {
        [Params(0, 4, 7)]
        public int DateTimeOmittedZeros { get; set; } = 0;

        [Benchmark]
        [Test]
        public void DateTimeEncodeToString()
        {
            _ = m_dateTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture);
        }

        [Benchmark]
        [Test]
        public void ConvertToUniversalTime()
        {
            _ = JsonEncoder.ConvertUniversalTimeToString(m_dateTime);
        }

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // for validating benchmark tests
            m_dateTime = DateTime.UtcNow;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            // for validating benchmark tests
            switch (DateTimeOmittedZeros)
            {
                case 4: m_dateTime = new DateTime(2011, 11, 11, 11, 11, 11, 999, DateTimeKind.Utc); break;
                case 7: m_dateTime = new DateTime(2011, 11, 11, 11, 11, 11, DateTimeKind.Utc); break;
                default:
                    do
                    {
                        m_dateTime = DateTime.UtcNow;
                    } while (m_dateTime.Ticks % 10 == 0);
                    break;
            }
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
        }
        #endregion

        #region Private Fields
        private DateTime m_dateTime;
        #endregion
    }


    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser(printSource: true)]
    public class JsonEncoderEscapeStringBenchmark
    {

        [Params(1,2,3,4)]
        public int StringVariantIndex  { get; set; } = 4;

        [Test]
        [Benchmark]
        public void EscapeStringBenchmark1()
        {
            m_memoryStream.SetLength(0);
            m_memoryStream.Position = 0;
            EscapedStringToStream(m_testString);
            m_streamWriter?.Flush();
        }

        [Test]
        [Benchmark]
        public void EscapeStringBenchmark2()
        {
            m_memoryStream.SetLength(0);
            m_memoryStream.Position = 0;
            EscapeString(m_testString);
            m_streamWriter?.Flush();
        }

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_memoryManager = new Microsoft.IO.RecyclableMemoryStreamManager();
            m_memoryStream = new Microsoft.IO.RecyclableMemoryStream(m_memoryManager);// new MemoryStream();
            m_streamWriter = new StreamWriter(m_memoryStream, Encoding.UTF8, m_streamSize, false);

            m_testString = "Test string ascii, special characters \n \b and control characters \0 \x04 ␀ ␁ ␂ ␃ ␄";
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            var result = Encoding.UTF8.GetString(m_memoryStream.ToArray());
            Assert.NotNull(result);

            m_streamWriter?.Dispose();
            m_streamWriter = null;
            m_memoryStream?.Dispose();
            m_memoryStream = null;
            m_recyclableMemoryStream?.Dispose();
            m_recyclableMemoryStream = null;
            m_memoryManager = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_memoryStream = new MemoryStream();
            m_streamWriter = new StreamWriter(m_memoryStream, Encoding.UTF8, m_streamSize, false);

            // for validating benchmark tests
            switch (StringVariantIndex )
            {
                case 1: m_testString = "Ascii characters 12345"; break;
                case 2: m_testString = "\" \n \r \t \b \f \\"; break;
                case 3: m_testString = "\0 \x01 \x02 \x03 \x04"; break;
                default: m_testString = "Ascii characters , special characters \n \b & control characters \0 \x04 ␀ ␁ ␂ ␃ ␄"; break;
            }
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_streamWriter?.Dispose();
            m_streamWriter = null;
            m_memoryStream?.Dispose();
            m_memoryStream = null;
            m_recyclableMemoryStream?.Dispose();
            m_recyclableMemoryStream = null;
            m_memoryManager = null;
        }
        #endregion

        #region Private Methods
        private void EscapedStringToStream(string value)
        {
            foreach (char ch in value)
            {
                bool found = false;

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (ch < 32)
                    {
                        m_streamWriter.Write("\\u");
                        m_streamWriter.Write("{0:X4}", (int)ch);
                        continue;
                    }

                    m_streamWriter.Write(ch);
                }
            }
        }

        private static readonly Dictionary<char, string> m_replace = new Dictionary<char, string>
        {
            {  '\"', "\\\"" },
            {  '\\', "\\\\" },
            { '\n', "\\n" },
            { '\r', "\\r" },
            { '\t', "\\t" },
            { '\b', "\\b" },
            { '\f', "\\f" }
        };
        private void EscapeString(string value)
        {
            StringBuilder m_stringBuilder = new StringBuilder(value.Length * 2);

            Dictionary<char, string> substitution = new Dictionary<char, string>(m_replace);

            foreach (char ch in value)
            {
                // Check if ch is present in the dictionary
                if (substitution.TryGetValue(ch, out string escapeSequence))
                {
                    m_stringBuilder.Append(escapeSequence);
                }
                else if (ch < 32)
                {
                    m_stringBuilder.Append("\\u");
                    m_stringBuilder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    m_stringBuilder.Append(ch);
                }
            }
            m_streamWriter.Write(m_stringBuilder);
        }
        #endregion

        #region Private Fields
        private static string m_testString;
        private Microsoft.IO.RecyclableMemoryStreamManager m_memoryManager;
        private Microsoft.IO.RecyclableMemoryStream m_recyclableMemoryStream;
        private MemoryStream m_memoryStream;
        private StreamWriter m_streamWriter;
        private int m_streamSize = 2048;
        private static readonly char[] m_specialChars = new char[] { '\"', '\\', '\n', '\r', '\t', '\b', '\f', };
        private static readonly char[] m_substitution = new char[] { '\"', '\\', 'n', 'r', 't', 'b', 'f' };
        #endregion
    }
}
