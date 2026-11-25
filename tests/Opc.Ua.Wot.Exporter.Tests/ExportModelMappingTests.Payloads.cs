// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Wot.Exporter.Tests
{
    public sealed partial class ExportModelMappingTests
    {
        /// <summary>
        /// Leaves foreign or incomplete Range encodings in the native projection without inventing limits.
        /// </summary>
        [TestCase("i=99999", "<Range><Low>0</Low><High>3600</High></Range>")]
        [TestCase("i=885", "<Other><Range><Low>0</Low><High>3600</High></Range></Other>")]
        [TestCase("i=885", "<Range><Low>0</Low></Range>")]
        [TestCase("i=885", "<Range><Low>invalid</Low><High>3600</High></Range>")]
        public void UndecodableEngineeringRangesRetainTheirNativePayload(string encodingId, string body)
        {
            UANodeSet source = Source(k_analogNodes.Replace("i=885", encodingId, StringComparison.Ordinal)
                .Replace("<Range><Low>LOW</Low><High>HIGH</High></Range>", body, StringComparison.Ordinal));
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            using WotDocument original = WotNodeSetConverter.FromNodeSet(source);
            using var enriched = WotDocument.Parse(mapping.Apply(original.Utf8Json, "model", []));
            Assert.That(enriched.Properties["Speed"].TryGetProperty("uav:euRange", out _), Is.False);
            UANodeSet restored = WotNodeSetConverter.ToNodeSet(enriched);
            Assert.That(restored.Items!.OfType<UAVariable>().Single(node => node.BrowseName == "EURange")
                .Value!.OuterXml, Is.EqualTo(source.Items!.OfType<UAVariable>()
                    .Single(node => node.BrowseName == "EURange").Value!.OuterXml));
        }

        /// <summary>
        /// Preserves authored payload bounds when the readable schema already carries enrichment markers.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void EnrichedAnalogSchemasRetainAuthoredPayloadBounds(bool hasEngineeringRange)
        {
            UANodeSet source = Source(k_analogNodes.Replace("LOW", "0", StringComparison.Ordinal)
                .Replace("HIGH", "3600", StringComparison.Ordinal));
            JsonObject json = JsonNode.Parse(k_analogJson)!.AsObject();
            JsonNode speed = json["properties"]!["Speed"]!;
            if (hasEngineeringRange)
            {
                speed["uav:euRange"] = new JsonObject { ["minimum"] = 0, ["maximum"] = 3600 };
            }
            else
            {
                speed["uav:typeName"] = "org.opcfoundation.UA.UInt16";
            }
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, json.ToJsonString());
            JsonElement property = document.RootElement.GetProperty("properties").GetProperty("Speed");
            Assert.That(property.GetProperty("maximum").GetInt32(), Is.EqualTo(3600));
            Assert.That(property.GetProperty("uav:euRange").GetProperty("maximum").GetInt32(), Is.EqualTo(3600));
            using JsonDocument reapplied = Apply(mapping, document.RootElement.GetRawText());
            Assert.That(reapplied.RootElement.GetProperty("properties").GetProperty("Speed")
                .GetProperty("maximum").GetInt32(), Is.EqualTo(3600));
        }

        /// <summary>
        /// Keeps engineering bounds separate from integer validation and identifies the run-time unit vocabulary.
        /// </summary>
        [TestCase("0", "3600", 1)]
        [TestCase("NaN", "NaN", 0)]
        public void EngineeringRangesAndUnitCodesAreNotPayloadDisplayConstraints(
            string low, string high, int endpointPairs)
        {
            UANodeSet source = Source(
                k_analogNodes.Replace("LOW", low, StringComparison.Ordinal)
                    .Replace("HIGH", high, StringComparison.Ordinal));
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            string json = endpointPairs == 0
                ? k_analogJson.Replace("\"minimum\":0,\"maximum\":3600,", string.Empty, StringComparison.Ordinal)
                : k_analogJson;
            using JsonDocument document = Apply(mapping, json);
            JsonElement speed = document.RootElement.GetProperty("properties").GetProperty("Speed");
            Assert.That(speed.GetProperty("minimum").GetInt32(), Is.Zero);
            Assert.That(speed.GetProperty("maximum").GetInt32(), Is.EqualTo(65535));
            Assert.That(speed.GetProperty("uav:euRange").EnumerateObject().Count(), Is.EqualTo(endpointPairs * 2));
            Assert.That(speed.TryGetProperty("uav:unitProperty", out _), Is.False);
            Assert.That(speed.GetProperty("uav:withUnit").GetString(), Is.EqualTo("/properties/SpeedUnit"));
            JsonElement unit = document.RootElement.GetProperty("properties").GetProperty("SpeedUnit");
            Assert.That(unit.GetProperty("default").GetString(), Is.EqualTo("RPM"));
            Assert.That(unit.GetProperty("uav:unitCodeSystem").GetString(),
                Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"));
        }

        /// <summary>
        /// Does not label a sample with localized text when no run-time unit vocabulary is known.
        /// </summary>
        [Test]
        public void UnknownUnitVocabularyRequiresExplicitCodeMetadata()
        {
            UANodeSet source = Source(k_analogNodes.Replace("LOW", "0", StringComparison.Ordinal)
                .Replace("HIGH", "3600", StringComparison.Ordinal));
            string json = k_analogJson.Replace("http://www.opcfoundation.org/UA/units/un/cefact",
                "urn:unknown:units", StringComparison.Ordinal);
            var diagnostics = new List<string>();
            using JsonDocument unknown = Apply(
                new ExportModelMapping([source], new ExportMetadata(), false), json, diagnostics);
            JsonElement properties = unknown.RootElement.GetProperty("properties");
            Assert.That(properties.GetProperty("Speed").TryGetProperty("uav:withUnit", out _), Is.False);
            Assert.That(properties.GetProperty("SpeedUnit").TryGetProperty("default", out _), Is.False);
            Assert.That(diagnostics.Single(), Does.Contain("code vocabulary"));
            var metadata = ExportMetadata.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                {"nsu=https://models.example.com/pump;i=11":{
                  "unitCodeSystem":"http://unitsofmeasure.org","unitCode":"m/s"}}
                """));
            using JsonDocument configured = Apply(new ExportModelMapping([source], metadata, false), json);
            Assert.That(configured.RootElement.GetProperty("properties").GetProperty("SpeedUnit")
                .GetProperty("default").GetString(), Is.EqualTo("m/s"));
        }

        /// <summary>
        /// Represents the null Union and every named selection in ordinary WoT schema terms.
        /// </summary>
        [Test]
        public async Task UnionAndRecursiveSchemasExposeAlternativesAndReportRecursionAsync()
        {
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Choice">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=12756</Reference></References>
                  <Definition Name="1:Choice" IsUnion="true">
                    <Field Name="Count" DataType="i=5" />
                    <Field Name="Text" DataType="i=12" />
                  </Definition>
                </UADataType>
                <UADataType NodeId="ns=1;i=3002" BrowseName="1:Tree" IsAbstract="true">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
                  <Definition Name="1:Tree">
                    <Field Name="Children" DataType="ns=1;i=3002" ValueRank="1" IsOptional="true" />
                  </Definition>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            using WotDocumentSet documents = result.Value!;
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            mapping.AddDefinitions(documents);
            var diagnostics = new List<string>();
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{
                  "choice":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001"},
                  "tree":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3002"}}}
                """, diagnostics);
            JsonElement choice = document.RootElement.GetProperty("properties").GetProperty("choice");
            Assert.That(choice.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(choice.GetProperty("maxProperties").GetInt32(), Is.EqualTo(1));
            Assert.That(choice.TryGetProperty("required", out _), Is.False);
            Assert.That(choice.GetProperty("oneOf").GetArrayLength(), Is.EqualTo(3));
            Assert.That(choice.GetProperty("oneOf")[0].GetProperty("required")[0].GetString(), Is.EqualTo("Count"));
            Assert.That(choice.GetProperty("oneOf")[2].GetProperty("const").EnumerateObject(), Is.Empty);
            Assert.That(diagnostics.Single(), Does.Contain("Recursive DataType")
                .And.Contain("#/properties/tree/properties/Children/items"));
        }

        /// <summary>
        /// Keeps translated singular engineering-unit text tagged with the locale selected from its source.
        /// </summary>
        [Test]
        public void EngineeringUnitFallbackTextRetainsItsLocale()
        {
            UANodeSet source = Source(k_analogNodes.Replace("LOW", "0", StringComparison.Ordinal)
                .Replace("HIGH", "3600", StringComparison.Ordinal));
            source.Items![0].DisplayName = [new Export.LocalizedText { Locale = "en", Value = "Pump" }];
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            JsonElement units = document.Properties.Values.Single(property =>
                property.TryGetProperty("uav:engineeringUnits", out _)).GetProperty("uav:engineeringUnits");
            Assert.That(units.GetProperty("@context").GetProperty("displayName")
                .GetProperty("@language").GetString(), Is.EqualTo("de"));
        }

        /// <summary>
        /// Verifies preservation after the final range and unit annotations, not only before enrichment.
        /// </summary>
        [Test]
        public void EnrichedAnalogOutputPreservesNativeSource()
        {
            UANodeSet source = Source(k_analogNodes.Replace("LOW", "0", StringComparison.Ordinal)
                .Replace("HIGH", "3600", StringComparison.Ordinal));
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            using WotDocument original = WotNodeSetConverter.FromNodeSet(source);
            using var enriched = WotDocument.Parse(mapping.Apply(original.Utf8Json, "model", []));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(enriched);
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            using WotDocument regenerated = WotNodeSetConverter.FromNodeSet(result.Value!);
            JsonElement speed = regenerated.Properties["Speed"];
            Assert.That(speed.GetProperty("maximum").GetInt32(), Is.EqualTo(65535));
            Assert.That(speed.GetProperty("uav:euRange").GetProperty("maximum").GetInt32(), Is.EqualTo(3600));
            Assert.That(speed.GetProperty("uav:withUnit").GetString(), Is.EqualTo("/properties/EngineeringUnits"));
            Assert.That(speed.TryGetProperty("uav:unitProperty", out _), Is.False);
            Assert.That(regenerated.Properties["EngineeringUnits"].GetProperty("default").GetString(),
                Is.EqualTo("RPM"));
            result.Value!.Extensions = result.Value.Extensions?.Where(extension =>
                extension.LocalName != "WoTJsonResidue" ||
                extension.NamespaceURI != "http://opcfoundation.org/UA/WoT-Binding/").ToArray();
            if (result.Value.Extensions is { Length: 0 })
            {
                result.Value.Extensions = null;
            }
            NodeSetComparisonResult comparison = NodeSetComparer.CompareEquivalent(source, result.Value!);
            Assert.That(comparison.AreEquivalent, Is.True, string.Join(Environment.NewLine, comparison.Differences));
        }

        /// <summary>
        /// Rejects engineering metadata that contradicts the authoritative native range.
        /// </summary>
        [Test]
        public void EngineeringRangeCannotOverridePreservedNativeRange()
        {
            UANodeSet source = Source(k_analogNodes.Replace("LOW", "0", StringComparison.Ordinal)
                .Replace("HIGH", "3600", StringComparison.Ordinal));
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            using WotDocument original = WotNodeSetConverter.FromNodeSet(source);
            JsonObject tampered = JsonNode.Parse(mapping.Apply(original.Utf8Json, "model", []).Span)!.AsObject();
            tampered["properties"]!["Speed"]!["uav:euRange"]!["maximum"] = 3601;
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(tampered.ToJsonString()));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.JsonPointer?.Contains("uav:euRange", StringComparison.Ordinal) == true), Is.True);
        }

        /// <summary>
        /// Treats native dimensions as upper bounds and retains independently authored minimum lengths.
        /// </summary>
        [Test]
        public void NativeArrayDimensionsPreserveAuthoredMinimumLengths()
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{"samples":{"uav:mapToType":"i=5","uav:valueRank":2,
                  "uav:arrayDimensions":[2,3],"minItems":1,"items":{"minItems":0}}}}
                """);
            JsonElement samples = document.RootElement.GetProperty("properties").GetProperty("samples");
            Assert.That(samples.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(samples.GetProperty("maxItems").GetInt32(), Is.EqualTo(2));
            Assert.That(samples.GetProperty("items").GetProperty("minItems").GetInt32(), Is.Zero);
            Assert.That(samples.GetProperty("items").GetProperty("maxItems").GetInt32(), Is.EqualTo(3));
        }

        /// <summary>
        /// Combines mandatory native fields with authored requiredness and intersects nested array upper bounds.
        /// </summary>
        [TestCase(2, 2)]
        [TestCase(6, 4)]
        public async Task NativeStructureFallbackDoesNotWeakenAuthoredConstraintsAsync(int authoredMax, int expectedMax)
        {
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Settings" IsAbstract="true">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
                  <Definition Name="1:Settings">
                    <Field Name="Enabled" DataType="i=1" />
                    <Field Name="Samples" DataType="i=5" ValueRank="2" ArrayDimensions="4,5" IsOptional="true" />
                  </Definition>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            using WotDocumentSet documents = result.Value!;
            mapping.AddDefinitions(documents);
            using JsonDocument document = Apply(mapping, """
                {"actions":{"Scan":{"input":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001",
                  "required":["Samples"],"properties":{"Samples":{"minItems":1,"maxItems":MAX,
                    "items":{"minItems":2,"maxItems":3}}}}}}}
                """.Replace("MAX", authoredMax.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            JsonElement input = document.RootElement.GetProperty("actions").GetProperty("Scan").GetProperty("input");
            Assert.That(input.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EquivalentTo(["Enabled", "Samples"]));
            JsonElement samples = input.GetProperty("properties").GetProperty("Samples");
            Assert.That(samples.GetProperty("minItems").GetInt32(), Is.EqualTo(1));
            Assert.That(samples.GetProperty("maxItems").GetInt32(), Is.EqualTo(expectedMax));
            Assert.That(samples.GetProperty("items").GetProperty("minItems").GetInt32(), Is.EqualTo(2));
            Assert.That(samples.GetProperty("items").GetProperty("maxItems").GetInt32(), Is.EqualTo(3));
        }

        /// <summary>
        /// Preserves literal JSON deeper than the platform default without exceeding the converter's budget.
        /// </summary>
        [TestCase(70, true)]
        [TestCase(130, false)]
        public void EnrichmentUsesTheConverterJsonDepthBudget(int depth, bool withinBudget)
        {
            string literal = string.Concat(Enumerable.Repeat("{\"child\":", depth)) + "42" + new string('}', depth);
            string json = "{\"default\":" + literal + "}";
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            if (!withinBudget)
            {
                Assert.That(() => mapping.Apply(Encoding.UTF8.GetBytes(json), "model", []),
                    Throws.InstanceOf<JsonException>());
                return;
            }
            using var document = WotDocument.Parse(mapping.Apply(Encoding.UTF8.GetBytes(json), "model", []));
            JsonElement value = document.RootElement.GetProperty("default");
            for (int i = 0; i < depth; i++)
            {
                value = value.GetProperty("child");
            }
            Assert.That(value.GetInt32(), Is.EqualTo(42));
        }

        /// <summary>
        /// Indexes DataType definitions containing deeply nested publisher annotations accepted by the converter.
        /// </summary>
        [Test]
        public void DeepDataTypeDefinitionsCanBeIndexedAndEnriched()
        {
            string literal = string.Concat(Enumerable.Repeat("{\"child\":", 70)) + "42" + new string('}', 70);
            string json = """
                {"uav:dataTypeDefinitions":[{"uav:dataTypeId":"nsu=https://models.example.com/pump;i=3001",
                  "uav:fields":[],"example:metadata":
                """ + literal + """
                }],"properties":{"value":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3001"}}}
                """;
            using var documents = new WotDocumentSet("model", [
                new WotDocumentSetEntry("model", WotDocument.Parse(Encoding.UTF8.GetBytes(json)))]);
            UANodeSet source = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:Settings">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), false);
            mapping.AddDefinitions(documents);
            using var document = WotDocument.Parse(mapping.Apply(documents.Entries[0].Document.Utf8Json, "model", []));
            Assert.That(document.Properties["value"].GetProperty("type").GetString(), Is.EqualTo("object"));
            JsonElement value = document.RootElement.GetProperty("uav:dataTypeDefinitions")[0]
                .GetProperty("example:metadata");
            for (int i = 0; i < 70; i++)
            {
                value = value.GetProperty("child");
            }
            Assert.That(value.GetInt32(), Is.EqualTo(42));
        }

        /// <summary>
        /// Intersects storage bounds with authored array item constraints without discarding nested limits.
        /// </summary>
        [Test]
        public void IntegerArrayItemsRetainAuthoredConstraints()
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"properties":{"samples":{"uav:mapToType":"i=5","uav:valueRank":2,
                  "items":{"items":{"minimum":100,"maximum":200,"exclusiveMinimum":100}}}}}
                """);
            JsonElement leaf = document.RootElement.GetProperty("properties").GetProperty("samples")
                .GetProperty("items").GetProperty("items");
            Assert.That(leaf.GetProperty("minimum").GetInt32(), Is.EqualTo(100));
            Assert.That(leaf.GetProperty("maximum").GetInt32(), Is.EqualTo(200));
            Assert.That(leaf.GetProperty("exclusiveMinimum").GetInt32(), Is.EqualTo(100));
        }

        /// <summary>
        /// Evaluates finite decimal constraints without rounding values onto an integer boundary.
        /// </summary>
        [TestCase("1e-100", "0", false)]
        [TestCase("1e100", "1e101", false)]
        [TestCase("-1e100", "1e100", true)]
        [TestCase("65534.000000000000000000000000000001", "65534", false)]
        [TestCase("1e-100", "1", true)]
        public void IntegerIntersectionsUseExactDecimalConstraints(string minimum, string maximum, bool nonempty)
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            string json = $$$$"""
                {"properties":{"value":{"uav:mapToType":"i=5","minimum":{{{{minimum}}}},"maximum":{{{{maximum}}}}}}}
                """;
            if (!nonempty)
            {
                Assert.That(() => mapping.Apply(Encoding.UTF8.GetBytes(json), "model", []),
                    Throws.TypeOf<FormatException>().With.Message.Contains("Empty integer"));
                return;
            }
            using JsonDocument document = Apply(mapping, json);
            JsonElement value = document.RootElement.GetProperty("properties").GetProperty("value");
            Assert.That(value.GetProperty("minimum").GetRawText(),
                Is.EqualTo(minimum == "-1e100" ? "0" : minimum));
        }

        /// <summary>
        /// Completes Structure fields across files, including a subtype with no additional definition.
        /// </summary>
        [Test]
        public async Task InheritedStructureFieldsResolveAcrossSourceFilesAsync()
        {
            UANodeSet parent = Source("""
                <UADataType NodeId="ns=1;i=3001" BrowseName="1:BaseSettings" IsAbstract="true">
                  <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
                  <Definition Name="1:BaseSettings"><Field Name="Enabled" DataType="i=1" /></Definition>
                </UADataType>
                """);
            UANodeSet child = Source("""
                <UADataType NodeId="ns=1;i=3002" BrowseName="1:Settings" IsAbstract="true">
                  <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=3001</Reference></References>
                  <Definition Name="1:Settings"><Field Name="Count" DataType="i=5" /></Definition>
                </UADataType>
                <UADataType NodeId="ns=1;i=3003" BrowseName="1:ConcreteSettings">
                  <References><Reference ReferenceType="i=45" IsForward="false">ns=1;i=3002</Reference></References>
                </UADataType>
                """);
            var mapping = new ExportModelMapping([parent, child], new ExportMetadata(), false);
            foreach (UANodeSet source in new[] { parent, child })
            {
                WotConversionResult<WotDocumentSet> result = await WotNodeSetConverter
                    .FromNodeSetDocumentsAsync(source, "model").ConfigureAwait(false);
                Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
                using WotDocumentSet documents = result.Value!;
                mapping.AddDefinitions(documents);
            }
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"actions":{"Scan":{"input":{"uav:mapToType":"nsu=https://models.example.com/pump;i=3003"}}}}
                """);
            JsonElement input = document.RootElement.GetProperty("actions").GetProperty("Scan").GetProperty("input");
            Assert.That(input.GetProperty("properties").EnumerateObject().Select(field => field.Name),
                Is.EqualTo(["Enabled", "Count"]));
            Assert.That(input.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(["Enabled", "Count"]));
        }

        /// <summary>
        /// Rewrites support-document fragments without changing their representation type or location.
        /// </summary>
        [Test]
        public void LocalSupportReferencesUseEmittedIdentifierKeys()
        {
            UANodeSet source = Source("""
                <UAObjectType NodeId="ns=1;i=2" BrowseName="1:OtherType" />
                <UAVariable NodeId="ns=1;i=10" BrowseName="1:Original" SymbolicName="Link">
                  <References><Reference ReferenceType="i=47" IsForward="false">ns=1;i=2</Reference></References>
                </UAVariable>
                """);
            using var documents = new WotDocumentSet("model", [
                new WotDocumentSetEntry("model", WotDocument.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                    {"uav:id":"nsu=https://models.example.com/pump;i=1",
                      "links":[{"rel":"related","href":"support#/properties/Original","type":"application/td+json"}],
                      "schemaDefinitions":{"Value":{"$ref":"support#/properties/Original"}}}
                    """))),
                new WotDocumentSetEntry("support", WotDocument.Parse(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """
                    {"uav:id":"nsu=https://models.example.com/pump;i=2","properties":{
                      "Original":{"uav:id":"nsu=https://models.example.com/pump;i=10","uav:mapToType":"i=5"}}}
                    """)))]);
            var mapping = new ExportModelMapping([source], new ExportMetadata(), true);
            mapping.AddDefinitions(documents);
            var registry = new RegistryModelReferences("https://registry.example.com", "opcua", mapping);
            ArrayOf<ReadOnlyMemory<byte>> output = registry.Write(documents, CancellationToken.None);
            using var document = JsonDocument.Parse(output[0]);
            JsonElement link = document.RootElement.GetProperty("links")[0];
            Assert.That(link.GetProperty("href").GetString(), Is.EqualTo("support#/properties/Link"));
            Assert.That(link.GetProperty("type").GetString(), Is.EqualTo("application/td+json"));
            Assert.That(document.RootElement.GetProperty("schemaDefinitions").GetProperty("Value")
                .GetProperty("$ref").GetString(), Is.EqualTo("support#/properties/Link"));
        }

        /// <summary>
        /// Reaches schemas carried by forms without changing endpoint URLs or adding property storage bounds.
        /// </summary>
        [Test]
        public void FormResponseSchemasAreOrdinaryValueSchemas()
        {
            var mapping = new ExportModelMapping([Source(string.Empty)], new ExportMetadata(), false);
            using JsonDocument document = Apply(mapping, /*lang=json,strict*/ """
                {"forms":[{"href":"https://endpoint.example.com/value",
                  "response":{"schema":{"uav:mapToType":"i=5","uav:valueRank":1}}}]}
                """);
            JsonElement form = document.RootElement.GetProperty("forms")[0];
            Assert.That(form.GetProperty("href").GetString(), Is.EqualTo("https://endpoint.example.com/value"));
            JsonElement item = form.GetProperty("response").GetProperty("schema").GetProperty("items");
            Assert.That(item.GetProperty("type").GetString(), Is.EqualTo("integer"));
            Assert.That(item.GetProperty("uav:typeName").GetString(), Is.EqualTo("org.opcfoundation.UA.UInt16"));
            Assert.That(item.TryGetProperty("minimum", out _), Is.False);
        }

        /// <summary>Contains native ownership, EUInformation and EURange, with replaceable range endpoints.</summary>
        private const string k_analogNodes = """
            <UAVariable NodeId="ns=1;i=10" BrowseName="1:Speed" DataType="i=5">
              <References>
                <Reference ReferenceType="i=47" IsForward="false">ns=1;i=1</Reference>
                <Reference ReferenceType="i=46">ns=1;i=11</Reference>
                <Reference ReferenceType="i=46">ns=1;i=12</Reference>
              </References>
            </UAVariable>
            <UAVariable NodeId="ns=1;i=11" BrowseName="EngineeringUnits" DataType="i=887">
              <Value><ExtensionObject xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TypeId><Identifier>i=888</Identifier></TypeId><Body><EUInformation>
                  <NamespaceUri>http://www.opcfoundation.org/UA/units/un/cefact</NamespaceUri>
                  <UnitId>5394509</UnitId>
                  <DisplayName><Locale>de</Locale><Text>Umdrehungen</Text></DisplayName>
                  <Description><Locale>de</Locale><Text>Drehzahl</Text></Description>
                </EUInformation></Body>
              </ExtensionObject></Value>
            </UAVariable>
            <UAVariable NodeId="ns=1;i=12" BrowseName="EURange" DataType="i=884">
              <Value><ExtensionObject xmlns="http://opcfoundation.org/UA/2008/02/Types.xsd">
                <TypeId><Identifier>i=885</Identifier></TypeId>
                <Body><Range><Low>LOW</Low><High>HIGH</High></Range></Body>
              </ExtensionObject></Value>
            </UAVariable>
            """;

        /// <summary>Models the earlier readable output that the export boundary enriches.</summary>
        private const string k_analogJson = /*lang=json,strict*/ """
            {"uav:id":"nsu=https://models.example.com/pump;i=1","properties":{
              "Speed":{"uav:id":"nsu=https://models.example.com/pump;i=10","uav:mapToType":"i=5",
                "minimum":0,"maximum":3600,"uav:unitProperty":"/properties/SpeedUnit"},
              "SpeedUnit":{"uav:id":"nsu=https://models.example.com/pump;i=11","uav:mapToType":"i=887",
                "type":"string","default":"Umdrehungen","uav:engineeringUnits":{
                  "namespaceUri":"http://www.opcfoundation.org/UA/units/un/cefact","unitId":5394509}}}}
            """;
    }
}
