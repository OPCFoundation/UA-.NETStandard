// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.Encoders.Tests
{
    [TestFixture]
    [Category("BinaryEncoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
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
            using var binaryDecoder = new BinaryDecoder(m_encodedByteArray, m_context);
            TestDecoding(binaryDecoder);
        }

        /// <summary>
        /// Test decoding with ArrayPool memory stream.
        /// </summary>
        [Test]
        public void BinaryDecoderArraySegmentStreamTest()
        {
            using var memoryStream = new ArraySegmentStream(m_encodedBufferList);
            using var binaryDecoder = new BinaryDecoder(memoryStream, m_context);
            TestDecoding(binaryDecoder);
        }

        /// <summary>
        /// Test decoding with ArrayPool memory stream that has no span support.
        /// </summary>
        [Test]
        public void BinaryDecoderArraySegmentStreamNoSpanTest()
        {
#if NET6_0_OR_GREATER
            using var arraySegmentStream = new ArraySegmentStreamNoSpan(m_encodedBufferList);
#else
            using var arraySegmentStream = new ArraySegmentStream(m_encodedBufferList);
#endif
            using var binaryDecoder = new BinaryDecoder(arraySegmentStream, m_context);
            TestDecoding(binaryDecoder);
        }

        /// <summary>
        /// Benchmark decoding with memory stream.
        /// </summary>
        [Benchmark(Baseline = true)]
        [Test]
        public void BinaryDecoderMemoryStream()
        {
            using var memoryStream = new MemoryStream(m_encodedByteArray);
            BinaryDecoder_Stream(memoryStream);
        }

        /// <summary>
        /// Benchmark decoding with array segment memory stream.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryDecoderArraySegmentStream()
        {
            using var arraySegmentStream = new ArraySegmentStream(m_encodedBufferList);
            BinaryDecoder_Stream(arraySegmentStream);
        }

        /// <summary>
        /// Benchmark decoding with array segment memory stream without span support.
        /// </summary>
        [Benchmark]
        [Test]
        public void BinaryDecoderArraySegmentStreamNoSpan()
        {
#if NET6_0_OR_GREATER
            using var arraySegmentStream = new ArraySegmentStreamNoSpan(m_encodedBufferList);
#else
            using var arraySegmentStream = new ArraySegmentStream(m_encodedBufferList);
#endif
            BinaryDecoder_Stream(arraySegmentStream);
        }

        private void BinaryDecoder_Stream(MemoryStream memoryStream)
        {
            using var binaryDecoder = new BinaryDecoder(memoryStream, m_context);
            TestDecoding(binaryDecoder);
            TestDecoding(binaryDecoder);
            binaryDecoder.Close();
        }

        [OneTimeSetUp]
        public override void OneTimeSetUp()
        {
            base.OneTimeSetUp();
            InitializeEncodedTestData();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();
        }

        /// <summary>
        /// Set up some variables for benchmarks.
        /// </summary>
        [GlobalSetup]
        public override void GlobalSetup()
        {
            base.GlobalSetup();
            InitializeEncodedBenchmarkData();
        }

        /// <summary>
        /// Tear down benchmark variables.
        /// </summary>
        [GlobalCleanup]
        public override void GlobalCleanup()
        {
            base.GlobalCleanup();
        }

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
                m_encodedByteArray = binaryEncoder.CloseAndReturnBuffer()!;
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
                m_encodedByteArray = binaryEncoder.CloseAndReturnBuffer()!;
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

        private byte[] m_encodedByteArray;
        private BufferCollection m_encodedBufferList;
    }
}
