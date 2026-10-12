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
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using static Opc.Ua.XRegistry.Bridge.Tests.Sync.XRegistrySyncFixture;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    /// <summary>
    /// Exercises typed correspondence boundaries without consulting a transport or mutating input documents.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryVersionCorrespondenceBoundaryTests
    {
        /// <summary>
        /// Maps typed map entries, including nested objects, while preserving null and opaque values.
        /// </summary>
        [TestCase(false, k_httpVersion, VersionPath)]
        [TestCase(true, VersionPath, k_httpVersion)]
        public void TypedMapsPreserveKeysNullsAndOpaqueValues(bool outbound, string input, string expected)
        {
            XRegistrySyncCorrespondence mapper = Mapper();
            JsonElement metadata = Json(new JsonObject
            {
                ["references"] = new JsonObject
                {
                    ["first"] = input,
                    ["missing"] = null,
                    ["unmapped"] = "/schemagroups/g/schemas/r/versions/other"
                },
                ["settings"] = new JsonObject
                {
                    ["first"] = new JsonObject { ["reference"] = input, ["opaque"] = input }
                },
                ["untyped"] = new JsonObject { ["reference"] = input }
            }.ToJsonString());

            JsonElement mapped = mapper.Metadata(Group, metadata, outbound);

            Assert.Multiple(() =>
            {
                Assert.That(mapped.GetProperty("references").GetProperty("first").GetString(), Is.EqualTo(expected));
                Assert.That(
                    mapped.GetProperty("references").GetProperty("missing").ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(mapped.GetProperty("references").GetProperty("unmapped").GetString(),
                    Is.EqualTo("/schemagroups/g/schemas/r/versions/other"));
                Assert.That(mapped.GetProperty("settings").GetProperty("first").GetProperty("reference").GetString(),
                    Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("settings").GetProperty("first").GetProperty("opaque").GetString(),
                    Is.EqualTo(input));
                Assert.That(mapped.GetProperty("untyped").GetProperty("reference").GetString(), Is.EqualTo(input));
                Assert.That(metadata.GetProperty("references").GetProperty("first").GetString(), Is.EqualTo(input));
            });
        }

        /// <summary>
        /// Maps array items by their declared type without changing their order or cardinality.
        /// </summary>
        [TestCase(false, k_httpVersion, VersionPath)]
        [TestCase(true, VersionPath, k_httpVersion)]
        public void TypedArraysPreserveOrderNullsAndOpaqueSiblings(bool outbound, string input, string expected)
        {
            XRegistrySyncCorrespondence mapper = Mapper();
            JsonElement metadata = Json(new JsonObject
            {
                ["references"] = new JsonObject(),
                ["links"] = new JsonArray(JsonValue.Create(input), null, JsonValue.Create("/unmapped")),
                ["objects"] = new JsonArray(new JsonObject { ["reference"] = input, ["opaque"] = input }),
                ["empty"] = new JsonArray()
            }.ToJsonString());

            JsonElement mapped = mapper.Metadata(Group, metadata, outbound);

            Assert.Multiple(() =>
            {
                Assert.That(mapped.GetProperty("links").GetArrayLength(), Is.EqualTo(3));
                Assert.That(mapped.GetProperty("links")[0].GetString(), Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("links")[1].ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(mapped.GetProperty("links")[2].GetString(), Is.EqualTo("/unmapped"));
                Assert.That(mapped.GetProperty("objects")[0].GetProperty("reference").GetString(),
                    Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("objects")[0].GetProperty("opaque").GetString(), Is.EqualTo(input));
                Assert.That(mapped.GetProperty("empty").GetArrayLength(), Is.Zero);
                Assert.That(metadata.GetProperty("links")[0].GetString(), Is.EqualTo(input));
            });
        }

        /// <summary>
        /// Uses both string and Boolean conditional definitions without treating an inactive sibling as a reference.
        /// </summary>
        [TestCase(false, k_httpVersion, VersionPath)]
        [TestCase(true, VersionPath, k_httpVersion)]
        public void ConditionalSiblingDefinitionsUseCaseInsensitiveScalarMatches(
            bool outbound, string input, string expected)
        {
            XRegistrySyncCorrespondence mapper = Mapper();
            JsonElement metadata = Json(new JsonObject
            {
                ["mode"] = "AcTiVe",
                ["selected"] = input,
                ["enabled"] = true,
                ["conditional"] = input,
                ["opaque"] = input
            }.ToJsonString());

            JsonElement mapped = mapper.Metadata(Group, metadata, outbound);
            JsonElement inactive = mapper.Metadata(Group, Json(new JsonObject
            {
                ["mode"] = "inactive",
                ["selected"] = input,
                ["enabled"] = false,
                ["conditional"] = input
            }.ToJsonString()), outbound);

            Assert.Multiple(() =>
            {
                Assert.That(mapped.GetProperty("selected").GetString(), Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("conditional").GetString(), Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("opaque").GetString(), Is.EqualTo(input));
                Assert.That(inactive.GetProperty("selected").GetString(), Is.EqualTo(input));
                Assert.That(inactive.GetProperty("conditional").GetString(), Is.EqualTo(input));
                Assert.That(metadata.GetProperty("selected").GetString(), Is.EqualTo(input));
            });
        }

        /// <summary>
        /// Rebases only clean absolute references within this caller's exact public registry root.
        /// </summary>
        [TestCase("https://http.example/registry" + k_httpVersion, VersionPath)]
        [TestCase("https://http.example/registry/unmapped", "/unmapped")]
        [TestCase("https://http.example/registry-lookalike" + k_httpVersion,
            "https://http.example/registry-lookalike" + k_httpVersion)]
        [TestCase("https://other.example/registry" + k_httpVersion,
            "https://other.example/registry" + k_httpVersion)]
        [TestCase("http://http.example/registry" + k_httpVersion,
            "http://http.example/registry" + k_httpVersion)]
        [TestCase("https://http.example:444/registry" + k_httpVersion,
            "https://http.example:444/registry" + k_httpVersion)]
        [TestCase("https://http.example/registry" + k_httpVersion + "?epoch=1",
            "https://http.example/registry" + k_httpVersion + "?epoch=1")]
        [TestCase("https://http.example/registry" + k_httpVersion + "#fragment",
            "https://http.example/registry" + k_httpVersion + "#fragment")]
        [TestCase("not-an-absolute-reference", "not-an-absolute-reference")]
        public void AbsoluteReferenceBoundariesPreserveForeignOrAmbiguousValues(string input, string expected)
        {
            XRegistrySyncCorrespondence mapper = Mapper();
            JsonElement mapped = mapper.Metadata(Group, Json(new JsonObject
            {
                ["reference"] = input,
                ["url"] = input,
                ["opaque"] = input
            }.ToJsonString()), false);

            Assert.Multiple(() =>
            {
                Assert.That(mapped.GetProperty("reference").GetString(), Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("url").GetString(), Is.EqualTo(expected));
                Assert.That(mapped.GetProperty("opaque").GetString(), Is.EqualTo(input));
            });
        }

        /// <summary>
        /// Changing one caller's public root cannot change another caller's translation rules.
        /// </summary>
        [Test]
        public void PublicRootChangesAreCallerLocalAndOutboundAbsoluteReferencesRemainOpaque()
        {
            XRegistrySyncCorrespondence first = Mapper();
            XRegistrySyncCorrespondence second = Mapper();
            second.SetDescription(new XRegistryEndpointDescription("second")
            {
                Model = Json(k_model),
                PublicRoot = new Uri("https://other.example/registry")
            });
            const string absolute = "https://http.example/registry" + k_httpVersion;
            JsonElement metadata = Json(new JsonObject { ["reference"] = absolute }.ToJsonString());

            Assert.Multiple(() =>
            {
                Assert.That(first.Metadata(Group, metadata, false).GetProperty("reference").GetString(),
                    Is.EqualTo(VersionPath));
                Assert.That(second.Metadata(Group, metadata, false).GetProperty("reference").GetString(),
                    Is.EqualTo(absolute));
                Assert.That(first.Metadata(Group, metadata, true).GetProperty("reference").GetString(),
                    Is.EqualTo(absolute));
                Assert.That(metadata.GetProperty("reference").GetString(), Is.EqualTo(absolute));
            });
        }

        /// <summary>
        /// Rejects malformed collections and mappings that would collapse two independently observed identities.
        /// </summary>
        [TestCase("""{"http-id":7}""")]
        [TestCase("""{"http-id":null}""")]
        [TestCase("""{"http-id":{"versionid":"http-id"},"v1":{"versionid":"v1"}}""")]
        public void InvalidCollectionsNeverCollapseOrRewriteTheInput(string json)
        {
            XRegistrySyncCorrespondence mapper = Mapper();
            JsonElement input = Json(json);

            Assert.Throws<JsonException>(() => mapper.Metadata(Resource + "/versions", input, false));
            Assert.That(input.GetRawText(), Is.EqualTo(json));
        }

        /// <summary>
        /// Traverses nested resources using declared types or retained mapping paths without rewriting model data.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void NestedCollectionsTranslateVersionAndDefaultIdentitiesWithoutAnEffectiveModel(bool includeModel)
        {
            XRegistrySyncCorrespondence mapper = Mapper();
            if (!includeModel)
            {
                mapper.SetDescription(new XRegistryEndpointDescription("legacy"));
            }
            JsonElement input = Json(/*lang=json,strict*/ """
                {
                  "schemagroups":{"g":{"schemas":{"r":{
                    "meta":{"defaultversionid":"http-id"},
                    "versions":{"http-id":{"versionid":"http-id","ancestorid":"http-id"}}
                  }}}},
                  "model":{"reference":"http-id"},
                  "capabilities":{"reference":"http-id"}
                }
                """);

            JsonElement mapped = mapper.Metadata("/", input, false);
            JsonElement resource = mapped.GetProperty("schemagroups").GetProperty("g")
                .GetProperty("schemas").GetProperty("r");

            Assert.Multiple(() =>
            {
                Assert.That(resource.GetProperty("meta").GetProperty("defaultversionid").GetString(), Is.EqualTo("v1"));
                Assert.That(resource.GetProperty("versions").TryGetProperty("http-id", out _), Is.False);
                Assert.That(resource.GetProperty("versions").GetProperty("v1").GetProperty("versionid").GetString(),
                    Is.EqualTo("v1"));
                Assert.That(resource.GetProperty("versions").GetProperty("v1").GetProperty("ancestorid").GetString(),
                    Is.EqualTo("v1"));
                Assert.That(mapped.GetProperty("model").GetProperty("reference").GetString(), Is.EqualTo("http-id"));
                Assert.That(mapped.GetProperty("capabilities").GetProperty("reference").GetString(),
                    Is.EqualTo("http-id"));
                Assert.That(input.GetProperty("schemagroups").GetProperty("g").GetProperty("schemas")
                    .GetProperty("r").GetProperty("versions").TryGetProperty("http-id", out _), Is.True);
            });
        }

        /// <summary>
        /// Only concrete default-version parameters use endpoint-local identities.
        /// </summary>
        [TestCase(XRegistrySyncSide.Http, "http-id")]
        [TestCase(XRegistrySyncSide.OpcUa, "opc-id")]
        public void DefaultVersionParametersPreserveSentinelsUnrelatedValuesAndParentScopes(
            XRegistrySyncSide side, string expected)
        {
            XRegistrySyncCorrespondence mapper = Mapper(side);
            ArrayOf<XRegistryParameter> parameters =
            [
                new("setdefaultversionid", "v1"),
                new("setdefaultversionid", null),
                new("setdefaultversionid", "null"),
                new("setdefaultversionid", "request"),
                new("epoch", "v1")
            ];

            ArrayOf<XRegistryParameter> mapped = mapper.Parameters(Resource, parameters);
            ArrayOf<XRegistryParameter> parent = mapper.Parameters(Group, parameters);
            JsonElement outbound = mapper.Metadata(VersionPath,
                Json(/*lang=json,strict*/ """{"versionid":"v1","ancestorid":"request"}"""), true);

            Assert.Multiple(() =>
            {
                Assert.That(mapped.Count, Is.EqualTo(5));
                Assert.That(mapped[0].Value, Is.EqualTo(expected));
                Assert.That(mapped[1].Value, Is.Null);
                Assert.That(mapped[2].Value, Is.EqualTo("null"));
                Assert.That(mapped[3].Value, Is.EqualTo("request"));
                Assert.That(mapped[4], Is.EqualTo(new XRegistryParameter("epoch", "v1")));
                Assert.That(parent[0].Value, Is.EqualTo("v1"));
                Assert.That(parameters[0].Value, Is.EqualTo("v1"));
                Assert.That(outbound.GetProperty("versionid").GetString(), Is.EqualTo(expected));
                Assert.That(outbound.GetProperty("ancestorid").GetString(), Is.EqualTo("request"));
            });
        }

        private static XRegistrySyncCorrespondence Mapper(XRegistrySyncSide side = XRegistrySyncSide.Http)
        {
            var mappings = new Dictionary<string, XRegistryVersionCorrespondence>(StringComparer.Ordinal)
            {
                [VersionPath] = new(VersionPath, Resource + "/versions/opc-id", k_httpVersion)
            };
            var mapper = new XRegistrySyncCorrespondence(mappings, side);
            mapper.SetDescription(new XRegistryEndpointDescription("http")
            {
                PublicRoot = new Uri("https://http.example/registry/"),
                Model = Json(k_model)
            });
            return mapper;
        }

        private const string k_httpVersion = Resource + "/versions/http-id";

        private const string k_model = /*lang=json,strict*/ """
            {"groups":{"schemagroups":{"singular":"schemagroup","attributes":{
              "reference":{"type":"xid"},
              "references":{"type":"map","item":{"type":"xid"}},
              "settings":{"type":"map","item":{"type":"object","attributes":{
                "reference":{"type":"xid"},"opaque":{"type":"string"}}}},
              "links":{"type":"array","item":{"type":"uri","target":"versions"}},
              "objects":{"type":"array","item":{"type":"object","attributes":{
                "reference":{"type":"xid"},"opaque":{"type":"string"}}}},
              "empty":{"type":"array","item":{"type":"xid"}},
              "untyped":{"type":"object"},
              "mode":{"type":"string","ifvalues":{
                "active":{"siblingattributes":{"selected":{"type":"xid"}}}}},
              "enabled":{"type":"boolean","ifvalues":{
                "true":{"siblingattributes":{"conditional":{"type":"xid"}}}}},
              "url":{"type":"url","target":"versions"},
              "opaque":{"type":"uri"}
            },"resources":{"schemas":{"singular":"schema"}}}}}
            """;
    }
}
