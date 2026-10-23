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
    /// Contains tests for WoT projection selection.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionSelectionTests
    {
        /// <summary>
        /// Verifies that only a direct same-kind affordance pointer is a projection selection.
        /// </summary>
        /// <param name="isProjectionSelection">Whether the reference is a projection selection.</param>
        [TestCase("properties", "/properties/value", true)]
        [TestCase("actions", "/actions/reset", true)]
        [TestCase("events", "/events/alarm", true)]
        [TestCase("properties", "/properties/a~1b~0c", true)]
        [TestCase("properties", "/actions/reset", false)]
        [TestCase("properties", "/events/alarm", false)]
        [TestCase("actions", "/properties/value", false)]
        [TestCase("actions", "/events/alarm", false)]
        [TestCase("events", "/properties/value", false)]
        [TestCase("events", "/actions/reset", false)]
        [TestCase("properties", "", false)]
        [TestCase("properties", "/properties", false)]
        [TestCase("properties", "/properties/structure/properties/inner", false)]
        [TestCase("actions", "/actions/reset/input", false)]
        [TestCase("events", "/events/alarm/data", false)]
        public void EnumeratedReferenceClassificationFollowsAffordanceMapAndKind(
            string collection, string pointer, bool isProjectionSelection)
        {
            const string sourceId = "urn:source:selection";
            string reference = sourceId + "#" + pointer;
            var projection = new JsonObject
            {
                ["@type"] = new JsonArray("Thing", "uav:projection"),
                ["title"] = "Selection view",
                ["uav:scenario"] = "urn:scenario:selection",
                ["uav:projects"] = new JsonArray(new JsonObject
                {
                    ["uav:sourceName"] = "source",
                    ["href"] = sourceId,
                    ["type"] = "application/td+json"
                }),
                [collection] = new JsonObject
                {
                    ["selected"] = new JsonObject
                    {
                        ["tm:ref"] = reference,
                        ["forms"] = new JsonArray(new JsonObject
                        {
                            ["href"] = "opc.tcp://example.test/?id=i%3D1",
                            ["op"] = "readproperty"
                        })
                    }
                }
            };
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var diagnostics = new System.Collections.Generic.List<WotDiagnostic>();

            WotProjection parsed = WotProjection.Parse(document, diagnostics)!;

            Assert.That(diagnostics, Is.Empty, string.Join("; ", diagnostics));
            if (isProjectionSelection)
            {
                Assert.That(parsed.References.Count, Is.EqualTo(1));
                Assert.That(parsed.References[0].AffordanceKind.ToString(), Is.EqualTo(
                    collection switch
                    {
                        "actions" => "Action",
                        "events" => "Event",
                        _ => "Property"
                    }));
                Assert.That(parsed.References[0].Reference, Is.EqualTo(reference));
                Assert.That(parsed.LocalAffordances, Is.Empty);
            }
            else
            {
                Assert.That(parsed.References, Is.Empty);
                Assert.That(parsed.LocalAffordances.Count, Is.EqualTo(1));
                Assert.That(parsed.LocalAffordances[0].Definition.GetProperty("tm:ref").GetString(),
                    Is.EqualTo(reference));
            }
        }

        /// <summary>
        /// Verifies that a selector cannot silently become a local definition when its source is undeclared.
        /// </summary>
        [Test]
        public async Task ProjectionSelectionWithUndeclaredSourceFailsResolution()
        {
            const string sourceId = "urn:source:undeclared";
            const string projectionId = "urn:projection:undeclared";
            string projectionJson = $$"""
                {
                  "@type":["Thing","uav:projection"],
                  "base":"{{projectionId}}",
                  "title":"Selection view",
                  "uav:scenario":"urn:scenario:selection",
                  "uav:projects":[
                    {"uav:sourceName":"source","href":"urn:source:declared","type":"application/td+json"}
                  ],
                  "properties":{
                    "selected":{
                      "tm:ref":"{{sourceId}}#/properties/value"
                    }
                  }
                }
                """;
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    It.IsAny<string>(), It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(SourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projectionJson));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? resolved = result.Value;

            Assert.That(resolved, Is.Null);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
        }

        /// <summary>
        /// Verifies that equivalent relative and absolute source URIs identify the same declared source.
        /// </summary>
        [Test]
        public async Task ProjectionSelectionMatchesResolvedSourceUri()
        {
            const string sourceUri = "https://example.test/sources/pump.json";
            const string projectionJson = """
                {
                  "@type":["Thing","uav:projection"],
                  "base":"https://example.test/plans/view.json",
                  "title":"Selection view",
                  "uav:scenario":"urn:scenario:selection",
                  "uav:projects":[
                    {
                      "uav:sourceName":"source",
                      "href":"../sources/./pump.json",
                      "type":"application/td+json"
                    }
                  ],
                  "properties":{
                    "selected":{
                      "tm:ref":"https://example.test/sources/pump.json#/properties/value"
                    }
                  }
                }
                """;
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    sourceUri, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(SourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projectionJson));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument? resolved = result.Value;

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(resolved!.RootElement.GetProperty("properties").GetProperty("selected")
                .GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo("https://example.test/sources/pump.json#/properties/value"));
            source.Verify(resolverService => resolverService.ResolveThingAsync(
                sourceUri, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        private const string SourceJson = /*lang=json,strict*/ """
            {
              "@context":"https://www.w3.org/2022/wot/td/v1.1",
              "@type":["Thing","uav:object"],
              "id":"urn:source:selection",
              "title":"Source",
              "securityDefinitions":{"none":{"scheme":"nosec"}},
              "security":"none",
              "properties":{
                "value":{"type":"number","forms":[{"href":"https://example.test/value","op":"readproperty"}]},
                "a/b~c":{"type":"boolean","forms":[{"href":"https://example.test/flag","op":"readproperty"}]},
                "structure":{
                  "type":"object",
                  "properties":{"inner":{"type":"string"}},
                  "forms":[{"href":"https://example.test/structure","op":"readproperty"}]
                }
              },
              "actions":{
                "reset":{
                  "input":{"type":"integer"},
                  "forms":[{"href":"https://example.test/reset","op":"invokeaction"}]
                }
              },
              "events":{
                "alarm":{
                  "data":{"type":"integer"},
                  "forms":[{"href":"https://example.test/alarm","op":"subscribeevent"}]
                }
              }
            }
            """;
    }
}
