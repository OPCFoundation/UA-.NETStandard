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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Regression tests for the OPC UA Binary decoding defects reported by
    /// the codec audit (B1, T1-1, E1-8).
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class BinaryDecoderAuditTests
    {
        private static readonly int[] s_oneElement = [1];
        private static readonly bool[] s_streamModes = [false, true];
        private static readonly double[] s_twoDoubles = [1.5, 2.5];

        private static ServiceMessageContext CreateContext()
        {
            return ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
        }

        private static byte[] Build(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
            }
            return stream.ToArray();
        }

        private static void AssertStatus(Action code, StatusCode statusCode)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(code);
            Assert.That(ex.StatusCode, Is.EqualTo(statusCode));
        }

        private static BinaryDecoder CreateDecoder(
            byte[] bytes,
            IServiceMessageContext context,
            bool useStream)
        {
            return useStream
                ? new BinaryDecoder(new MemoryStream(bytes, false), context)
                : new BinaryDecoder(bytes, context);
        }

        [Test]
        [TestCase(0x98, false)]
        [TestCase(0x98, true)]
        [TestCase(0x97, false)]
        [TestCase(0x97, true)]
        [TestCase(0x96, false)]
        [TestCase(0x8C, true)]
        [TestCase(0x91, false)]
        [TestCase(0x8B, false)]
        [TestCase(0x8B, true)]
        [TestCase(0x81, true)]
        public void ReadArrayRejectsLengthBeyondRemainingBytes(int encodingByte, bool useStream)
        {
            // A Variant array of 65535 (MaxArrayLength) elements followed by
            // nothing: the length was only checked against MaxArrayLength and
            // the element storage allocated before a single element was read.
            byte[] bytes = Build(w =>
            {
                w.Write((byte)encodingByte);
                w.Write(65535);
            });

            using BinaryDecoder decoder = CreateDecoder(bytes, CreateContext(), useStream);
            AssertStatus(() => decoder.ReadVariant(null), StatusCodes.BadDecodingError);
        }

        [Test]
        public void ReadArrayAcceptsLengthThatExactlyFitsTheRemainingBytes()
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0x8B);
                w.Write(2);
                w.Write(1.5);
                w.Write(2.5);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.That(value.GetDoubleArray().ToArray(), Is.EqualTo(s_twoDoubles));
        }

        [Test]
        public void NestedDataValueArraysAreRejectedBeforeAllocating()
        {
            // 200 levels of Variant(DataValue[65535]) whose first element is
            // the next level: ~1.2 KB that used to allocate ~700 MB.
            byte[] bytes = Build(w =>
            {
                for (int ii = 0; ii < 200; ii++)
                {
                    w.Write((byte)0x97);
                    w.Write(65535);
                    w.Write((byte)0x01);
                }
            });

            using var decoder = new BinaryDecoder(
                new MemoryStream(bytes, false),
                CreateContext());
            AssertStatus(() => decoder.ReadVariant(null), StatusCodes.BadDecodingError);
        }

#if NET
        [Test]
        public void NestedDataValueArraysWithPaddingAllocateProportionalToDecodedElements()
        {
            // With 1 MB of padding every level passes the remaining bytes
            // check, the allocation must still not scale with the lengths.
            const int levels = 100;
            byte[] bytes = Build(w =>
            {
                for (int ii = 0; ii < levels; ii++)
                {
                    w.Write((byte)0x97);
                    w.Write(65535);
                    w.Write((byte)0x01);
                }
                // The innermost value, then padding that the second element
                // of the innermost array fails on (invalid Variant type).
                w.Write((byte)0x01);
                w.Write((byte)0x01);
                byte[] padding = new byte[1024 * 1024];
                padding.AsSpan().Fill(0xFF);
                w.Write(padding);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Throws<ServiceResultException>(() => decoder.ReadVariant(null));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // 100 levels x 65535 DataValues used to be several hundred MB.
            Assert.That(allocated, Is.LessThan(32L * 1024 * 1024));
        }

        [Test]
        public void LargeArrayBackedByTheMessageIsAllocatedOnce()
        {
            IServiceMessageContext context = CreateContext();
            var values = new DataValue[50000];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new DataValue(
                    Variant.From((double)ii),
                    StatusCodes.Good,
                    new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(ii));
            }

            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteDataValueArray(null, values);
                bytes = encoder.CloseAndReturnBuffer()!;
            }

            // Growing from a small array would allocate about twice the
            // final array, a message that backs the length allocates it once.
            long arrayBytes = (long)values.Length *
                System.Runtime.CompilerServices.Unsafe.SizeOf<DataValue>();
            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
                long before = GC.GetAllocatedBytesForCurrentThread();
                ArrayOf<DataValue> decoded = decoder.ReadDataValueArray(null);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(decoded.ToArray(), Is.EqualTo(values));
                Assert.That(allocated, Is.LessThan(arrayBytes + (arrayBytes / 4)));
            }
        }
#endif

        [Test]
        public void LargeArraysRoundTripThroughGrowingStorage()
        {
            IServiceMessageContext context = CreateContext();
            var variants = new Variant[5000];
            var values = new DataValue[5000];
            string[] strings = new string[5000];
            for (int ii = 0; ii < variants.Length; ii++)
            {
                variants[ii] = Variant.From(ii);
                values[ii] = new DataValue(Variant.From((double)ii));
                strings[ii] = ii.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteVariantArray(null, variants);
                encoder.WriteDataValueArray(null, values);
                encoder.WriteStringArray(null, strings);
                bytes = encoder.CloseAndReturnBuffer()!;
            }

            foreach (bool useStream in s_streamModes)
            {
                using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
                Assert.That(decoder.ReadVariantArray(null).ToArray(), Is.EqualTo(variants));
                Assert.That(decoder.ReadDataValueArray(null).ToArray(), Is.EqualTo(values));
                Assert.That(decoder.ReadStringArray(null).ToArray(), Is.EqualTo(strings));
            }
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void ByteStringLengthBeyondRemainingBytesIsRejectedWithoutLimits(bool useStream)
        {
            // With MaxByteStringLength = 0 (unlimited) a 2 GB length from 4
            // bytes was allocated by BinaryReader.ReadBytes on the stream path.
            ServiceMessageContext context = CreateContext();
            context.MaxByteStringLength = 0;
            byte[] bytes = Build(w => w.Write(int.MaxValue));

            using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
            AssertStatus(() => decoder.ReadByteString(null), StatusCodes.BadDecodingError);
        }

        [Test]
        [TestCase(false)]
        [TestCase(true)]
        public void StringLengthBeyondRemainingBytesIsRejectedWithoutLimits(bool useStream)
        {
            ServiceMessageContext context = CreateContext();
            context.MaxStringLength = 0;
            byte[] bytes = Build(w => w.Write(int.MaxValue));

            using BinaryDecoder decoder = CreateDecoder(bytes, context, useStream);
            AssertStatus(() => decoder.ReadString(null), StatusCodes.BadDecodingError);
        }

        private static byte[] BuildXmlBodyExtensionObjects(
            IServiceMessageContext context,
            NodeId xmlEncodingId,
            int count)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteInt32(null, count);
            for (int ii = 0; ii < count; ii++)
            {
                encoder.WriteNodeId(null, xmlEncodingId);
                encoder.WriteByte(null, 0x02);
                encoder.WriteByteString(null, ByteString.From([(byte)'x']));
            }
            return encoder.CloseAndReturnBuffer()!;
        }

        [Test]
        public void MalformedXmlBodyOfKnownNamespaceZeroTypeIsDecodingError()
        {
            // The XML body branch logged every failure at Error level with the
            // attacker XML and kept the raw body even for ns=0 types.
            ServiceMessageContext context = CreateContext();
            context.Factory.AddEncodeableType(typeof(XmlSample));
            byte[] bytes = BuildXmlBodyExtensionObjects(
                context,
                new NodeId(XmlSample.XmlId, 0),
                1);

            using var decoder = new BinaryDecoder(bytes, context);
            AssertStatus(
                () => decoder.ReadExtensionObjectArray(null),
                StatusCodes.BadDecodingError);
        }

        [Test]
        public void NonElementXmlBodyOfKnownTypeFailsWithoutARuntimeException()
        {
            // A body that is not an XML element dereferenced a null element; the
            // NullReferenceException surfaced wrapped in the BadDecodingError.
            ServiceMessageContext context = CreateContext();
            context.Factory.AddEncodeableType(typeof(XmlSample));
            byte[] bytes = BuildXmlBodyExtensionObjects(
                context,
                new NodeId(XmlSample.XmlId, 0),
                1);

            using var decoder = new BinaryDecoder(bytes, context);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.ReadExtensionObjectArray(null));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            for (Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                Assert.That(inner, Is.InstanceOf<ServiceResultException>().Or.InstanceOf<System.Xml.XmlException>());
            }
        }

        [Test]
        public void MalformedXmlBodyIsRecoveredOnlyUpToMaxDecoderRecoveries()
        {
            const string uri = "urn:test:xmlsample";
            ServiceMessageContext context = CreateContext();
            ushort ns = context.NamespaceUris.GetIndexOrAppend(uri);
            context.Factory.AddEncodeableType(typeof(XmlSampleNs1));
            context.MaxDecoderRecoveries = 2;
            var xmlEncodingId = new NodeId(XmlSample.XmlId, ns);

            byte[] twoBodies = BuildXmlBodyExtensionObjects(context, xmlEncodingId, 2);
            using (var decoder = new BinaryDecoder(twoBodies, context))
            {
                ArrayOf<ExtensionObject> values = decoder.ReadExtensionObjectArray(null);
                Assert.That(values.Count, Is.EqualTo(2));
                Assert.That(values[0].TryGetAsXml(out _), Is.True);
            }

            byte[] threeBodies = BuildXmlBodyExtensionObjects(context, xmlEncodingId, 3);
            using (var decoder = new BinaryDecoder(threeBodies, context))
            {
                AssertStatus(
                    () => decoder.ReadExtensionObjectArray(null),
                    StatusCodes.BadDecodingError);
            }
        }

        [Test]
        public void EncodingLimitBreachInBinaryBodyIsNotRecovered()
        {
            // A vendor type body that exceeds MaxArrayLength used to be kept
            // as a raw ByteString when MaxDecoderRecoveries > 0.
            const string uri = "urn:test:xmlsample";
            ServiceMessageContext context = CreateContext();
            ushort ns = context.NamespaceUris.GetIndexOrAppend(uri);
            context.Factory.AddEncodeableType(typeof(ArraySample));
            context.MaxDecoderRecoveries = 10;
            context.MaxArrayLength = 2;

            byte[] bytes;
            using (var encoder = new BinaryEncoder(context))
            {
                encoder.WriteNodeId(null, new NodeId(88912u, ns));
                encoder.WriteByte(null, 0x01);
                encoder.WriteInt32(null, 4 + (3 * 4));
                encoder.WriteInt32(null, 3);
                encoder.WriteInt32(null, 1);
                encoder.WriteInt32(null, 2);
                encoder.WriteInt32(null, 3);
                bytes = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(bytes, context);
            AssertStatus(
                () => decoder.ReadExtensionObject(null),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        /// <summary>
        /// A vendor namespace encodeable with an Int32 array field.
        /// </summary>
        public sealed class ArraySample : IEncodeable
        {
            private const string kUri = "urn:test:xmlsample";

            public ArrayOf<int> Values { get; set; }

            public ExpandedNodeId TypeId => new(88911u, kUri);
            public ExpandedNodeId BinaryEncodingId => new(88912u, kUri);
            public ExpandedNodeId XmlEncodingId => new(88913u, kUri);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32Array("Values", Values);
            }

            public void Decode(IDecoder decoder)
            {
                Values = decoder.ReadInt32Array("Values");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return ReferenceEquals(this, encodeable);
            }

            public object Clone()
            {
                return new ArraySample { Values = Values };
            }
        }

        /// <summary>
        /// A minimal ns=0 encodeable with an XML encoding id.
        /// </summary>
        public sealed class XmlSample : IEncodeable
        {
            public const uint XmlId = 88903;

            public int Value { get; set; }

            public ExpandedNodeId TypeId => new(88901, 0);
            public ExpandedNodeId BinaryEncodingId => new(88902, 0);
            public ExpandedNodeId XmlEncodingId => new(XmlId, 0);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is XmlSample other && other.Value == Value;
            }

            public object Clone()
            {
                return new XmlSample { Value = Value };
            }
        }

        /// <summary>
        /// The same encodeable in a vendor namespace.
        /// </summary>
        public sealed class XmlSampleNs1 : IEncodeable
        {
            private const string kUri = "urn:test:xmlsample";

            public int Value { get; set; }

            public ExpandedNodeId TypeId => new(88901u, kUri);
            public ExpandedNodeId BinaryEncodingId => new(88902u, kUri);
            public ExpandedNodeId XmlEncodingId => new(XmlSample.XmlId, kUri);

            public void Encode(IEncoder encoder)
            {
                encoder.WriteInt32("Value", Value);
            }

            public void Decode(IDecoder decoder)
            {
                Value = decoder.ReadInt32("Value");
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                return encodeable is XmlSampleNs1 other && other.Value == Value;
            }

            public object Clone()
            {
                return new XmlSampleNs1 { Value = Value };
            }
        }

        [Test]
        [TestCase(-1)]
        [TestCase(0)]
        public void LoadStringTableRejectsNullOrEmptyEntry(int entryLength)
        {
            byte[] bytes = Build(w =>
            {
                w.Write(1);
                w.Write(entryLength);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(
                () => decoder.LoadStringTable(new StringTable()),
                StatusCodes.BadDecodingError);
        }

        [Test]
        public void LoadStringTableAppliesMaxArrayLength()
        {
            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 1;
            byte[] bytes = Build(w =>
            {
                w.Write(2);
                w.Write(1);
                w.Write((byte)'a');
                w.Write(1);
                w.Write((byte)'b');
            });

            using var decoder = new BinaryDecoder(bytes, context);
            AssertStatus(
                () => decoder.LoadStringTable(new StringTable()),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        [Test]
        public void LoadStringTableReadsEntries()
        {
            byte[] bytes = Build(w =>
            {
                w.Write(2);
                w.Write(1);
                w.Write((byte)'a');
                w.Write(1);
                w.Write((byte)'b');
            });

            var table = new StringTable();
            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Assert.That(decoder.LoadStringTable(table), Is.True);
            Assert.That(table.Count, Is.EqualTo(2));
            Assert.That(table.GetString(1), Is.EqualTo("b"));
        }

        [Test]
        public void ShortReadMessageNamesRequestedAndReadBytesInOrder()
        {
            using var decoder = new BinaryDecoder(
                new MemoryStream(new byte[3], false),
                CreateContext());

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => decoder.SafeReadBytes(new byte[8].AsSpan()));
            Assert.That(ex.Message, Does.Contain("Reading 8 bytes"));
            Assert.That(ex.Message, Does.Contain("after 3 bytes"));
        }

        [Test]
        public void EncoderAndDecoderAgreeOnVariantNestingLimit()
        {
            // The encoder did not count a scalar leaf Variant, the decoder
            // did: the encoder could emit a message one level deeper than the
            // decoder accepts.
            ServiceMessageContext context = CreateContext();
            context.MaxEncodingNestingLevels = 5;
            bool encoderRejected = false;

            for (int depth = 1; depth <= 10; depth++)
            {
                Variant value = Variant.From(42);
                for (int ii = 1; ii < depth; ii++)
                {
                    value = Variant.From(new DataValue(value));
                }

                byte[] bytes;
                using (var encoder = new BinaryEncoder(context))
                {
                    try
                    {
                        encoder.WriteVariant(null, value);
                    }
                    catch (ServiceResultException sre) when (
                        sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
                    {
                        encoderRejected = true;
                        break;
                    }
                    bytes = encoder.CloseAndReturnBuffer()!;
                }

                using var decoder = new BinaryDecoder(bytes, context);
                Assert.That(
                    () => decoder.ReadVariant(null),
                    Throws.Nothing,
                    $"The decoder rejects depth {depth} that the encoder wrote.");
            }

            Assert.That(encoderRejected, Is.True);
        }

        [Test]
        public void DataValuePicosecondsOfAtLeast10000AreTreatedAs9999()
        {
            // SourceTimestamp | SourcePicoseconds | ServerTimestamp | ServerPicoseconds
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0x3C);
                w.Write(DateTime.UtcNow.ToFileTimeUtc());
                w.Write((ushort)0xFFFF);
                w.Write(DateTime.UtcNow.ToFileTimeUtc());
                w.Write((ushort)10000);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            DataValue value = decoder.ReadDataValue(null);
            Assert.Multiple(() =>
            {
                Assert.That(value.SourcePicoseconds, Is.EqualTo(9999));
                Assert.That(value.ServerPicoseconds, Is.EqualTo(9999));
            });
        }

        [Test]
        [TestCase(26)]
        [TestCase(27)]
        [TestCase(28)]
        [TestCase(29)]
        [TestCase(30)]
        [TestCase(31)]
        public void ReservedVariantTypeIdScalarIsDecodedAsByteString(int typeId)
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)typeId);
                w.Write(3);
                w.Write(new byte[] { 1, 2, 3 });
                w.Write((byte)0x2A);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.Multiple(() =>
            {
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
                Assert.That(value.GetByteString().ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                // The rest of the message is not desynchronized.
                Assert.That(decoder.ReadByte(null), Is.EqualTo(0x2A));
            });
        }

        [Test]
        [TestCase(26)]
        [TestCase(29)]
        [TestCase(31)]
        public void ReservedVariantTypeIdArrayIsDecodedAsByteStringArray(int typeId)
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)(typeId | 0x80));
                w.Write(1);
                w.Write(3);
                w.Write(new byte[] { 1, 2, 3 });
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.Multiple(() =>
            {
                Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
                Assert.That(value.GetByteStringArray().Count, Is.EqualTo(1));
            });
        }

        [Test]
        public void VariantMatrixWithMoreThan32DimensionsExceedsEncodingLimits()
        {
            // Int32 | Array | ArrayDimensions, one value, 33 dimensions of 1:
            // the matrix is valid per Part 6, but no .NET array (and hence no
            // consumer) can have a rank above 32, an implementation limit.
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0xC6);
                w.Write(1);
                w.Write(0);
                w.Write(33);
                for (int ii = 0; ii < 33; ii++)
                {
                    w.Write(1);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(
                () => decoder.ReadVariant(null),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        [Test]
        public void VariantMatrixWithRankAboveShortMaxValueExceedsEncodingLimits()
        {
            // A rank above short.MaxValue made TypeInfo throw an
            // ArgumentOutOfRangeException out of the decoder.
            const int rank = 40000;
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0xC1);
                w.Write(1);
                w.Write((byte)1);
                w.Write(rank);
                for (int ii = 0; ii < rank; ii++)
                {
                    w.Write(1);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(
                () => decoder.ReadVariant(null),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullVariantWithArrayBitConsumesArrayLengthAndDimensions(bool matrix)
        {
            // Null | Array (| ArrayDimensions): ArrayLength and ArrayDimensions
            // are present (5.2.2.16) and must be consumed, or the Int32 that
            // follows the Variant is read from the wrong position.
            byte[] bytes = Build(w =>
            {
                w.Write((byte)(matrix ? 0xC0 : 0x80));
                w.Write(2);
                if (matrix)
                {
                    w.Write(2);
                    w.Write(1);
                    w.Write(2);
                }
                w.Write(0x12345678);
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.Multiple(() =>
            {
                Assert.That(value.IsNull, Is.True);
                Assert.That(decoder.ReadInt32(null), Is.EqualTo(0x12345678));
            });
        }

        [Test]
        public void NullVariantArrayLengthIsBoundedByMaxArrayLength()
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0x80);
                w.Write(1000);
            });

            ServiceMessageContext context = CreateContext();
            context.MaxArrayLength = 10;
            using var decoder = new BinaryDecoder(bytes, context);
            AssertStatus(
                () => decoder.ReadVariant(null),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        [Test]
        public void VariantMatrixWith32DimensionsIsAccepted()
        {
            byte[] bytes = Build(w =>
            {
                w.Write((byte)0xC6);
                w.Write(1);
                w.Write(7);
                w.Write(32);
                for (int ii = 0; ii < 32; ii++)
                {
                    w.Write(1);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            Variant value = decoder.ReadVariant(null);
            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(32));
        }

        [Test]
        public void EmptyInlineMatrixWithMoreThan32DimensionsExceedsEncodingLimits()
        {
            // An empty inline matrix (a zero dimension, no values) was
            // accepted with any rank.
            byte[] bytes = Build(w =>
            {
                w.Write(33);
                for (int ii = 0; ii < 33; ii++)
                {
                    w.Write(0);
                }
            });

            using var decoder = new BinaryDecoder(bytes, CreateContext());
            AssertStatus(
                () => decoder.ReadVariantValue(
                    null,
                    TypeInfo.Create(BuiltInType.Int32, ValueRanks.TwoDimensions)),
                StatusCodes.BadEncodingLimitsExceeded);
        }

        [Test]
        public void MatrixValidatorsRejectMoreThan32Dimensions()
        {
            int[] dimensions = new int[33];
            dimensions.AsSpan().Fill(1);

            Assert.Multiple(() =>
            {
                Assert.That(MatrixOf.IsValidMatrix(dimensions, 1), Is.False);
                Assert.That(
                    MatrixOf.TryGetInlineMatrixElementCount(dimensions, out _, out _),
                    Is.False);
                Assert.That(
                    MatrixOf.IsValidMatrix(dimensions.AsSpan(0, 32), 1),
                    Is.True);
                Assert.Throws<ArgumentException>(
                    () => _ = new MatrixOf<int>(s_oneElement, dimensions));
            });
        }
    }
}
