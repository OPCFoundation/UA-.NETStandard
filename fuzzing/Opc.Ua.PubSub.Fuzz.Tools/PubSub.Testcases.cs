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
using System.IO;
using System.Text.Json;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Encoding.Uadp;

namespace Opc.Ua.Fuzzing
{
    public static partial class Testcases
    {
        public static void Run(string workPath, ITelemetryContext telemetry)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workPath);
            _ = telemetry;
            foreach ((string bucket, string name, byte[] data) in CreatePubSubSeeds())
            {
                string directory = workPath + "." + bucket;
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name), data);
            }
        }

        internal static IEnumerable<(string Bucket, string Name, byte[] Data)> CreatePubSubSeeds()
        {
            yield return ("Json", "metadata-keyframe-verbose.json", CreateJsonSeed(JsonEncodingMode.Verbose));
            yield return ("Json", "metadata-keyframe-compact.json", CreateJsonSeed(JsonEncodingMode.Compact));
            yield return ("Json", "metadata-keyframe-raw.json", CreateJsonSeed(JsonEncodingMode.RawData));
            yield return ("Json", "metadata-keyframe-datavalue.json",
                CreateJsonSeed(JsonEncodingMode.Verbose, PubSubFieldEncoding.DataValue));
            yield return ("Uadp", "metadata-keyframe-variant.uadp", CreateUadpSeed(PubSubFieldEncoding.Variant));
            yield return ("Uadp", "metadata-keyframe-datavalue.uadp", CreateUadpSeed(PubSubFieldEncoding.DataValue));
            byte[] rawData = CreateUadpSeed(PubSubFieldEncoding.RawData);
            yield return ("Uadp", "metadata-keyframe-raw.uadp", rawData);

            var chunker = new UadpChunker();
            yield return ("Chunks", "metadata-keyframe-complete.bin",
                chunker.Split(rawData, 42, rawData.Length + UadpChunker.ChunkHeaderSize)[0]);
            IReadOnlyList<byte[]> chunks = chunker.Split(rawData, 42, 42);
            for (int i = 0; i < chunks.Count; i++)
            {
                yield return ("Chunks", FormattableString.Invariant($"metadata-keyframe-chunk-{i:000}.bin"), chunks[i]);
            }
        }

        internal static byte[] CreateJsonSeed(
            JsonEncodingMode mode,
            PubSubFieldEncoding fieldEncoding = PubSubFieldEncoding.Variant)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("MessageId", FormattableString.Invariant($"retained-{mode}-{fieldEncoding}"));
                writer.WriteString("MessageType", "ua-data");
                writer.WriteString("PublisherId", "300");
                writer.WriteString("DataSetClassId", FuzzableCode.SeedDataSetClassId.ToString());
                writer.WriteStartArray("Messages");
                writer.WriteStartObject();
                writer.WriteNumber("DataSetWriterId", 1);
                writer.WriteNumber("SequenceNumber", 42);
                writer.WriteString("MessageType", "ua-keyframe");
                writer.WriteString("Timestamp", FuzzableCode.SeedTime);
                writer.WriteStartObject("MetaDataVersion");
                writer.WriteNumber("MajorVersion", 1);
                writer.WriteNumber("MinorVersion", 2);
                writer.WriteEndObject();
                JsonFieldEncoder.EncodeFields(
                    writer,
                    CreateFields(fieldEncoding),
                    FuzzableCode.CreateMetaData(),
                    mode,
                    FuzzableCode.NewContext().MessageContext);
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            return stream.ToArray();
        }

        internal static byte[] CreateUadpSeed(PubSubFieldEncoding fieldEncoding)
        {
            return UadpEncoder.EncodeData(CreateUadpMessage(fieldEncoding), FuzzableCode.NewContext());
        }

        internal static UadpNetworkMessage CreateUadpMessage(PubSubFieldEncoding fieldEncoding)
        {
            DataSetMetaDataType metaData = FuzzableCode.CreateMetaData();
            return new UadpNetworkMessage
            {
                ContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.SequenceNumber |
                    UadpNetworkMessageContentMask.DataSetClassId |
                    UadpNetworkMessageContentMask.Timestamp,
                PublisherId = FuzzableCode.SeedPublisherId,
                WriterGroupId = 1,
                SequenceNumber = 42,
                DataSetClassId = FuzzableCode.SeedDataSetClassId,
                Timestamp = new DateTimeUtc(FuzzableCode.SeedTime),
                MetaData = metaData,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        ContentMask =
                            UadpDataSetMessageContentMask.SequenceNumber |
                            UadpDataSetMessageContentMask.MajorVersion |
                            UadpDataSetMessageContentMask.MinorVersion |
                            UadpDataSetMessageContentMask.Timestamp,
                        DataSetWriterId = 1,
                        SequenceNumber = 42,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        FieldEncoding = fieldEncoding,
                        FieldContentMask =
                            DataSetFieldContentMask.StatusCode |
                            DataSetFieldContentMask.SourceTimestamp |
                            DataSetFieldContentMask.ServerTimestamp |
                            DataSetFieldContentMask.SourcePicoSeconds |
                            DataSetFieldContentMask.ServerPicoSeconds,
                        MetaDataVersion = metaData.ConfigurationVersion,
                        Timestamp = new DateTimeUtc(FuzzableCode.SeedTime),
                        Fields = CreateFields(fieldEncoding)
                    }
                ]
            };
        }

        internal static ArrayOf<DataSetField> CreateFields(PubSubFieldEncoding encoding)
        {
            return
            [
                CreateField("Running", Variant.From(true), encoding),
                CreateField("Count", Variant.From(42), encoding) with { StatusCode = StatusCodes.Uncertain },
                CreateField("Temperature", Variant.From(21.5), encoding),
                CreateField("Label", Variant.From("pubsub"), encoding)
            ];
        }

        private static DataSetField CreateField(string name, Variant value, PubSubFieldEncoding encoding)
        {
            return new DataSetField
            {
                Name = name,
                Value = value,
                Encoding = encoding,
                SourceTimestamp = new DateTimeUtc(FuzzableCode.SeedTime),
                ServerTimestamp = new DateTimeUtc(FuzzableCode.SeedTime.AddSeconds(1)),
                SourcePicoSeconds = 10,
                ServerPicoSeconds = 20
            };
        }
    }
}
