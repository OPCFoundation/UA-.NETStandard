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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT projection security.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionSecurityTests
    {
        /// <summary>
        /// Security qualification preserves exact UTF-8 bytes with RFC 3986 component escaping.
        /// </summary>
        /// <param name="sourceName">The source's manifest alias.</param>
        /// <param name="schemeName">The authored scheme name in both owner domains.</param>
        /// <param name="encodedSource">The expected escaped source component.</param>
        /// <param name="encodedScheme">The expected escaped scheme component.</param>
        [TestCase("pump", "opcua_sc", "pump", "opcua_sc")]
        [TestCase(UnreservedName, UnreservedName, UnreservedName, UnreservedName)]
        [TestCase("a:b", "c%d", "a%3Ab", "c%25d")]
        [TestCase("a%3Ab", "q:s:a:b", "a%253Ab", "q%3As%3Aa%3Ab")]
        [TestCase("a/b", "c d+e", "a%2Fb", "c%20d%2Be")]
        [TestCase("\u00e9", "\u00e9", "%C3%A9", "%C3%A9")]
        [TestCase("e\u0301", "e\u0301", "e%CC%81", "e%CC%81")]
        [TestCase("\ud83d\ude80", "\ud83d\ude80", "%F0%9F%9A%80", "%F0%9F%9A%80")]
        public async Task SecurityNamesUseExactUtf8PercentEncodingAsync(
            string sourceName, string schemeName, string encodedSource, string encodedScheme)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest(sourceName, "urn:source"));
            projection["securityDefinitions"] = new JsonObject
            {
                [schemeName] = new JsonObject { ["scheme"] = "basic" }
            };
            projection["security"] = schemeName;

            WotConversionResult<WotDocument> result = await ResolveAsync(
                projection, new Dictionary<string, JsonObject>
                {
                    ["urn:source"] = Source("value", schemeName, "nosec")
                }).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string sourceSecurity = "q:s:" + encodedSource + ":" + encodedScheme;
            string ownerSecurity = "q:p:" + encodedScheme;
            Assert.That(Security(view, "value"), Is.EqualTo(sourceSecurity));
            Assert.That(view.RootElement.GetProperty("security").GetString(), Is.EqualTo(ownerSecurity));
            Assert.That(view.SecurityDefinitions.Keys, Is.EquivalentTo(new[] { sourceSecurity, ownerSecurity }));
            Assert.That(view.SecurityDefinitions[sourceSecurity].GetProperty("scheme").GetString(),
                Is.EqualTo("nosec"));
            Assert.That(view.SecurityDefinitions[ownerSecurity].GetProperty("scheme").GetString(),
                Is.EqualTo("basic"));
        }

        /// <summary>
        /// Local security requirements and URI-template closure retain the projection's scheme ownership.
        /// </summary>
        /// <param name="map">The local affordance map.</param>
        /// <param name="requirement">The location of the security requirement.</param>
        [Test]
        public async Task LocalAffordanceSecurityRetainsProjectionOwnershipAsync(
            [Values("properties", "actions", "events")] string map,
            [Values("affordance", "form", "inherited-template")] string requirement)
        {
            JsonObject projection = Projection();
            JsonObject manifest = Manifest("source", "urn:source");
            manifest["uav:selectAll"] = false;
            projection["uav:projects"] = new JsonArray(manifest);
            projection["securityDefinitions"] = new JsonObject
            {
                ["auth"] = new JsonObject { ["scheme"] = "basic" }
            };
            projection["security"] = "auth";
            projection["uriVariables"] = new JsonObject
            {
                ["zone"] = new JsonObject { ["type"] = "string" }
            };
            var form = new JsonObject
            {
                ["href"] = requirement == "inherited-template"
                    ? "https://example.test/{zone}" : "https://example.test/local",
                ["op"] = map switch
                {
                    "actions" => "invokeaction",
                    "events" => "subscribeevent",
                    _ => "readproperty"
                }
            };
            var local = new JsonObject
            {
                ["type"] = "number",
                ["forms"] = new JsonArray(form)
            };
            if (requirement == "affordance")
            {
                local["security"] = "auth";
            }
            else if (requirement == "form")
            {
                form["security"] = "auth";
            }
            projection[map] = new JsonObject { ["local"] = local };

            WotConversionResult<WotDocument> result = await ResolveAsync(
                projection, new Dictionary<string, JsonObject>
                {
                    ["urn:source"] = Source("unused", "auth", "nosec")
                }).ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view, Is.Not.Null);
            JsonElement selected = view!.RootElement.GetProperty(map).GetProperty("local");
            JsonElement selectedForm = selected.GetProperty("forms")[0];
            if (requirement == "affordance")
            {
                Assert.That(selected.GetProperty("security").GetString(), Is.EqualTo("q:p:auth"));
            }
            if (selectedForm.TryGetProperty("security", out JsonElement security))
            {
                Assert.That(security.ValueKind == JsonValueKind.Array
                    ? security[0].GetString() : security.GetString(), Is.EqualTo("q:p:auth"));
            }
            else
            {
                Assert.That(requirement, Is.Not.EqualTo("form"),
                    "An explicit form requirement must not disappear during resolution.");
                Assert.That(view.RootElement.GetProperty("security").GetString(), Is.EqualTo("q:p:auth"));
            }
            Assert.That(view.SecurityDefinitions["q:p:auth"].GetProperty("scheme").GetString(),
                Is.EqualTo("basic"));
            Assert.That(selected.TryGetProperty("uav:resolvedFrom", out _), Is.False);
        }

        /// <summary>
        /// Verifies that reviewed source affordance security retains its owner.
        /// </summary>
        /// <param name="enumerated">The enumerated flag for this test case.</param>
        /// <param name="array">The array flag for this test case.</param>
        [Test]
        public async Task ReviewedSourceAffordanceSecurityRetainsItsOwner(
            [Values("properties", "actions", "events")] string map,
            [Values] bool enumerated,
            [Values] bool array)
        {
            JsonObject projection = Projection();
            JsonObject manifest = Manifest("source", "urn:source");
            projection["uav:projects"] = new JsonArray(manifest);
            JsonObject source = Source("value", "auth", "basic");
            var affordance = (JsonObject)source["properties"]!["value"]!.DeepClone();
            source.Remove("properties");
            source[map] = new JsonObject { ["value"] = affordance };
            affordance["security"] = array ? new JsonArray("auth") : JsonValue.Create("auth");
            affordance["forms"]![0]!["op"] = map switch
            {
                "actions" => "invokeaction",
                "events" => "subscribeevent",
                _ => "readproperty"
            };
            if (enumerated)
            {
                manifest.Remove("uav:selectAll");
                projection[map] = new JsonObject
                {
                    ["selected"] = new JsonObject { ["tm:ref"] = "urn:source#/" + map + "/value" }
                };
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(
                projection, new Dictionary<string, JsonObject> { ["urn:source"] = source }).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            JsonElement selected = view.RootElement.GetProperty(map).GetProperty(enumerated ? "selected" : "value");
            JsonElement security = selected.GetProperty("security");
            Assert.That((array ? security[0] : security).GetString(), Is.EqualTo("q:s:source:auth"));
            Assert.That(view.SecurityDefinitions["q:s:source:auth"].GetProperty("scheme").GetString(),
                Is.EqualTo("basic"));
            Assert.That(selected.GetProperty("forms")[0].GetProperty("security")[0].GetString(),
                Is.EqualTo("q:s:source:auth"));
        }

        /// <summary>
        /// Verifies that reviewed affordance requirement cannot select another sources anonymous scheme.
        /// </summary>
        /// <param name="reversed">The reversed flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReviewedAffordanceRequirementCannotSelectAnotherSourcesAnonymousScheme(bool reversed)
        {
            const string raw = "q:s:a_b:c";
            JsonObject projection = Projection();
            JsonObject victim = Manifest("source", "urn:victim");
            JsonObject other = Manifest("a_b", "urn:other");
            projection["uav:projects"] = reversed ? new JsonArray(other, victim) : new JsonArray(victim, other);
            JsonObject source = Source("victim", raw, "basic");
            source["properties"]!["victim"]!["security"] = raw;

            WotConversionResult<WotDocument> result = await ResolveAsync(
                projection, new Dictionary<string, JsonObject>
                {
                    ["urn:victim"] = source,
                    ["urn:other"] = Source("other", "c", "nosec")
                }).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string required = view.Properties["victim"].GetProperty("security").GetString().Required();
            Assert.That(required, Is.EqualTo("q:s:source:q%3As%3Aa_b%3Ac"));
            Assert.That(view.SecurityDefinitions[required].GetProperty("scheme").GetString(), Is.EqualTo("basic"));
            Assert.That(view.SecurityDefinitions[raw].GetProperty("scheme").GetString(), Is.EqualTo("nosec"));
        }

        /// <summary>
        /// Verifies that reviewed model affordance security carries its closure without forms.
        /// </summary>
        [Test]
        public async Task ReviewedModelAffordanceSecurityCarriesItsClosureWithoutForms()
        {
            JsonObject projection = Projection();
            projection["@type"] = new JsonArray("tm:ThingModel", "uav:projection");
            JsonObject manifest = Manifest("source", "urn:source");
            manifest["type"] = "application/tm+json";
            projection["uav:projects"] = new JsonArray(manifest);
            JsonObject source = Source("value", "auth", "basic");
            source["@type"] = "tm:ThingModel";
            source.Remove("security");
            source["properties"]!["value"]!["security"] = "auth";
            ((JsonObject)source["properties"]!["value"]!).Remove("forms");

            WotConversionResult<WotDocument> result = await ResolveAsync(
                projection, new Dictionary<string, JsonObject> { ["urn:source"] = source }).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Properties["value"].GetProperty("security").GetString(),
                Is.EqualTo("q:s:source:auth"));
            Assert.That(view.SecurityDefinitions["q:s:source:auth"].GetProperty("scheme").GetString(),
                Is.EqualTo("basic"));
            Assert.That(view.Properties["value"].TryGetProperty("forms", out _), Is.False);
        }

        /// <summary>
        /// Verifies that reviewed duplicate security containers cannot replace earlier requirements.
        /// </summary>
        /// <param name="host">The host flag for this test case.</param>
        /// <param name="equivalent">The equivalent flag for this test case.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ReviewedDuplicateSecurityContainersCannotReplaceEarlierRequirements(
            bool host, bool equivalent)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("source", "urn:source"));
            JsonObject source = Source("value", "auth", "basic");
            if (host)
            {
                projection["security"] = "auth";
            }
            string owner = (host ? projection : source).ToJsonString();
            if (!host)
            {
                source.Remove("securityDefinitions");
                owner = source.ToJsonString();
            }
            owner = owner[..^1] +
                ",\"securityDefinitions\":{\"auth\":{\"scheme\":\"basic\"}}," +
                "\"securityDefinitions\":{\"auth\":{\"scheme\":\"" +
                (equivalent ? "basic" : "nosec") +
                "\"}}}";
            var documents = new Mock<IWotThingResolver>();
            documents.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(
                    Encoding.UTF8.GetBytes(host ? source.ToJsonString() : owner)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(host ? owner : projection.ToJsonString()));
            var resolver = new WotProjectionResolver(documents.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.EqualTo(equivalent), string.Join("; ", result.Diagnostics));
            if (equivalent)
            {
                Assert.That(view.Required().SecurityDefinitions[host ? "q:p:auth" : "q:s:source:auth"]
                    .GetProperty("scheme").GetString(), Is.EqualTo("basic"));
            }
            else
            {
                Assert.That(result.Value, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
                documents.Verify(value => value.ResolveThingAsync(
                    "urn:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()),
                    host ? Times.Never() : Times.Once());
            }
        }

        /// <summary>
        /// Verifies that distinct source security origins cannot collide through underscores.
        /// </summary>
        /// <param name="reversed">The reversed flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DistinctSourceSecurityOriginsCannotCollideThroughUnderscores(bool reversed)
        {
            JsonObject projection = Projection();
            JsonObject left = Manifest("a_b", "urn:source:left");
            JsonObject right = Manifest("a", "urn:source:right");
            projection["uav:projects"] = reversed ? new JsonArray(right, left) : new JsonArray(left, right);
            var sources = new Dictionary<string, JsonObject>
            {
                ["urn:source:left"] = Source("left", "c", "nosec"),
                ["urn:source:right"] = Source("right", "b_c", "basic")
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.SecurityDefinitions, Has.Count.EqualTo(2));
            string leftSecurity = Security(view, "left");
            string rightSecurity = Security(view, "right");
            Assert.That(leftSecurity, Is.EqualTo("q:s:a_b:c"));
            Assert.That(rightSecurity, Is.EqualTo("q:s:a:b_c"));
            Assert.That(view.SecurityDefinitions[leftSecurity].GetProperty("scheme").GetString(), Is.EqualTo("nosec"));
            Assert.That(view.SecurityDefinitions[rightSecurity].GetProperty("scheme").GetString(), Is.EqualTo("basic"));
            Assert.That(sources["urn:source:right"]["security"]!.GetValue<string>(), Is.EqualTo("b_c"));
        }

        /// <summary>
        /// Verifies that projection owned names cannot impersonate qualified source security.
        /// </summary>
        [Test]
        public async Task ProjectionOwnedNamesCannotImpersonateQualifiedSourceSecurity()
        {
            const string authored = "q:s:a_b:c";
            JsonObject projection = Projection();
            JsonObject host = Manifest("host", "urn:source:host");
            host.Remove("uav:selectAll");
            host["uav:routing"] = "projection";
            projection["uav:projects"] = new JsonArray(Manifest("a_b", "urn:source:left"), host);
            projection["securityDefinitions"] = new JsonObject
            {
                [authored] = new JsonObject { ["scheme"] = "basic" }
            };
            projection["security"] = authored;
            projection["properties"] = new JsonObject
            {
                ["host"] = new JsonObject
                {
                    ["tm:ref"] = "urn:source:host#/properties/host",
                    ["security"] = authored,
                    ["forms"] = new JsonArray(new JsonObject
                    {
                        ["href"] = "https://host.test/value",
                        ["op"] = "readproperty",
                        ["security"] = new JsonArray(authored)
                    })
                }
            };
            var sources = new Dictionary<string, JsonObject>
            {
                ["urn:source:left"] = Source("left", "c", "nosec"),
                ["urn:source:host"] = Source("host", "c", "nosec")
            };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(Security(view, "left"), Is.EqualTo("q:s:a_b:c"));
            Assert.That(Security(view, "host"), Is.EqualTo("q:p:q%3As%3Aa_b%3Ac"));
            Assert.That(view.RootElement.GetProperty("security").GetString(), Is.EqualTo("q:p:q%3As%3Aa_b%3Ac"));
            Assert.That(view.Properties["host"].GetProperty("security").GetString(),
                Is.EqualTo("q:p:q%3As%3Aa_b%3Ac"));
            Assert.That(view.SecurityDefinitions, Has.Count.EqualTo(2));
            Assert.That(view.SecurityDefinitions["q:s:a_b:c"].GetProperty("scheme").GetString(),
                Is.EqualTo("nosec"));
            Assert.That(view.SecurityDefinitions["q:p:q%3As%3Aa_b%3Ac"].GetProperty("scheme").GetString(),
                Is.EqualTo("basic"));
            Assert.That(projection["security"]!.GetValue<string>(), Is.EqualTo(authored));
        }

        /// <summary>
        /// Verifies that duplicate security names cannot discard contradictory facts.
        /// </summary>
        /// <param name="projectionOwned">The projection-owned flag for this test case.</param>
        /// <param name="consistent">The consistent flag for this test case.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DuplicateSecurityNamesCannotDiscardContradictoryFacts(bool projectionOwned, bool consistent)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject sourceDocument = Source("value", "c", "nosec");
            if (projectionOwned)
            {
                projection["securityDefinitions"] = new JsonObject
                {
                    ["c"] = new JsonObject { ["scheme"] = "nosec" }
                };
                projection["security"] = "c";
            }
            string input = (projectionOwned ? projection : sourceDocument).ToJsonString();
            string duplicate = "\"c\":{\"scheme\":\"nosec\"},\"c\":{\"scheme\":\"" +
                (consistent ? "nosec" : "basic") +
                "\"}";
            input = input.Replace("\"c\":{\"scheme\":\"nosec\"}", duplicate, StringComparison.Ordinal);
            string sourceJson = projectionOwned ? sourceDocument.ToJsonString() : input;
            string projectionJson = projectionOwned ? input : projection.ToJsonString();
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    "urn:source", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(sourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projectionJson));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.EqualTo(consistent), string.Join("; ", result.Diagnostics));
            if (consistent)
            {
                Assert.That(view.Required().SecurityDefinitions["q:s:a:c"].GetProperty("scheme").GetString(),
                    Is.EqualTo("nosec"));
            }
            else
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
        }

        /// <summary>
        /// Verifies that referenced security closures must be complete.
        /// </summary>
        /// <param name="projectionOwned">The projection-owned flag for this test case.</param>
        [TestCase(false, "missing-root")]
        [TestCase(true, "missing-root")]
        [TestCase(false, "missing-child")]
        [TestCase(true, "missing-child")]
        [TestCase(false, "cycle")]
        [TestCase(true, "cycle")]
        public async Task ReferencedSecurityClosuresMustBeComplete(bool projectionOwned, string failure)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject source = Source("value", "c", "nosec");
            JsonObject owner = projectionOwned ? projection : source;
            owner["security"] = "c";
            owner["securityDefinitions"] = failure == "missing-root"
                ? []
                : new JsonObject
                {
                    ["c"] = new JsonObject
                    {
                        ["scheme"] = "combo",
                        ["allOf"] = new JsonArray(failure == "cycle" ? "c" : "missing")
                    }
                };
            var sources = new Dictionary<string, JsonObject> { ["urn:source"] = source };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.False, "An incomplete authentication requirement must not be published.");
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        /// <summary>
        /// Verifies that combo closures retain scoped references and unrelated metadata.
        /// </summary>
        /// <param name="projectionOwned">The projection-owned flag for this test case.</param>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ComboClosuresRetainScopedReferencesAndUnrelatedMetadata(bool projectionOwned)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject source = Source("value", "c", "nosec");
            JsonObject owner = projectionOwned ? projection : source;
            owner["security"] = "c";
            owner["securityDefinitions"] = new JsonObject
            {
                ["c"] = new JsonObject
                {
                    ["scheme"] = "combo",
                    ["allOf"] = new JsonArray("auth", "none"),
                    ["vendor:data"] = new JsonObject { ["allOf"] = new JsonArray("auth", "none") }
                },
                ["auth"] = new JsonObject { ["scheme"] = "basic" },
                ["none"] = new JsonObject { ["scheme"] = "nosec" }
            };
            var sources = new Dictionary<string, JsonObject> { ["urn:source"] = source };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string selected = projectionOwned
                ? view.RootElement.GetProperty("security").GetString().Required()
                : Security(view, "value");
            Assert.That(selected, Is.EqualTo(projectionOwned ? "q:p:c" : "q:s:a:c"));
            JsonElement combo = view.SecurityDefinitions[selected];
            Assert.That(combo.GetProperty("allOf")[0].GetString(),
                Is.EqualTo(projectionOwned ? "q:p:auth" : "q:s:a:auth"));
            Assert.That(combo.GetProperty("allOf")[1].GetString(),
                Is.EqualTo(projectionOwned ? "q:p:none" : "q:s:a:none"));
            Assert.That(combo.GetProperty("vendor:data").GetProperty("allOf")[0].GetString(), Is.EqualTo("auth"));
            Assert.That(combo.GetProperty("vendor:data").GetProperty("allOf")[1].GetString(), Is.EqualTo("none"));
        }

        /// <summary>
        /// Verifies that security depth bounds do not depend on definition enumeration order.
        /// </summary>
        /// <param name="reversed">The reversed flag for this test case.</param>
        /// <param name="accepted">The accepted flag for this test case.</param>
        [TestCase(3, false, true)]
        [TestCase(3, true, true)]
        [TestCase(2, false, false)]
        [TestCase(2, true, false)]
        public async Task SecurityDepthBoundsDoNotDependOnDefinitionEnumerationOrder(
            int depth, bool reversed, bool accepted)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject source = Source("value", "a", "nosec");
            var definitions = new JsonObject();
            foreach (string name in (string[])(reversed ? ["c", "b", "a"] : ["a", "b", "c"]))
            {
                definitions[name] = name == "c"
                    ? new JsonObject { ["scheme"] = "nosec" }
                    : new JsonObject
                    {
                        ["scheme"] = "combo",
                        ["allOf"] = new JsonArray(name == "a" ? "b" : "c")
                    };
            }
            source["securityDefinitions"] = definitions;
            var sources = new Dictionary<string, JsonObject> { ["urn:source"] = source };

            WotConversionResult<WotDocument> result = await ResolveAsync(
                projection, sources, new WotNodeSetConverterOptions { MaxResolverDepth = depth }).ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.EqualTo(accepted), string.Join("; ", result.Diagnostics));
            if (accepted)
            {
                Assert.That(view.Required().SecurityDefinitions, Has.Count.EqualTo(3));
                Assert.That(Security(view, "value"), Is.EqualTo("q:s:a:a"));
            }
            else
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
        }

        /// <summary>
        /// Verifies that non combo security metadata is not rewritten as a reference.
        /// </summary>
        [Test]
        public async Task NonComboSecurityMetadataIsNotRewrittenAsAReference()
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject source = Source("value", "c", "basic");
            source["securityDefinitions"]!["c"]!["allOf"] = new JsonArray("literal-metadata");
            var sources = new Dictionary<string, JsonObject> { ["urn:source"] = source };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument view = result.Value.Required();

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.SecurityDefinitions["q:s:a:c"].GetProperty("allOf")[0].GetString(),
                Is.EqualTo("literal-metadata"));
        }

        /// <summary>
        /// Verifies that malformed combo requirements cannot survive qualification.
        /// </summary>
        [TestCase("\"auth\"")]
        [TestCase("[]")]
        [TestCase("[\"auth\",42]")]
        [TestCase("null")]
        public async Task MalformedComboRequirementsCannotSurviveQualification(string requirement)
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject source = Source("value", "c", "nosec");
            source["securityDefinitions"] = new JsonObject
            {
                ["c"] = new JsonObject
                {
                    ["scheme"] = "combo",
                    ["allOf"] = JsonNode.Parse(requirement)
                },
                ["auth"] = new JsonObject { ["scheme"] = "basic" }
            };
            var sources = new Dictionary<string, JsonObject> { ["urn:source"] = source };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument? view = result.Value;

            Assert.That(result.Success, Is.False);
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        private static JsonObject Projection()
        {
            return new JsonObject
            {
                ["@type"] = new JsonArray("Thing", "uav:projection"),
                ["title"] = "Security origins",
                ["uav:scenario"] = "urn:scenario:security"
            };
        }

        private static JsonObject Manifest(string name, string href)
        {
            return new JsonObject
            {
                ["uav:sourceName"] = name,
                ["href"] = href,
                ["type"] = "application/td+json",
                ["uav:selectAll"] = true
            };
        }

        private static JsonObject Source(string name, string security, string scheme)
        {
            return new JsonObject
            {
                ["@type"] = new JsonArray("Thing"),
                ["title"] = name,
                ["securityDefinitions"] = new JsonObject
                {
                    [security] = new JsonObject { ["scheme"] = scheme }
                },
                ["security"] = security,
                ["properties"] = new JsonObject
                {
                    [name] = new JsonObject
                    {
                        ["type"] = "number",
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = "https://example.test/" + name,
                            ["op"] = "readproperty"
                        })
                    }
                }
            };
        }

        private static string Security(WotDocument document, string property)
        {
            JsonElement form = document.Properties[property].GetProperty("forms")[0];
            return form.GetProperty("security")[0].GetString().Required();
        }

        private static async ValueTask<WotConversionResult<WotDocument>> ResolveAsync(
            JsonObject projection, Dictionary<string, JsonObject> sources, WotNodeSetConverterOptions? options = null)
        {
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns((string reference, WotResolutionContext _, CancellationToken _) =>
                    new ValueTask<WotResolverResult>(sources.TryGetValue(reference, out JsonObject? value)
                        ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(value.ToJsonString()))
                        : WotResolverResult.NotFound));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object, options);
            return await resolver.ResolveAsync(document).ConfigureAwait(false);
        }

        /// <summary>
        /// Every ASCII byte that security qualification must preserve.
        /// </summary>
        private const string UnreservedName = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
    }
}
