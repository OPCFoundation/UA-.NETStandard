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
using System.Linq;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Tests.NativeTestSupport;

namespace Opc.Ua.EndpointRegistry.Tests
{
    /// <summary>
    /// Projects Endpoint Registry documents to their generated named records and restores them exactly.
    /// </summary>
    [TestFixture]
    [Category("EndpointRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class EndpointRegistryRecordMapperTests
    {
        [Test]
        public void Mqtt5EndpointWithInlineMessageProjectsTypedFieldsAndRestoresExactly()
        {
            RegistryValueDataType document = Json(Mqtt5Endpoint);

            RegistryRecordDataType record = Mapper.Project(document, DataTypeIds.EndpointDataType);

            var endpoint = (EndpointDataType)record;
            var options = (EndpointProtocolOptionsMQTT50DataType)endpoint.ProtocolOptions;
            RegistryNumberValueDataType qos = options.Qos;
            MessageDefinitionMapEntryDataType entry = endpoint.Messages.Entries.ToArray()!.Single();
            MessageDefinitionDataType message = entry.Value;
            var messageOptions = (MessageDefinitionProtocolOptionsMQTT50DataType)message.ProtocolOptions;
            var schema = (JsonSchemaContentDataType)message.DataSchema;
            RegistryValueDataType restored = Mapper.Restore(record);
            Assert.Multiple(() =>
            {
                Assert.That(endpoint.PresentFields.ToArray(), Is.EqualTo(s_endpointPresent));
                Assert.That(endpoint.AdditionalFields.ToArray()!.Select(m => m.Name), Is.EqualTo(s_rootExtensions));
                Assert.That(endpoint.EndpointId, Is.EqualTo("line1-ingest"));
                Assert.That(endpoint.Protocol, Is.EqualTo("mqtt"));
                Assert.That(endpoint.Usage.ToArray(), Is.EqualTo(s_producer));
                Assert.That(endpoint.Description, Is.Empty);
                Assert.That(endpoint.Deprecated, Is.Null);
                Assert.That(endpoint.Epoch, Is.Null);
                Assert.That(options.PresentFields.ToArray(), Is.EqualTo(s_optionsPresent));
                Assert.That(options.AdditionalFields.ToArray()!.Select(m => m.Name), Is.EqualTo(s_optionsExtensions));
                Assert.That(options.Topic, Is.EqualTo("factory/line1/temperature"));
                Assert.That(options.CleanStart, Is.False);
                Assert.That(options.Retain, Is.False);
                Assert.That(qos.Coefficient.ToArray(), Is.EqualTo(new byte[] { 1 }));
                Assert.That((qos.Exponent, qos.IsInteger, qos.NegativeZero), Is.EqualTo((0L, true, false)));
                Assert.That(options.SessionExpiryInterval.Coefficient.ToArray(), Is.EqualTo(new byte[] { 0x0E, 0x10 }));
                Assert.That(options.Endpoints.ToArray()!.Single().Uri, Is.EqualTo("mqtts://broker.example.test"));
                Assert.That(options.Endpoints.ToArray()!.Single().AdditionalFields.ToArray()!.Single().Name,
                    Is.EqualTo("x-endpoint"));
                Assert.That(options.Authorization.ToArray()!.Single().Authorityuri,
                    Is.EqualTo("https://identity.example.test/provisioning"));
                Assert.That(entry.Name, Is.EqualTo("temperature"));
                Assert.That(message.MessageId, Is.EqualTo("temperature"));
                Assert.That(message.DataSchemaFormat, Is.EqualTo("JsonSchema/2020-12"));
                Assert.That(messageOptions.TopicName, Is.EqualTo("factory/line1/temperature"));
                Assert.That(messageOptions.UserProperties.ToArray()!.Single().Value, Is.EqualTo("C"));
                Assert.That(schema.Format, Is.EqualTo("JsonSchema/2020-12"));
                Assert.That(schema.Root, Is.TypeOf<JsonSchemaObjectDataType>());
                Assert.That(((JsonSchemaObjectDataType)schema.Root).AdditionalFields.ToArray()!.Select(m => m.Name),
                    Does.Contain("x-unit"));
                Assert.That(message.AdditionalFields.ToArray()!.Select(m => m.Name), Is.EqualTo(s_messageExtensions));
                Assert.That(RegistryValues.Identical(document, restored), Is.True, ToJson(restored));
            });
        }

        [Test]
        public void EndpointRecordSurvivesBinaryEncodingAndRestoresExactly()
        {
            RegistryValueDataType document = Json(new StringBuilder(Mqtt5Endpoint).Replace(
                "\"dataschemaformat\": \"JsonSchema/2020-12\"", "\"dataschemaformat\": \"Vendor/1\"").ToString());
            RegistryRecordDataType record = Mapper.Project(document, DataTypeIds.EndpointDataType);

            IEncodeable body = BinaryRoundTrip(record);

            Assert.Multiple(() =>
            {
                Assert.That(body, Is.TypeOf<EndpointDataType>());
                Assert.That(((EndpointDataType)body).Messages.Entries[0].Value.DataSchema,
                    Is.TypeOf<ExtensionSchemaContentDataType>());
                Assert.That(Render(body), Is.EqualTo(Render(record)));
                Assert.That(RegistryValues.Identical(document, Mapper.Restore((RegistryRecordDataType)body)), Is.True);
            });
        }

        [TestCase("JsonSchema/2020-12", "{\"type\":\"object\",\"properties\":{\"v\":{\"type\":\"number\"}}}")]
        [TestCase("Avro/1.11", "{\"type\":\"record\",\"name\":\"R\",\"fields\":[{\"name\":\"a\",\"type\":\"long\"}]}")]
        public void ProviderSchemaContentSurvivesBinaryEncodingAndRestoresExactly(string format, string schema)
        {
            RegistryValueDataType document = Json(
                "{\"dataschemaformat\":\"" + format + "\",\"dataschema\":" + schema + "}");
            RegistryRecordDataType record = Mapper.Project(document, DataTypeIds.MessageDefinitionDataType);

            IEncodeable body = BinaryRoundTrip(record);

            Assert.Multiple(() =>
            {
                Assert.That(Render(body), Is.EqualTo(Render(record)));
                Assert.That(RegistryValues.Identical(document, Mapper.Restore((RegistryRecordDataType)body)), Is.True);
            });
        }

        [TestCase("1", "01", 0L, true)]
        [TestCase("1.0", "0a", -1L, false)]
        [TestCase("1e0", "01", 0L, false)]
        [TestCase("-0.0", "00", -1L, false)]
        [TestCase("18446744073709551616", "010000000000000000", 0L, true)]
        public void QosKeepsItsExactNumericForm(string qos, string coefficient, long exponent, bool isInteger)
        {
            RegistryValueDataType document = Json(
                "{\"protocol\":\"MQTT/5.0\",\"protocoloptions\":{\"qos\":" + qos + "}}");

            var record = (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);

            RegistryNumberValueDataType value = ((EndpointProtocolOptionsMQTT50DataType)record.ProtocolOptions).Qos;
            Assert.Multiple(() =>
            {
                Assert.That(Hex(value.Coefficient), Is.EqualTo(coefficient));
                Assert.That((value.Exponent, value.IsInteger), Is.EqualTo((exponent, isInteger)));
                Assert.That(ToJson(Mapper.Restore(record)), Is.EqualTo(ToJson(document)));
            });
        }

        [TestCase("1.5", "fractional")]
        [TestCase("5e-9223372036854775808", "fractional")]
        [TestCase("-1", "negative")]
        [TestCase("\"1\"", "number is required")]
        [TestCase("true", "number is required")]
        [TestCase("null", "number is required")]
        public void QosOutsideItsSourceShapeIsRejectedWithItsPath(string qos, string reason)
        {
            RegistryValueDataType document = Json(
                "{\"protocol\":\"MQTT\",\"protocoloptions\":{\"qos\":" + qos + "}}");

            RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(
                () => Mapper.Project(document, DataTypeIds.EndpointDataType))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(error.Message, Does.Contain(reason).IgnoreCase);
                Assert.That(error.Path.ToArray(), Is.EqualTo(s_qosPath));
            });
        }

        [TestCase("MQTT/5.0", typeof(EndpointProtocolOptionsMQTT50DataType))]
        [TestCase("mqtt/5.0", typeof(EndpointProtocolOptionsMQTT50DataType))]
        [TestCase("MQTT", typeof(EndpointProtocolOptionsMQTT50DataType))]
        [TestCase("Mqtt", typeof(EndpointProtocolOptionsMQTT50DataType))]
        [TestCase("MQTT/3.1.1", typeof(EndpointProtocolOptionsMQTT311DataType))]
        [TestCase("amqp", typeof(EndpointProtocolOptionsAMQP10DataType))]
        [TestCase("Kafka", typeof(EndpointProtocolOptionsKAFKADataType))]
        [TestCase("webrtc", typeof(EndpointProtocolOptionsWebRTCDataType))]
        [TestCase("rist-main/2024", typeof(EndpointProtocolOptionsRISTMain2024DataType))]
        [TestCase("MQTT/5", typeof(EndpointProtocolOptionsExtensionDataType))]
        [TestCase("MQTT5", typeof(EndpointProtocolOptionsExtensionDataType))]
        [TestCase("urn:vendor:proto/1", typeof(EndpointProtocolOptionsExtensionDataType))]
        public void ProtocolSelectorAcceptsAliasesAndCaseButPreservesTheAuthoredSelector(
            string protocol,
            Type expected)
        {
            RegistryValueDataType document = Json(
                "{\"protocol\":\"" + protocol + "\",\"protocoloptions\":{\"deployed\":true,\"x-vendor\":[1,\"a\"]}}");

            var record = (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);

            Assert.Multiple(() =>
            {
                Assert.That(record.ProtocolOptions, Is.TypeOf(expected));
                Assert.That(record.Protocol, Is.EqualTo(protocol));
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(record)), Is.True);
            });
        }

        [TestCase("{\"protocol\":\"Custom/1\",\"protocoloptions\":7}", "7")]
        [TestCase("{\"protocol\":\"Custom/1\",\"protocoloptions\":[\"a\",1.50,null]}", "[\"a\",150e-2,null]")]
        [TestCase("{\"protocoloptions\":\"raw\"}", "\"raw\"")]
        public void UnknownOrMissingSelectorKeepsANonObjectSiblingValueExactly(string text, string value)
        {
            RegistryValueDataType document = Json(text);

            var record = (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);

            var options = (EndpointProtocolOptionsExtensionDataType)record.ProtocolOptions;
            Assert.Multiple(() =>
            {
                Assert.That(options.PresentFields.ToArray(), Is.Empty);
                Assert.That(options.AdditionalFields.ToArray(), Is.Empty);
                Assert.That(ToJson(options.ExtensionValue), Is.EqualTo(value));
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(record)), Is.True);
            });
        }

        [Test]
        public void UnknownSelectorKeepsObjectMembersWithoutKnownSelectorConstraints()
        {
            RegistryValueDataType document = Json(
                "{\"protocol\":\"urn:vendor:proto/1\",\"protocoloptions\":" +
                "{\"deployed\":\"not-a-boolean\",\"qos\":\"high\",\"nested\":{\"a\":[1,2.50]}}}");

            var record = (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);

            var options = (EndpointProtocolOptionsExtensionDataType)record.ProtocolOptions;
            Assert.Multiple(() =>
            {
                Assert.That(options.AdditionalFields.ToArray()!.Select(m => m.Name), Is.EqualTo(s_fallbackMembers));
                Assert.That(options.Deployed, Is.False);
                Assert.That(options.ExtensionValue, Is.Null);
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(record)), Is.True);
            });
        }

        [TestCase("{\"protocol\":\"MQTT/5.0\",\"protocoloptions\":7}", "Object")]
        [TestCase("{\"protocol\":\"MQTT/5.0\",\"protocoloptions\":{\"retain\":\"yes\"}}", "Boolean")]
        [TestCase("{\"protocol\":5,\"protocoloptions\":{}}", "String value is required")]
        [TestCase("{\"usage\":\"producer\"}", "array value is required")]
        [TestCase("{\"usage\":[1]}", "String value is required")]
        [TestCase("{\"messages\":[]}", "map value requires an Object")]
        [TestCase("{\"labels\":{\"a\":1}}", "String value is required")]
        [TestCase("{\"epoch\":-1}", "negative")]
        public void DeclaredShapesAreEnforcedExplicitly(string text, string reason)
        {
            RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(
                () => Mapper.Project(Json(text), DataTypeIds.EndpointDataType))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(error.Message, Does.Contain(reason).IgnoreCase);
            });
        }

        [Test]
        public void PresenceDistinguishesAbsentFalseEmptyAndNull()
        {
            RegistryValueDataType explicitDocument = Json(
                "{\"name\":\"\",\"usage\":[],\"protocoloptions\":{},\"labels\":{},\"messagescount\":null," +
                "\"deprecated\":{}}");
            var absent = (EndpointDataType)Mapper.Project(Json("{}"), DataTypeIds.EndpointDataType);

            var present = (EndpointDataType)Mapper.Project(explicitDocument, DataTypeIds.EndpointDataType);

            Assert.Multiple(() =>
            {
                Assert.That(absent.PresentFields.ToArray(), Is.Empty);
                Assert.That(present.PresentFields.ToArray(), Is.EqualTo(s_explicitPresent));
                Assert.That((absent.Name, present.Name), Is.EqualTo((string.Empty, string.Empty)));
                Assert.That((absent.Usage.Count, present.Usage.Count), Is.EqualTo((0, 0)));
                Assert.That(absent.Labels, Is.Null);
                Assert.That(present.Labels.Entries.ToArray(), Is.Empty);
                Assert.That(absent.Messagescount, Is.Null);
                Assert.That(present.Messagescount, Is.TypeOf<RegistryNullValueDataType>());
                Assert.That(present.ProtocolOptions, Is.TypeOf<EndpointProtocolOptionsExtensionDataType>());
                Assert.That(ToJson(Mapper.Restore(absent)), Is.EqualTo("{}"));
                Assert.That(RegistryValues.Identical(explicitDocument, Mapper.Restore(present)), Is.True);
            });
        }

        [Test]
        public void KnownFieldsFollowDeclarationOrderIndependentOfDocumentOrder()
        {
            var first = (EndpointDataType)Mapper.Project(
                Json("{\"usage\":[\"b\",\"a\"],\"name\":\"n\",\"x-late\":1,\"channel\":\"c\",\"x-early\":2}"),
                DataTypeIds.EndpointDataType);
            var second = (EndpointDataType)Mapper.Project(
                Json("{\"channel\":\"c\",\"x-late\":1,\"usage\":[\"b\",\"a\"],\"x-early\":2,\"name\":\"n\"}"),
                DataTypeIds.EndpointDataType);

            Assert.Multiple(() =>
            {
                Assert.That(first.PresentFields.ToArray(), Is.EqualTo(s_orderedPresent));
                Assert.That(second.PresentFields.ToArray(), Is.EqualTo(s_orderedPresent));
                Assert.That(first.AdditionalFields.ToArray()!.Select(m => m.Name), Is.EqualTo(s_lateEarly));
                Assert.That(Render(first), Is.EqualTo(Render(second)));
            });
        }

        [Test]
        public void RootRegistryContainsEndpointAndMessageGroupMapsWithMessageVersions()
        {
            RegistryValueDataType document = Json(
                "{\"registryid\":\"r\",\"endpoints\":{\"e\":{\"endpointid\":\"e\",\"messages\":{\"m\":{}}}}," +
                "\"messagegroups\":{\"g\":{\"messagegroupid\":\"g\",\"messages\":{\"m\":{\"versionid\":\"1\"," +
                "\"versions\":{\"1\":{\"versionid\":\"1\"}}}}}},\"endpointscount\":1}");

            var root = (EndpointRegistryDocumentDataType)Mapper.Project(document,
                DataTypeIds.EndpointRegistryDocumentDataType);

            MessageDefinitionDataType groupMessage = root.Messagegroups.Entries.ToArray()!.Single().Value.Messages
                .Entries.ToArray()!.Single().Value;
            Assert.Multiple(() =>
            {
                Assert.That(root.Endpoints.Entries.ToArray()!.Single().Value.Messages.Entries.Count, Is.EqualTo(1));
                Assert.That(groupMessage.Versions.Entries.ToArray()!.Single().Value.VersionId, Is.EqualTo("1"));
                Assert.That(root.Endpointscount, Is.TypeOf<RegistryNumberValueDataType>());
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(root)), Is.True);
            });
        }

        [Test]
        public void InlineSchemaUsesInjectedProvidersOrFailsExplicitly()
        {
            RegistryValueDataType json = Json(
                "{\"dataschemaformat\":\"jsonschema/2020-12\",\"dataschema\":{\"type\":\"string\",\"x-k\":[1.0]}}");
            RegistryValueDataType avro = Json(
                "{\"dataschemaformat\":\"Avro/1.11\",\"dataschema\":{\"type\":\"record\",\"name\":\"R\"," +
                "\"fields\":[{\"name\":\"a\",\"type\":\"long\"}]}}");
            RegistryValueDataType custom = Json("{\"dataschemaformat\":\"Custom/1\",\"dataschema\":[1,{\"a\":null}]}");
            RegistryValueDataType unlabeled = Json("{\"dataschema\":{\"type\":\"string\"}}");
            RegistryValueDataType arrow = Json("{\"dataschemaformat\":\"ApacheArrow/1.0\",\"dataschema\":{}}");
            RegistryRecordMapper withoutProviders = EndpointRegistryNativeCatalog.CreateMapper(Context, []);

            var jsonRecord = (MessageDefinitionDataType)Mapper.Project(json, DataTypeIds.MessageDefinitionDataType);
            var avroRecord = (MessageDefinitionDataType)Mapper.Project(avro, DataTypeIds.MessageDefinitionDataType);
            var customRecord = (MessageDefinitionDataType)Mapper.Project(custom,
                DataTypeIds.MessageDefinitionDataType);
            var unlabeledRecord = (MessageDefinitionDataType)Mapper.Project(unlabeled,
                DataTypeIds.MessageDefinitionDataType);
            RegistryRecordMappingException missing = Assert.Throws<RegistryRecordMappingException>(
                () => withoutProviders.Project(json, DataTypeIds.MessageDefinitionDataType))!;
            RegistryRecordMappingException inlineArrow = Assert.Throws<RegistryRecordMappingException>(
                () => Mapper.Project(arrow, DataTypeIds.MessageDefinitionDataType))!;

            Assert.Multiple(() =>
            {
                Assert.That(jsonRecord.DataSchema, Is.TypeOf<JsonSchemaContentDataType>());
                Assert.That(avroRecord.DataSchema, Is.TypeOf<AvroSchemaContentDataType>());
                Assert.That(customRecord.DataSchema, Is.TypeOf<ExtensionSchemaContentDataType>());
                Assert.That(ToJson(((ExtensionSchemaContentDataType)customRecord.DataSchema).Root),
                    Is.EqualTo("[1,{\"a\":null}]"));
                Assert.That(unlabeledRecord.DataSchema, Is.TypeOf<ExtensionSchemaContentDataType>());
                Assert.That(RegistryValues.Identical(json, Mapper.Restore(jsonRecord)), Is.True);
                Assert.That(RegistryValues.Identical(avro, Mapper.Restore(avroRecord)), Is.True);
                Assert.That(RegistryValues.Identical(custom, Mapper.Restore(customRecord)), Is.True);
                Assert.That(RegistryValues.Identical(unlabeled, Mapper.Restore(unlabeledRecord)), Is.True);
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(missing.Path.ToArray(), Is.EqualTo(s_schemaPath));
                Assert.That(inlineArrow.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(inlineArrow.Message, Does.Contain("Arrow"));
                Assert.That(() => withoutProviders.Restore(jsonRecord),
                    Throws.TypeOf<RegistryRecordMappingException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadNotSupported));
            });
        }

        [Test]
        public void NativeInlineArrowSchemaIsRejectedAsAnInvalidArgument()
        {
            var record = (MessageDefinitionDataType)Mapper.Project(
                Json("{\"dataschemaformat\":\"ApacheArrow/1.0\"}"), DataTypeIds.MessageDefinitionDataType);
            record.DataSchema = new ArrowIpcSchemaContentDataType { Format = "ApacheArrow/1.0" };
            record.PresentFields = ["DataSchemaFormat", "DataSchema"];

            RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(
                () => Mapper.Restore(record))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(error.Message, Does.Contain("Arrow"));
                Assert.That(error.Path.ToArray(), Is.EqualTo(s_schemaPath));
            });
        }

        [Test]
        public void RestoreRejectsContradictoryOrNoncanonicalRecords()
        {
            RegistryValueDataType document = Json(
                "{\"name\":\"n\",\"protocol\":\"MQTT\",\"protocoloptions\":{\"retain\":true}," +
                "\"labels\":{\"a\":\"1\"}}");
            EndpointDataType Fresh() => (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);
            var amqp = (EndpointDataType)Mapper.Project(
                Json("{\"protocol\":\"AMQP\",\"protocoloptions\":{\"durable\":true}}"), DataTypeIds.EndpointDataType);
            var fallback = (EndpointDataType)Mapper.Project(
                Json("{\"protocoloptions\":{\"retain\":true}}"), DataTypeIds.EndpointDataType);
            EndpointDataType wrongSubtype = Fresh();
            wrongSubtype.ProtocolOptions = amqp.ProtocolOptions;
            EndpointDataType extensionUnderMqtt = Fresh();
            extensionUnderMqtt.ProtocolOptions = fallback.ProtocolOptions;
            EndpointDataType abstractRoot = Fresh();
            abstractRoot.ProtocolOptions = new EndpointProtocolOptionsDataType();
            EndpointDataType selectorChanged = Fresh();
            selectorChanged.Protocol = "AMQP";
            EndpointDataType selectorMissing = Fresh();
            selectorMissing.Protocol = string.Empty;
            selectorMissing.PresentFields = [.. Fresh().PresentFields.ToArray()!.Where(name => name != "Protocol")];
            EndpointDataType shadowed = Fresh();
            shadowed.AdditionalFields = [new RegistryMemberDataType { Name = "protocol", Value = Json("\"x\"") }];
            EndpointDataType absentWithValue = Fresh();
            absentWithValue.Channel = "c";
            EndpointDataType absentNull = Fresh();
            absentNull.Channel = null;
            EndpointDataType unknownPresence = Fresh();
            unknownPresence.PresentFields = [.. Fresh().PresentFields, "Nope"];
            EndpointDataType repeatedPresence = Fresh();
            repeatedPresence.PresentFields = [.. Fresh().PresentFields, "Name"];
            EndpointDataType duplicateEntry = Fresh();
            duplicateEntry.Labels.Entries = [.. duplicateEntry.Labels.Entries, duplicateEntry.Labels.Entries[0]];
            EndpointDataType presentNull = Fresh();
            presentNull.Labels = null!;

            Assert.Multiple(() =>
            {
                Assert.That(RegistryValues.Identical(document, Mapper.Restore(Fresh())), Is.True);
                foreach ((string label, EndpointDataType record) in new[]
                {
                    ("wrong family subtype", wrongSubtype),
                    ("fallback under a declared selector", extensionUnderMqtt),
                    ("abstract family root", abstractRoot),
                    ("selector changed", selectorChanged),
                    ("selector missing", selectorMissing),
                    ("known member in AdditionalFields", shadowed),
                    ("absent field with a value", absentWithValue),
                    ("absent String left null", absentNull),
                    ("unknown presence", unknownPresence),
                    ("repeated presence", repeatedPresence),
                    ("duplicate map entry", duplicateEntry),
                    ("present field without a value", presentNull)
                })
                {
                    Assert.That(() => Mapper.Restore(record), Throws.TypeOf<RegistryRecordMappingException>(), label);
                }
            });
        }

        [Test]
        public void NullGenericArrayCannotBeNormalizedDuringRecordMapping()
        {
            var array = new RegistryArrayValueDataType { Kind = 4, Items = ArrayOf<RegistryValueDataType>.Null };
            Assert.That(array.Items.IsNull, Is.True);
            var record = (EndpointDataType)Mapper.Project(Json("""{"x-container":[]}"""), DataTypeIds.EndpointDataType);
            record.AdditionalFields[0].Value = array;
            Assert.That(() => Mapper.Restore(record), Throws.TypeOf<RegistryRecordMappingException>());
            foreach (string name in new[] { "x-container", "usage" })
            {
                var source = new RegistryObjectValueDataType
                {
                    Kind = 5,
                    Members = [new RegistryMemberDataType { Name = name, Value = array }]
                };
                Assert.That(() => Mapper.Project(source, DataTypeIds.EndpointDataType),
                    Throws.TypeOf<RegistryRecordMappingException>());
            }
        }

        [Test]
        public void ProjectionIsDetachedFromTheSourceDocument()
        {
            var document = (RegistryObjectValueDataType)Json(
                "{\"usage\":[\"producer\"],\"x-vendor\":{\"list\":[1]},\"labels\":{\"k\":\"v\"}}");
            string before = ToJson(document);
            var record = (EndpointDataType)Mapper.Project(document, DataTypeIds.EndpointDataType);

            ((RegistryArrayValueDataType)((RegistryObjectValueDataType)document.Members[1].Value).Members[0].Value)
                .Items = [];
            ((RegistryStringValueDataType)((RegistryArrayValueDataType)document.Members[0].Value).Items[0])
                .Value = "changed";

            Assert.That(RegistryValues.Identical(Json(before), Mapper.Restore(record)), Is.True);
        }

        [Test]
        public void NestingBeyondTheNativeLimitIsExplicit()
        {
            string deep = string.Concat(Enumerable.Repeat("[", 200)) + string.Concat(Enumerable.Repeat("]", 200));

            RegistryRecordMappingException error = Assert.Throws<RegistryRecordMappingException>(
                () => Mapper.Project(RegistryValues.Parse(
                    System.Text.Encoding.UTF8.GetBytes("{\"x-deep\":" + deep + "}"), 256, 100000),
                    DataTypeIds.EndpointDataType))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
        }

        [Test]
        public void NonRecordOrUnpublishedDataTypesAreRejected()
        {
            RegistryValueDataType document = Json("{}");

            Assert.Multiple(() =>
            {
                Assert.That(() => Mapper.Project(document, DataTypeIds.MessageDefinitionMapDataType),
                    Throws.TypeOf<RegistryRecordMappingException>());
                Assert.That(() => Mapper.Project(document, DataTypeIds.EndpointProtocolOptionsDataType),
                    Throws.TypeOf<RegistryRecordMappingException>().With.Message.Contains("abstract"));
                Assert.That(() => Mapper.Project(document, DataTypeIds.PubSubBindingSnapshotDataType),
                    Throws.TypeOf<RegistryRecordMappingException>());
            });
        }

        [Test]
        public void DependencyInjectionProvidesTheSchemaContentAdapter()
        {
            using ServiceProvider services = new ServiceCollection()
                .AddSchemaRegistryFormats()
                .AddEndpointRegistryNativeMapping()
                .AddEndpointRegistryNativeMapping()
                .BuildServiceProvider();
            IRegistryNativeValueAdapter[] adapters = [.. services.GetServices<IRegistryNativeValueAdapter>()];
            var mapper = new RegistryRecordMapper(EndpointRegistryNativeCatalog.Catalog, Context, adapters);
            RegistryValueDataType document = Json(
                "{\"dataschemaformat\":\"JsonSchema/2020-12\",\"dataschema\":{\"type\":\"object\"}}");

            var record = (MessageDefinitionDataType)mapper.Project(document, DataTypeIds.MessageDefinitionDataType);

            Assert.Multiple(() =>
            {
                Assert.That(adapters, Has.Length.EqualTo(1));
                Assert.That(adapters[0], Is.TypeOf<SchemaContentValueAdapter>());
                Assert.That(record.DataSchema, Is.TypeOf<JsonSchemaContentDataType>());
                Assert.That(RegistryValues.Identical(document, mapper.Restore(record)), Is.True);
            });
        }

        private const string Mqtt5Endpoint = """
            {
              "endpointid": "line1-ingest",
              "name": "Line 1 ingest",
              "usage": ["producer"],
              "channel": "temperature",
              "protocol": "mqtt",
              "protocoloptions": {
                "endpoints": [{"uri": "mqtts://broker.example.test", "x-endpoint": true}],
                "topic": "factory/line1/temperature",
                "qos": 1,
                "cleanstart": false,
                "sessionexpiryinterval": 3600,
                "authorization": [{"type": "Plain", "authorityuri": "https://identity.example.test/provisioning"}],
                "x-vendor": {"retry": [1, 2.50, null], "nested": {"deep": {"flag": false}}}
              },
              "messages": {
                "temperature": {
                  "messageid": "temperature",
                  "protocol": "MQTT/5.0",
                  "protocoloptions": {
                    "topic_name": "factory/line1/temperature",
                    "qos": 1,
                    "user_properties": [{"name": "unit", "value": "C"}]
                  },
                  "datacontenttype": "application/json",
                  "dataschemaformat": "JsonSchema/2020-12",
                  "dataschema": {
                    "type": "object",
                    "properties": {"value": {"type": "number"}},
                    "x-unit": {"symbol": "C", "scale": 1.00}
                  },
                  "x-message-extension": [true, {"k": 9007199254740993.125}]
                }
              },
              "x-root": {"a": []}
            }
            """;

        private static readonly string[] s_endpointPresent =
            ["Name", "EndpointId", "Usage", "Channel", "Protocol", "ProtocolOptions", "Messages"];
        private static readonly string[] s_rootExtensions = ["x-root"];
        private static readonly string[] s_producer = ["producer"];
        private static readonly string[] s_optionsPresent =
            ["Authorization", "Endpoints", "Topic", "Qos", "CleanStart", "SessionExpiryInterval"];
        private static readonly string[] s_optionsExtensions = ["x-vendor"];
        private static readonly string[] s_messageExtensions = ["x-message-extension"];
        private static readonly string[] s_qosPath = ["protocoloptions", "qos"];
        private static readonly string[] s_schemaPath = ["dataschema"];
        private static readonly string[] s_fallbackMembers = ["deployed", "qos", "nested"];
        private static readonly string[] s_explicitPresent =
            ["Name", "Deprecated", "Labels", "Usage", "ProtocolOptions", "Messagescount"];
        private static readonly string[] s_orderedPresent = ["Name", "Usage", "Channel"];
        private static readonly string[] s_lateEarly = ["x-late", "x-early"];
    }
}
