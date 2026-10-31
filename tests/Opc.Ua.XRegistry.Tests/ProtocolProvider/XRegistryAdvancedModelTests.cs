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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;
using static Opc.Ua.XRegistry.Tests.ProtocolProvider.XRegistryProviderCoverage;

namespace Opc.Ua.XRegistry.Tests.ProtocolProvider
{
    [TestFixture]
    public sealed class XRegistryAdvancedModelTests
    {
        [Test]
        public async Task ConditionalAttributesActivateCaseInsensitivelyAndValidateAtomicallyAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","attributes":{
                  "kind":{"type":"string","required":true,"ifvalues":{
                    "sensor":{"siblingattributes":{"unit":{"type":"string","required":true,"default":"C"}}},
                    "actuator":{"siblingattributes":{"position":{"type":"decimal","required":true}}}
                  }}
                },"resources":{}}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g", /*lang=json,strict*/ """{"kind":"SENSOR"}"""))
                .ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/groups/g",
                /*lang=json,strict*/ """{"kind":"actuator","position":2}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
                Assert.That(created.Metadata.GetProperty("unit").GetString(), Is.EqualTo("C"));
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
            });
            XRegistryResponse replaced = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """{"kind":"actuator","position":2}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replaced.StatusCode, Is.EqualTo(200), replaced.Error?.Detail);
                Assert.That(replaced.Metadata.TryGetProperty("unit", out _), Is.False);
                Assert.That(replaced.Metadata.GetProperty("position").GetInt32(), Is.EqualTo(2));
                Assert.That(replaced.Metadata.GetProperty("epoch").GetInt32(), Is.EqualTo(1));
            });
        }

        [TestCase("ifvalues", """{"one":{"siblingattributes":{}},"ONE":{"siblingattributes":{}}}""")]
        [TestCase("ifvalues", """{"^reserved":{"siblingattributes":{}}}""")]
        [TestCase("target", "\"/groups/schemas/invalid\"")]
        public void InvalidConditionalOrTargetModelsAreRejected(string name, string value)
        {
            string model =
                "{\"groups\":{},\"attributes\":{\"ref\":{\"type\":\"string\",\"" + name + "\":" + value + "}}}";
            Assert.Throws<ArgumentException>(() =>
            {
                using XRegistryTransactionalEndpoint endpoint = Create(model);
            });
        }

        [TestCase("/groups/g/schemas/r", 201)]
        [TestCase("/groups/g/schemas/r/versions/v1", 201)]
        [TestCase("/other/g/schemas/r", 400)]
        [TestCase("/groups/g", 400)]
        public async Task TypedReferencesEnforceTheirDeclaredTargetShapeAsync(string reference, int status)
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","attributes":{
                  "ref":{"type":"xid","target":"/groups/schemas[/versions]"}
                },"resources":{"schemas":{"singular":"schema"}}}}}
                """);
            string json = new JsonObject { ["ref"] = reference }.ToJsonString();
            XRegistryResponse response = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g", json)).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(status), response.Error?.Detail);
        }

        [Test]
        public async Task LocalResourceImportsAreExpandedOnlyInTheEffectiveModelAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{
                  "library":{"singular":"library","resources":{"schemas":{"singular":"schema"}}},
                  "groups":{"singular":"group","ximportresources":["/library/schemas"]}
                }}
                """);
            XRegistryResponse model =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model")).ConfigureAwait(false);
            XRegistryResponse source =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/modelsource")).ConfigureAwait(false);
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(
                    model.Metadata.GetProperty("groups").GetProperty("groups").TryGetProperty("ximportresources",
                    out _), Is.False);
                Assert.That(
                    source.Metadata.GetProperty("groups").GetProperty("groups").GetProperty("ximportresources")[0]
                    .GetString(), Is.EqualTo("/library/schemas"));
                Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            });
        }

        [Test]
        public async Task AllowListedIncludesPreserveSourceAndApplyFirstThenLocalPrecedenceAsync()
        {
            var resolver = new XRegistryModelDocumentResolver(
            [
                new XRegistryModelDocument(new Uri("https://models.example/one.json"),
                    Parse(/*lang=json,strict*/
                        """{"attrs":{"site":{"type":"string","default":"one","required":true}}}""")),
                new XRegistryModelDocument(new Uri("https://models.example/two.json"),
                    Parse(/*lang=json,strict*/
                        """{"attrs":{"site":{"type":"string","default":"two","required":true}}}"""))
            ]);
            using var endpoint = new XRegistryTransactionalEndpoint(Options(/*lang=json,strict*/ """
                {"attributes":{"$includes":["one.json#/attrs","two.json#/attrs"]},"groups":{}}
                """) with { ModelResolver = resolver, ModelSourceUri = new Uri("https://models.example/root.json") },
                new InMemoryXRegistryTransactionStore());
            XRegistryResponse root =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false);
            XRegistryResponse source =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/modelsource")).ConfigureAwait(false);
            XRegistryResponse model =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(root.Metadata.GetProperty("site").GetString(), Is.EqualTo("one"));
                Assert.That(source.Metadata.GetProperty("attributes").TryGetProperty("$includes", out _), Is.True);
                Assert.That(model.Metadata.GetProperty("attributes").TryGetProperty("$includes", out _), Is.False);
            });
            XRegistryResponse denied = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/modelsource",
                /*lang=json,strict*/ """{"$include":"https://unapproved.example/model"}""")).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(400));
            Assert.That((await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/")).ConfigureAwait(false))
                .Metadata.GetProperty("site").GetString(), Is.EqualTo("one"));
        }

        [Test]
        public async Task SemanticVersionOrderingIgnoresBuildMetadataAndLinksPrereleasesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{
                  "schemas":{"singular":"schema","versionmode":"semver"}
                }}}}
                """);
            foreach (string id in new[] { "2.0.0", "1.0.0", "2.0.0-alpha.10", "2.0.0-alpha.2", "10.0.0+build" })
            {
                XRegistryResponse created = await endpoint.ExecuteAsync(
                    Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/" + Uri.EscapeDataString(id), "{}"))
                    .ConfigureAwait(false);
                Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            }
            XRegistryResponse meta =
                await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g/schemas/r/meta"))
                .ConfigureAwait(false);
            XRegistryResponse release = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/2.0.0")).ConfigureAwait(false);
            XRegistryResponse invalid = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/01.0.0", "{}")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(meta.Metadata.GetProperty("defaultversionid").GetString(), Is.EqualTo("10.0.0+build"));
                Assert.That(release.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("2.0.0-alpha.10"));
                Assert.That(invalid.StatusCode, Is.EqualTo(400));
            });
        }

        [TestCase("text/plain", "hello", "string")]
        [TestCase("application/json", "{\"key\":1}", "json")]
        [TestCase("application/vnd.test+json", "{\"key\":1}", "json")]
        [TestCase("application/json", "invalid", "binary")]
        [TestCase("application/octet-stream", "bytes", "binary")]
        public async Task InlinedDocumentsFollowTypeMapWithoutDamagingOriginalBytesAsync(
            string type, string content, string representation)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}") with
                {
                    Document = ByteString.From(Encoding.UTF8.GetBytes(content)),
                    ContentType = type
                }).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") with
                {
                    Parameters = [new("inline", "schema")]
                }).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(200), read.Error?.Detail);
            Assert.That(
                read.Metadata.TryGetProperty(representation == "binary" ? "schemabase64" : "schema", out _), Is.True);
            XRegistryResponse exact = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") with { View = XRegistryView.Default })
                .ConfigureAwait(false);
            Assert.That(exact.Document.Span.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes(content)));
        }

        [Test]
        public async Task GroupConstraintDefaultsOverrideTheResourceModelAndNarrowValuesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group",
                  "constraints":{"schemas.color":{"default":"blue","enum":["blue"]}},
                  "resources":{"schemas":{"singular":"schema",
                    "attributes":{"color":{"type":"string","default":"red","required":true,"enum":["red","blue"]}}
                  }}
                }}}
                """);
            XRegistryResponse first = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v2",
                    /*lang=json,strict*/ """{"color":"red"}""")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(201), first.Error?.Detail);
                Assert.That(first.Metadata.GetProperty("color").GetString(), Is.EqualTo("blue"));
                Assert.That(rejected.StatusCode, Is.EqualTo(400));
            });
        }

        [Test]
        public async Task SemanticVersionAssignmentProducesValidIncreasingIdentifiersAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","resources":{
                  "schemas":{"singular":"schema","versionmode":"semver","setversionid":false}
                }}}}
                """);
            XRegistryResponse first = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Create, "/groups/g/schemas/r", "{}")).ConfigureAwait(false);
            XRegistryResponse second = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Create, "/groups/g/schemas/r", "{}")).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(first.StatusCode, Is.EqualTo(201), first.Error?.Detail);
                Assert.That(second.StatusCode, Is.EqualTo(200), second.Error?.Detail);
                Assert.That(first.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("1.0.0"));
                Assert.That(second.Metadata.GetProperty("versionid").GetString(), Is.EqualTo("2.0.0"));
                Assert.That(second.Metadata.GetProperty("ancestorid").GetString(), Is.EqualTo("1.0.0"));
            });
        }

        [Test]
        public async Task GroupChangesCannotInvalidateRetainedVersionsAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","attributes":{"tenant":{"type":"string"}},
                  "constraints":{"schemas.owner":{"equals":"tenant"}},
                  "resources":{"schemas":{"singular":"schema","attributes":{"owner":{"type":"string"}}}}
                }}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """
                {"tenant":"a","schemas":{"r":{"versions":{"v1":{"owner":"a"},"v2":{"owner":"a"}}}}}
                """)).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse rejected = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/groups/g",
                /*lang=json,strict*/ """{"tenant":"b"}""")).ConfigureAwait(false);
            XRegistryResponse group = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/groups/g"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(rejected.Error?.Code, Is.EqualTo("constraint_failure"), rejected.Error?.Detail);
                Assert.That(group.Metadata.GetProperty("tenant").GetString(), Is.EqualTo("a"));
                Assert.That(group.Metadata.GetProperty("epoch").GetInt32(), Is.Zero);
            });
        }

        [Test]
        public async Task ModelUpdatesRetainValidActiveConditionalAttributesAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create(/*lang=json,strict*/ """
                {"groups":{"groups":{"singular":"group","attributes":{
                  "kind":{"type":"string","ifvalues":{"sensor":{"siblingattributes":{"unit":{"type":"string"}}}}}
                }}}}
                """);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """{"kind":"sensor","unit":"C"}""")).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            XRegistryResponse updated = await endpoint.ExecuteAsync(Request(XRegistryAction.Merge, "/modelsource",
                /*lang=json,strict*/ """{"description":"Updated description"}""")).ConfigureAwait(false);
            Assert.That(updated.StatusCode, Is.EqualTo(200), updated.Error?.Detail);
        }

        [Test]
        public async Task ResolvedIncludesAreStableAcrossOrdinaryWritesAndReopeningAsync()
        {
            var resolver = new CountingResolver();
            XRegistryTransactionalOptions options = Options(
                /*lang=json,strict*/ """{"$include":"https://models.example/model"}""") with
            {
                ModelResolver = resolver
            };
            var store = new InMemoryXRegistryTransactionStore();
            using (var endpoint = new XRegistryTransactionalEndpoint(options, store))
            {
                _ = await endpoint.InspectAsync(Writer).ConfigureAwait(false);
                _ = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model")).ConfigureAwait(false);
                XRegistryResponse written = await endpoint.ExecuteAsync(
                    Request(XRegistryAction.Merge, "/", /*lang=json,strict*/ """{"name":"persist"}"""))
                    .ConfigureAwait(false);
                Assert.That(written.StatusCode, Is.EqualTo(200), written.Error?.Detail);
                Assert.That(resolver.Calls, Is.EqualTo(1));
            }
            using var reopened = new XRegistryTransactionalEndpoint(options, store);
            _ = await reopened.InspectAsync(Writer).ConfigureAwait(false);
            Assert.That(resolver.Calls, Is.EqualTo(1));
            XRegistryResponse updated = await reopened.ExecuteAsync(Request(XRegistryAction.Merge, "/modelsource",
                /*lang=json,strict*/ """{"description":"resolve again"}""")).ConfigureAwait(false);
            Assert.That(updated.StatusCode, Is.EqualTo(200), updated.Error?.Detail);
            Assert.That(resolver.Calls, Is.EqualTo(2));
        }

        [TestCase("relative")]
        [TestCase("https://user:password@models.example/model")]
        [TestCase("https://models.example/model#fragment")]
        public void InvalidModelOriginFailsAtConstruction(string address)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                using var endpoint = new XRegistryTransactionalEndpoint(
                    Options() with { ModelSourceUri = new Uri(address, UriKind.RelativeOrAbsolute) },
                    new InMemoryXRegistryTransactionStore());
            });
        }

        [Test]
        public async Task CompleteModelIncludesNavigationAndProtectedStandardDefinitionsAsync()
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            XRegistryResponse model = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/model"))
                .ConfigureAwait(false);
            XRegistryResponse source = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/modelsource"))
                .ConfigureAwait(false);
            JsonElement root = model.Metadata.GetProperty("attributes");
            JsonElement group = model.Metadata.GetProperty("groups").GetProperty("groups");
            JsonElement schema = group.GetProperty("resources").GetProperty("schemas");
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("specversion").GetProperty("default").GetString(), Is.EqualTo("1.0-rc4"));
                Assert.That(root.GetProperty("registryid").GetProperty("immutable").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("groups").GetProperty("type").GetString(), Is.EqualTo("map"));
                Assert.That(group.GetProperty("attributes").GetProperty("schemascount").GetProperty("readonly")
                    .GetBoolean(), Is.True);
                Assert.That(schema.GetProperty("attributes").GetProperty("schemaurl").GetProperty("type")
                    .GetString(), Is.EqualTo("url"));
                Assert.That(schema.GetProperty("resourceattributes").GetProperty("versions").GetProperty("type")
                    .GetString(), Is.EqualTo("map"));
                Assert.That(
                    schema.GetProperty("metaattributes").GetProperty("defaultversionsticky").GetProperty("default")
                    .GetBoolean(), Is.False);
                Assert.That(source.Metadata.TryGetProperty("attributes", out _), Is.False);
            });
        }

        [TestCase("https://documents.example/schema.json")]
        [TestCase("urn:example:schema:v1")]
        [TestCase("../schemas/external")]
        public async Task ExternalDocumentReferencesAreRetainedWithoutInventingBytesAsync(string uri)
        {
            using XRegistryTransactionalEndpoint endpoint = Create();
            string body = new JsonObject { ["schemaurl"] = uri }.ToJsonString();
            XRegistryResponse created = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", body)).ConfigureAwait(false);
            XRegistryResponse touched = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1", "{}")).ConfigureAwait(false);
            XRegistryResponse redirect = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") with { View = XRegistryView.Default })
                .ConfigureAwait(false);
            XRegistryResponse inline = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") with
                { Parameters = [new("inline", "schema")] }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
                Assert.That(touched.StatusCode, Is.EqualTo(200), touched.Error?.Detail);
                Assert.That(redirect.StatusCode, Is.EqualTo(303));
                Assert.That(redirect.Location, Is.EqualTo(uri));
                Assert.That(redirect.Document.IsNull, Is.True);
                Assert.That(inline.Metadata.GetProperty("schemaurl").GetString(), Is.EqualTo(uri));
                Assert.That(inline.Metadata.TryGetProperty("schema", out _), Is.False);
                Assert.That(inline.Metadata.TryGetProperty("schemabase64", out _), Is.False);
            });
            XRegistryResponse replaced = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Replace, "/groups/g/schemas/r/versions/v1") with
                { Document = ByteString.Empty }).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(replaced.StatusCode, Is.EqualTo(200), replaced.Error?.Detail);
                Assert.That(replaced.Metadata.TryGetProperty("schemaurl", out _), Is.False);
            });
        }

        [Test]
        public async Task DiscoveryAndMethodAdvertisementRespectCallerPermissionsAsync()
        {
            using var endpoint = new XRegistryTransactionalEndpoint(Options() with
            {
                DiscoveryRegistries = [new Uri("https://catalog.example/registry")]
            }, new InMemoryXRegistryTransactionStore());
            XRegistryResponse discovery = await endpoint.ExecuteAsync(Request(XRegistryAction.Read, "/.xregistry"))
                .ConfigureAwait(false);
            XRegistryResponse reader = await endpoint.ExecuteAsync(
                Request(XRegistryAction.Describe, "/groups") with { Context = XRegistryCallContext.Anonymous })
                .ConfigureAwait(false);
            XRegistryResponse writer = await endpoint.ExecuteAsync(Request(XRegistryAction.Describe, "/groups"))
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(discovery.Metadata.GetProperty("registries")[0].GetString(),
                    Is.EqualTo("https://catalog.example/registry"));
                Assert.That(reader.AllowedActions.ToList(), Is.EquivalentTo(s_readActions));
                Assert.That(writer.AllowedActions.ToList(), Does.Contain(XRegistryAction.Create));
            });
        }

        [TestCase("epoch", """{"type":"string"}""")]
        [TestCase("self", """{"type":"url","readonly":false}""")]
        [TestCase("registryid", """{"type":"string","required":false}""")]
        [TestCase("custom", """{"type":"string","immutable":true}""")]
        [TestCase("custom", """{"type":"string","unknownkeyword":true}""")]
        [TestCase("custom", """{"type":"string","namecharset":"extended"}""")]
        [TestCase("custom", """{"type":"object","namecharset":"arbitrary"}""")]
        public void ProtectedStandardsAndUnknownModelKeywordsCannotBeOverridden(string name, string definition)
        {
            Assert.Throws<ArgumentException>(() =>
            {
                using XRegistryTransactionalEndpoint endpoint = Create(
                    "{\"groups\":{},\"attributes\":{\"" + name + "\":" + definition + "}}");
            });
        }

        [TestCase("strict", false)]
        [TestCase("extended", true)]
        [TestCase("EXTENDED", true)]
        public async Task ObjectNameCharsetControlsDeclaredAndConditionalNamesAsync(string charset, bool permitted)
        {
            string model = """
                {"groups":{"groups":{"singular":"group","attributes":{"settings":{
                  "type":"object","namecharset":"CHARSET","attributes":{
                    "kind":{"type":"string","ifvalues":{"sensor":{"siblingattributes":{"unit-name":{"type":"string"}}}}}
                  }}}}}}
                """.Replace("CHARSET", charset, StringComparison.Ordinal);
            if (!permitted)
            {
                Assert.Throws<ArgumentException>(() =>
                {
                    using XRegistryTransactionalEndpoint rejected = Create(model);
                });
                return;
            }
            using XRegistryTransactionalEndpoint endpoint = Create(model);
            XRegistryResponse created = await endpoint.ExecuteAsync(Request(XRegistryAction.Replace, "/groups/g",
                /*lang=json,strict*/ """{"settings":{"kind":"sensor","unit-name":"C"}}""")).ConfigureAwait(false);
            Assert.That(created.StatusCode, Is.EqualTo(201), created.Error?.Detail);
            Assert.That(created.Metadata.GetProperty("settings").GetProperty("unit-name").GetString(), Is.EqualTo("C"));
        }

        private sealed class CountingResolver : IXRegistryModelDocumentResolver
        {
            public int Calls { get; private set; }

            public ValueTask<JsonElement> ResolveAsync(Uri documentUri, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(documentUri, Is.EqualTo(new Uri("https://models.example/model")));
                Calls++;
                return new ValueTask<JsonElement>(Parse(/*lang=json,strict*/ """{"groups":{}}"""));
            }
        }

        private static readonly XRegistryAction[] s_readActions = [XRegistryAction.Read, XRegistryAction.Describe];
    }
}
