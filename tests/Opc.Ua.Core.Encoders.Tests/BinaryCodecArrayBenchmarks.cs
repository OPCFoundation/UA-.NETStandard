/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Encoders.Tests
{
    /// <summary>
    /// The element type of an array benchmark.
    /// </summary>
    public enum CodecArrayKind
    {
        String,
        Int32,
        Double,
        NodeId,
        Variant,
        DataValue,
        ExtensionObject,
        Encodeable,
        EncodeableByTypeId
    }

    /// <summary>
    /// The message of a message benchmark.
    /// </summary>
    public enum CodecMessageKind
    {
        ReadResponse,
        PublishResponse,
        NestedVariant
    }

    /// <summary>
    /// Shared setup of the binary codec benchmarks.
    /// </summary>
    public abstract class BinaryCodecBenchmarkBase
    {
        protected static ServiceMessageContext CreateContext()
        {
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.MaxArrayLength = 10_000_000;
            context.MaxMessageSize = 256 * 1024 * 1024;
            return context;
        }

        protected static readonly DateTimeUtc s_timestamp =
            new DateTime(2024, 03, 01, 06, 05, 59, DateTimeKind.Utc);

        protected static Variant CreateScalarVariant(int ii)
        {
            return (ii % 5) switch
            {
                0 => new Variant(ii),
                1 => new Variant(ii * 0.5),
                2 => new Variant("Value" + ii),
                3 => new Variant(ii % 2 == 0),
                _ => new Variant(s_timestamp)
            };
        }

        protected static DataValue CreateDataValue(int ii)
        {
            return new DataValue(
                new Variant(ii * 0.25),
                ii % 10 == 0 ? StatusCodes.UncertainLastUsableValue : StatusCodes.Good,
                s_timestamp,
                s_timestamp);
        }

        protected static NodeId CreateNodeId(int ii)
        {
            return ii % 2 == 0
                ? new NodeId((uint)(1000 + ii), 2)
                : new NodeId("Channel1.Device1.Tag" + ii, 3);
        }

        protected static ReadValueId CreateReadValueId(int ii)
        {
            return new ReadValueId
            {
                NodeId = CreateNodeId(ii),
                AttributeId = Attributes.Value
            };
        }

        protected ServiceMessageContext m_context = null!;
        protected MemoryStream m_stream = null!;
        protected byte[] m_encoded = null!;
        protected int m_sink;
    }

    /// <summary>
    /// Encodes and decodes one large array of an element type with the
    /// binary codec.
    /// </summary>
    [TestFixture]
    [Category("BinaryEncoder")]
    [NonParallelizable]
    [MemoryDiagnoser]
    public class BinaryCodecArrayBenchmarks : BinaryCodecBenchmarkBase
    {
        [ParamsAllValues]
        public CodecArrayKind Kind { get; set; }

        [Params(16, 1024, 100_000)]
        public int Count { get; set; } = 16;

        [GlobalSetup]
        public void GlobalSetup()
        {
            m_context = CreateContext();
            m_strings = new string[Count];
            m_int32s = new int[Count];
            m_doubles = new double[Count];
            m_nodeIds = new NodeId[Count];
            m_variants = new Variant[Count];
            m_dataValues = new DataValue[Count];
            m_extensionObjects = new ExtensionObject[Count];
            m_readValueIds = new ReadValueId[Count];
            for (int ii = 0; ii < Count; ii++)
            {
                m_strings[ii] = "Channel1.Device1.Tag" + ii;
                m_int32s[ii] = ii * 7;
                m_doubles[ii] = ii * 0.5;
                m_nodeIds[ii] = CreateNodeId(ii);
                m_variants[ii] = CreateScalarVariant(ii);
                m_dataValues[ii] = CreateDataValue(ii);
                m_readValueIds[ii] = CreateReadValueId(ii);
                m_extensionObjects[ii] = new ExtensionObject(m_readValueIds[ii]);
            }

            using var encoder = new BinaryEncoder(m_context);
            WriteArray(encoder);
            m_encoded = encoder.CloseAndReturnBuffer()!;
            m_stream = new MemoryStream(m_encoded!.Length * 2);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_stream?.Dispose();
        }

        [Benchmark]
        public void Encode()
        {
            m_stream.Position = 0;
            using var encoder = new BinaryEncoder(m_stream, m_context, true);
            WriteArray(encoder);
        }

        [Benchmark]
        public int Decode()
        {
            using var decoder = new BinaryDecoder(m_encoded, m_context);
            return m_sink = ReadArray(decoder);
        }

        [Test]
        public void ArraysRoundTrip([Values] CodecArrayKind kind)
        {
            Kind = kind;
            GlobalSetup();
            try
            {
                Encode();
                Assert.That(Decode(), Is.EqualTo(Count));
            }
            finally
            {
                GlobalCleanup();
            }
        }

        private void WriteArray(BinaryEncoder encoder)
        {
            switch (Kind)
            {
                case CodecArrayKind.String:
                    encoder.WriteStringArray(null, m_strings);
                    break;
                case CodecArrayKind.Int32:
                    encoder.WriteInt32Array(null, m_int32s);
                    break;
                case CodecArrayKind.Double:
                    encoder.WriteDoubleArray(null, m_doubles);
                    break;
                case CodecArrayKind.NodeId:
                    encoder.WriteNodeIdArray(null, m_nodeIds);
                    break;
                case CodecArrayKind.Variant:
                    encoder.WriteVariantArray(null, m_variants);
                    break;
                case CodecArrayKind.DataValue:
                    encoder.WriteDataValueArray(null, m_dataValues);
                    break;
                case CodecArrayKind.ExtensionObject:
                    encoder.WriteExtensionObjectArray(null, m_extensionObjects);
                    break;
                case CodecArrayKind.Encodeable:
                case CodecArrayKind.EncodeableByTypeId:
                    encoder.WriteEncodeableArray(null, (ArrayOf<ReadValueId>)m_readValueIds);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported array kind {Kind}.");
            }
        }

        private int ReadArray(BinaryDecoder decoder)
        {
            return Kind switch
            {
                CodecArrayKind.String => decoder.ReadStringArray(null).Count,
                CodecArrayKind.Int32 => decoder.ReadInt32Array(null).Count,
                CodecArrayKind.Double => decoder.ReadDoubleArray(null).Count,
                CodecArrayKind.NodeId => decoder.ReadNodeIdArray(null).Count,
                CodecArrayKind.Variant => decoder.ReadVariantArray(null).Count,
                CodecArrayKind.DataValue => decoder.ReadDataValueArray(null).Count,
                CodecArrayKind.ExtensionObject => decoder.ReadExtensionObjectArray(null).Count,
                CodecArrayKind.Encodeable => decoder.ReadEncodeableArray<ReadValueId>(null).Count,
                CodecArrayKind.EncodeableByTypeId => decoder.ReadEncodeableArray<ReadValueId>(
                    null,
                    DataTypeIds.ReadValueId).Count,
                _ => throw new InvalidOperationException($"Unsupported array kind {Kind}.")
            };
        }

        private string[] m_strings = null!;
        private int[] m_int32s = null!;
        private double[] m_doubles = null!;
        private NodeId[] m_nodeIds = null!;
        private Variant[] m_variants = null!;
        private DataValue[] m_dataValues = null!;
        private ExtensionObject[] m_extensionObjects = null!;
        private ReadValueId[] m_readValueIds = null!;
    }

    /// <summary>
    /// Encodes and decodes many scalar Variants one by one.
    /// </summary>
    [TestFixture]
    [Category("BinaryEncoder")]
    [NonParallelizable]
    [MemoryDiagnoser]
    public class BinaryCodecScalarVariantBenchmarks : BinaryCodecBenchmarkBase
    {
        [Params(1000)]
        public int Count { get; set; } = 1000;

        [GlobalSetup]
        [OneTimeSetUp]
        public void GlobalSetup()
        {
            m_context = CreateContext();
            m_variants = new Variant[Count];
            for (int ii = 0; ii < Count; ii++)
            {
                m_variants[ii] = CreateScalarVariant(ii);
            }
            using var encoder = new BinaryEncoder(m_context);
            WriteVariants(encoder);
            m_encoded = encoder.CloseAndReturnBuffer()!;
            m_stream = new MemoryStream(m_encoded!.Length * 2);
        }

        [GlobalCleanup]
        [OneTimeTearDown]
        public void GlobalCleanup()
        {
            m_stream?.Dispose();
        }

        [Benchmark]
        [Test]
        public void EncodeScalarVariants()
        {
            m_stream.Position = 0;
            using var encoder = new BinaryEncoder(m_stream, m_context, true);
            WriteVariants(encoder);
        }

        [Benchmark]
        [Test]
        public void DecodeScalarVariants()
        {
            using var decoder = new BinaryDecoder(m_encoded, m_context);
            int count = 0;
            for (int ii = 0; ii < Count; ii++)
            {
                if (!decoder.ReadVariant(null).IsNull)
                {
                    count++;
                }
            }
            m_sink = count;
        }

        private void WriteVariants(BinaryEncoder encoder)
        {
            for (int ii = 0; ii < m_variants.Length; ii++)
            {
                encoder.WriteVariant(null, m_variants[ii]);
            }
        }

        private Variant[] m_variants;
    }

    /// <summary>
    /// Encodes and decodes realistic service messages and a nested
    /// Variant structure.
    /// </summary>
    [TestFixture]
    [Category("BinaryEncoder")]
    [NonParallelizable]
    [MemoryDiagnoser]
    public class BinaryCodecMessageBenchmarks : BinaryCodecBenchmarkBase
    {
        [ParamsAllValues]
        public CodecMessageKind Kind { get; set; }

        [Params(100, 10_000)]
        public int Count { get; set; } = 100;

        [GlobalSetup]
        public void GlobalSetup()
        {
            m_context = CreateContext();
            m_message = Kind switch
            {
                CodecMessageKind.ReadResponse => CreateReadResponse(Count),
                CodecMessageKind.PublishResponse => CreatePublishResponse(Count),
                CodecMessageKind.NestedVariant => CreateNestedVariant(Count),
                _ => throw new InvalidOperationException($"Unsupported message kind {Kind}.")
            };
            m_encoded = BinaryEncoder.EncodeMessage(m_message, m_context);
            m_stream = new MemoryStream(m_encoded.Length * 2);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_stream?.Dispose();
        }

        [Benchmark]
        public void EncodeMessage()
        {
            m_stream.Position = 0;
            BinaryEncoder.EncodeMessage(m_message, m_stream, m_context, true);
        }

        [Benchmark]
        public IEncodeable DecodeMessage()
        {
            return BinaryDecoder.DecodeMessage<IEncodeable>(m_encoded, m_context);
        }

        [Benchmark]
        public IEncodeable DecodeMessageFromStream()
        {
            using var stream = new MemoryStream(m_encoded, false);
            return BinaryDecoder.DecodeMessage<IEncodeable>(stream, m_context);
        }

        [Test]
        public void MessagesRoundTrip([Values] CodecMessageKind kind)
        {
            Kind = kind;
            GlobalSetup();
            try
            {
                EncodeMessage();
                Assert.That(DecodeMessage(), Is.EqualTo(m_message));
                Assert.That(DecodeMessageFromStream(), Is.EqualTo(m_message));
            }
            finally
            {
                GlobalCleanup();
            }
        }

        private static ReadResponse CreateReadResponse(int count)
        {
            var results = new DataValue[count];
            for (int ii = 0; ii < count; ii++)
            {
                results[ii] = CreateDataValue(ii);
            }
            return new ReadResponse
            {
                ResponseHeader = new ResponseHeader { Timestamp = s_timestamp, RequestHandle = 42 },
                Results = results
            };
        }

        private static PublishResponse CreatePublishResponse(int count)
        {
            var items = new MonitoredItemNotification[count];
            for (int ii = 0; ii < count; ii++)
            {
                items[ii] = new MonitoredItemNotification
                {
                    ClientHandle = (uint)ii,
                    Value = CreateDataValue(ii)
                };
            }
            var notification = new DataChangeNotification { MonitoredItems = items };
            return new PublishResponse
            {
                ResponseHeader = new ResponseHeader { Timestamp = s_timestamp, RequestHandle = 43 },
                SubscriptionId = 7,
                AvailableSequenceNumbers = new uint[] { 11, 12 },
                NotificationMessage = new NotificationMessage
                {
                    SequenceNumber = 12,
                    PublishTime = s_timestamp,
                    NotificationData = new ExtensionObject[] { new(notification) }
                }
            };
        }

        /// <summary>
        /// A write request whose values are Variant arrays of Variant arrays
        /// that hold DataValues and ExtensionObjects.
        /// </summary>
        private static WriteRequest CreateNestedVariant(int count)
        {
            var nodesToWrite = new WriteValue[Math.Max(1, count / 100)];
            for (int jj = 0; jj < nodesToWrite.Length; jj++)
            {
                var outer = new Variant[100];
                for (int ii = 0; ii < outer.Length; ii++)
                {
                    var inner = new Variant[]
                    {
                        CreateScalarVariant(ii),
                        new(CreateDataValue(ii)),
                        new(new ExtensionObject(CreateReadValueId(ii))),
                        new(new Variant[] { CreateScalarVariant(ii + 1), CreateScalarVariant(ii + 2) })
                    };
                    outer[ii] = new Variant(inner);
                }
                nodesToWrite[jj] = new WriteValue
                {
                    NodeId = CreateNodeId(jj),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(new Variant(outer))
                };
            }
            return new WriteRequest
            {
                RequestHeader = new RequestHeader { Timestamp = s_timestamp, RequestHandle = 44 },
                NodesToWrite = nodesToWrite
            };
        }

        private IEncodeable m_message = null!;
    }
}
