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
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
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
        public void StreamWriter_RecyclableMemoryStream()
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
        public void StreamWriter_MemoryStream()
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
        public void JsonEncoder_Constructor2()
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
        public void JsonEncoder_StreamLeaveOpen_MemoryStream()
        {
            JsonEncoder_StreamLeaveOpen(m_memoryStream);
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoder_StreamLeaveOpen_RecyclableMemoryStream()
        {
            JsonEncoder_StreamLeaveOpen(m_recyclableMemoryStream);
        }

        /// <summary>
        /// Benchmark encoding with recyclable memory stream kept open.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoder_StreamLeaveOpen_ArraySegmentStream()
        {
            JsonEncoder_StreamLeaveOpen(m_arraySegmentStream);
        }

        /// <summary>
        /// Benchmark encoding with memory stream kept open,
        /// use internal reflection to get string from memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void JsonEncoder_Constructor_Streamwriter_Reflection2()
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
}
