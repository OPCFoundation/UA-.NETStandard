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
    [TestFixture]
    [Category("WoT")]
    public sealed class WotProjectionSelectionTests
    {
        [TestCase("properties", "/properties/value", true)]
        [TestCase("actions", "/actions/reset", true)]
        [TestCase("events", "/events/alarm", true)]
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
        [TestCase("properties", "/properties/a~1b~0c", true)]
        public async Task EnumeratedReferencesSelectOnlyAnAffordanceOfTheDeclaredKind(
            string collection, string pointer, bool accepted)
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
                    ["selected"] = new JsonObject { ["tm:ref"] = reference }
                }
            };
            var source = new Mock<IWotThingResolver>();
            source.Setup(resolver => resolver.ResolveThingAsync(
                    sourceId, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(SourceJson)));
            using var document = WotDocument.Parse(Encoding.UTF8.GetBytes(projection.ToJsonString()));
            var resolver = new WotProjectionResolver(source.Object);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(document).ConfigureAwait(false);
            using WotDocument resolved = result.Value;

            Assert.That(result.Success, Is.EqualTo(accepted), string.Join("; ", result.Diagnostics));
            if (accepted)
            {
                Assert.That(resolved, Is.Not.Null);
                JsonElement selected = resolved.RootElement.GetProperty(collection).GetProperty("selected");
                Assert.That(selected.GetProperty("uav:resolvedFrom").GetString(), Is.EqualTo(reference));
                Assert.That(selected.GetProperty("forms").GetArrayLength(), Is.EqualTo(1));
            }
            else
            {
                Assert.That(resolved, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True);
            }
            Assert.That(projection[collection]!["selected"]!["tm:ref"]!.GetValue<string>(), Is.EqualTo(reference));
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
