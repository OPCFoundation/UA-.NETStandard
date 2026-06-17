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
using Opc.Ua.PubSub.MetaData;
using PsJson = Opc.Ua.PubSub.Encoding.Json;

namespace Opc.Ua.PubSub.Bench
{
    /// <summary>
    /// JSON encoder / decoder round-trip micro-benchmarks across two
    /// of the three Part 14 v1.05.06 encoding modes (Verbose,
    /// Compact). Implements the
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5 JSON NetworkMessage</see> mapping.
    /// </summary>
    [MemoryDiagnoser]
    public class JsonEncodingBenchmarks
    {
        private const ushort PublisherIdValue = 1234;
        private const ushort DataSetWriterIdValue = 100;

        private PsJson.JsonEncoder m_verbose = null!;
        private PsJson.JsonEncoder m_compact = null!;
        private PsJson.JsonDecoder m_decoder = null!;
        private PubSubNetworkMessageContext m_context = null!;

        private PsJson.JsonNetworkMessage m_singleField = null!;
        private PsJson.JsonNetworkMessage m_tenFields = null!;
        private PsJson.JsonNetworkMessage m_hundredFields = null!;
        private PsJson.JsonNetworkMessage m_strings = null!;
        private PsJson.JsonNetworkMessage m_largeArray = null!;

        private ReadOnlyMemory<byte> m_singleFieldBytes;
        private ReadOnlyMemory<byte> m_tenFieldsBytes;
        private ReadOnlyMemory<byte> m_hundredFieldsBytes;

        [GlobalSetup]
        public async Task SetupAsync()
        {
            m_verbose = new PsJson.JsonEncoder(PsJson.JsonEncodingMode.Verbose);
            m_compact = new PsJson.JsonEncoder(PsJson.JsonEncodingMode.Compact);
            m_decoder = new PsJson.JsonDecoder();
            m_context = BenchmarkContext.NewContext();

            DataSetMetaDataType meta = BenchmarkContext.BuildScalarMetaData(
                "Mixed-100",
                BuildFieldDescriptions(100));
            BenchmarkContext.Registry.Register(
                new DataSetMetaDataKey(
                    PublisherId.FromUInt16(PublisherIdValue), 0, 1, Uuid.Empty, 1),
                meta);

            m_singleField = BuildMessage(BuildScalarFields(1));
            m_tenFields = BuildMessage(BuildScalarFields(10));
            m_hundredFields = BuildMessage(BuildScalarFields(100));
            m_strings = BuildMessage(BuildStringFields(10, 64));
            m_largeArray = BuildMessage(BuildLargeArrayFields(256));

            m_singleFieldBytes = await m_verbose.EncodeAsync(m_singleField, m_context).ConfigureAwait(false);
            m_tenFieldsBytes = await m_verbose.EncodeAsync(m_tenFields, m_context).ConfigureAwait(false);
            m_hundredFieldsBytes = await m_verbose.EncodeAsync(m_hundredFields, m_context).ConfigureAwait(false);
        }

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Verbose_TenFields()
            => m_verbose.EncodeAsync(m_tenFields, m_context);

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Compact_TenFields()
            => m_compact.EncodeAsync(m_tenFields, m_context);

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Verbose_SingleField()
            => m_verbose.EncodeAsync(m_singleField, m_context);

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Verbose_HundredFields()
            => m_verbose.EncodeAsync(m_hundredFields, m_context);

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Verbose_Strings()
            => m_verbose.EncodeAsync(m_strings, m_context);

        [Benchmark]
        public ValueTask<ReadOnlyMemory<byte>> Encode_Verbose_LargeArray()
            => m_verbose.EncodeAsync(m_largeArray, m_context);

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_SingleField()
            => m_decoder.TryDecodeAsync(m_singleFieldBytes, m_context);

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_TenFields()
            => m_decoder.TryDecodeAsync(m_tenFieldsBytes, m_context);

        [Benchmark]
        public ValueTask<PubSubNetworkMessage?> Decode_HundredFields()
            => m_decoder.TryDecodeAsync(m_hundredFieldsBytes, m_context);

        private static PsJson.JsonNetworkMessage BuildMessage(DataSetField[] fields)
        {
            return new PsJson.JsonNetworkMessage
            {
                MessageId = "bench",
                PublisherId = PublisherId.FromUInt16(PublisherIdValue),
                DataSetClassId = Uuid.Empty,
                DataSetMessages =
                [
                    new PsJson.JsonDataSetMessage
                    {
                        DataSetWriterId = DataSetWriterIdValue,
                        SequenceNumber = 1,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = 1,
                            MinorVersion = 0
                        },
                        Fields = fields
                    }
                ]
            };
        }

        private static (string Name, BuiltInType Type)[] BuildFieldDescriptions(int count)
        {
            var result = new (string, BuiltInType)[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ($"Field-{i}", (i % 5) switch
                {
                    0 => BuiltInType.UInt32,
                    1 => BuiltInType.Double,
                    2 => BuiltInType.Boolean,
                    3 => BuiltInType.Int16,
                    _ => BuiltInType.Int64
                });
            }
            return result;
        }

        private static DataSetField[] BuildScalarFields(int count)
        {
            var fields = new DataSetField[count];
            for (int i = 0; i < count; i++)
            {
                Variant value = (i % 5) switch
                {
                    0 => new Variant((uint)i),
                    1 => new Variant((double)i / 3.0),
                    2 => new Variant(i % 2 == 0),
                    3 => new Variant((short)i),
                    _ => new Variant((long)i)
                };
                fields[i] = new DataSetField
                {
                    Name = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Field-{0}", i),
                    Value = value,
                    Encoding = PubSubFieldEncoding.Variant
                };
            }
            return fields;
        }

        private static DataSetField[] BuildStringFields(int count, int length)
        {
            var fields = new DataSetField[count];
            string sample = new('x', length);
            for (int i = 0; i < count; i++)
            {
                fields[i] = new DataSetField
                {
                    Name = $"S-{i}",
                    Value = new Variant(sample),
                    Encoding = PubSubFieldEncoding.Variant
                };
            }
            return fields;
        }

        private static DataSetField[] BuildLargeArrayFields(int length)
        {
            float[] payload = new float[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = i * 0.5f;
            }
            return
            [
                new DataSetField
                {
                    Name = "Floats",
                    Value = (Variant)new ArrayOf<float>(payload.AsMemory()),
                    Encoding = PubSubFieldEncoding.Variant
                }
            ];
        }
    }
}
