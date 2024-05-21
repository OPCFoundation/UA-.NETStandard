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

#pragma warning disable CA5394 // Do not use insecure randomness

using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.IO;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Common code for encoder/decoder benchmarks.
    /// </summary>
    public class EncoderBenchmarks
    {
        public const int StreamBufferSize = 4096;
        public const int DataValueCount = 10;

        public EncoderBenchmarks()
        {
            m_random = new Random(0x62541);
            m_nodeId = new NodeId((uint)m_random.Next(50000));
            m_list = new List<int>();
            for (int i = 0; i < DataValueCount; i++)
            {
                m_list.Add(m_random.Next());
            }
            m_values = new List<DataValue>();
            DateTime now = new DateTime(2024, 03, 01, 06, 05, 59, DateTimeKind.Utc);
            now += TimeSpan.FromTicks(456789);
            for (int i = 0; i < DataValueCount; i++)
            {
                m_values.Add(new DataValue(new Variant((m_random.NextDouble() - 0.5) * 1000.0), m_random.NextDouble() > 0.1 ? StatusCodes.Good : StatusCodes.BadDataLost, now, now));
            }
        }

        [Params(64, 1024)]
        public int PayLoadSize { get; set; } = 64;

        #region Private Methods
        protected void TestEncoding(IEncoder encoder)
        {
            var now = DateTime.UtcNow;
            int payLoadSize = PayLoadSize;
            encoder.WriteInt32("PayloadSize", payLoadSize);
            while (payLoadSize-- > 0)
            {
                encoder.WriteBoolean("Boolean", true);
                encoder.WriteByte("Byte", 123);
                encoder.WriteUInt16("UInt16", 1234);
                encoder.WriteUInt32("UInt32", 123456);
                encoder.WriteUInt64("UInt64", 1234566890);
                encoder.WriteSByte("Int8", -123);
                encoder.WriteInt16("Int16", -1234);
                encoder.WriteInt32("Int32", -123456);
                encoder.WriteInt64("Int64", -1234566890);
                encoder.WriteFloat("Float", 123.456f);
                encoder.WriteDouble("Double", 123456.789);
                encoder.WriteDateTime("DateTime", now);
                encoder.WriteString("String", "The quick brown fox jumps over the lazy dog.");
                encoder.WriteNodeId("NodeId", m_nodeId);
                encoder.WriteNodeId("ExpandedNodeId", m_nodeId);
                encoder.WriteInt32Array("Array", m_list);
                encoder.WriteDataValueArray("DataValues", m_values);
            }
        }

        protected void TestDecoding(IDecoder decoder)
        {
            var payLoadSize = decoder.ReadInt32("PayloadSize");
            while (payLoadSize-- > 0)
            {
                _ = decoder.ReadBoolean("Boolean");
                _ = decoder.ReadByte("Byte");
                _ = decoder.ReadUInt16("UInt16");
                _ = decoder.ReadUInt32("UInt32");
                _ = decoder.ReadUInt64("UInt64");
                _ = decoder.ReadSByte("Int8");
                _ = decoder.ReadInt16("Int16");
                _ = decoder.ReadInt32("Int32");
                _ = decoder.ReadInt64("Int64");
                _ = decoder.ReadFloat("Float");
                _ = decoder.ReadDouble("Double");
                _ = decoder.ReadDateTime("DateTime");
                _ = decoder.ReadString("String");
                _ = decoder.ReadNodeId("NodeId");
                _ = decoder.ReadNodeId("ExpandedNodeId");
                _ = decoder.ReadInt32Array("Array");
                _ = decoder.ReadDataValueArray("DataValues");
            }
        }
        #endregion

        #region Test Setup
        public void OneTimeSetUp()
        {
            // for validating benchmark tests
            m_context = new ServiceMessageContext();
            m_memoryManager = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = StreamBufferSize });
            m_bufferManager = new BufferManager(nameof(BinaryEncoder), StreamBufferSize);
        }

        public void OneTimeTearDown()
        {
            m_context = null;
            m_memoryManager = null;
            m_bufferManager = null;
        }
        #endregion

        #region Benchmark Setup
        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        public void GlobalSetup()
        {
            // for validating benchmark tests
            m_context = new ServiceMessageContext();
            m_memoryManager = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = StreamBufferSize });
            m_bufferManager = new BufferManager(nameof(BinaryEncoder), StreamBufferSize);
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        public void GlobalCleanup()
        {
            m_context = null;
            m_memoryManager = null;
            m_bufferManager = null;
        }
        #endregion

        #region Protected Fields
        protected Random m_random;
        protected NodeId m_nodeId = new NodeId(1234);
        protected List<Int32> m_list;
        protected List<DataValue> m_values;
        protected IServiceMessageContext m_context;
        protected RecyclableMemoryStreamManager m_memoryManager;
        protected BufferManager m_bufferManager;
        #endregion
    }

#if NET6_0_OR_GREATER && ECC_SUPPORT
    /// <summary>
    /// Helper class to test ArraySegmentStream without Span support.
    /// </summary>
    public class ArraySegmentStreamNoSpan : ArraySegmentStream
    {
        public ArraySegmentStreamNoSpan(BufferManager bufferManager)
            : base(bufferManager)
        {
        }

        public ArraySegmentStreamNoSpan(BufferCollection buffers)
            : base(buffers)
        {
        }

        public override int Read(Span<byte> buffer)
        {
            return base.ReadMemoryStream(buffer);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.WriteMemoryStream(buffer);
        }
    }
#endif
}
