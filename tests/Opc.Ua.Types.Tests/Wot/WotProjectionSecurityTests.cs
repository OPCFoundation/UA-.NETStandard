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
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionSecurityTests
    {
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
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.SecurityDefinitions, Has.Count.EqualTo(2));
            string leftSecurity = Security(view, "left");
            string rightSecurity = Security(view, "right");
            Assert.That(leftSecurity, Is.EqualTo("q:s:YV9i:Yw"));
            Assert.That(rightSecurity, Is.EqualTo("q:s:YQ:Yl9j"));
            Assert.That(view.SecurityDefinitions[leftSecurity].GetProperty("scheme").GetString(), Is.EqualTo("nosec"));
            Assert.That(view.SecurityDefinitions[rightSecurity].GetProperty("scheme").GetString(), Is.EqualTo("basic"));
            Assert.That(sources["urn:source:right"]["security"]!.GetValue<string>(), Is.EqualTo("b_c"));
        }

        [Test]
        public async Task ProjectionOwnedNamesCannotImpersonateQualifiedSourceSecurity()
        {
            const string authored = "q:s:YV9i:Yw";
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
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(Security(view, "left"), Is.EqualTo("q:s:YV9i:Yw"));
            Assert.That(Security(view, "host"), Is.EqualTo("q:p:cTpzOllWOWk6WXc"));
            Assert.That(view.RootElement.GetProperty("security").GetString(), Is.EqualTo("q:p:cTpzOllWOWk6WXc"));
            Assert.That(view.Properties["host"].GetProperty("security").GetString(),
                Is.EqualTo("q:p:cTpzOllWOWk6WXc"));
            Assert.That(view.SecurityDefinitions, Has.Count.EqualTo(2));
            Assert.That(view.SecurityDefinitions["q:s:YV9i:Yw"].GetProperty("scheme").GetString(),
                Is.EqualTo("nosec"));
            Assert.That(view.SecurityDefinitions["q:p:cTpzOllWOWk6WXc"].GetProperty("scheme").GetString(),
                Is.EqualTo("basic"));
            Assert.That(projection["security"]!.GetValue<string>(), Is.EqualTo(authored));
        }

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
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(consistent), string.Join("; ", result.Diagnostics));
            if (consistent)
            {
                Assert.That(view.SecurityDefinitions["q:s:YQ:Yw"].GetProperty("scheme").GetString(),
                    Is.EqualTo("nosec"));
            }
            else
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
        }

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
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.False, "An incomplete authentication requirement must not be published.");
            Assert.That(view, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

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
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            string selected = projectionOwned
                ? view.RootElement.GetProperty("security").GetString()
                : Security(view, "value");
            Assert.That(selected, Is.EqualTo(projectionOwned ? "q:p:Yw" : "q:s:YQ:Yw"));
            JsonElement combo = view.SecurityDefinitions[selected];
            Assert.That(combo.GetProperty("allOf")[0].GetString(),
                Is.EqualTo(projectionOwned ? "q:p:YXV0aA" : "q:s:YQ:YXV0aA"));
            Assert.That(combo.GetProperty("allOf")[1].GetString(),
                Is.EqualTo(projectionOwned ? "q:p:bm9uZQ" : "q:s:YQ:bm9uZQ"));
            Assert.That(combo.GetProperty("vendor:data").GetProperty("allOf")[0].GetString(), Is.EqualTo("auth"));
            Assert.That(combo.GetProperty("vendor:data").GetProperty("allOf")[1].GetString(), Is.EqualTo("none"));
        }

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
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(accepted), string.Join("; ", result.Diagnostics));
            if (accepted)
            {
                Assert.That(view.SecurityDefinitions, Has.Count.EqualTo(3));
                Assert.That(Security(view, "value"), Is.EqualTo("q:s:YQ:YQ"));
            }
            else
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
        }

        [Test]
        public async Task NonComboSecurityMetadataIsNotRewrittenAsAReference()
        {
            JsonObject projection = Projection();
            projection["uav:projects"] = new JsonArray(Manifest("a", "urn:source"));
            JsonObject source = Source("value", "c", "basic");
            source["securityDefinitions"]!["c"]!["allOf"] = new JsonArray("literal-metadata");
            var sources = new Dictionary<string, JsonObject> { ["urn:source"] = source };

            WotConversionResult<WotDocument> result = await ResolveAsync(projection, sources).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.SecurityDefinitions["q:s:YQ:Yw"].GetProperty("allOf")[0].GetString(),
                Is.EqualTo("literal-metadata"));
        }

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
            using WotDocument view = result.Value;

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
            return form.GetProperty("security")[0].GetString();
        }

        private static async ValueTask<WotConversionResult<WotDocument>> ResolveAsync(
            JsonObject projection, Dictionary<string, JsonObject> sources, WotNodeSetConverterOptions options = null)
        {
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns((string reference, WotResolutionContext _, CancellationToken _) =>
                    new ValueTask<WotResolverResult>(sources.TryGetValue(reference, out JsonObject value)
                        ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(value.ToJsonString()))
                        : WotResolverResult.NotFound));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object, options);
            return await resolver.ResolveAsync(document).ConfigureAwait(false);
        }
    }
}
