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
        [TestCase("enumerated-only", true)]
        [TestCase("same-name", true)]
        [TestCase("different-name", false)]
        [TestCase("select-all", true)]
        public async Task ExplicitSelectionKeepsPrecedenceOverUncertainBulkMembership(string mode, bool success)
        {
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject();
            if (mode != "select-all")
            {
                string name = mode == "different-name" ? "renamed" : "Value";
                plan["properties"]![name] = new JsonObject { ["tm:ref"] = SourceHref + "#/properties/Value" };
            }
            if (mode != "enumerated-only")
            {
                plan["uav:projects"]![0]!["uav:select"] = new JsonArray(
                    new JsonObject { ["@type"] = "https://predicate.test/Sensor" });
            }
            if (mode == "select-all")
            {
                plan["uav:projects"]![0]!["uav:selectAll"] = true;
            }

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, UnknownBulkTypeSource())
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(success), string.Join("; ", result.Diagnostics));
            if (!success)
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionSelectorInvalid),
                    Is.True);
                return;
            }
            Assert.That(view.Properties, Has.Count.EqualTo(1));
            Assert.That(view.Properties["Value"].GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Value"));
            WotConversionResult<UANodeSet> native = WotNodeSetConverter.ToNodeSetResult(view);
            Assert.That(native.Success, Is.True, string.Join("; ", native.Diagnostics));
            UAVariable value = native.Value.Items.OfType<UAVariable>().Single();
            Assert.That(value.DataType, Is.EqualTo("i=11"));
            Assert.That(value.ValueRank, Is.EqualTo(-1));
            Assert.That(QualifiedName.Parse(value.BrowseName).Name, Is.EqualTo("Reading"));
        }

        [Test]
        public async Task BulkCollisionUsesTotalOrderBeforeUncertainLaterPredicates(
            [Values] bool earlierKnown, [Values] bool reverseInput)
        {
            JsonObject plan = Plan();
            plan["properties"] = new JsonObject();
            plan["uav:projects"]![0]!["uav:namePrefix"] = "device";
            plan["uav:projects"]![0]!["uav:select"] = new JsonArray(
                new JsonObject { ["@type"] = "https://predicate.test/Sensor" });
            JsonObject source = UnknownBulkTypeSource();
            JsonNode earlier = source["properties"]!["Value"]!.DeepClone();
            JsonNode later = source["properties"]!["Value"]!.DeepClone();
            JsonNode known = earlierKnown ? earlier : later;
            known["@type"] = new JsonArray("uav:variable", "https://predicate.test/Sensor");
            later["uav:id"] = "nsu=urn:source;i=3";
            var properties = new JsonObject();
            if (reverseInput)
            {
                properties.Add("alpha", later);
                properties.Add("Alpha", earlier);
            }
            else
            {
                properties.Add("Alpha", earlier);
                properties.Add("alpha", later);
            }
            source["properties"] = properties;

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, source).ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.That(result.Success, Is.EqualTo(earlierKnown), string.Join("; ", result.Diagnostics));
            if (!earlierKnown)
            {
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(item => item.Code == WotDiagnosticCode.ProjectionSelectorInvalid),
                    Is.True);
                return;
            }
            Assert.That(view.Properties, Has.Count.EqualTo(1));
            JsonElement selected = view.Properties["deviceAlpha"];
            Assert.That(selected.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(SourceHref + "#/properties/Alpha"));
            Assert.That(selected.GetProperty("uav:id").GetString(), Is.EqualTo("nsu=urn:source;i=2"));
        }

        private static JsonObject UnknownBulkTypeSource()
        {
            JsonObject source = ContextReviewSource();
            source["@context"]!.AsArray().Add("https://unacquired.test/context.jsonld");
            source["@context"]!.AsArray().Add(new JsonObject
            {
                ["uav"] = "http://opcfoundation.org/UA/WoT-Binding/",
                ["s"] = "urn:source",
                ["Thing"] = "https://www.w3.org/2019/wot/td#Thing"
            });
            source["properties"]!["Value"]!["@type"] = new JsonArray("uav:variable", "Mystery");
            return source;
        }
    }
}
