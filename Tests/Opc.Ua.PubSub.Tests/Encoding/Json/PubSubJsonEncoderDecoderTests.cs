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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Tests;

#pragma warning disable CS0618 // Type or member is obsolete

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Surgical unit tests for <see cref="PubSubJsonEncoder"/> and
    /// <see cref="PubSubJsonDecoder"/> covering all primitive types,
    /// special floating-point values, complex OPC UA types, arrays,
    /// null/default-value handling, and encoding-mode branches.
    /// Each test uses the round-trip pattern: encode a value, then
    /// decode the resulting JSON and assert equality.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("5.4.1", Part = 6)]
    [TestSpec("7.2.5")]
    public sealed class PubSubJsonEncoderDecoderTests
    {
        // ── helpers ────────────────────────────────────────────────────────────

        private static ServiceMessageContext NewContext()
            => (ServiceMessageContext)ServiceMessageContext.CreateEmpty(null!);

        /// <summary>Encode one or more fields and return the complete JSON text.</summary>
        private static string Encode(
            Action<PubSubJsonEncoder> write,
            PubSubJsonEncoding encoding = PubSubJsonEncoding.Reversible)
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, encoding);
            write(enc);
            return enc.CloseAndReturnText();
        }

        /// <summary>Create a decoder for the supplied JSON text.</summary>
        private static PubSubJsonDecoder MakeDecoder(string json)
            => new PubSubJsonDecoder(json, NewContext());

        /// <summary>
        /// Encode then immediately decode, returning the decoded value.
        /// The same <see cref="NewContext"/> is used for both sides so
        /// namespace-index mappings are consistent.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private static T RoundTrip<T>(
            Action<PubSubJsonEncoder> write,
            Func<PubSubJsonDecoder, T> read,
            PubSubJsonEncoding encoding = PubSubJsonEncoding.Reversible)
        {
            string json = Encode(write, encoding);
            using var dec = MakeDecoder(json);
            return read(dec);
        }

        // ── Static arrays for CA1861 (constant array argument warnings) ────────

        private static readonly int[] s_int10_20_30 = [10, 20, 30];
        private static readonly string[] s_strA_B_C = ["a", "b", "c"];
        private static readonly bool[] s_boolTFTF = [true, false, true, false];
        private static readonly string[] s_strAlphaBetaGamma = ["alpha", "beta", "gamma"];

        // ── Boolean ────────────────────────────────────────────────────────────

        [TestCase(true)]
        [TestCase(false)]
        public void BooleanRoundTrip(bool value)
        {
            Assert.That(
                RoundTrip(e => e.WriteBoolean("f", value), d => d.ReadBoolean("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void ReadBooleanFromNonBooleanTokenReturnsFalse()
        {
            using var dec = MakeDecoder("{\"f\":42}");
            Assert.That(dec.ReadBoolean("f"), Is.False);
        }

        // ── SByte ──────────────────────────────────────────────────────────────

        [TestCase((sbyte)0)]
        [TestCase((sbyte)-128)]
        [TestCase((sbyte)127)]
        public void SByteRoundTrip(sbyte value)
        {
            Assert.That(
                RoundTrip(e => e.WriteSByte("f", value), d => d.ReadSByte("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void ReadSByteAboveRangeReturnsZero()
        {
            // 200 > sbyte.MaxValue → decoder returns 0
            using var dec = MakeDecoder("{\"f\":200}");
            Assert.That(dec.ReadSByte("f"), Is.Zero);
        }

        // ── Byte ───────────────────────────────────────────────────────────────

        [TestCase((byte)0)]
        [TestCase((byte)255)]
        public void ByteRoundTrip(byte value)
        {
            Assert.That(
                RoundTrip(e => e.WriteByte("f", value), d => d.ReadByte("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void ReadByteNegativeValueReturnsZero()
        {
            using var dec = MakeDecoder("{\"f\":-1}");
            Assert.That(dec.ReadByte("f"), Is.Zero);
        }

        // ── Int16 / UInt16 ─────────────────────────────────────────────────────

        [TestCase((short)0)]
        [TestCase(short.MinValue)]
        [TestCase(short.MaxValue)]
        public void Int16RoundTrip(short value)
        {
            Assert.That(
                RoundTrip(e => e.WriteInt16("f", value), d => d.ReadInt16("f")),
                Is.EqualTo(value));
        }

        [TestCase((ushort)0)]
        [TestCase(ushort.MaxValue)]
        public void UInt16RoundTrip(ushort value)
        {
            Assert.That(
                RoundTrip(e => e.WriteUInt16("f", value), d => d.ReadUInt16("f")),
                Is.EqualTo(value));
        }

        // ── Int32 / UInt32 ─────────────────────────────────────────────────────

        [TestCase(0)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void Int32RoundTrip(int value)
        {
            Assert.That(
                RoundTrip(e => e.WriteInt32("f", value), d => d.ReadInt32("f")),
                Is.EqualTo(value));
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void UInt32RoundTrip(uint value)
        {
            Assert.That(
                RoundTrip(e => e.WriteUInt32("f", value), d => d.ReadUInt32("f")),
                Is.EqualTo(value));
        }

        // ── Int64 / UInt64 — encoded as quoted strings in Reversible mode ──────

        [TestCase(0L)]
        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        [TestCase(12345678901234L)]
        public void Int64RoundTrip(long value)
        {
            Assert.That(
                RoundTrip(e => e.WriteInt64("f", value), d => d.ReadInt64("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void ReadInt64FromStringToken()
        {
            // Reversible encoding serialises Int64 as a quoted string; verify the
            // decoder can parse it when the JSON was produced externally.
            using var dec = MakeDecoder("{\"f\":\"9876543210\"}");
            Assert.That(dec.ReadInt64("f"), Is.EqualTo(9876543210L));
        }

        [TestCase(0UL)]
        [TestCase(ulong.MaxValue)]
        public void UInt64RoundTrip(ulong value)
        {
            Assert.That(
                RoundTrip(e => e.WriteUInt64("f", value), d => d.ReadUInt64("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void ReadUInt64FromStringToken()
        {
            using var dec = MakeDecoder("{\"f\":\"18446744073709551615\"}");
            Assert.That(dec.ReadUInt64("f"), Is.EqualTo(ulong.MaxValue));
        }

        // ── Float ──────────────────────────────────────────────────────────────

        [TestCase(0.0f)]
        [TestCase(1.5f)]
        [TestCase(-3.14f)]
        public void FloatRoundTrip(float value)
        {
            Assert.That(
                RoundTrip(e => e.WriteFloat("f", value), d => d.ReadFloat("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void FloatNaNRoundTrip()
        {
            Assert.That(
                RoundTrip(e => e.WriteFloat("f", float.NaN), d => d.ReadFloat("f")),
                Is.NaN);
        }

        [Test]
        public void FloatPositiveInfinityRoundTrip()
        {
            Assert.That(
                RoundTrip(e => e.WriteFloat("f", float.PositiveInfinity), d => d.ReadFloat("f")),
                Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void FloatNegativeInfinityRoundTrip()
        {
            Assert.That(
                RoundTrip(e => e.WriteFloat("f", float.NegativeInfinity), d => d.ReadFloat("f")),
                Is.EqualTo(float.NegativeInfinity));
        }

        [Test]
        public void ReadFloatNaNFromStringToken()
        {
            using var dec = MakeDecoder("{\"f\":\"NaN\"}");
            Assert.That(dec.ReadFloat("f"), Is.NaN);
        }

        [Test]
        public void ReadFloatPositiveInfinityFromStringToken()
        {
            using var dec = MakeDecoder("{\"f\":\"Infinity\"}");
            Assert.That(dec.ReadFloat("f"), Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void ReadFloatNegativeInfinityFromStringToken()
        {
            using var dec = MakeDecoder("{\"f\":\"-Infinity\"}");
            Assert.That(dec.ReadFloat("f"), Is.EqualTo(float.NegativeInfinity));
        }

        // ── Double ─────────────────────────────────────────────────────────────

        [TestCase(0.0)]
        [TestCase(3.141592653589793)]
        [TestCase(-1.0e308)]
        public void DoubleRoundTrip(double value)
        {
            Assert.That(
                RoundTrip(e => e.WriteDouble("f", value), d => d.ReadDouble("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void DoubleNaNRoundTrip()
        {
            Assert.That(
                RoundTrip(e => e.WriteDouble("f", double.NaN), d => d.ReadDouble("f")),
                Is.NaN);
        }

        [Test]
        public void DoublePositiveInfinityRoundTrip()
        {
            Assert.That(
                RoundTrip(e => e.WriteDouble("f", double.PositiveInfinity), d => d.ReadDouble("f")),
                Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void DoubleNegativeInfinityRoundTrip()
        {
            Assert.That(
                RoundTrip(e => e.WriteDouble("f", double.NegativeInfinity), d => d.ReadDouble("f")),
                Is.EqualTo(double.NegativeInfinity));
        }

        [Test]
        public void ReadDoubleNaNFromStringToken()
        {
            using var dec = MakeDecoder("{\"f\":\"NaN\"}");
            Assert.That(dec.ReadDouble("f"), Is.NaN);
        }

        // ── String ─────────────────────────────────────────────────────────────

        [TestCase("hello")]
        [TestCase("")]
        [TestCase("unicode \u00e9\u4e2d\u6587")]
        public void StringRoundTrip(string value)
        {
            Assert.That(
                RoundTrip(e => e.WriteString("f", value), d => d.ReadString("f")),
                Is.EqualTo(value));
        }

        [Test]
        public void StringWithEscapedSpecialCharsRoundTrip()
        {
            const string special = "tab\there\nnewline\"quote\\backslash\x01control";
            Assert.That(
                RoundTrip(e => e.WriteString("f", special), d => d.ReadString("f")),
                Is.EqualTo(special));
        }

        [Test]
        public void NullStringOmittedByReversibleEncoding()
        {
            // Reversible: IncludeDefaultValues=false → null string field is suppressed.
            string json = Encode(e => e.WriteString("f", null), PubSubJsonEncoding.Reversible);
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
            Assert.That(dec.ReadString("f"), Is.Null);
        }

        [Test]
        public void NullStringWrittenByNonReversibleEncoding()
        {
            // NonReversible: null strings are omitted from the JSON output (field absent),
            // and reading the missing field returns null.
            string json = Encode(e => e.WriteString("f", null), PubSubJsonEncoding.NonReversible);
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
            Assert.That(dec.ReadString("f"), Is.Null);
        }

        // ── DateTime ───────────────────────────────────────────────────────────

        [Test]
        public void DateTimeRoundTrip()
        {
            var dt = new DateTimeUtc(2024, 3, 15, 10, 30, 0);
            Assert.That(
                RoundTrip(e => e.WriteDateTime("f", dt), d => d.ReadDateTime("f")),
                Is.EqualTo(dt));
        }

        [Test]
        public void DateTimeMinValueNotStoredByReversibleEncoding()
        {
            string json = Encode(e => e.WriteDateTime("f", DateTimeUtc.MinValue));
            using var dec = MakeDecoder(json);
            Assert.That(dec.ReadDateTime("f"), Is.EqualTo(DateTimeUtc.MinValue));
        }

        // ── Guid ───────────────────────────────────────────────────────────────

        [Test]
        public void GuidRoundTrip()
        {
            var guid = Uuid.NewUuid();
            Assert.That(
                RoundTrip(e => e.WriteGuid("f", guid), d => d.ReadGuid("f")),
                Is.EqualTo(guid));
        }

        [Test]
        public void EmptyGuidOmittedByReversibleEncoding()
        {
            string json = Encode(e => e.WriteGuid("f", Uuid.Empty));
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
        }

        // ── ByteString ─────────────────────────────────────────────────────────

        [Test]
        public void ByteStringRoundTrip()
        {
            var bs = ByteString.From(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
            var result = RoundTrip(e => e.WriteByteString("f", bs), d => d.ReadByteString("f"));
            Assert.That(result.ToArray(), Is.EqualTo(bs.ToArray()));
        }

        [Test]
        public void ByteStringEmptyRoundTrip()
        {
            var bs = ByteString.Empty;
            var result = RoundTrip(e => e.WriteByteString("f", bs), d => d.ReadByteString("f"));
            Assert.That(result.IsEmpty, Is.True);
        }

        // ── NodeId ─────────────────────────────────────────────────────────────

        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public void NodeIdNumericNs0RoundTrip(uint id)
        {
            var nodeId = new NodeId(id, 0);
            var result = RoundTrip(e => e.WriteNodeId("f", nodeId), d => d.ReadNodeId("f"));
            Assert.That(result, Is.EqualTo(nodeId));
        }

        [Test]
        public void NodeIdStringRoundTrip()
        {
            var nodeId = new NodeId("MyStringNode", 0);
            var result = RoundTrip(e => e.WriteNodeId("f", nodeId), d => d.ReadNodeId("f"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
            Assert.That(result.Identifier, Is.EqualTo("MyStringNode"));
        }

        [Test]
        public void NodeIdGuidRoundTrip()
        {
            var guid = new Uuid(Guid.Parse("12345678-1234-5678-1234-567812345678"));
            var nodeId = new NodeId(guid, 0);
            var result = RoundTrip(e => e.WriteNodeId("f", nodeId), d => d.ReadNodeId("f"));
            Assert.That(result.IdType, Is.EqualTo(IdType.Guid));
            Assert.That(result.Identifier, Is.EqualTo(guid));
        }

        [Test]
        public void NodeIdOpaqueRoundTrip()
        {
            var bs = ByteString.From(new byte[] { 1, 2, 3 });
            var nodeId = new NodeId(bs, 0);
            var result = RoundTrip(e => e.WriteNodeId("f", nodeId), d => d.ReadNodeId("f"));
            Assert.That(result.IdType, Is.EqualTo(IdType.Opaque));
        }

        [Test]
        public void NullNodeIdOmittedByReversibleEncoding()
        {
            string json = Encode(e => e.WriteNodeId("f", NodeId.Null));
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
            Assert.That(dec.ReadNodeId("f"), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void NodeIdWithNamespaceIndexRoundTrip()
        {
            // Register a namespace so the index is stable across encoder/decoder.
            var ctx = NewContext();
            ctx.NamespaceUris.GetIndexOrAppend("urn:test:ns");
            var nodeId = new NodeId(99u, 1);

            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            enc.WriteNodeId("f", nodeId);
            string json = enc.CloseAndReturnText();

            using var dec = new PubSubJsonDecoder(json, ctx);
            var result = dec.ReadNodeId("f");
            Assert.That(result.NamespaceIndex, Is.EqualTo((ushort)1));
            Assert.That(result.Identifier, Is.EqualTo(99u));
        }

        // ── ExpandedNodeId ─────────────────────────────────────────────────────

        [Test]
        public void ExpandedNodeIdNumericRoundTrip()
        {
            var eid = new ExpandedNodeId(42u, 0);
            var result = RoundTrip(e => e.WriteExpandedNodeId("f", eid), d => d.ReadExpandedNodeId("f"));
            Assert.That(result, Is.EqualTo(eid));
        }

        [Test]
        public void ExpandedNodeIdStringRoundTrip()
        {
            var eid = new ExpandedNodeId("SomeNode", 0);
            var result = RoundTrip(e => e.WriteExpandedNodeId("f", eid), d => d.ReadExpandedNodeId("f"));
            Assert.That(result.IdType, Is.EqualTo(IdType.String));
        }

        // ── QualifiedName ──────────────────────────────────────────────────────

        [Test]
        public void QualifiedNameNs0RoundTrip()
        {
            var qn = new QualifiedName("BrowseName", 0);
            var result = RoundTrip(e => e.WriteQualifiedName("f", qn), d => d.ReadQualifiedName("f"));
            Assert.That(result.Name, Is.EqualTo("BrowseName"));
            Assert.That(result.NamespaceIndex, Is.Zero);
        }

        [Test]
        public void NullQualifiedNameOmittedByReversibleEncoding()
        {
            string json = Encode(e => e.WriteQualifiedName("f", QualifiedName.Null));
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
        }

        // ── LocalizedText ──────────────────────────────────────────────────────

        [Test]
        public void LocalizedTextReversibleRoundTrip()
        {
            var lt = new LocalizedText("en-US", "Hello World");
            var result = RoundTrip(
                e => e.WriteLocalizedText("f", lt),
                d => d.ReadLocalizedText("f"),
                PubSubJsonEncoding.Reversible);
            Assert.That(result.Text, Is.EqualTo("Hello World"));
            Assert.That(result.Locale, Is.EqualTo("en-US"));
        }

        [Test]
        public void LocalizedTextNonReversibleEncodesAsPlainString()
        {
            // NonReversible omits locale and writes only the text.
            var lt = new LocalizedText("de-DE", "Hallo Welt");
            string json = Encode(e => e.WriteLocalizedText("f", lt), PubSubJsonEncoding.NonReversible);
            using var dec = MakeDecoder(json);
            var result = dec.ReadLocalizedText("f");
            Assert.That(result.Text, Is.EqualTo("Hallo Welt"));
        }

        [Test]
        public void LocalizedTextWithoutLocaleRoundTrip()
        {
            var lt = new LocalizedText("just text");
            var result = RoundTrip(
                e => e.WriteLocalizedText("f", lt),
                d => d.ReadLocalizedText("f"),
                PubSubJsonEncoding.Reversible);
            Assert.That(result.Text, Is.EqualTo("just text"));
        }

        [Test]
        public void NullLocalizedTextOmittedByReversibleEncoding()
        {
            string json = Encode(e => e.WriteLocalizedText("f", LocalizedText.Null));
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
        }

        // ── StatusCode ─────────────────────────────────────────────────────────

        [Test]
        public void StatusCodeGoodRoundTrip()
        {
            var sc = StatusCodes.Good;
            var result = RoundTrip(e => e.WriteStatusCode("f", sc), d => d.ReadStatusCode("f"));
            Assert.That(result, Is.EqualTo(sc));
        }

        [Test]
        public void StatusCodeBadRoundTrip()
        {
            var sc = StatusCodes.Bad;
            var result = RoundTrip(e => e.WriteStatusCode("f", sc), d => d.ReadStatusCode("f"));
            Assert.That(result, Is.EqualTo(sc));
        }

        [Test]
        public void StatusCodeUncertainRoundTrip()
        {
            var sc = StatusCodes.Uncertain;
            var result = RoundTrip(e => e.WriteStatusCode("f", sc), d => d.ReadStatusCode("f"));
            Assert.That(result, Is.EqualTo(sc));
        }

        [Test]
        public void MissingStatusCodeFieldReturnsGood()
        {
            using var dec = MakeDecoder("{\"other\":1}");
            Assert.That(dec.ReadStatusCode("status"), Is.EqualTo(StatusCodes.Good));
        }

        // ── DiagnosticInfo ─────────────────────────────────────────────────────

        [Test]
        public void DiagnosticInfoRoundTrip()
        {
            var di = new DiagnosticInfo
            {
                SymbolicId = 5,
                AdditionalInfo = "some extra info",
                InnerStatusCode = StatusCodes.Bad
            };
            // NonReversible includes default values so all fields are written.
            var result = RoundTrip(
                e => e.WriteDiagnosticInfo("f", di),
                d => d.ReadDiagnosticInfo("f"),
                PubSubJsonEncoding.NonReversible);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SymbolicId, Is.EqualTo(5));
            Assert.That(result.AdditionalInfo, Is.EqualTo("some extra info"));
        }

        [Test]
        public void NullDiagnosticInfoOmittedByReversibleEncoding()
        {
            string json = Encode(e => e.WriteDiagnosticInfo("f", null));
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
        }

        // ── Variant ────────────────────────────────────────────────────────────

        [Test]
        public void VariantBooleanRoundTrip()
        {
            var v = new Variant(true);
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That(result.Value, Is.True);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
        }

        [Test]
        public void VariantInt32RoundTrip()
        {
            var v = new Variant(12345);
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That(result.Value, Is.EqualTo(12345));
        }

        [Test]
        public void VariantInt64RoundTrip()
        {
            var v = new Variant(long.MaxValue);
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That(result.Value, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void VariantStringRoundTrip()
        {
            var v = new Variant("round-trip-string");
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That(result.Value, Is.EqualTo("round-trip-string"));
        }

        [Test]
        public void VariantDoubleRoundTrip()
        {
            var v = new Variant(3.14159);
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That((double)result.Value!, Is.EqualTo(3.14159).Within(1e-10));
        }

        [Test]
        public void VariantNullOmittedByReversibleEncoding()
        {
            string json = Encode(e => e.WriteVariant("f", Variant.Null));
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.False);
            Assert.That(dec.ReadVariant("f"), Is.EqualTo(Variant.Null));
        }

        [Test]
        public void VariantInt32ArrayRoundTrip()
        {
            var v = new Variant(s_int10_20_30);
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That(result.Value, Is.EqualTo(s_int10_20_30));
        }

        [Test]
        public void VariantStringArrayRoundTrip()
        {
            var v = new Variant(s_strA_B_C);
            var result = RoundTrip(e => e.WriteVariant("f", v), d => d.ReadVariant("f"));
            Assert.That(result.Value, Is.EqualTo(s_strA_B_C));
        }

        [Test]
        public void VariantCompactEncodingRoundTrip()
        {
            var v = new Variant(42);
            var result = RoundTrip(
                e => e.WriteVariant("f", v),
                d => d.ReadVariant("f"),
                PubSubJsonEncoding.Compact);
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void VariantVerboseEncodingRoundTrip()
        {
            var v = new Variant("verbose-value");
            var result = RoundTrip(
                e => e.WriteVariant("f", v),
                d => d.ReadVariant("f"),
                PubSubJsonEncoding.Verbose);
            Assert.That(result.Value, Is.EqualTo("verbose-value"));
        }

        // ── DataValue ──────────────────────────────────────────────────────────

        [Test]
        public void DataValueWithInt32VariantRoundTrip()
        {
            var dv = new DataValue(new Variant(99));
            var result = RoundTrip(
                e => e.WriteDataValue("f", dv),
                d => d.ReadDataValue("f"));
            Assert.That(result.WrappedValue.Value, Is.EqualTo(99));
        }

        [Test]
        public void DataValueWithStatusCodeRoundTrip()
        {
            var dv = new DataValue(new Variant(42))
                .WithStatus(StatusCodes.BadNodeIdInvalid);
            var result = RoundTrip(
                e => e.WriteDataValue("f", dv),
                d => d.ReadDataValue("f"),
                PubSubJsonEncoding.Reversible);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void DataValueWithTimestampsRoundTrip()
        {
            var ts = new DateTimeUtc(2025, 1, 1, 0, 0, 0);
            var dv = new DataValue(new Variant(7))
                .WithSourceTimestamp(ts)
                .WithServerTimestamp(ts);
            var result = RoundTrip(
                e => e.WriteDataValue("f", dv),
                d => d.ReadDataValue("f"),
                PubSubJsonEncoding.Reversible);
            Assert.That(result.SourceTimestamp, Is.EqualTo(ts));
            Assert.That(result.ServerTimestamp, Is.EqualTo(ts));
        }

        [Test]
        public void DataValueWithStringVariantRoundTrip()
        {
            var dv = new DataValue(new Variant("sensor-reading"));
            var result = RoundTrip(
                e => e.WriteDataValue("f", dv),
                d => d.ReadDataValue("f"),
                PubSubJsonEncoding.Compact);
            Assert.That(result.WrappedValue.Value, Is.EqualTo("sensor-reading"));
        }

        // ── Arrays of primitives ───────────────────────────────────────────────

        [Test]
        public void BooleanArrayRoundTrip()
        {
            ArrayOf<bool> values = s_boolTFTF;
            var result = RoundTrip(
                e => e.WriteBooleanArray("f", values),
                d => d.ReadBooleanArray("f"));
            Assert.That(result.ToArray(), Is.EqualTo(s_boolTFTF));
        }

        [Test]
        public void Int32ArrayRoundTrip()
        {
            ArrayOf<int> values = new int[] { 1, -2, int.MaxValue };
            var result = RoundTrip(
                e => e.WriteInt32Array("f", values),
                d => d.ReadInt32Array("f"));
            Assert.That(result.ToArray(), Is.EqualTo(new int[] { 1, -2, int.MaxValue }));
        }

        [Test]
        public void Int64ArrayRoundTrip()
        {
            ArrayOf<long> values = new long[] { long.MinValue, 0L, long.MaxValue };
            var result = RoundTrip(
                e => e.WriteInt64Array("f", values),
                d => d.ReadInt64Array("f"));
            Assert.That(result.ToArray(), Is.EqualTo(new long[] { long.MinValue, 0L, long.MaxValue }));
        }

        [Test]
        public void StringArrayRoundTrip()
        {
            ArrayOf<string> values = s_strAlphaBetaGamma;
            var result = RoundTrip(
                e => e.WriteStringArray("f", values),
                d => d.ReadStringArray("f"));
            Assert.That(result.ToArray(), Is.EqualTo(s_strAlphaBetaGamma));
        }

        [Test]
        public void FloatArrayWithSpecialValuesRoundTrip()
        {
            ArrayOf<float> values = new float[] { 1.0f, float.NaN, float.PositiveInfinity };
            var result = RoundTrip(
                e => e.WriteFloatArray("f", values),
                d => d.ReadFloatArray("f"));
            Assert.That(result[0], Is.EqualTo(1.0f));
            Assert.That(result[1], Is.NaN);
            Assert.That(result[2], Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void DoubleArrayWithSpecialValuesRoundTrip()
        {
            ArrayOf<double> values = new double[] { double.NegativeInfinity, 0.0, double.NaN };
            var result = RoundTrip(
                e => e.WriteDoubleArray("f", values),
                d => d.ReadDoubleArray("f"));
            Assert.That(result[0], Is.EqualTo(double.NegativeInfinity));
            Assert.That(result[1], Is.Zero);
            Assert.That(result[2], Is.NaN);
        }

        [Test]
        public void EmptyInt32ArrayRoundTrip()
        {
            ArrayOf<int> values = new(Array.Empty<int>());
            var result = RoundTrip(
                e => e.WriteInt32Array("f", values),
                d => d.ReadInt32Array("f"));
            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void GuidArrayRoundTrip()
        {
            var g1 = Uuid.NewUuid();
            var g2 = Uuid.NewUuid();
            ArrayOf<Uuid> values = new Uuid[] { g1, g2 };
            var result = RoundTrip(
                e => e.WriteGuidArray("f", values),
                d => d.ReadGuidArray("f"));
            Assert.That(result[0], Is.EqualTo(g1));
            Assert.That(result[1], Is.EqualTo(g2));
        }

        [Test]
        public void NodeIdArrayRoundTrip()
        {
            ArrayOf<NodeId> values = new NodeId[]
            {
                new NodeId(1u, 0),
                new NodeId("Test", 0)
            };
            var result = RoundTrip(
                e => e.WriteNodeIdArray("f", values),
                d => d.ReadNodeIdArray("f"));
            Assert.That(result[0], Is.EqualTo(new NodeId(1u, 0)));
            Assert.That(result[1].IdType, Is.EqualTo(IdType.String));
        }

        // ── Decoder missing-field behaviour ────────────────────────────────────

        [Test]
        public void ReadMissingBooleanFieldReturnsFalse()
        {
            using var dec = MakeDecoder("{\"other\":42}");
            Assert.That(dec.ReadBoolean("missing"), Is.False);
        }

        [Test]
        public void ReadMissingInt32FieldReturnsZero()
        {
            using var dec = MakeDecoder("{\"other\":\"hello\"}");
            Assert.That(dec.ReadInt32("missing"), Is.Zero);
        }

        [Test]
        public void ReadMissingStringFieldReturnsNull()
        {
            using var dec = MakeDecoder("{\"other\":42}");
            Assert.That(dec.ReadString("missing"), Is.Null);
        }

        [Test]
        public void ReadMissingNodeIdFieldReturnsNull()
        {
            using var dec = MakeDecoder("{\"other\":42}");
            Assert.That(dec.ReadNodeId("missing"), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ReadMissingVariantFieldReturnsNull()
        {
            using var dec = MakeDecoder("{\"other\":42}");
            Assert.That(dec.ReadVariant("missing"), Is.EqualTo(Variant.Null));
        }

        // ── HasField ───────────────────────────────────────────────────────────

        [Test]
        public void HasFieldReturnsTrueForPresentField()
        {
            using var dec = MakeDecoder("{\"present\":true}");
            Assert.That(dec.HasField("present"), Is.True);
        }

        [Test]
        public void HasFieldReturnsFalseForAbsentField()
        {
            using var dec = MakeDecoder("{\"present\":true}");
            Assert.That(dec.HasField("absent"), Is.False);
        }

        [Test]
        public void HasFieldReturnsTrueForNullOrEmptyFieldName()
        {
            // null/empty field name always returns true (spec behaviour: check current scope)
            using var dec = MakeDecoder("{}");
            Assert.That(dec.HasField(null), Is.True);
            Assert.That(dec.HasField(string.Empty), Is.True);
        }

        // ── Encoder properties and Close ───────────────────────────────────────

        [Test]
        public void EncoderEncodingTypeIsJson()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            Assert.That(enc.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void EncoderCloseReturnsPositiveLength()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            enc.WriteBoolean("f", true);
            int length = enc.Close();
            Assert.That(length, Is.GreaterThan(0));
        }

        [Test]
        public void EncoderTopLevelArrayProducesArrayJson()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible, topLevelIsArray: true);
            enc.WriteInt32(null, 1);
            enc.WriteInt32(null, 2);
            string json = enc.CloseAndReturnText();
            Assert.That(json, Does.StartWith("["));
            Assert.That(json, Does.EndWith("]"));
        }

        [Test]
        public void EncoderUseReversibleEncodingIsTrue()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            Assert.That(enc.UseReversibleEncoding, Is.True);
        }

        [Test]
        public void EncoderUseReversibleEncodingIsFalseForNonReversible()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.NonReversible);
            Assert.That(enc.UseReversibleEncoding, Is.False);
        }

        [Test]
        public void EncoderCanOmitFieldsIsTrue()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            Assert.That(enc.CanOmitFields, Is.True);
        }

        [Test]
        public void EncoderUsingAlternateEncodingSwitchesAndRestores()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            // Write a LocalizedText in NonReversible inside an alternate-encoding scope.
            enc.UsingAlternateEncoding(
                (fn, v) => enc.WriteLocalizedText(fn, v),
                "lt",
                new LocalizedText("en", "text"),
                PubSubJsonEncoding.NonReversible);
            string json = enc.CloseAndReturnText();
            // In NonReversible mode, LocalizedText is just the string.
            Assert.That(json, Does.Contain("\"text\""));
        }

        // ── Decoder properties ─────────────────────────────────────────────────

        [Test]
        public void DecoderEncodingTypeIsJson()
        {
            using var dec = MakeDecoder("{}");
            Assert.That(dec.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void DecoderContextIsPreserved()
        {
            var ctx = NewContext();
            using var dec = new PubSubJsonDecoder("{}", ctx);
            Assert.That(dec.Context, Is.SameAs(ctx));
        }

        // ── Static guard tests ─────────────────────────────────────────────────

        [Test]
        public void EncodeMessageStaticNullMessageThrows()
        {
            var ctx = NewContext();
            var buf = new byte[1024];
            Assert.Throws<ArgumentNullException>(() =>
                PubSubJsonEncoder.EncodeMessage(null!, buf, ctx));
        }

        [Test]
        public void EncodeMessageStaticNullBufferThrows()
        {
            var ctx = NewContext();
            Assert.Throws<ArgumentNullException>(() =>
                PubSubJsonEncoder.EncodeMessage(new MinimalEncodeable(), null!, ctx));
        }

        [Test]
        public void EncodeMessageStaticNullContextThrows()
        {
            var buf = new byte[1024];
            Assert.Throws<ArgumentNullException>(() =>
                PubSubJsonEncoder.EncodeMessage(new MinimalEncodeable(), buf, null!));
        }

        [Test]
        public void DecodeMessageStaticNullContextThrows()
        {
            var buffer = new ArraySegment<byte>(new byte[32]);
            Assert.Throws<ArgumentNullException>(() =>
                PubSubJsonDecoder.DecodeMessage<MinimalEncodeable>(buffer, null!));
        }

        [Test]
        public void DecodeMessageStaticMaxMessageSizeExceededThrows()
        {
            var ctx = new ServiceMessageContext { MaxMessageSize = 5 };
            var buffer = new ArraySegment<byte>(new byte[100]);
            var ex = Assert.Throws<ServiceResultException>(() =>
                PubSubJsonDecoder.DecodeMessage<MinimalEncodeable>(buffer, ctx));
            Assert.That(ex!.StatusCode, Is.EqualTo((uint)StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void DecoderConstructorNullContextThrows()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new PubSubJsonDecoder("{}", null!));
        }

        // ── Default-value suppression differences between encoding modes ────────

        [Test]
        public void ReversibleIncludesDefaultNumberZero()
        {
            // IncludeDefaultNumberValues=true by default in Reversible, so 0 IS written.
            string json = Encode(e => e.WriteInt32("f", 0), PubSubJsonEncoding.Reversible);
            using var dec = MakeDecoder(json);
            Assert.That(dec.HasField("f"), Is.True);
            Assert.That(dec.ReadInt32("f"), Is.Zero);
        }

        [Test]
        public void EncoderWriteSwitchFieldCompact()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Compact);
            enc.WriteSwitchField(1u, out string? name);
            // In Compact (non-SuppressArtifacts) the SwitchField is written
            string json = enc.CloseAndReturnText();
            Assert.That(json, Does.Contain("SwitchField"));
        }

        [Test]
        public void EncoderWriteSwitchFieldReversible()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            enc.WriteSwitchField(2u, out string? fieldName);
            // Reversible mode: SwitchField is written and fieldName is set to "Value"
            Assert.That(fieldName, Is.EqualTo("Value"));
            string json = enc.CloseAndReturnText();
            Assert.That(json, Does.Contain("SwitchField"));
        }

        [Test]
        public void EncoderWriteSwitchFieldNonReversibleDoesNotWrite()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.NonReversible);
            enc.WriteSwitchField(3u, out string? fieldName);
            // NonReversible: no SwitchField written, fieldName remains null
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void EncoderWriteEncodingMaskCompact()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Compact);
            enc.WriteEncodingMask(7u);
            string json = enc.CloseAndReturnText();
            Assert.That(json, Does.Contain("EncodingMask"));
        }

        [Test]
        public void EncoderWriteEncodingMaskReversible()
        {
            var ctx = NewContext();
            using var enc = new PubSubJsonEncoder(ctx, PubSubJsonEncoding.Reversible);
            enc.WriteEncodingMask(15u);
            string json = enc.CloseAndReturnText();
            Assert.That(json, Does.Contain("EncodingMask"));
        }

        // ── PushNamespace / PopNamespace are no-ops on decoder ─────────────────

        [Test]
        public void DecoderPushAndPopNamespaceAreSafe()
        {
            using var dec = MakeDecoder("{\"f\":1}");
            Assert.DoesNotThrow(() =>
            {
                dec.PushNamespace("urn:test");
                dec.PopNamespace();
            });
        }

        // ── Multiple fields in one JSON object ─────────────────────────────────

        [Test]
        public void MultipleFieldsRoundTrip()
        {
            string json = Encode(e =>
            {
                e.WriteBoolean("boolF", true);
                e.WriteInt32("intF", 42);
                e.WriteString("strF", "hello");
            });

            using var dec = MakeDecoder(json);
            Assert.That(dec.ReadBoolean("boolF"), Is.True);
            Assert.That(dec.ReadInt32("intF"), Is.EqualTo(42));
            Assert.That(dec.ReadString("strF"), Is.EqualTo("hello"));
        }

        // ── ReadEnumerated ─────────────────────────────────────────────────────

        [Test]
        public void ReadEnumeratedFromIntegerToken()
        {
            using var dec = MakeDecoder("{\"f\":2}");
            var result = dec.ReadEnumerated<NodeClass>("f");
            Assert.That((int)result, Is.EqualTo(2));
        }

        [Test]
        public void ReadEnumeratedFromSymbolString()
        {
            // Encoder may emit "Variable_2" format for non-reversible enums.
            using var dec = MakeDecoder("{\"f\":\"Variable_2\"}");
            var result = dec.ReadEnumerated<NodeClass>("f");
            Assert.That((int)result, Is.EqualTo(2));
        }

        // ── Minimal helper encodeable ──────────────────────────────────────────

        private sealed class MinimalEncodeable : IEncodeable
        {
            public ExpandedNodeId TypeId => NodeId.Null;
            public ExpandedNodeId BinaryEncodingId => NodeId.Null;
            public ExpandedNodeId XmlEncodingId => NodeId.Null;

            public void Encode(IEncoder encoder) { }
            public void Decode(IDecoder decoder) { }
            public bool IsEqual(IEncodeable? encodeable) => true;
            public object Clone() => new MinimalEncodeable();
        }
    }
}
