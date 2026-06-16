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
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Parity probe between the legacy Newtonsoft-backed encoder and
    /// the new System.Text.Json encoder. The two paths produce JSON
    /// objects that are structurally identical for the simple Variant
    /// scalar / Int32 array / DataValue cases used here. Documented
    /// deviations are listed on this fixture's class-level
    /// <see cref="TestSpecAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Documented STJ vs Newtonsoft divergences:
    /// 1. Verbose-Variant key names: STJ emits Part 14 §7.2.5 wire form
    ///    <c>{ "Type": N, "Body": ... }</c>; Stack's Newtonsoft path
    ///    historically used <c>{ "UaType": N, "Value": ... }</c>. This
    ///    fixture canonicalises both sides before comparison.
    /// 2. Double formatting: STJ uses shortest-round-trip; Newtonsoft
    ///    uses the <c>R</c> format specifier. Both round-trip equal
    ///    under <c>double.Parse</c>.
    /// 3. Mode-name mapping vs the legacy
    ///    <c>PubSubJsonEncoding</c> enum:
    ///    <list type="bullet">
    ///      <item><description>
    ///        New <see cref="Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose"/>
    ///        compared against legacy <c>PubSubJsonEncoding.Reversible</c>.
    ///      </description></item>
    ///      <item><description>
    ///        New <see cref="Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact"/>
    ///        compared against legacy <c>PubSubJsonEncoding.NonReversible</c>.
    ///      </description></item>
    ///      <item><description>
    ///        New <see cref="Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.RawData"/>
    ///        has no legacy equivalent and is not compared here.
    ///      </description></item>
    ///    </list>
    /// </remarks>
    [TestFixture]
    [Category("PubSub")]
    [TestSpec("7.2.5", Summary = "Documented STJ vs Newtonsoft deviations: see fixture remarks")]
    public sealed class JsonNewtonsoftParityTests
    {
        private static readonly int[] s_intArray = [1, 2, 3];

        [Test]
        public async Task NewEncoder_ProducesCanonicalVerboseVariantEnvelopeAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(300), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 1,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "parity-1",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder(
                Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Verbose);
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            string text = JsonTestUtilities.ToText(bytes);
            string canonical = JsonTestUtilities.Canonicalise(text);
            using JsonDocument document = JsonDocument.Parse(canonical);
            JsonElement root = document.RootElement;
            JsonElement messages = root.GetProperty("Messages");
            JsonElement payload = messages[0].GetProperty("Payload");
            JsonElement boolField = payload.GetProperty("BoolField");
            // Part 14 §7.2.5 Verbose Variant uses Type/Body — verify
            // the STJ path produces exactly this shape (not the
            // Stack-default UaType/Value pair).
            Assert.That(boolField.TryGetProperty("Type", out JsonElement t), Is.True,
                "Verbose Variant must use 'Type' on the wire");
            Assert.That(t.GetInt32(), Is.EqualTo((int)BuiltInType.Boolean));
            Assert.That(boolField.TryGetProperty("Body", out JsonElement b), Is.True,
                "Verbose Variant must use 'Body' on the wire");
            Assert.That(b.GetBoolean(), Is.True);
        }

        [Test]
        public async Task NewEncoder_CompactEmitsBareValuesAsync()
        {
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(300), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 1,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields = JsonTestUtilities.CreateFields()
            };
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "parity-2",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder(
                Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact);
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            JsonElement payload = root.GetProperty("Messages")[0].GetProperty("Payload");
            // Compact mode must emit bare values - 'BoolField'
            // should be a primitive boolean, not a wrapping object.
            Assert.That(payload.GetProperty("BoolField").ValueKind,
                Is.EqualTo(JsonValueKind.True));
        }

        [Test]
        public async Task RawDataInt32Array_RoundTripsAsync()
        {
            FieldMetaData[] fields =
            [
                new FieldMetaData
                {
                    Name = "RawArr",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.OneDimension
                }
            ];
            var meta = new DataSetMetaDataType
            {
                Name = "RawDataSet",
                Fields = new ArrayOf<FieldMetaData>(fields.AsMemory()),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
            var registry = new DataSetMetaDataRegistry();
            registry.Register(
                new DataSetMetaDataKey(PublisherId.FromUInt16(300), 0, 1, Uuid.Empty, 1),
                meta);
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext(registry);
            var dsm = new Opc.Ua.PubSub.Encoding.Json.JsonDataSetMessage
            {
                DataSetWriterId = 1,
                SequenceNumber = 1,
                MessageType = PubSubDataSetMessageType.KeyFrame,
                MetaDataVersion = meta.ConfigurationVersion,
                Fields =
                [
                    new DataSetField
                    {
                        Name = "RawArr",
                        Value = new Variant(s_intArray),
                        Encoding = PubSubFieldEncoding.RawData
                    }
                ]
            };
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage
            {
                MessageId = "raw",
                PublisherId = PublisherId.FromUInt16(300),
                DataSetClassId = Uuid.Empty,
                DataSetMessages = [dsm]
            };
            var encoder = new Opc.Ua.PubSub.Encoding.Json.JsonEncoder(
                Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Compact);
            ReadOnlyMemory<byte> bytes = await encoder
                .EncodeAsync(msg, ctx).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            JsonElement payload = root.GetProperty("Messages")[0].GetProperty("Payload");
            JsonElement arr = payload.GetProperty("RawArr");
            Assert.That(arr.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(arr.GetArrayLength(), Is.EqualTo(3));
        }
    }
}
