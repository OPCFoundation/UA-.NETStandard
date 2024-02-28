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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.IO;
using NUnit.Framework;
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
            using (var memoryStream = new RecyclableMemoryStream(m_memoryManager))
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
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_recyclableMemoryStream = new RecyclableMemoryStream(m_memoryManager);
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
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_recyclableMemoryStream = new RecyclableMemoryStream(m_memoryManager);
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
        private RecyclableMemoryStreamManager m_memoryManager;
        private RecyclableMemoryStream m_recyclableMemoryStream;
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
    public class JsonEncoderEscapeStringBenchmarks
    {
        public const int InnerLoops = 100;
        [DatapointSource]
        [Params(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)]
        public int StringVariantIndex { get; set; } = 1;

        [DatapointSource]
        // for benchmarking with different escaped strings
        public static readonly string[] EscapeTestStrings =
        {
            // The use case without escape characters, plain text
                "The quick brown fox jumps over the lazy dog.",
            // The use case with many control characters escaped, 1 char spaces
                "\" \n \r \t \b \f \\ ",
            // The use case with many control characters escaped, 2 char spaces
                "  \"  \n  \r  \t  \b  \f  \\  ",
            // The use case with many control characters escaped, 3 char spaces
                "   \"   \n   \r   \t   \b   \f   \\   ",
            // The use case with many control characters escaped, 5 char spaces
                "     \"     \n     \r     \t     \b     \f     \\     ",
            // The use case with many binary characters escaped, 1 char spaces
                "\0 \x01 \x02 \x03 \x04 ",
            // The use case with many binary characters escaped, 2 char spaces
                "  \0  \x01  \x02  \x03  \x04  ",
            // The use case with many binary characters escaped, 3 char spaces
                "   \0   \x01   \x02   \x03   \x04   ",
            // The use case with many binary characters escaped, 5 char spaces
                "     \0     \x01     \x02     \x03     \x04     ",
            // The use case with all escape characters and a long string
                "Ascii characters, special characters \n \b & control characters \0 \x04 ␀ ␁ ␂ ␃ ␄. This is a test.",
        };

        /// <summary>
        /// Benchmark encoding of the previous implementation.
        /// </summary>
        [Benchmark(Baseline = true)]
        public void EscapeStringLegacy()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapedStringLegacy(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// Benchmark encoding of the previous implementation with snall improvement for binary encoding.
        /// </summary>
        [Benchmark]
        public void EscapeStringLegacyPlus()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapedStringLegacyPlus(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using StringBuilder.
        /// </summary>
        [Benchmark]
        public void EscapeStringStringBuilder()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeString(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ThreadLocal StringBuilder.
        /// </summary>
        [Benchmark]
        public void EscapeStringThreadLocal()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringThreadLocal(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpan()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpan(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and char write.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanChars()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanChars(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and char write.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanCharsInline()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanCharsInline(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and char write with const arrays.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanCharsInlineConst()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanCharsInlineConst(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and IndexOf.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanIndex()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanIndex(m_testString);
            }
            m_streamWriter.Flush();
        }

        /// <summary>
        /// A new implementation using ReadOnlySpan and Dictionary.
        /// </summary>
        [Benchmark]
        public void EscapeStringSpanDict()
        {
            m_memoryStream.Position = 0;
            int repeats = InnerLoops;
            while (repeats-- > 0)
            {
                EscapeStringSpanDict(m_testString);
            }
            m_streamWriter.Flush();
        }

        [Theory]
        [TestCase("No Escape chars", 0)]
        [TestCase("control chars escaped, 1 char space", 1)]
        [TestCase("control chars escaped, 2 char spaces", 2)]
        [TestCase("control chars escaped, 3 char spaces", 3)]
        [TestCase("control chars escaped, 5 char spaces", 4)]
        [TestCase("binary chars escaped, 1 char space", 5)]
        [TestCase("binary chars escaped, 2 char spaces", 6)]
        [TestCase("binary chars escaped, 3 char spaces", 7)]
        [TestCase("binary chars escaped, 5 char spaces", 8)]
        [TestCase("all escape chars and long string", 9)]
        public void EscapeStringValidation(string name, int index)
        {
            m_testString = EscapeTestStrings[index];
            TestContext.Out.WriteLine(m_testString);
            var testArray = m_testString.ToCharArray();

            m_memoryStream.Position = 0;
            EscapeStringLegacy();
            m_streamWriter.Flush();
            byte[] resultLegacy = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultLegacy));

            m_memoryStream.Position = 0;
            EscapeStringLegacyPlus();
            m_streamWriter.Flush();
            byte[] resultLegacyPlus = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultLegacyPlus));

            m_memoryStream.Position = 0;
            EscapeStringStringBuilder();
            m_streamWriter.Flush();
            byte[] result = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(result));

            m_memoryStream.Position = 0;
            EscapeStringThreadLocal();
            m_streamWriter.Flush();
            byte[] resultThreadLocal = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultThreadLocal));

            m_memoryStream.Position = 0;
            EscapeStringSpan(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpan = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpan));

            m_memoryStream.Position = 0;
            EscapeStringSpanChars(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanChars = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanChars));

            m_memoryStream.Position = 0;
            EscapeStringSpanCharsInline(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanCharsInline = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanCharsInline));

            m_memoryStream.Position = 0;
            EscapeStringSpanCharsInlineConst(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanCharsInlineConst = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanCharsInlineConst));

            m_memoryStream.Position = 0;
            EscapeStringSpanIndex(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanIndex = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanIndex));

            m_memoryStream.Position = 0;
            EscapeStringSpanDict(m_testString);
            m_streamWriter.Flush();
            byte[] resultSpanDict = m_memoryStream.ToArray();
            TestContext.Out.WriteLine(Encoding.UTF8.GetString(resultSpanDict));

            Assert.IsTrue(Utils.IsEqual(resultLegacy, result));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultLegacyPlus));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultThreadLocal));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpan));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanChars));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanCharsInline));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanCharsInlineConst));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanIndex));
            Assert.IsTrue(Utils.IsEqual(resultLegacy, resultSpanDict));
        }

        #region Test Setup
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, new UTF8Encoding(false), m_streamSize, false);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_streamWriter?.Dispose();
            m_streamWriter = null;
            m_memoryStream?.Dispose();
            m_memoryStream = null;
            m_memoryManager = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>4
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public void GlobalSetup()
        {
            m_memoryManager = new RecyclableMemoryStreamManager();
            m_memoryStream = new RecyclableMemoryStream(m_memoryManager);
            m_streamWriter = new StreamWriter(m_memoryStream, new UTF8Encoding(false), m_streamSize, false);
            m_testString = EscapeTestStrings[StringVariantIndex - 1];
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_streamWriter?.Dispose();
            m_streamWriter = null;
            m_memoryStream?.Dispose();
            m_memoryStream = null;
            m_memoryManager = null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Version used previously in JsonEncoder.
        /// </summary>
        private void EscapedStringLegacy(string value)
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

        /// <summary>
        /// Version used previously in JsonEncoder plus improvement of binary encoding.
        /// </summary>
        /// <remarks>
        /// For the underlying stream writer it is faster to write two chars than a 2 char string.
        /// </remarks>
        private void EscapedStringLegacyPlus(string value)
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
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write('u');
                        m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                        continue;
                    }

                    m_streamWriter.Write(ch);
                }
            }
        }

        /// <summary>
        /// Using a span to escape the string, write strings to stream writer if possible.
        /// </summary>
        private void EscapeStringSpan(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        if (lastOffset < i)
                        {
#if NETCOREAPP2_1_OR_GREATER
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                        }
                        lastOffset = i + 1;
                        m_streamWriter.Write(m_substitutionStrings[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write("\\u");
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        /// <summary>
        /// Use span to escape the string, write only chars to stream writer.
        /// </summary>
        /// <param name="value"></param>
        private void EscapeStringSpanChars(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        if (lastOffset < i)
                        {
#if NETCOREAPP2_1_OR_GREATER
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                            m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                        }
                        lastOffset = i + 1;
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    if (lastOffset < i - 1)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    else
                    {
                        while (lastOffset < i)
                        {
                            m_streamWriter.Write(charSpan[lastOffset++]);
                        }
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan);
#else
                m_streamWriter.Write(value);
#endif
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteSpan(ref int lastOffset, ReadOnlySpan<char> valueSpan, int index)
        {
            if (lastOffset < index - 2)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(valueSpan.Slice(lastOffset, index - lastOffset));
#else
                m_streamWriter.Write(valueSpan.Slice(lastOffset, index - lastOffset).ToString());
#endif
            }
            else
            {
                while (lastOffset < index)
                {
                    m_streamWriter.Write(valueSpan[lastOffset++]);
                }
            }
            lastOffset = index + 1;
        }

        /// <summary>
        /// Write only chars to stream writer, inline the write sequence for readability.
        /// </summary>
        /// <param name="value"></param>
        private void EscapeStringSpanCharsInline(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialChars.Length; ii++)
                {
                    if (m_specialChars[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitution[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    WriteSpan(ref lastOffset, charSpan, i);
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan);
#else
                m_streamWriter.Write(value);
#endif
            }
            else
            {
                WriteSpan(ref lastOffset, charSpan, charSpan.Length);
            }
        }

        // create version of EscapeStringSpanCharsInline that references cosnt arrays
        private void EscapeStringSpanCharsInlineConst(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();
            int lastOffset = 0;

            for (int i = 0; i < charSpan.Length; i++)
            {
                bool found = false;
                char ch = charSpan[i];

                for (int ii = 0; ii < m_specialCharsConst.Length; ii++)
                {
                    if (m_specialCharsConst[ii] == ch)
                    {
                        WriteSpan(ref lastOffset, charSpan, i);
                        m_streamWriter.Write('\\');
                        m_streamWriter.Write(m_substitutionConst[ii]);
                        found = true;
                        break;
                    }
                }

                if (!found && ch < 32)
                {
                    WriteSpan(ref lastOffset, charSpan, i);
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }

            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else
            {
                WriteSpan(ref lastOffset, charSpan, charSpan.Length);
            }
        }


        private void EscapeStringSpanIndex(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                char ch = charSpan[i];

                int index = m_specialString.IndexOf(ch);
                if (index >= 0)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write(m_substitutionStrings[index]);
                    continue;
                }

                if (ch < 32)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        private void EscapeStringSpanDict(string value)
        {
            ReadOnlySpan<char> charSpan = value.AsSpan();

            int lastOffset = 0;
            for (int i = 0; i < charSpan.Length; i++)
            {
                char ch = charSpan[i];

                if (m_replace.TryGetValue(ch, out string escapeSequence))
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write(escapeSequence);
                    continue;
                }

                if (ch < 32)
                {
                    if (lastOffset < i)
                    {
#if NETCOREAPP2_1_OR_GREATER
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset));
#else
                        m_streamWriter.Write(charSpan.Slice(lastOffset, i - lastOffset).ToString());
#endif
                    }
                    lastOffset = i + 1;
                    m_streamWriter.Write('\\');
                    m_streamWriter.Write('u');
                    m_streamWriter.Write(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
            }
            if (lastOffset == 0)
            {
                m_streamWriter.Write(value);
            }
            else if (lastOffset < charSpan.Length)
            {
#if NETCOREAPP2_1_OR_GREATER
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset));
#else
                m_streamWriter.Write(charSpan.Slice(lastOffset, charSpan.Length - lastOffset).ToString());
#endif
            }
        }

        private void EscapeString(string value)
        {
            StringBuilder stringBuilder = new StringBuilder(value.Length * 2);

            foreach (char ch in value)
            {
                if (m_replace.TryGetValue(ch, out string escapeSequence))
                {
                    stringBuilder.Append(escapeSequence);
                }
                else if (ch < 32)
                {
                    stringBuilder.Append("\\u");
                    stringBuilder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    stringBuilder.Append(ch);
                }
            }
            m_streamWriter.Write(stringBuilder);
        }

        private void EscapeStringThreadLocal(string value)
        {
            StringBuilder stringBuilder = m_stringBuilderPool.Value;
            stringBuilder.Clear();
            stringBuilder.EnsureCapacity(value.Length * 2);

            foreach (char ch in value)
            {
                if (m_replace.TryGetValue(ch, out string escapeSequence))
                {
                    stringBuilder.Append(escapeSequence);
                }
                else if (ch < 32)
                {
                    stringBuilder.Append("\\u");
                    stringBuilder.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    stringBuilder.Append(ch);
                }
            }
            m_streamWriter.Write(stringBuilder);
        }
        #endregion

        #region Private Fields
        private ThreadLocal<StringBuilder> m_stringBuilderPool = new ThreadLocal<StringBuilder>(() => new StringBuilder());
        private static string m_testString;
        private RecyclableMemoryStreamManager m_memoryManager;
        private RecyclableMemoryStream m_memoryStream;
        private StreamWriter m_streamWriter;
        private int m_streamSize = 1024;
        private static readonly string m_specialString = "\"\\\n\r\t\b\f";

        // Declare static readonly characters for the special characters
        private static readonly char sro_quotation = '\"';
        private static readonly char sro_backslash = '\\';
        private static readonly char sro_newline = '\n';
        private static readonly char sro_return = '\r';
        private static readonly char sro_tab = '\t';
        private static readonly char sro_backspace = '\b';
        private static readonly char sro_formfeed = '\f';
        private static readonly char[] m_specialChars = new char[] { sro_quotation, sro_backslash, sro_newline, sro_return, sro_tab, sro_backspace, sro_formfeed };

        // Declare static readonly characters for the substitution characters
        private static readonly char sro_quotationSub = '\"';
        private static readonly char sro_backslashSub = '\\';
        private static readonly char sro_newlineSub = 'n';
        private static readonly char sro_returnSub = 'r';
        private static readonly char sro_tabSub = 't';
        private static readonly char sro_backspaceSub = 'b';
        private static readonly char sro_formfeedSub = 'f';
        private static readonly char[] m_substitution = new char[] { sro_quotationSub, sro_backslashSub, sro_newlineSub, sro_returnSub, sro_tabSub, sro_backspaceSub, sro_formfeedSub };

        // Special characters as const
        private const char s_quotation = '\"';
        private const char s_backslash = '\\';
        private const char s_newline = '\n';
        private const char s_return = '\r';
        private const char s_tab = '\t';
        private const char s_backspace = '\b';
        private const char s_formfeed = '\f';

        private static readonly char[] m_specialCharsConst = new char[] { s_quotation, s_backslash, s_newline, s_return, s_tab, s_backspace, s_formfeed };

        // Substitution as const
        private const char s_quotationSub = '\"';
        private const char s_backslashSub = '\\';
        private const char s_newlineSub = 'n';
        private const char s_returnSub = 'r';
        private const char s_tabSub = 't';
        private const char s_backspaceSub = 'b';
        private const char s_formfeedSub = 'f';

        private static readonly char[] m_substitutionConst = new char[] { s_quotationSub, s_backslashSub, s_newlineSub, s_returnSub, s_tabSub, s_backspaceSub, s_formfeedSub };

        private static readonly string[] m_substitutionStrings = new string[] { "\\\"", "\\\\", "\\n", "\\r", "\\t", "\\b", "\\f" };
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
        #endregion
    }
}
