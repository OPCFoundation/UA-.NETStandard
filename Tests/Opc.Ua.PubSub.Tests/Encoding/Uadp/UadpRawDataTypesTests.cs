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
 *
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
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Encoding.Uadp
{
    /// <summary>
    /// Targeted coverage for RawData field encoding/decoding of every
    /// OPC UA built-in scalar and one-dimensional array — exercises
    /// the WriteRawScalarCore / WriteRawArrayCore branches.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.5.4")]
    public class UadpRawDataTypesTests
    {
        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        public async Task RawData_Scalar_RoundTrip(BuiltInType builtIn)
        {
            await RoundTripRawDataAsync(builtIn, ValueRanks.Scalar)
                .ConfigureAwait(false);
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        public async Task RawData_Array_RoundTrip(BuiltInType builtIn)
        {
            await RoundTripRawDataAsync(builtIn, ValueRanks.OneDimension)
                .ConfigureAwait(false);
        }

        private static async Task RoundTripRawDataAsync(
            BuiltInType builtIn, int valueRank)
        {
            var registry = new DataSetMetaDataRegistry();
            PubSubNetworkMessageContext context =
                UadpTestUtilities.NewContext(registry);

            var publisherId = PublisherId.FromByte(1);
            const ushort writerGroupId = 1;
            const ushort writerId = 100;
            var classId = (Uuid)Guid.Empty;
            const uint majorVer = 1;

            var meta = new DataSetMetaDataType
            {
                Name = "RawMeta",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVer,
                    MinorVersion = 0
                },
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "f0",
                        BuiltInType = (byte)builtIn,
                        ValueRank = valueRank
                    }
                ]
            };
            registry.Register(
                new DataSetMetaDataKey(publisherId, writerGroupId, writerId,
                    classId, majorVer),
                meta);

            Variant value = SampleVariant(builtIn, valueRank);

            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.GroupHeader
                    | UadpNetworkMessageContentMask.WriterGroupId
                    | UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = writerId,
                        ContentMask = UadpDataSetMessageContentMask.MajorVersion,
                        MetaDataVersion = new ConfigurationVersionDataType
                        {
                            MajorVersion = majorVer, MinorVersion = 0
                        },
                        FieldEncoding = PubSubFieldEncoding.RawData,
                        Fields = [new DataSetField { Value = value }]
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await new UadpEncoder().EncodeAsync(msg, context).ConfigureAwait(false);
            var decoded = (UadpNetworkMessage?)UadpDecoder.Decode(bytes, context);
            Assert.That(decoded, Is.Not.Null,
                $"Decode failed for {builtIn} rank={valueRank}");
            Assert.That(((PubSubDataSetMessage[]?)decoded!.DataSetMessages) ?? [], Has.Length.EqualTo(1));
            var dsm = (UadpDataSetMessage)decoded.DataSetMessages[0];
            Assert.That(((DataSetField[]?)dsm.Fields) ?? [], Has.Length.EqualTo(1));
        }

        private static readonly bool[] s_boolArr = [true, false, true];
        private static readonly sbyte[] s_sbyteArr = [-1, 2, -3];
        private static readonly byte[] s_byteArr = [1, 2, 3];
        private static readonly short[] s_shortArr = [1, 2, 3];
        private static readonly ushort[] s_ushortArr = [1, 2, 3];
        private static readonly int[] s_intArr = [1, 2, 3];
        private static readonly uint[] s_uintArr = [1u, 2u, 3u];
        private static readonly long[] s_longArr = [1L, 2L, 3L];
        private static readonly ulong[] s_ulongArr = [1UL, 2UL, 3UL];
        private static readonly float[] s_floatArr = [1.0f, 2.0f];
        private static readonly double[] s_doubleArr = [1.0, 2.0];
        private static readonly string[] s_stringArr = ["a", "b"];
        private static readonly byte[] s_byteStringPayload = [9, 8, 7];

        private static Variant SampleVariant(BuiltInType builtIn, int rank)
        {
            if (rank == ValueRanks.Scalar)
            {
                return builtIn switch
                {
                    BuiltInType.Boolean => (Variant)true,
                    BuiltInType.SByte => (Variant)(sbyte)-7,
                    BuiltInType.Byte => (Variant)(byte)42,
                    BuiltInType.Int16 => (Variant)(short)-12345,
                    BuiltInType.UInt16 => (Variant)(ushort)54321,
                    BuiltInType.Int32 => (Variant)(-100000),
                    BuiltInType.UInt32 => (Variant)123456u,
                    BuiltInType.Int64 => (Variant)(-1234567890123L),
                    BuiltInType.UInt64 => (Variant)1234567890123UL,
                    BuiltInType.Float => (Variant)3.14f,
                    BuiltInType.Double => (Variant)2.7182818,
                    BuiltInType.String => (Variant)"raw-string",
                    BuiltInType.DateTime =>
                        (Variant)(DateTimeUtc)new DateTime(
                            2026, 6, 15, 0, 0, 0, DateTimeKind.Utc).Ticks,
                    BuiltInType.Guid =>
                        (Variant)(Uuid)new Guid(
                            "11112222-3333-4444-5555-666677778888"),
                    BuiltInType.ByteString =>
                        (Variant)new ByteString(s_byteStringPayload),
                    BuiltInType.NodeId =>
                        (Variant)new NodeId(1234u, 2),
                    BuiltInType.ExpandedNodeId =>
                        (Variant)new ExpandedNodeId(
                            new NodeId(99u, 1), "ns", 0),
                    BuiltInType.StatusCode =>
                        (Variant)new StatusCode((uint)StatusCodes.GoodCallAgain),
                    BuiltInType.QualifiedName =>
                        (Variant)new QualifiedName("Field", 1),
                    BuiltInType.LocalizedText =>
                        (Variant)new LocalizedText("en", "hello"),
                    _ => default
                };
            }
            return builtIn switch
            {
                BuiltInType.Boolean => (Variant)new ArrayOf<bool>(s_boolArr),
                BuiltInType.SByte => (Variant)new ArrayOf<sbyte>(s_sbyteArr),
                BuiltInType.Byte => (Variant)new ArrayOf<byte>(s_byteArr),
                BuiltInType.Int16 => (Variant)new ArrayOf<short>(s_shortArr),
                BuiltInType.UInt16 => (Variant)new ArrayOf<ushort>(s_ushortArr),
                BuiltInType.Int32 => (Variant)new ArrayOf<int>(s_intArr),
                BuiltInType.UInt32 => (Variant)new ArrayOf<uint>(s_uintArr),
                BuiltInType.Int64 => (Variant)new ArrayOf<long>(s_longArr),
                BuiltInType.UInt64 => (Variant)new ArrayOf<ulong>(s_ulongArr),
                BuiltInType.Float => (Variant)new ArrayOf<float>(s_floatArr),
                BuiltInType.Double => (Variant)new ArrayOf<double>(s_doubleArr),
                BuiltInType.String => (Variant)new ArrayOf<string>(s_stringArr),
                _ => default
            };
        }

        [Test]
        public async Task DataValueEncoding_RoundTrips()
        {
            PubSubNetworkMessageContext context = UadpTestUtilities.NewContext();
            var msg = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromByte(2),
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        FieldEncoding = PubSubFieldEncoding.DataValue,
                        Fields =
                        [
                            new DataSetField
                            {
                                Value = (Variant)42,
                                StatusCode = (StatusCode)StatusCodes.Good,
                                SourceTimestamp = (DateTimeUtc)new DateTime(
                                    2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks
                            }
                        ]
                    }
                ]
            };
            ReadOnlyMemory<byte> bytes =
                await new UadpEncoder().EncodeAsync(msg, context).ConfigureAwait(false);
            var decoded = (UadpNetworkMessage?)UadpDecoder.Decode(bytes, context);
            Assert.That(decoded, Is.Not.Null);
            var dsm = (UadpDataSetMessage)decoded!.DataSetMessages[0];
            Assert.That(((DataSetField[]?)dsm.Fields) ?? [], Has.Length.EqualTo(1));
        }
    }
}
