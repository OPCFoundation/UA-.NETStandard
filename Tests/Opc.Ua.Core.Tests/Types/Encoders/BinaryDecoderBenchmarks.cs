/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("BinaryEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class BinaryDecoderBenchmarks : EncoderBenchmarks
    {
        /// <summary>
        /// Test decoding with internal memory stream.
        /// </summary>
        [Test]
        public void BinaryDecoderInternalMemoryStreamTest()
        {
            using (var binaryDecoder = new BinaryDecoder(m_encodedByteArray, m_context))
            {
                TestDecoding(binaryDecoder);
            }
        }

        /// <summary>
        /// Test decoding with ArrayPool memory stream.
        /// </summary>
        [Test]
        public void BinaryDecoderArraySegmentStreamTest()
        {
            using (var memoryStream = new ArraySegmentStream(m_encodedBufferList))
            using (var binaryDecoder = new BinaryDecoder(memoryStream, m_context))
            {
                TestDecoding(binaryDecoder);
            }
        }

        /// <summary>
        /// Test decoding with ArrayPool memory stream that has no span support.
        /// </summary>
        [Test]
        public void BinaryDecoderArraySegmentStreamNoSpanTest()
        {
#if NET6_0_OR_GREATER && ECC_SUPPORT
            using (var arraySegmentStream = new ArraySegmentStreamNoSpan(m_encodedBufferList))
#else
            using (var arraySegmentStream = new ArraySegmentStream(m_encodedBufferList))
#endif
            using (var binaryDecoder = new BinaryDecoder(arraySegmentStream, m_context))
            {
                TestDecoding(binaryDecoder);
            }
        }

        /// <summary>
        /// Benchmark decoding with memory stream.
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void BinaryDecoderMemoryStream()
        {
            using (var memoryStream = new MemoryStream(m_encodedByteArray))
            {
                BinaryDecoder_Stream(memoryStream);
            }
        }

        /// <summary>
        /// Benchmark decoding with array segment memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryDecoderArraySegmentStream()
        {
            using (var arraySegmentStream = new ArraySegmentStream(m_encodedBufferList))
            {
                BinaryDecoder_Stream(arraySegmentStream);
            }
        }

        /// <summary>
        /// Benchmark decoding with array segment memory stream without span support.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryDecoderArraySegmentStreamNoSpan()
        {
#if NET6_0_OR_GREATER && ECC_SUPPORT
            using (var arraySegmentStream = new ArraySegmentStreamNoSpan(m_encodedBufferList))
#else
            using (var arraySegmentStream = new ArraySegmentStream(m_encodedBufferList))
#endif
            {
                BinaryDecoder_Stream(arraySegmentStream);
            }
        }

        #region Private Methods
        private void TestStreamDecode(MemoryStream memoryStream)
        {
            using (var binaryDecoder = new BinaryDecoder(memoryStream, m_context))
            {
                TestDecoding(binaryDecoder);
                TestDecoding(binaryDecoder);
                binaryDecoder.Close();
            }
        }

        private void BinaryDecoder_Stream(MemoryStream memoryStream)
        {
            using (var binaryDecoder = new BinaryDecoder(memoryStream, m_context))
            {
                TestDecoding(binaryDecoder);
                TestDecoding(binaryDecoder);
                binaryDecoder.Close();
            }
        }
        #endregion

        #region Test Setup
        [OneTimeSetUp]
        public new void OneTimeSetUp()
        {
            base.OneTimeSetUp();
            InitializeEncodedTestData();
        }

        [OneTimeTearDown]
        public new void OneTimeTearDown()
        {
            base.OneTimeTearDown();
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public new void GlobalSetup()
        {
            base.GlobalSetup();
            InitializeEncodedBenchmarkData();
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public new void GlobalCleanup()
        {
            base.GlobalCleanup();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Initialize encoded data.
        /// </summary>
        private void InitializeEncodedTestData()
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                TestEncoding(binaryEncoder);
                m_encodedByteArray = binaryEncoder.CloseAndReturnBuffer();
            }

            using (var memoryStream = new ArraySegmentStream(m_bufferManager))
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                TestEncoding(binaryEncoder);
                binaryEncoder.Close();
                m_encodedBufferList = memoryStream.GetBuffers("writer");
            }
        }

        /// <summary>
        /// Initialize encoded data.
        /// </summary>
        private void InitializeEncodedBenchmarkData()
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                TestEncoding(binaryEncoder);
                m_encodedByteArray = binaryEncoder.CloseAndReturnBuffer();
            }

            using (var memoryStream = new ArraySegmentStream(m_bufferManager))
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                TestEncoding(binaryEncoder);
                binaryEncoder.Close();
                m_encodedBufferList = memoryStream.GetBuffers("writer");
            }
        }
        #endregion

        private byte[] m_encodedByteArray;
        private BufferCollection m_encodedBufferList;

    }
}
