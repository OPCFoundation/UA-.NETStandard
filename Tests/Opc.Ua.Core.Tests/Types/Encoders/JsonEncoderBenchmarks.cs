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

using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.IO;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("JsonEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class JsonEncoderBenchmarks : EncoderBenchmarks
    {
        [Params(128, 512, 1024, 4096, 8192, 65536)]
        public int StreamSize { get; set; } = 1024;

        /// <summary>
        /// Benchmark overhead to create StreamWriter, MemoryStream is kept open.
        /// </summary>
        [Test]
        public void StreamWriter()
        {
            using (var memoryStream = new MemoryStream())
            using (var test = new StreamWriter(memoryStream, Encoding.UTF8, StreamSize, true))
            {
                test.Flush();
            }
        }

        /// <summary>
        /// Benchmark overhead to create StreamWriter and MemoryStream.
        /// </summary>
        [Test]
        public void StreamWriterRecyclableMemoryStream()
        {
            using (var memoryStream = new RecyclableMemoryStream(m_memoryManager))
            using (var test = new StreamWriter(memoryStream, Encoding.UTF8, StreamSize))
            {
                test.Flush();
            }
        }

        /// <summary>
        /// Benchmark overhead to create StreamWriter and MemoryStream.
        /// </summary>
        [Test]
        public void StreamWriterMemoryStream()
        {
            using (var memoryStream = new MemoryStream())
            using (var test = new StreamWriter(memoryStream, Encoding.UTF8, StreamSize))
            {
                test.Flush();
            }
        }

        /// <summary>
        /// Benchmark encoding with internal memory stream.
        /// </summary>
        [Test]
        public void JsonEncoderConstructor()
        {
            using (var jsonEncoder = new JsonEncoder(m_context, false))
            {
                TestEncoding(jsonEncoder);
                _ = jsonEncoder.CloseAndReturnText();
            }
        }

        /// <summary>
        /// Test encoding with ArrayPool memory stream.
        /// </summary>
        [Theory]
        public void JsonEncoderArraySegmentStreamTest(bool toText)
        {
            using (var memoryStream = new ArraySegmentStream(m_bufferManager, StreamBufferSize, 0, StreamBufferSize))
            {
                TestStreamEncode(memoryStream, toText);
            }
        }

        /// <summary>
        /// Test encoding with memory stream kept open.
        /// </summary>
        [Theory]
        public void JsonEncoderMemoryStreamTest(bool toText)
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            {
                TestStreamEncode(memoryStream, toText);
            }
        }

        /// <summary>
        /// Test encoding with recyclable memory stream kept open.
        /// </summary>
        [Theory]
        public void JsonEncoderRecyclableMemoryStream(bool toText)
        {
            using (var memoryStream = new RecyclableMemoryStream(m_memoryManager))
            {
                TestStreamEncode(memoryStream, toText);
            }
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderMemoryStream()
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            {
                JsonEncoder_StreamLeaveOpen(memoryStream);
                // get buffer for write
                _ = memoryStream.ToArray();
            }
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderRecyclableMemoryStream()
        {
            using (var recyclableMemoryStream = new RecyclableMemoryStream(m_memoryManager))
            {
                JsonEncoder_StreamLeaveOpen(recyclableMemoryStream);
                // get buffers for write
                _ = recyclableMemoryStream.GetReadOnlySequence();
            }
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderArraySegmentStream()
        {
            using (var arraySegmentStream = new ArraySegmentStream(m_bufferManager))
            {
                JsonEncoder_StreamLeaveOpen(arraySegmentStream);
                // get buffers and return them to buffer manager
                var buffers = arraySegmentStream.GetBuffers("writer");
                foreach (var buffer in buffers)
                {
                    m_bufferManager.ReturnBuffer(buffer.Array, "testreturn");
                }
            }
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoderArraySegmentStreamNoSpan()
        {
            // ECC_SUPPORT is used to distinguish also from platforms which do not support Span
#if NET6_0_OR_GREATER && ECC_SUPPORT
            using (var arraySegmentStream = new ArraySegmentStreamNoSpan(m_bufferManager))
#else
            using (var arraySegmentStream = new ArraySegmentStream(m_bufferManager))
#endif
            {
                JsonEncoder_StreamLeaveOpen(arraySegmentStream);
                // get buffers and return them to buffer manager
                var buffers = arraySegmentStream.GetBuffers("writer");
                foreach (var buffer in buffers)
                {
                    m_bufferManager.ReturnBuffer(buffer.Array, "testreturn");
                }
            }
        }

        #region Private Methods
        private void TestStreamEncode(MemoryStream memoryStream, bool toArray)
        {
            using (var jsonEncoder = new JsonEncoder(m_context, false, false, memoryStream, true, StreamSize))
            {
                TestEncoding(jsonEncoder);
                _ = jsonEncoder.Close();
            }
            using (var jsonEncoder = new JsonEncoder(m_context, false, false, memoryStream, true, StreamSize))
            {
                TestEncoding(jsonEncoder);
                if (toArray)
                {
                    int length = jsonEncoder.Close();
                    Assert.AreEqual(length, memoryStream.Position);
                    var result = memoryStream.ToArray();
                    Assert.NotNull(result);
                    Assert.AreEqual(length, result.Length);
                }
                else
                {
                    var result = jsonEncoder.CloseAndReturnText();
                    Assert.NotNull(result);
                }
            }
        }

        private void JsonEncoder_StreamLeaveOpen(MemoryStream stream, bool testResult = false)
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
            if (testResult)
            {
                var result = Encoding.UTF8.GetString(stream.ToArray());
                Assert.NotNull(result);
                Assert.AreEqual(length1 * 2, length2);
                Assert.AreEqual(length2, result.Length);
            }
        }
        #endregion

        #region Test Setup
        [OneTimeSetUp]
        public new void OneTimeSetUp() => base.OneTimeSetUp();

        [OneTimeTearDown]
        public new void OneTimeTearDown() => base.OneTimeTearDown();
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public new void GlobalSetup() => base.GlobalSetup();

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public new void GlobalCleanup() => base.GlobalCleanup();
        #endregion
    }
}
