// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT opaque semantic boundary.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable(ParallelScope.All)]
    public sealed class WotOpaqueSemanticBoundaryTests
    {
        /// <summary>
        /// Verifies that opaque values do not contribute model identities or definitions.
        /// </summary>
        /// <param name="onProperty">The on-property flag for this test case.</param>
        /// <param name="asynchronous">The asynchronous flag for this test case.</param>
        [Test]
        [Combinatorial]
        public async Task OpaqueValuesDoNotContributeModelIdentitiesOrDefinitions(
            [Values("identity", "definition", "inline-definition", "external-schema", "type-name", "scoped-context")]
            string kind,
            [Values] bool onProperty,
            [Values] bool asynchronous)
        {
            const string model = """
                {
                  "@context": [
                    "https://www.w3.org/2022/wot/td/v1.1",
                    {
                      "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                      "device": "urn:opaque-boundary#"
                    }
                  ],
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Device",
                  "uav:id": "nsu=urn:opaque-boundary#;s=Device",
                  "uav:browseName": "device:Device",
                  "properties": {
                    "Value": {
                      "type": "number",
                      "uav:id": "nsu=urn:opaque-boundary#;s=Value",
                      "uav:mapToType": "i=11",
                      "uav:browseName": "device:Value"
                    }
                  }
                }
                """;
            string payload = kind switch
            {
                "identity" => /*lang=json,strict*/ """{"uav:id":"business-key"}""",
                "definition" => /*lang=json,strict*/ """{"uav:dataTypeDefinition":{"@id":"urn:vendor:unresolved-definition"}}""",
                "inline-definition" => /*lang=json,strict*/ """
                    {
                      "uav:dataTypeDefinition": {
                        "@id": "urn:vendor:definition", "@type": "uav:StructureDefinition", "uav:fields": "business-data"
                      }
                    }
                    """,
                "external-schema" => /*lang=json,strict*/ """{"uav:externalSchema":"https://unfetched.invalid/schema"}""",
                "type-name" => /*lang=json,strict*/ """{"uav:dataTypeName":"device:Imaginary"}""",
                _ => /*lang=json,strict*/ """
                    {
                      "urn:vendor:payload": {
                        "@context": {"uav": "urn:vendor:business#"},
                        "uav:id": "business-key", "uav:dataTypeDefinition": {"@id": "not-an-IRI"}
                      }
                    }
                    """
            };
            JsonObject root = JsonNode.Parse(model)!.AsObject();
            JsonObject owner = onProperty ? root["properties"]!["Value"]!.AsObject() : root;
            string member = onProperty ? "uav:propertyConfiguration" : "uav:metadata";
            owner[member] = JsonNode.Parse(payload);
            string expected = owner[member]!.ToJsonString();
            var options = new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Never };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
            var nodeResolver = new Mock<IWotNodeResolver>();
            Mock<IWotDataTypeDefinitionResolver> definitions = nodeResolver.As<IWotDataTypeDefinitionResolver>();

            WotConversionResult<UANodeSet> converted = asynchronous
                ? await WotNodeSetConverter.ToNodeSetResultAsync(
                    document, options, null, null, nodeResolver.Object).ConfigureAwait(false)
                : WotNodeSetConverter.ToNodeSetResult(document, options);

            Assert.That(converted.Success, Is.True, string.Join("; ", converted.Diagnostics));
            Assert.That(converted.Diagnostics, Is.Empty);
            Assert.That(converted.Value, Is.Not.Null);
            Assert.That(converted.Value!.Items.Required().OfType<UADataType>(), Is.Empty);
            Assert.That(converted.Value.Items.Select(node => node.NodeId), Does.Not.Contain("business-key"));
            for (int iteration = 0; iteration < 2; iteration++)
            {
                using WotDocument restored = WotNodeSetConverter.FromNodeSet(converted.Value, options: options);
                JsonElement restoredOwner = onProperty
                    ? restored.RootElement.GetProperty("properties").GetProperty("Value")
                    : restored.RootElement;
                Assert.That(restoredOwner.GetProperty(member).GetRawText(), Is.EqualTo(expected));
                converted = asynchronous
                    ? await WotNodeSetConverter.ToNodeSetResultAsync(
                        restored, options, null, null, nodeResolver.Object).ConfigureAwait(false)
                    : WotNodeSetConverter.ToNodeSetResult(restored, options);
                Assert.That(converted.Success, Is.True, string.Join("; ", converted.Diagnostics));
                Assert.That(converted.Diagnostics, Is.Empty);
                Assert.That(converted.Value!.Items.Required().OfType<UADataType>(), Is.Empty);
            }
            definitions.Verify(value => value.ResolveDataTypeDefinitionsAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            nodeResolver.Verify(value => value.ResolveByBrowseNameAsync(
                It.IsAny<string>(), "Imaginary", WotExpectedNodeClass.DataType, It.IsAny<CancellationToken>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that nested opaque members remain verbatim when mapped siblings are removed.
        /// </summary>
        /// <param name="mappedSibling">The mapped-sibling flag for this test case.</param>
        [Test]
        [Combinatorial]
        public void NestedOpaqueMembersRemainVerbatimWhenMappedSiblingsAreRemoved(
            [Values("uav:metadata", "uav:propertyConfiguration", "uav:actionConfiguration", "uav:eventConfiguration")]
            string member,
            [Values] bool mappedSibling)
        {
            const string opaque = /*lang=json,strict*/ """{ "uav:dataTypeDefinition": {"@id":"urn:vendor:opaque"}, "text" : "\u0041" }""";
            string mapped = mappedSibling
                ? "\"uav:dataTypeDefinition\":{\"@id\":\"urn:vendor:mapped\"}," : string.Empty;
            string json = $$"""
                {
                  "title": "Device",
                  "urn:vendor:container": {
                    {{mapped}}
                    "{{member}}": {{opaque}}
                  }
                }
                """;
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(json));
            var nodes = new UANodeSet();
            var options = new WotNodeSetConverterOptions();
            var diagnostics = new List<WotDiagnostic>();

            WotJsonResidue.Replace(nodes, document, options, diagnostics);
            byte[] restored = WotJsonResidue.Apply(
                Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"title":"Device"}"""), nodes, options, diagnostics);

            Assert.That(diagnostics, Is.Empty);
            using var result = JsonDocument.Parse(restored);
            JsonElement container = result.RootElement.GetProperty("urn:vendor:container");
            Assert.That(container.TryGetProperty("uav:dataTypeDefinition", out _), Is.False);
            Assert.That(container.GetProperty(member).GetRawText(), Is.EqualTo(opaque));
        }
    }
}
