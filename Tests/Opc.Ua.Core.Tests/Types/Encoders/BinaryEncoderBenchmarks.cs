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
using System.Text;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    [TestFixture, Category("BinaryEncoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [NonParallelizable]
    [MemoryDiagnoser]
    [DisassemblyDiagnoser]
    public class BinaryEncoderBenchmarks : EncoderBenchmarks
    {
        /// <summary>
        /// Test encoding with internal memory stream.
        /// </summary>
        [Test]
        public void BinaryEncoderInternalMemoryStreamTest()
        {
            using (var binaryEncoder = new BinaryEncoder(m_context))
            {
                TestEncoding(binaryEncoder);
                var result = binaryEncoder.CloseAndReturnBuffer();
                Assert.NotNull(result);
            }
        }


        /// <summary>
        /// Test encoding with internal memory stream,
        /// uses reflection to get array from memory stream.
        /// </summary>
        [Test]
        public void BinaryEncoderConstructorStreamwriterReflection2()
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                var result = binaryEncoder.CloseAndReturnBuffer();
                Assert.NotNull(result);
            }
        }

        /// <summary>
        /// Test encoding with ArrayPool memory stream.
        /// </summary>
        [Theory]
        public void BinaryEncoderArraySegmentStreamTest(bool toArray)
        {
            using (var memoryStream = new ArraySegmentStream(m_bufferManager))
            {
                TestStreamEncode(memoryStream, toArray);
            }
        }

        /// <summary>
        /// Test encoding with memory stream kept open.
        /// </summary>
        [Theory]
        public void BinaryEncoderMemoryStreamTest(bool toArray)
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            {
                TestStreamEncode(memoryStream, toArray);
            }
        }

        /// <summary>
        /// Test encoding with recyclable memory stream kept open.
        /// </summary>
        [Theory]
        public void BinaryEncoderRecyclableMemoryStream(bool toArray)
        {
            using (var memoryStream = new RecyclableMemoryStream(m_memoryManager))
            {
                TestStreamEncode(memoryStream, toArray);
            }
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open.
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void BinaryEncoderMemoryStream()
        {
            using (var memoryStream = new MemoryStream(StreamBufferSize))
            {
                BinaryEncoder_StreamLeaveOpen(memoryStream);
                // get buffer for write
                _ = memoryStream.ToArray();
            }
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderRecyclableMemoryStream()
        {
            using (var recyclableMemoryStream = new RecyclableMemoryStream(m_memoryManager))
            {
                BinaryEncoder_StreamLeaveOpen(recyclableMemoryStream);
                // get buffers for write
                _ = recyclableMemoryStream.GetReadOnlySequence();
            }
        }

        /// <summary>
        /// Benchmark encoding with array segment memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderArraySegmentStream()
        {
            using (var arraySegmentStream = new ArraySegmentStream(m_bufferManager))
            {
                BinaryEncoder_StreamLeaveOpen(arraySegmentStream);
                // get buffers and return them to buffer manager
                var buffers = arraySegmentStream.GetBuffers("writer");
                foreach (var buffer in buffers)
                {
                    m_bufferManager.ReturnBuffer(buffer.Array, "testreturn");
                }
            }
        }

        /// <summary>
        /// Benchmark encoding with array segment memory stream kept open,
        /// to compare the version without span support.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderArraySegmentStreamNoSpan()
        {
#if NET6_0_OR_GREATER && ECC_SUPPORT
            using (var arraySegmentStream = new ArraySegmentStreamNoSpan(m_bufferManager))
#else
            using (var arraySegmentStream = new ArraySegmentStream(m_bufferManager))
#endif
            {
                BinaryEncoder_StreamLeaveOpen(arraySegmentStream);
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
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                _ = binaryEncoder.Close();
            }
            using (var binaryEncoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                if (toArray)
                {
                    int length = binaryEncoder.Close();
                    Assert.AreEqual(length, memoryStream.Position);
                    var result = memoryStream.ToArray();
                    Assert.NotNull(result);
                    Assert.AreEqual(length, result.Length);
                }
                else
                {
                    var result = binaryEncoder.CloseAndReturnBuffer();
                    Assert.NotNull(result);
                }
            }
        }

        private int BinaryEncoder_StreamLeaveOpen(MemoryStream memoryStream, bool testResult = false)
        {
            int length1;
            int length2;
            using (var encoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(encoder);
                length1 = encoder.Close();
            }
            using (var encoder = new BinaryEncoder(memoryStream, m_context, true))
            {
                TestEncoding(encoder);
                length2 = encoder.Close();
            }
            if (testResult)
            {
                Assert.AreEqual(length1 * 2, length2);
                var result = Encoding.UTF8.GetString(memoryStream.ToArray());
                Assert.NotNull(result);
                Assert.AreEqual(length2, memoryStream.Position);
            }
            return length1 + length2;
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
