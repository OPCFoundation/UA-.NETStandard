/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Benchmarks
{
    /// <summary>
    /// UADP encoder / decoder round-trip micro-benchmarks. Covers
    /// dataset shapes used in CTT and the reference applications.
    /// Implements the
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP NetworkMessage</see> mapping.
    /// </summary>
    [MemoryDiagnoser]
    public class UadpEncodingBenchmarks
    {
        private const ushort PublisherIdValue = 1234;
        private const ushort WriterGroupIdValue = 5;
        private const ushort DataSetWriterIdValue = 100;

        private UadpEncoder m_encoder = null!;
        private UadpDecoder m_decoder = null!;
        private PubSubNetworkMessageContext m_context = null!;

        private UadpNetworkMessage m_singleField = null!;
        private UadpNetworkMessage m_tenFields = null!;
        private UadpNetworkMessage m_hundredFields = null!;
        private UadpNetworkMessage m_strings = null!;
        private UadpNetworkMessage m_largeArray = null!;

        private ReadOnlyMemory<byte> m_singleFieldBytes;
        private ReadOnlyMemory<byte> m_tenFieldsBytes;
        private ReadOnlyMemory<byte> m_hundredFieldsBytes;
        private ReadOnlyMemory<byte> m_stringsBytes;
        private ReadOnlyMemory<byte> m_largeArrayBytes;

        [GlobalSetup]
        public async Task SetupAsync()
        {
            m_encoder = new UadpEncoder();
            m_decoder = new UadpDecoder();
            m_context = BenchmarkContext.NewContext();

            m_singleField = BuildScalar("UInt32-1", 1, BuiltInType.UInt32, () => new Variant(42U));
            m_tenFields = BuildMixedPrimitives("Mixed-10", 10);
            m_hundredFields = BuildMixedPrimitives("Mixed-100", 100);
            m_strings = BuildStrings("Strings-10", 10, 64);
            m_largeArray = BuildFloatArray("Floats-256", 256);

            m_singleFieldBytes = await m_encoder.EncodeAsync(m_singleField, m_context).ConfigureAwait(false);
            m_tenFieldsBytes = await m_encoder.EncodeAsync(m_tenFields, m_context).ConfigureAwait(false);
            m_hundredFieldsBytes = await m_encoder.EncodeAsync(m_hundredFields, m_context).ConfigureAwait(false);
            m_stringsBytes = await m_encoder.EncodeAsync(m_strings, m_context).ConfigureAwait(false);
            m_largeArrayBytes = await m_encoder.EncodeAsync(m_largeArray, m_context).ConfigureAwait(false);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_SingleField()
        {
            return m_encoder.EncodeAsync(m_singleField, m_context);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_TenFields()
        {
            return m_encoder.EncodeAsync(m_tenFields, m_context);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_HundredFields()
        {
            return m_encoder.EncodeAsync(m_hundredFields, m_context);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Strings()
        {
            return m_encoder.EncodeAsync(m_strings, m_context);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_LargeArray()
        {
            return m_encoder.EncodeAsync(m_largeArray, m_context);
        }

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_SingleField()
        {
            return m_decoder.TryDecodeAsync(m_singleFieldBytes, m_context);
        }

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_TenFields()
        {
            return m_decoder.TryDecodeAsync(m_tenFieldsBytes, m_context);
        }

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_HundredFields()
        {
            return m_decoder.TryDecodeAsync(m_hundredFieldsBytes, m_context);
        }

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_Strings()
        {
            return m_decoder.TryDecodeAsync(m_stringsBytes, m_context);
        }

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_LargeArray()
        {
            return m_decoder.TryDecodeAsync(m_largeArrayBytes, m_context);
        }

        private static UadpNetworkMessage BuildScalar(
            string name, int fieldCount, BuiltInType type, Func<Variant> factory)
        {
            var fields = new DataSetField[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                fields[i] = new DataSetField { Value = factory() };
            }
            return new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId,
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                WriterGroupId = WriterGroupIdValue,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = DataSetWriterIdValue,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = fields
                    }
                ]
            };
        }

        private static UadpNetworkMessage BuildMixedPrimitives(string name, int fieldCount)
        {
            var fields = new DataSetField[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                fields[i] = (i % 5) switch
                {
                    0 => new DataSetField { Value = new Variant((uint)i) },
                    1 => new DataSetField { Value = new Variant(i / 3.0) },
                    2 => new DataSetField { Value = new Variant(i % 2 == 0) },
                    3 => new DataSetField { Value = new Variant((short)i) },
                    _ => new DataSetField { Value = new Variant((long)i) }
                };
            }
            return new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId,
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                WriterGroupId = WriterGroupIdValue,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = DataSetWriterIdValue,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = fields
                    }
                ]
            };
        }

        private static UadpNetworkMessage BuildStrings(string name, int fieldCount, int length)
        {
            var fields = new DataSetField[fieldCount];
            string sample = new('x', length);
            for (int i = 0; i < fieldCount; i++)
            {
                fields[i] = new DataSetField { Value = new Variant(sample) };
            }
            return new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId,
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                WriterGroupId = WriterGroupIdValue,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = DataSetWriterIdValue,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = fields
                    }
                ]
            };
        }

        private static UadpNetworkMessage BuildFloatArray(string name, int length)
        {
            float[] payload = new float[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = i * 0.5f;
            }
            return new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId,
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                WriterGroupId = WriterGroupIdValue,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = DataSetWriterIdValue,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields =
                        [
                            new DataSetField
                            {
                                Value = (Variant)new ArrayOf<float>(payload.AsMemory())
                            }
                        ]
                    }
                ]
            };
        }
    }
}
