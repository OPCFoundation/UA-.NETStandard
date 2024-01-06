/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
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
    public class BinaryEncoderBenchmarks
    {
        const int kBufferSize = 4096;

        [Params(1, 2, 8, 64)]
        public int PayLoadSize { get; set; } = 64;

        /// <summary>
        /// Benchmark encoding with internal memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderConstructor2()
        {
            using (var binaryEncoder = new BinaryEncoder(m_context))
            {
                TestEncoding(binaryEncoder);
                _ = binaryEncoder.CloseAndReturnBuffer();
            }
        }

        /// <summary>
        /// Benchmark encoding with ArrayPool memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderConstructor3()
        {
            using (var stream = new ArraySegmentStream(m_bufferManager, kBufferSize, 0, kBufferSize))
            {
                using (var binaryEncoder = new BinaryEncoder(stream, m_context, false))
                {
                    TestEncoding(binaryEncoder);
                    _ = binaryEncoder.CloseAndReturnBuffer();
                }
            }
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderConstructorStreamwriter2()
        {
            using (IEncoder binaryEncoder = new BinaryEncoder(m_memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                int length = binaryEncoder.Close();
                var result = Encoding.UTF8.GetString(m_memoryStream.ToArray());
            }
        }

        /// <summary>
        /// Encoding test with memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderStreamLeaveOpenMemoryStream()
        {
            BinaryEncoder_StreamLeaveOpen(m_memoryStream);
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderStreamLeaveOpenRecyclableMemoryStream()
        {
            BinaryEncoder_StreamLeaveOpen(m_recyclableMemoryStream);
        }

        /// <summary>
        /// Encoding test with memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderStreamLeaveOpenArraySegmentStream()
        {
            BinaryEncoder_StreamLeaveOpen(m_arraySegmentStream);
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open,
        /// use internal reflection to get string from memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryEncoderConstructorStreamwriterReflection2()
        {
            using (var binaryEncoder = new BinaryEncoder(m_memoryStream, m_context, true))
            {
                TestEncoding(binaryEncoder);
                var result = binaryEncoder.CloseAndReturnBuffer();
            }
        }

        #region Private Methods
        private void BinaryEncoder_StreamLeaveOpen(MemoryStream stream)
        {
            int length1;
            int length2;
            m_memoryStream.Position = 0;
            using (IEncoder encoder = new BinaryEncoder(stream, m_context, true))
            {
                TestEncoding(encoder);
                length1 = encoder.Close();
            }
            using (IEncoder encoder = new BinaryEncoder(stream, m_context, true))
            {
                TestEncoding(encoder);
                length2 = encoder.Close();
            }
            Assert.AreEqual(length1 * 2, length2);
            var result = Encoding.UTF8.GetString(stream.ToArray());
            Assert.NotNull(result);
            Assert.AreEqual(length2, stream.Position);
        }

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
}
