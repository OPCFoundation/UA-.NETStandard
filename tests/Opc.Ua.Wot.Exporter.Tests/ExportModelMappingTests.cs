// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter.Tests
{
    /// <summary>
    /// Covers publisher metadata and ordinary WoT schemas at the export boundary.
    /// </summary>
    [TestFixture]
    public sealed partial class ExportModelMappingTests
    {
        /// <summary>
        /// Keeps document identity, native type names and declaration names separate.
        /// </summary>
        [Test]
        public void NamesUseOriginalDeclaringTypeAndNotAffordanceKeys()
        {
            UANodeSet source = Source("""
                <UAObjectType NodeId="ns=1;i=2" BrowseName="1:ChildType">
                  <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=1</Reference></References>
                </UAObjectType>
                <UAVariable NodeId="ns=1;i=10" BrowseName="1:Speed" DataType="i=5">
                  <References><Reference ReferenceType="i=47" IsForward="false">ns=1;i=1</Reference></References>
                </UAVariable>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {
                  "id": "urn:independent:document",
                  "uav:id": "nsu=https://models.example.com/pump;i=2",
                  "properties": {
                    "renamed": {
                      "uav:id": "nsu=https://models.example.com/pump;i=10",
                      "uav:mapToType": "i=5"
                    }
                  }
                }
                """);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("id").GetString(), Is.EqualTo("urn:independent:document"));
            Assert.That(root.GetProperty("uav:fullName").GetString(),
                Is.EqualTo("com.example.models.pump.ChildType"));
            Assert.That(root.GetProperty("uav:typeName").GetString(),
                Is.EqualTo("com.example.models.pump.ChildType"));
            JsonElement property = root.GetProperty("properties").GetProperty("renamed");
            Assert.That(property.GetProperty("uav:fullName").GetString(),
                Is.EqualTo("com.example.models.pump.PumpType.Speed"));
            Assert.That(property.GetProperty("uav:typeName").GetString(),
                Is.EqualTo("org.opcfoundation.UA.UInt16"));
        }

        /// <summary>
        /// Requires an explicit native-identity mapping when reverse-DNS derivation is invalid.
        /// </summary>
        [TestCase("urn:example:pump")]
        [TestCase("https://models.example.com:443/pump")]
        [TestCase("https://models.example.com/pump?query")]
        [TestCase("https://models.example.com/pump#fragment")]
        [TestCase("https://user@models.example.com/pump")]
        [TestCase("https://models.example.com./pump")]
        [TestCase("https://model-host.example.com/pump")]
        [TestCase("https://models.example.com/123")]
        public void InvalidDerivedNamesRequireExplicitMappings(string namespaceUri)
        {
            UANodeSet source = Source(string.Empty);
            source.NamespaceUris = [namespaceUri];
            string identity = new ExpandedNodeId(new NodeId(1), namespaceUri).ToString();
            string json = new System.Text.Json.Nodes.JsonObject { ["uav:id"] = identity }.ToJsonString();
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            Assert.That(() => mapping.Apply(Encoding.UTF8.GetBytes(json), "model", []),
                Throws.TypeOf<FormatException>().With.Message.Contains("explicit"));
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(
                new System.Text.Json.Nodes.JsonObject
                {
                    [identity] = new System.Text.Json.Nodes.JsonObject { ["name"] = "configured.Pump" }
                }.ToJsonString()));
            mapping = new ExportModelMapping([source], metadata, false);
            using JsonDocument document = Apply(mapping, json);
            Assert.That(document.RootElement.GetProperty("uav:typeName").GetString(), Is.EqualTo("configured.Pump"));
        }

        /// <summary>
        /// Fails only documents affected by a name collision and permits explicit disambiguation.
        /// </summary>
        [Test]
        public void CollisionsDoNotUseOrderDependentNameSuffixes()
        {
            UANodeSet source = Source("""
                <UAObjectType NodeId="ns=1;i=2" BrowseName="1:PumpType" />
                <UAObjectType NodeId="ns=1;i=3" BrowseName="1:UnaffectedType" />
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            Assert.That(() => mapping.Apply(Encoding.UTF8.GetBytes(k_root), "model", []),
                Throws.TypeOf<FormatException>().With.Message.Contains("collision"));
            using JsonDocument unaffected = Apply(mapping,
                k_root.Replace(";i=1", ";i=3", StringComparison.Ordinal));
            Assert.That(unaffected.RootElement.GetProperty("uav:typeName").GetString(),
                Does.EndWith(".UnaffectedType"));
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {"nsu=https://models.example.com/pump;i=2":{"name":"configured.SecondPump"}}
                """));
            mapping = new ExportModelMapping([source], metadata, false);
            using JsonDocument resolved = Apply(mapping, k_root);
            Assert.That(resolved.RootElement.GetProperty("uav:typeName").GetString(),
                Is.EqualTo("com.example.models.pump.PumpType"));
        }

        /// <summary>
        /// Distinguishes false standalone intent from omission and preserves it through native residue.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void StandaloneIntentSurvivesRoundTripWithoutInheritance(bool standalone)
        {
            UANodeSet source = Source("""
                <UAObjectType NodeId="ns=1;i=2" BrowseName="1:DerivedType">
                  <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=1</Reference></References>
                </UAObjectType>
                """);
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(
                $$$"""
                {"nsu=https://models.example.com/pump;i=1":{
                  "isStandalone":{{{standalone.ToString().ToLowerInvariant()}}}
                }}
                """));
            var mapping = new ExportModelMapping([source], metadata, false);
            using WotDocument original = WotNodeSetConverter.FromNodeSet(source);
            using var published = WotDocument.Parse(mapping.Apply(
                original.Utf8Json, "model", []));
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(published);
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(restored);
            Assert.That(regenerated.RootElement.GetProperty("uav:isStandalone").GetBoolean(), Is.EqualTo(standalone));
            Assert.That(restored.Items!.OfType<UAVariable>(), Is.Empty);
            using JsonDocument child = Apply(mapping, k_root.Replace(";i=1", ";i=2", StringComparison.Ordinal));
            Assert.That(child.RootElement.TryGetProperty("uav:isStandalone", out _), Is.False);
        }

        /// <summary>
        /// Rejects standalone intent outside an ObjectType model root.
        /// </summary>
        [Test]
        public void StandaloneIntentIsNotAppliedToEventModels()
        {
            UANodeSet source = Source(string.Empty);
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {"nsu=https://models.example.com/pump;i=1":{"isStandalone":true}}
                """));
            var mapping = new ExportModelMapping([source], metadata, false);
            Assert.That(() => mapping.Apply(Encoding.UTF8.GetBytes(k_root.Replace(
                "\"uav:id\"", "\"@type\":\"uav:eventType\",\"uav:id\"", StringComparison.Ordinal)), "event", []),
                Throws.TypeOf<FormatException>().With.Message.Contains("not permitted"));
        }

        /// <summary>
        /// Rejects invalid publisher assertions instead of silently changing their meaning.
        /// </summary>
        [TestCase(/*lang=json,strict*/ """{"isStandalone":"false"}""")]
        [TestCase(/*lang=json,strict*/ """{"name":"bad-name"}""")]
        [TestCase(/*lang=json,strict*/ """{"eventContractType":[]}""")]
        [TestCase(/*lang=json,strict*/ """{"eventContractType":["test.Contract.v01"]}""")]
        [TestCase(/*lang=json,strict*/ """{"eventContractType":["test.Contract.v1","test.Contract.v1"]}""")]
        public void InvalidPublisherMetadataFails(string entry)
        {
            Assert.That(() => ExportMetadata.Parse(Encoding.UTF8.GetBytes(
                "{\"i=1\":" + entry + "}")), Throws.TypeOf<FormatException>());
        }

        /// <summary>
        /// Adds an assigned CloudEvents contract only to its interaction.
        /// </summary>
        [Test]
        public void CloudEventsContractsAreExplicitAndAffordanceScoped()
        {
            UANodeSet source = Source("""
                <UAVariable NodeId="ns=1;i=10" BrowseName="1:Speed" DataType="i=5">
                  <References><Reference ReferenceType="i=47" IsForward="false">ns=1;i=1</Reference></References>
                </UAVariable>
                """);
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {"nsu=https://models.example.com/pump;i=10":{"eventContractType":["plant.Speed.v2","plant.Speed.v3"]}}
                """));
            const string json = /*lang=json,strict*/ """
                {"uav:id":"nsu=https://models.example.com/pump;i=1","properties":{
                  "Speed":{"uav:id":"nsu=https://models.example.com/pump;i=10","uav:mapToType":"i=5"}}}
                """;
            using JsonDocument assigned = Apply(new ExportModelMapping([source], metadata, false), json);
            Assert.That(assigned.RootElement.TryGetProperty("uav:eventContractType", out _), Is.False);
            Assert.That(assigned.RootElement.GetProperty("properties").GetProperty("Speed")
                .GetProperty("uav:eventContractType").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(["plant.Speed.v2", "plant.Speed.v3"]));
            using JsonDocument unassigned = Apply(new ExportModelMapping([source], new ExportMetadata(), false), json);
            Assert.That(unassigned.RootElement.GetProperty("properties").GetProperty("Speed")
                .TryGetProperty("uav:eventContractType", out _), Is.False);
        }

        /// <summary>
        /// Uses exact storage limits at integer leaves, not on Method or Structure fields.
        /// </summary>
        [TestCase("i=2", "-128", "127")]
        [TestCase("i=3", "0", "255")]
        [TestCase("i=4", "-32768", "32767")]
        [TestCase("i=5", "0", "65535")]
        [TestCase("i=6", "-2147483648", "2147483647")]
        [TestCase("i=7", "0", "4294967295")]
        [TestCase("i=8", "-9223372036854775808", "9223372036854775807")]
        [TestCase("i=9", "0", "18446744073709551615")]
        public void IntegerBoundsAreExactAtArrayItemSchemas(string dataType, string minimum, string maximum)
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, $$$$"""
                {
                  "properties": {"array":{"uav:mapToType":"{{{{dataType}}}}","uav:valueRank":2}},
                  "actions": {"call":{"input":{"uav:mapToType":"{{{{dataType}}}}","uav:valueRank":-1}}}
                }
                """);
            JsonElement array = document.RootElement.GetProperty("properties").GetProperty("array");
            Assert.That(array.TryGetProperty("minimum", out _), Is.False);
            JsonElement leaf = array.GetProperty("items").GetProperty("items");
            Assert.That(leaf.GetProperty("minimum").GetDecimal(),
                Is.EqualTo(decimal.Parse(minimum, CultureInfo.InvariantCulture)));
            Assert.That(leaf.GetProperty("maximum").GetDecimal(),
                Is.EqualTo(decimal.Parse(maximum, CultureInfo.InvariantCulture)));
            Assert.That(document.RootElement.GetProperty("actions").GetProperty("call").GetProperty("input")
                .TryGetProperty("minimum", out _), Is.False);
        }

        /// <summary>
        /// Resolves simple aliases without deriving limits from current values.
        /// </summary>
        [Test]
        public void CustomSimpleTypesRetainAuthoredConstraints()
        {
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Counter">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=5</Reference></References>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{"Counter":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001",
                  "minimum":3,"exclusiveMaximum":42,"default":4}}}
                """);
            JsonElement counter = document.RootElement.GetProperty("properties").GetProperty("Counter");
            Assert.That(counter.GetProperty("minimum").GetInt32(), Is.EqualTo(3));
            Assert.That(counter.GetProperty("exclusiveMaximum").GetInt32(), Is.EqualTo(42));
            Assert.That(counter.GetProperty("maximum").GetInt32(), Is.EqualTo(65535));
            Assert.That(() => mapping.Apply(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {"properties":{"Counter":{"uav:mapToType":"i=5","exclusiveMaximum":0}}}
                """), "model", []), Throws.TypeOf<FormatException>().With.Message.Contains("Empty integer"));
        }

        /// <summary>
        /// Expands the shared converter's inherited Structure and enum definitions recursively.
        /// </summary>
        [Test]
        public async Task OrdinaryPayloadSchemasContainFieldsRequirednessAndAlternativesAsync()
        {
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3000" BrowseName="1:BaseSettings" IsAbstract="true">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
                  <Definition Name="1:BaseSettings"><Field Name="Enabled" DataType="i=1" /></Definition>
                </UADataType>
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Settings" IsAbstract="true">
                  <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=3000</Reference></References>
                  <Definition Name="1:Settings">
                    <Field Name="Count" DataType="i=5" ValueRank="2" ArrayDimensions="2,3" />
                    <Field Name="Mode" DataType="ns=1;i=3002" IsOptional="true" />
                  </Definition>
                </UADataType>
                <UADataType NodeId="ns=1;i=3002" BrowseName="1:Mode">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=29</Reference></References>
                  <Definition Name="1:Mode"><Field Name="Off" Value="0" /><Field Name="On" Value="7" /></Definition>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            using WotDocumentSet documents = result.Value!;
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            mapping.AddDefinitions(documents);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"actions":{"Scan":{"input":{"type":"object","properties":{
                  "Settings":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001"}},
                  "required":["Settings"]},"output":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001",
                  "uav:valueRank":1}}}}
                """);
            JsonElement input = document.RootElement.GetProperty("actions").GetProperty("Scan").GetProperty("input")
                .GetProperty("properties").GetProperty("Settings");
            Assert.That(input.GetProperty("properties").EnumerateObject().Select(field => field.Name),
                Is.EqualTo(["Enabled", "Count", "Mode"]));
            Assert.That(input.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(["Enabled", "Count"]));
            JsonElement fields = input.GetProperty("properties");
            Assert.That(fields.GetProperty("Count").TryGetProperty("minimum", out _), Is.False);
            JsonElement counts = fields.GetProperty("Count");
            Assert.That(counts.TryGetProperty("minItems", out _), Is.False);
            Assert.That(counts.GetProperty("maxItems").GetInt32(), Is.EqualTo(2));
            Assert.That(counts.GetProperty("items").GetProperty("maxItems").GetInt32(), Is.EqualTo(3));
            Assert.That(counts.GetProperty("items").GetProperty("items").GetProperty("type").GetString(),
                Is.EqualTo("integer"));
            Assert.That(counts.GetProperty("items").GetProperty("items").TryGetProperty("minimum", out _), Is.False);
            Assert.That(fields.GetProperty("Mode").GetProperty("oneOf")[1].GetProperty("const").GetInt32(),
                Is.EqualTo(7));
            Assert.That(document.RootElement.GetProperty("actions").GetProperty("Scan").GetProperty("output")
                .GetProperty("items").GetProperty("properties").TryGetProperty("Count", out _), Is.True);
        }

        /// <summary>
        /// Identifies the exact payload location where ordinary schema expansion cannot finish.
        /// </summary>
        [Test]
        public void MissingOrdinarySchemaHasLocationSpecificDiagnostic()
        {
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Unavailable" />
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            var diagnostics = new List<string>();
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"events":{"changed":{"data":{"type":"object","properties":{
                  "Results":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001","uav:valueRank":1}}}}}}
                """, diagnostics);
            Assert.That(diagnostics.Single(), Does.Contain("Incomplete generic WoT schema")
                .And.Contain("model#/events/changed/data/properties/Results/items"));
        }

        /// <summary>
        /// Allocates SymbolicName keys in canonical native-identity order and updates complete pointers.
        /// </summary>
        [Test]
        public void IdentifierSafeKeysUpdateOptionalityAndDoNotRewriteLiterals()
        {
            UANodeSet source = Source("""
                <UAVariable NodeId="ns=1;i=2" BrowseName="1:Second" SymbolicName="Link" />
                <UAVariable NodeId="ns=1;i=10" BrowseName="1:First" SymbolicName="Link" />
                """);
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {
                  "nsu=https://models.example.com/pump;i=2":{"name":"explicit.Second"},
                  "nsu=https://models.example.com/pump;i=10":{"name":"explicit.First"}
                }
                """));
            var mapping = new ExportModelMapping([source], metadata, true);
            const string json = /*lang=json,strict*/ """
                {"uav:id":"nsu=https://models.example.com/pump;i=1",
                 "properties":{
                   "second":{"uav:id":"nsu=https://models.example.com/pump;i=2","uav:mapToType":"i=5"},
                   "a/b~":{"uav:id":"nsu=https://models.example.com/pump;i=10","uav:mapToType":"i=5",
                     "tm:ref":"#/properties/second","default":{"tm:ref":"#/properties/second"},
                     "uav:withUnit":"/properties/second","uav:resolvedFrom":"#/properties/second",
                     "uav:instanceOfDeclaration":["#/properties/second"]}},
                 "links":[{"rel":"related","href":"#/properties/a~1b~0"}],
                 "tm:optional":["/properties/a~1b~0","/properties/second"]}
                """;
            using var documents = new WotDocumentSet("model", [
                new WotDocumentSetEntry("model", WotDocument.Parse(Encoding.UTF8.GetBytes(json)))]);
            mapping.AddDefinitions(documents);
            using JsonDocument document = Apply(mapping, json);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("properties").EnumerateObject().Select(value => value.Name),
                Is.EqualTo(["Link_2", "Link"]));
            Assert.That(root.GetProperty("tm:optional").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(["/properties/Link", "/properties/Link_2"]));
            JsonElement link = root.GetProperty("properties").GetProperty("Link");
            Assert.That(link.GetProperty("tm:ref").GetString(), Is.EqualTo("#/properties/Link_2"));
            Assert.That(link.GetProperty("uav:withUnit").GetString(), Is.EqualTo("/properties/Link_2"));
            Assert.That(link.GetProperty("uav:resolvedFrom").GetString(), Is.EqualTo("#/properties/Link_2"));
            Assert.That(link.GetProperty("uav:instanceOfDeclaration")[0].GetString(),
                Is.EqualTo("#/properties/Link_2"));
            Assert.That(root.GetProperty("links")[0].GetProperty("href").GetString(),
                Is.EqualTo("#/properties/Link"));
            Assert.That(link.GetProperty("default").GetProperty("tm:ref").GetString(),
                Is.EqualTo("#/properties/second"));
        }

        /// <summary>
        /// Parses the enriched JSON for assertions on the emitted representation.
        /// </summary>
        private static JsonDocument Apply(
            ExportModelMapping mapping, string json, List<string>? diagnostics = null)
        {
            return JsonDocument.Parse(mapping.Apply(Encoding.UTF8.GetBytes(json), "model", diagnostics ?? []));
        }

        /// <summary>
        /// Creates a native test model with one unambiguous declaring ObjectType.
        /// </summary>
        private static UANodeSet Source(string nodes)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
                $$"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
                  <NamespaceUris><Uri>https://models.example.com/pump</Uri></NamespaceUris>
                  <Models><Model ModelUri="https://models.example.com/pump" Version="1.0" /></Models>
                  <UAObjectType NodeId="ns=1;i=1" BrowseName="1:PumpType" />
                  {{nodes}}
                </UANodeSet>
                """));
            return UANodeSet.Read(stream)!;
        }

        /// <summary>Identifies the native type represented by the test document.</summary>
        private const string k_root = /*lang=json,strict*/ """{"uav:id":"nsu=https://models.example.com/pump;i=1"}""";
    }
}
