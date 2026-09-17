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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [Test]
        public async Task DataTypeLiteralKeysKeepTheirOwnerContextThroughNativeRoundTrip(
            [Values] bool contextArray,
            [Values] bool absoluteKey)
        {
            JsonObject source = DataTypeSource();
            JsonObject plan = DataTypePlan();
            var context = new JsonArray(
                "https://www.w3.org/2022/wot/td/v1.1",
                "http://opcfoundation.org/UA/WoT-Binding/v1.1/opc-ua-wot-binding.context.jsonld",
                new JsonObject
                {
                    ["t"] = "urn:test:projection-types",
                    ["v"] = "https://host.test/"
                });
            source["@context"] = context;
            plan["@context"] = context.DeepClone();
            JsonObject definition = ReadingDefinition();
            var localContext = new JsonObject { ["v"] = "https://source.test/" };
            definition["@context"] = contextArray ? new JsonArray(localContext) : localContext;
            definition["uav:metadata"] = "literal-placeholder";
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            string key = absoluteKey ? "https://source.test/bag" : "v:bag";
            string literal = "{ \"" + key + "\": { \"n\":1.00 } }";
            string sourceJson = source.ToJsonString().Replace(
                "\"literal-placeholder\"", literal, StringComparison.Ordinal);

            WotConversionResult<WotDocument> projected = await ResolveAsync(plan.ToJsonString(), sourceJson)
                .ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            JsonElement projectedDefinition = view.RootElement.GetProperty("uav:dataTypeDefinitions")[0];
            Assert.That(projectedDefinition.GetProperty("uav:metadata").GetRawText(), Is.EqualTo(literal));
            if (!absoluteKey)
            {
                Assert.That(view.TryGetContextPrefix("v", out string prefix, projectedDefinition), Is.True);
                Assert.That(prefix, Is.EqualTo("https://source.test/"));
            }
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            AssertNativeReadingIdentity(native.Value);

            WotConversionResult<WotDocument> restored = WotNodeSetConverter.FromNodeSetResult(native.Value);
            using WotDocument document = restored.Value;
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
            JsonElement restoredDefinition = document.RootElement.GetProperty("uav:dataTypeDefinitions")[0];
            Assert.That(restoredDefinition.GetProperty("uav:metadata").GetRawText(), Is.EqualTo(literal));
            Assert.That(document.TryGetContextPrefix("v", out string rootPrefix), Is.True);
            Assert.That(rootPrefix, Is.EqualTo("https://host.test/"));
            if (!absoluteKey)
            {
                Assert.That(document.TryGetContextPrefix("v", out string prefix, restoredDefinition), Is.True);
                Assert.That(prefix, Is.EqualTo("https://source.test/"));
            }
            WotConversionResult<UANodeSet> reconverted = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(reconverted.Success, Is.True, string.Join("; ", reconverted.Diagnostics));
            AssertNativeReadingIdentity(reconverted.Value);
        }

        [TestCase("ns1")]
        [TestCase("ns2")]
        public async Task DataTypeLiteralContextPreservesGeneratedNativeNamesAndLocalizedText(string prefix)
        {
            JsonObject source = DataTypeSource();
            JsonObject definition = ReadingDefinition();
            definition["@context"] = new JsonObject
            {
                [prefix] = "https://source.test/",
                ["@language"] = "de",
                ["title"] = new JsonObject
                {
                    ["@id"] = "https://www.w3.org/2019/wot/td#title",
                    ["@language"] = "fr"
                }
            };
            definition["title"] = "Lecture";
            definition["uav:metadata"] = new JsonObject
            {
                [prefix + ":bag"] = new JsonObject { ["n"] = 1 }
            };
            source["uav:dataTypeDefinitions"] = new JsonArray(definition);
            WotConversionResult<WotDocument> projected = await ResolveAsync(
                DataTypePlan().ToJsonString(), source.ToJsonString()).ConfigureAwait(false);
            using WotDocument view = projected.Value;
            Assert.That(projected.Success, Is.True, string.Join("; ", projected.Diagnostics));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            AssertNativeReadingIdentity(native.Value);
            UADataType original = native.Value.Items.OfType<UADataType>().Single();
            Assert.That(original.DisplayName.Single().Locale, Is.EqualTo("fr"));
            Assert.That(original.DisplayName.Single().Value, Is.EqualTo("Lecture"));

            WotConversionResult<WotDocument> restored = WotNodeSetConverter.FromNodeSetResult(native.Value);
            using WotDocument document = restored.Value;
            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics));
            JsonElement restoredDefinition = document.RootElement.GetProperty("uav:dataTypeDefinitions")[0];
            Assert.That(document.TryGetContextPrefix(prefix, out string namespaceUri, restoredDefinition), Is.True);
            Assert.That(namespaceUri, Is.EqualTo("https://source.test/"));
            Assert.That(restoredDefinition.GetProperty("uav:metadata").GetRawText(),
                Is.EqualTo(view.RootElement.GetProperty("uav:dataTypeDefinitions")[0]
                    .GetProperty("uav:metadata").GetRawText()));
            WotConversionResult<UANodeSet> reconverted = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(reconverted.Success, Is.True, string.Join("; ", reconverted.Diagnostics));
            AssertNativeReadingIdentity(reconverted.Value);
            UADataType actual = reconverted.Value.Items.OfType<UADataType>().Single();
            Assert.That(actual.DisplayName.Single().Locale, Is.EqualTo("fr"));
            Assert.That(actual.DisplayName.Single().Value, Is.EqualTo("Lecture"));
        }

        private static void AssertNativeReadingIdentity(UANodeSet nodeSet)
        {
            UADataType definition = nodeSet.Items.OfType<UADataType>().Single();
            var id = NodeId.Parse(definition.NodeId);
            Assert.That(id.NamespaceIndex, Is.GreaterThan(0));
            Assert.That(id, Is.EqualTo(new NodeId(3000u, id.NamespaceIndex)));
            Assert.That(nodeSet.NamespaceUris[id.NamespaceIndex - 1], Is.EqualTo("urn:test:projection-types"));
            var name = QualifiedName.Parse(definition.BrowseName);
            Assert.That(name.NamespaceIndex, Is.GreaterThan(0));
            Assert.That(name.Name, Is.EqualTo("Reading"));
            Assert.That(nodeSet.NamespaceUris[name.NamespaceIndex - 1], Is.EqualTo("urn:test:projection-types"));
            Assert.That(definition.Definition.Field.Single().DataType, Is.EqualTo("i=11"));
        }
    }
}
