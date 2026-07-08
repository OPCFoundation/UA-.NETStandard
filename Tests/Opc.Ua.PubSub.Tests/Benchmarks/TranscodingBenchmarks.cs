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
using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;
using JsonDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage;
using JsonEncoderV2 = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpDataSetMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpEncoderV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessageContentMask = Opc.Ua.UadpNetworkMessageContentMask;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Benchmarks
{
    /// <summary>
    /// Micro-benchmarks for the PubSub transcoder covering cross-encoding
    /// (UADP↔JSON), the raw-frame identity fast path, and a field
    /// projection pipeline. Implements the transcode path over the
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4</see> / §7.2.5 mappings.
    /// </summary>
    [MemoryDiagnoser]
    public class TranscodingBenchmarks
    {
        private TranscodeContext m_context = null!;
        private PubSubTranscoder m_uadpToJson = null!;
        private PubSubTranscoder m_jsonToUadp = null!;
        private PubSubTranscoder m_identity = null!;
        private PubSubTranscoder m_projection = null!;

        private TranscodeInput m_uadpInput;
        private TranscodeInput m_jsonInput;
        private TranscodeInput m_identityInput;

        [GlobalSetup]
        public async Task SetupAsync()
        {
            m_context = new TranscodeContext(
                BenchmarkContext.NewContext(), BenchmarkTelemetry.Instance);
            Dictionary<string, INetworkMessageEncoder> encoders = BuildEncoders();

            m_uadpToJson = new PubSubTranscoder(
                new TranscodeSpec { TargetEncoding = TranscodeEncoding.Json }, encoders, m_context);
            m_jsonToUadp = new PubSubTranscoder(
                new TranscodeSpec { TargetEncoding = TranscodeEncoding.Uadp }, encoders, m_context);
            m_identity = new PubSubTranscoder(
                new TranscodeSpec { TargetEncoding = TranscodeEncoding.Uadp }, encoders, m_context);
            m_projection = new PubSubTranscoder(
                new TranscodeSpec
                {
                    TargetEncoding = TranscodeEncoding.Json,
                    Transforms = [new FieldProjectionTransform(["f0", "f1", "f2"])]
                },
                encoders,
                m_context);

            UadpNetworkMessageV2 uadp = BuildUadp(10);
            ReadOnlyMemory<byte> frame = await new UadpEncoderV2()
                .EncodeAsync(uadp, m_context.EncodingContext).ConfigureAwait(false);
            m_uadpInput = new TranscodeInput(uadp);
            m_identityInput = new TranscodeInput(uadp, frame);
            m_jsonInput = new TranscodeInput(BuildJson(10));
        }

        [Benchmark]
        public ValueTask<TranscodeResult> UadpToJson()
        {
            return m_uadpToJson.TranscodeAsync(m_uadpInput);
        }

        [Benchmark]
        public ValueTask<TranscodeResult> JsonToUadp()
        {
            return m_jsonToUadp.TranscodeAsync(m_jsonInput);
        }

        [Benchmark(Baseline = true)]
        public ValueTask<TranscodeResult> IdentityFastPath()
        {
            return m_identity.TranscodeAsync(m_identityInput);
        }

        [Benchmark]
        public ValueTask<TranscodeResult> ProjectionToJson()
        {
            return m_projection.TranscodeAsync(m_uadpInput);
        }

        private static Dictionary<string, INetworkMessageEncoder> BuildEncoders()
        {
            var uadp = new UadpEncoderV2();
            var json = new JsonEncoderV2();
            return new Dictionary<string, INetworkMessageEncoder>(StringComparer.Ordinal)
            {
                [uadp.TransportProfileUri] = uadp,
                [json.TransportProfileUri] = json
            };
        }

        private static UadpNetworkMessageV2 BuildUadp(int fieldCount)
        {
            return new UadpNetworkMessageV2
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(1),
                WriterGroupId = 10,
                DataSetMessages =
                [
                    new UadpDataSetMessageV2
                    {
                        DataSetWriterId = 100,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = BuildFields(fieldCount)
                    }
                ]
            };
        }

        private static JsonNetworkMessageV2 BuildJson(int fieldCount)
        {
            return new JsonNetworkMessageV2
            {
                PublisherId = PublisherId.FromByte(1),
                WriterGroupId = 10,
                DataSetMessages =
                [
                    new JsonDataSetMessageV2
                    {
                        DataSetWriterId = 100,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields = BuildFields(fieldCount)
                    }
                ]
            };
        }

        private static DataSetField[] BuildFields(int fieldCount)
        {
            var fields = new DataSetField[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                fields[i] = new DataSetField
                {
                    Name = "f" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Value = new Variant(i)
                };
            }
            return fields;
        }

        private sealed class BenchmarkTelemetry : TelemetryContextBase
        {
            public static readonly BenchmarkTelemetry Instance = new();

            private BenchmarkTelemetry()
                : base(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            {
            }
        }
    }
}
