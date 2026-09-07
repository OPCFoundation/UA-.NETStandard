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

using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotArchiveConsistencyTests
    {
        [TestCase("uav:id", "\"nsu=urn:test:model;i=9999\"")]
        [TestCase("uav:browseName", "\"nsu=urn:test:model;OtherType\"")]
        [TestCase("@type", "\"uav:object\"")]
        public void ConflictingRootFactDoesNotReplaceArchive(string member, string json)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root[member] = JsonNode.Parse(json);

            AssertConflictPreservesArchive(source, root, "/" + member);
        }

        [TestCase("uav:id", "\"nsu=urn:test:model;i=9999\"")]
        [TestCase("uav:browseName", "\"nsu=urn:test:model;OtherSpeed\"")]
        [TestCase("uav:dataTypeId", "\"i=12\"")]
        [TestCase("uav:mapToType", "\"ua:String\"")]
        [TestCase("type", "\"boolean\"")]
        [TestCase("uav:valueRank", "1")]
        [TestCase("uav:arrayDimensions", "[3]")]
        [TestCase("const", "99.5")]
        [TestCase("default", "99.5")]
        [TestCase("readOnly", "true")]
        public void ConflictingPropertyFactDoesNotReplaceArchive(string member, string json)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root["properties"]!["Speed"]![member] = JsonNode.Parse(json);

            AssertConflictPreservesArchive(source, root, "/properties/Speed/" + member);
        }

        [Test]
        public void ConflictingTypeDefinitionDoesNotReplaceArchive()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root["properties"]!["Speed"]!["links"]![0]!["href"] = "i=68";

            AssertConflictPreservesArchive(source, root, "/properties/Speed/links/0");
        }

        [Test]
        public void ConflictingDeclaredReferenceDoesNotReplaceArchive()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root["uav:hasComponent"] = new JsonArray("nsu=urn:test:model;i=9999");

            AssertConflictPreservesArchive(source, root, "/uav:hasComponent/0");
        }

        [Test]
        public void AffordanceIdentityCannotChangeItsNodeClass()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root["actions"]!["Reset"]!["uav:id"] = "nsu=urn:test:model;i=6001";

            AssertConflictPreservesArchive(source, root, "/actions/Reset");
        }

        [Test]
        public void ExistingNodeCannotBecomeAnotherThingsImplicitAffordance()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root["uav:id"] = "nsu=urn:test:model;i=5001";
            root["uav:browseName"] = "nsu=urn:test:model;Machine";
            root["@type"] = "uav:object";
            root.Remove("description");
            root.Remove("actions");
            root.Remove("events");

            AssertConflictPreservesArchive(source, root, "/properties/Speed");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameMappedFactsAndExplicitDocumentTitleAreConsistent(bool includeNative)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source, includeNative);
            root["properties"]!["Speed"]!["const"] = JsonNode.Parse("42.50");
            root["properties"]!["Speed"]!["default"] = JsonNode.Parse("42.5");

            using WotDocument document = Parse(root);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(
                result.Value!.Items![0].DisplayName![0].Value,
                Is.EqualTo("MachineType"));
        }

        [Test]
        public void ReadableSubsetDoesNotRequireEveryArchivedFact()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root.Remove("actions");
            root.Remove("events");
            root.Remove("uav:dataTypeDefinitions");
            root.Remove("links");
            root["properties"] = new JsonObject
            {
                ["speedAlias"] = new JsonObject
                {
                    ["uav:id"] = "nsu=urn:test:model;i=6001",
                    ["uav:dataTypeId"] = "i=11"
                }
            };

            using WotDocument document = Parse(root);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(source.Items!.Length));
        }

        [Test]
        public void QualifiedBrowseNamesUseTheDocumentContextAndArchiveAliases()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            JsonObject root = Archive(source);
            root["@context"]![1]!["model"] = "urn:test:model";
            root["uav:browseName"] = "model:MachineType";
            root["properties"]!["Speed"]!["uav:browseName"] = "model:Speed";
            root["properties"]!["Speed"]!["uav:dataTypeId"] = "i=11";
            root["properties"]!["Speed"]!["uav:mapToType"] = "ua:Double";

            using WotDocument document = Parse(root);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
        }

        [Test]
        public void ADeclaredAliasOutranksTheStandardSpellingDuringComparison()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            source.Aliases!.Single(alias => alias.Alias == "Double").Value = "i=12";
            source.Items!.OfType<UAVariable>().Single(variable => variable.NodeId == "ns=1;i=6001").Value =
                WotTestData.ParseValue(
                    "<uax:String xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">value</uax:String>");
            JsonObject root = Archive(source);
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(root["properties"]!["Speed"]!["type"]!.GetValue<string>(), Is.EqualTo("string"));
            Assert.That(result.Success, Is.True, Describe(result));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompanionReferenceDirectionIsComparedUsingArchivedNames(bool conflict)
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            source.Aliases = [.. source.Aliases!, new NodeIdAlias
            {
                Alias = "ControlAlias",
                Value = "ns=1;i=4001"
            }];
            source.Items![0].References =
            [
                .. source.Items[0].References!,
                new Reference
                {
                    ReferenceType = "ControlAlias",
                    IsForward = false,
                    Value = "ns=1;i=5001"
                }
            ];
            JsonObject root = Archive(source);
            JsonNode link = root["links"]!.AsArray().Single(item =>
                item!["uav:refId"]?.GetValue<string>() == "nsu=urn:test:model;i=4001")!;
            if (conflict)
            {
                link["rel"] = "ns1:Controls";
                AssertConflictPreservesArchive(source, root, "/links");
            }
            else
            {
                using WotDocument document = Parse(root);
                WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
                Assert.That(result.Success, Is.True, Describe(result));
            }
        }

        [Test]
        public void DataTypeDefinitionSubsetUsesLogicalIdentity()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            source.Items =
            [
                .. source.Items!,
                new UADataType
                {
                    NodeId = "ns=1;i=9999",
                    BrowseName = "1:AnotherType",
                    References =
                    [
                        new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=12" }
                    ]
                }
            ];
            JsonObject root = Archive(source);
            JsonNode definition = root["uav:dataTypeDefinitions"]!.AsArray().Single(item =>
                item!["uav:dataTypeId"]?.GetValue<string>() == "nsu=urn:test:model;i=9999")!.DeepClone();
            definition["@id"] = "#definition";
            definition["@type"] = new JsonArray("uav:SimpleDataType", "vendor:Annotation");
            definition["uav:dataTypeName"] = "ns1:AnotherType";
            definition["uav:isAbstract"] = false;
            root["uav:dataTypeDefinitions"] = new JsonArray(definition);
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
        }

        [Test]
        public void EqualLocalBrowseNamesInDifferentNamespacesDoNotMatch()
        {
            UANodeSet source = WotTestData.CreateRichNodeSet();
            source.NamespaceUris = ["urn:test:model", "urn:test:other"];
            JsonObject root = Archive(source);
            root["properties"]!["Speed"]!["uav:browseName"] = "nsu=urn:test:other;Speed";

            AssertConflictPreservesArchive(source, root, "/properties/Speed/uav:browseName");
        }

        [Test]
        public void UnknownJsonIsNotAnArchivedModelConflict()
        {
            JsonObject root = Archive(WotTestData.CreateRichNodeSet());
            root["vendor:metadata"] = new JsonObject { ["value"] = 42 };
            root["properties"]!["Speed"]!["vendor:quality"] = "good";

            using WotDocument document = Parse(root);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(
                result.Value!.Extensions!.Any(element => element.LocalName == "WoTJsonResidue"),
                Is.True);
        }

        [Test]
        public async Task AsyncRestoreChecksKnownFactsWithoutResolvingTypeBindingsAsync()
        {
            JsonObject root = Archive(WotTestData.CreateRichNodeSet());
            root["properties"]!["Speed"]!["uav:valueRank"] = 2;
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result =
                await WotNodeSetConverter.ToNodeSetResultAsync(document).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.True);
        }

        [Test]
        public void CorruptDigestIsRejectedBeforeReadableFactsAreCompared()
        {
            JsonObject root = Archive(WotTestData.CreateRichNodeSet());
            root["uav:id"] = "nsu=urn:test:model;i=9999";
            root["uav:nodeSet"]!["sha256"] = new string('0', 64);
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.DigestMismatch),
                Is.True);
            Assert.That(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict),
                Is.False);
        }

        [Test]
        public void InvalidArchiveBytesDoNotTakeTheReadableSynthesisPath()
        {
            JsonObject root = Archive(WotTestData.CreateRichNodeSet());
            root["uav:nodeSet"]!["data"] = "not base64";
            using WotDocument document = Parse(root);

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == WotDiagnosticCode.InvalidBase64),
                Is.True);
        }

        private static JsonObject Archive(UANodeSet source, bool includeNative = false)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                "An explicitly supplied archival document title",
                new WotNodeSetConverterOptions
                {
                    PreservationMode = WotNodeSetPreservationMode.Always
                });
            JsonObject root = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            if (!includeNative)
            {
                root.Remove("uav:nodes");
            }
            return root;
        }

        private static WotDocument Parse(JsonObject root)
        {
            return WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        private static void AssertConflictPreservesArchive(
            UANodeSet source,
            JsonObject root,
            string pointer)
        {
            using WotDocument document = Parse(root);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                    diagnostic.Location?.JsonPointer?.StartsWith(
                        pointer, System.StringComparison.Ordinal) == true),
                Is.True,
                Describe(result));
            Assert.That(result.Value, Is.Not.Null);
            NodeSetComparisonResult comparison = NodeSetComparer.Compare(source, result.Value!);
            Assert.That(comparison.AreEquivalent, Is.True, string.Join("; ", comparison.Differences));
        }

        private static string Describe(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }
    }
}
