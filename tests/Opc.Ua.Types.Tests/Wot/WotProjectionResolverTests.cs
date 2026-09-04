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
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Exercises <see cref="WotProjectionResolver"/> against the worked
    /// examples of WoT Binding Section 12 and each of the selection, merge and
    /// carriage rules positively and negatively.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotProjectionResolverTests
    {
        [Test]
        public async Task ResolvesPredictiveMaintenanceProjectionToExpectedView()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            Assert.That(result.Success, Is.True);
            using WotDocument view = result.Value!;
            Assert.That(view, Is.Not.Null);
            AssertJsonEqual(PredictiveResolvedJson, view!.RootElement);
        }

        [Test]
        public async Task ResolvesAssetInstanceProjectionToExpectedView()
        {
            var resolver = Resolver(
                ("./06-anchored-paths-and-device-identity.jsonld", IdentitySourceJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(AssetProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            using WotDocument view = result.Value!;
            Assert.That(view, Is.Not.Null);
            AssertJsonEqual(AssetResolvedJson, view!.RootElement);
        }

        [Test]
        public async Task SelectsEnumeratedAffordance()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement speed = Property(result.Value!, "pumpSpeed");
            Assert.That(
                speed.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo("./01-opcua-td-pump.jsonld#/properties/pumpSpeed"));
        }

        [Test]
        public async Task SelectsWholeDocumentWithSelectAll()
        {
            var resolver = Resolver(
                ("./06-anchored-paths-and-device-identity.jsonld", IdentitySourceJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(AssetProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            JsonElement properties = result.Value!.RootElement.GetProperty("properties");
            Assert.That(
                PropertyNames(properties),
                Is.EquivalentTo(s_identityMembers));
        }

        [Test]
        public async Task SelectsByPredicateAffordanceKind()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement root = result.Value!.RootElement;
            Assert.That(root.TryGetProperty("actions", out _), Is.False);
            Assert.That(root.TryGetProperty("events", out _), Is.False);
            Assert.That(
                PropertyNames(root.GetProperty("properties")),
                Contains.Item("pumpSpeed"));
        }

        [Test]
        public async Task PredicateRequiresEveryTypeToken()
        {
            var resolver = Resolver(("urn:sensors", TwoTypedPropertiesJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(TypeTokenProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            Assert.That(
                PropertyNames(result.Value!.RootElement.GetProperty("properties")),
                Is.EqualTo(s_sensorMembers));
        }

        [Test]
        public async Task EnumeratedSelectionWinsOverBulkAndReportsDrop()
        {
            var resolver = Resolver(("urn:pump", MinimalPumpJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(FirstWinsProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            JsonElement speed = Property(result.Value!, "pumpSpeed");
            Assert.That(
                speed.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo("urn:pump#/properties/pumpSpeed"));
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionSelectionDropped &&
                    d.Severity == WotDiagnosticSeverity.Warning),
                Is.True);
        }

        [Test]
        public async Task AnnotationOverridesSourceMembersAndDiscardsTmRef()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement speed = Property(result.Value!, "pumpSpeed");
            Assert.That(
                speed.GetProperty("title").GetString(),
                Is.EqualTo("Pump speed (condition signal)"));
            Assert.That(
                speed.GetProperty("uav:browseName").GetString(),
                Is.EqualTo("pump:PumpSpeed"));
            Assert.That(speed.TryGetProperty("tm:ref", out _), Is.False);
        }

        [Test]
        public async Task NamePrefixUpperCasesSourceName()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            Assert.That(
                PropertyNames(result.Value!.RootElement.GetProperty("properties")),
                Contains.Item("deviceSerialNumber"));
        }

        [Test]
        public async Task WithoutNamePrefixNamesAreUnchanged()
        {
            var resolver = Resolver(
                ("./06-anchored-paths-and-device-identity.jsonld", IdentitySourceJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(AssetProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(
                PropertyNames(result.Value!.RootElement.GetProperty("properties")),
                Contains.Item("serialNumber"));
        }

        [Test]
        public async Task SelfReferentialProjectionSourceReportsCycle()
        {
            var resolver = Resolver(("urn:self", SelfProjectionJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(SelfProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionCycle),
                Is.True);
        }

        /// <summary>
        /// A group two organized branches both reach is one group, and the
        /// acyclicity walk visits it once. Meeting a group that has already
        /// been walked is not a cycle - a cycle is meeting one that is still
        /// on the current path - so a diamond resolves rather than being
        /// refused.
        /// </summary>
        [Test]
        public async Task AGroupReachedByTwoBranchesIsNotACycleAsync()
        {
            var resolver = Resolver(
                ("urn:pump", MinimalPumpJson),
                ("urn:group-a", Group("urn:group-a", "urn:group-c")),
                ("urn:group-b", Group("urn:group-b", "urn:group-c")),
                ("urn:group-c", Group("urn:group-c", string.Empty)));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(DiamondProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.Multiple(() =>
            {
                Assert.That(result.Value, Is.Not.Null, Reasons(result));
                Assert.That(
                    result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ProjectionCycle),
                    Is.False,
                    Reasons(result));
            });
        }

        /// <summary>
        /// A group that organizes itself through another group is a cycle, and
        /// the same walk that tolerates a diamond refuses it.
        /// </summary>
        [Test]
        public async Task AGroupThatOrganizesItsOwnOrganizerIsACycleAsync()
        {
            var resolver = Resolver(
                ("urn:pump", MinimalPumpJson),
                ("urn:group-a", Group("urn:group-a", "urn:group-c")),
                ("urn:group-b", Group("urn:group-b", "urn:group-c")),
                ("urn:group-c", Group("urn:group-c", "urn:group-a")));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(DiamondProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.ProjectionCycle),
                Is.True,
                Reasons(result));
        }

        private static string Reasons(WotConversionResult<WotDocument> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        /// <summary>
        /// A Thing that organizes at most one other group, used to build an
        /// <c>ua:Organizes</c> graph a walk has to traverse.
        /// </summary>
        private static string Group(string id, string organizes)
        {
            string links = organizes.Length == 0
                ? string.Empty
                : ",\"links\":[{\"rel\":\"ua:Organizes\",\"href\":\"" + organizes +
                    "\",\"type\":\"application/td+json\"}]";
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"}]," +
                "\"@type\":\"uav:object\",\"id\":\"" + id + "\",\"title\":\"Group\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"security\":\"nosec_sc\"" + links + "}";
        }

        private const string DiamondProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "ua": "http://opcfoundation.org/UA/",
              "tm": "https://www.w3.org/2019/wot/tm#"
            }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:diamond",
          "title": "Diamond",
          "uav:scenario": "http://example.com/scenario/Diamond",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "pump",
              "href": "urn:pump",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            {
              "rel": "ua:Organizes",
              "href": "urn:group-a",
              "uav:refName": "GroupA",
              "type": "application/td+json"
            },
            {
              "rel": "ua:Organizes",
              "href": "urn:group-b",
              "uav:refName": "GroupB",
              "type": "application/td+json"
            }
          ]
        }
        """;

        [Test]
        public async Task DigestMismatchReportsError()
        {
            var resolver = Resolver(("urn:pump", MinimalPumpJson));
            string projection = DigestProjection("sha-256:" + new string('0', 64));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(projection));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionDigestMismatch),
                Is.True);
        }

        [Test]
        public async Task MatchingDigestResolves()
        {
            var resolver = Resolver(("urn:pump", MinimalPumpJson));
            string digest = "sha-256:" + Sha256Hex(MinimalPumpJson);
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(DigestProjection(digest)));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionDigestMismatch),
                Is.False);
        }

        [Test]
        public async Task ConflictingContextPrefixReportsError()
        {
            var resolver = Resolver(
                ("urn:a", ContextSource("ex", "http://example.com/a")),
                ("urn:b", ContextSource("ex", "http://example.com/b")));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(TwoSourceProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.True);
        }

        [Test]
        public async Task CompatibleContextPrefixMerges()
        {
            var resolver = Resolver(
                ("urn:a", ContextSource("ex", "http://example.com/shared")),
                ("urn:b", ContextSource("ex", "http://example.com/shared")));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(TwoSourceProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionContextConflict),
                Is.False);
        }

        [Test]
        public async Task SourceRoutingAbsolutizesFormAndCopiesSecurityClosure()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement speed = Property(result.Value!, "pumpSpeed");
            JsonElement form = speed.GetProperty("forms")[0];
            Assert.That(
                form.GetProperty("href").GetString(),
                Is.EqualTo(
                    "opc.tcp://opcuademo.com:4840/?id=nsu=" +
                    "http://example.com/demo/pump;s=PumpSpeed"));
            Assert.That(
                form.GetProperty("security")[0].GetString(),
                Is.EqualTo("pump_opcua_sc"));

            JsonElement definitions =
                result.Value!.RootElement.GetProperty("securityDefinitions");
            JsonElement combo = definitions.GetProperty("pump_opcua_sc");
            Assert.That(
                combo.GetProperty("allOf").EnumerateArray()
                    .Select(e => e.GetString()),
                Is.EqualTo(s_pumpComboAllOf));
            Assert.That(
                definitions.TryGetProperty("pump_opcua_channel_sc", out _), Is.True);
            Assert.That(
                definitions.TryGetProperty("pump_opcua_authentication_sc", out _),
                Is.True);
        }

        [Test]
        public async Task ProjectionRoutingKeepsProjectionForms()
        {
            var resolver = Resolver(("urn:pump", MinimalPumpJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(ProjectionRoutedJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            JsonElement speed = Property(result.Value!, "speed");
            Assert.That(
                speed.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://gateway.example/pump/speed"));
            JsonElement definitions =
                result.Value!.RootElement.GetProperty("securityDefinitions");
            Assert.That(
                PropertyNames(definitions), Is.EqualTo(s_nosecOnly));
        }

        [Test]
        public async Task FormWithoutEffectiveSecurityDeclaresNone()
        {
            var resolver = Resolver(("urn:pump", NoSecurityPumpJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(NoSecurityProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            JsonElement speed = Property(result.Value!, "pumpSpeed");
            Assert.That(speed.GetProperty("forms")[0].TryGetProperty("security", out _),
                Is.False);
        }

        [Test]
        public async Task BulkProvenanceIsHrefPlusPointer()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement setpoint = Property(result.Value!, "speedSetpoint");
            Assert.That(
                setpoint.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo(
                    "./01-opcua-td-pump.jsonld#/properties/speedSetpoint"));
        }

        [Test]
        public async Task RelativeBrowsePathInheritsSourceAnchor()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement serial = Property(result.Value!, "deviceSerialNumber");
            Assert.That(
                serial.GetProperty("uav:browsePathAnchor").GetString(),
                Is.EqualTo("nsu=http://example.com/demo/pump;s=Pump07"));
        }

        [Test]
        public async Task AbsoluteBrowsePathDoesNotInheritAnchor()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            JsonElement speed = Property(result.Value!, "pumpSpeed");
            Assert.That(speed.TryGetProperty("uav:browsePathAnchor", out _), Is.False);
        }

        /// <summary>
        /// Section 12.4 carries the source's <em>effective</em> anchor, which
        /// Section 5.1.4 defines as the nearest enclosing
        /// <c>uav:browsePathAnchor</c> and, failing that, the nearest enclosing
        /// <c>uav:id</c>. A source that identifies the Node it describes
        /// therefore anchors its carried paths just as one that states an
        /// anchor does - and it has to be written down, because the view's own
        /// root identifies a different Node.
        /// </summary>
        [Test]
        public async Task ASourceIdentityBecomesTheCarriedAnchorAsync()
        {
            JsonElement speed = await CarryAsync(
                rootTerms: "\"uav:id\": \"nsu=urn:anchor;s=Source\",",
                affordanceTerms: string.Empty).ConfigureAwait(false);

            Assert.That(
                speed.GetProperty("uav:browsePathAnchor").GetString(),
                Is.EqualTo("nsu=urn:anchor;s=Source"));
        }

        /// <summary>
        /// The two sources are ordered by kind, not by depth, so an anchor at
        /// the source root outranks an identity the affordance states and is
        /// the value that has to be carried. Carrying the affordance's own
        /// identity instead would resolve the path beneath a different Node
        /// than the one it resolved beneath in the source.
        /// </summary>
        [Test]
        public async Task ARootAnchorOutranksTheAffordancesOwnIdentityAsync()
        {
            JsonElement speed = await CarryAsync(
                rootTerms:
                    "\"uav:id\": \"nsu=urn:anchor;s=Source\"," +
                    "\"uav:browsePathAnchor\": \"nsu=urn:anchor;s=Anchor\",",
                affordanceTerms: "\"uav:id\": \"nsu=urn:anchor;s=Member\",")
                .ConfigureAwait(false);

            Assert.That(
                speed.GetProperty("uav:browsePathAnchor").GetString(),
                Is.EqualTo("nsu=urn:anchor;s=Anchor"));
        }

        /// <summary>
        /// Where the source states no anchor, the affordance's own identity is
        /// what its relative path resolved against, and that identity travels
        /// with the affordance. Writing an anchor as well would state a second
        /// answer to a question the carried document already answers.
        /// </summary>
        [Test]
        public async Task AnAffordanceThatCarriesItsOwnIdentityNeedsNoAnchorAsync()
        {
            JsonElement speed = await CarryAsync(
                rootTerms: "\"uav:id\": \"nsu=urn:anchor;s=Source\",",
                affordanceTerms: "\"uav:id\": \"nsu=urn:anchor;s=Member\",")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(speed.TryGetProperty("uav:browsePathAnchor", out _), Is.False);
                Assert.That(
                    speed.GetProperty("uav:id").GetString(),
                    Is.EqualTo("nsu=urn:anchor;s=Member"));
            });
        }

        /// <summary>
        /// An anchor the affordance stated itself is already the nearest one,
        /// so it is carried unchanged rather than replaced by the source root's.
        /// </summary>
        [Test]
        public async Task AnAffordancesOwnAnchorIsKeptAsync()
        {
            JsonElement speed = await CarryAsync(
                rootTerms: "\"uav:browsePathAnchor\": \"nsu=urn:anchor;s=Anchor\",",
                affordanceTerms: "\"uav:browsePathAnchor\": \"nsu=urn:anchor;s=Own\",")
                .ConfigureAwait(false);

            Assert.That(
                speed.GetProperty("uav:browsePathAnchor").GetString(),
                Is.EqualTo("nsu=urn:anchor;s=Own"));
        }

        /// <summary>
        /// A source that anchors nothing has nothing to carry: the path did not
        /// resolve there either, and inventing an anchor for the view would
        /// make it resolve against a Node the source never named.
        /// </summary>
        [Test]
        public async Task ASourceThatAnchorsNothingCarriesNothingAsync()
        {
            JsonElement speed = await CarryAsync(
                rootTerms: string.Empty, affordanceTerms: string.Empty)
                .ConfigureAwait(false);

            Assert.That(speed.TryGetProperty("uav:browsePathAnchor", out _), Is.False);
        }

        private static async Task<JsonElement> CarryAsync(
            string rootTerms, string affordanceTerms)
        {
            var resolver = Resolver(
                ("urn:anchor-source", AnchorSourceJson(rootTerms, affordanceTerms)));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(AnchorProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(
                result.Value,
                Is.Not.Null,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            return Property(result.Value!, "speed");
        }

        private static string AnchorSourceJson(string rootTerms, string affordanceTerms)
        {
            return "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"http://example.com/demo/pump\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"],\"id\":\"urn:anchor-source\"," +
                "\"title\":\"Source\"," + rootTerms +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"security\":\"nosec_sc\",\"base\":\"opc.tcp://opcuademo.com:4840\"," +
                "\"properties\":{\"speed\":{\"@type\":\"uav:variable\"," +
                "\"title\":\"Speed\",\"type\":\"number\"," + affordanceTerms +
                "\"uav:browsePath\":\"pump:Speed\"," +
                "\"forms\":[{\"href\":\"/?id=nsu=urn:anchor;s=Speed\"," +
                "\"contentType\":\"application/octet-stream\"," +
                "\"op\":[\"readproperty\"]}]}}}";
        }

        private const string AnchorProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "tm": "https://www.w3.org/2019/wot/tm#"
            }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:anchor-view",
          "title": "Anchor view",
          "uav:scenario": "http://example.com/scenario/Anchors",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "source",
              "href": "urn:anchor-source",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        [Test]
        public async Task SourceWithoutBaseUsesSourceUriAsBase()
        {
            var resolver = Resolver(
                ("https://things.example/pump.td.jsonld", NoBasePumpJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(NoBaseProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Success, Is.True);
            JsonElement speed = Property(result.Value!, "pumpSpeed");
            Assert.That(
                speed.GetProperty("forms")[0].GetProperty("href").GetString(),
                Is.EqualTo("https://things.example/read/pumpSpeed"));
        }

        [Test]
        public async Task ResolvedViewCarriesNoProjectionMarker()
        {
            WotConversionResult<WotDocument> result = await ResolvePredictiveAsync();

            using WotDocument view = result.Value!;
            Assert.That(WotProjection.IsProjection(view!), Is.False);
            Assert.That(view!.RootElement.TryGetProperty("uav:projects", out _),
                Is.False);
            Assert.That(view.TypeTokens, Does.Not.Contain("uav:projection"));
        }

        [Test]
        public async Task NonProjectionDocumentIsRejected()
        {
            var resolver = Resolver();
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(MinimalPumpJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error),
                Is.True);
        }

        [Test]
        public async Task UnresolvableSourceReportsError()
        {
            var resolver = Resolver();
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(TwoSourceProjectionJson));

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(doc);

            Assert.That(result.Value, Is.Null);
            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code == WotDiagnosticCode.ProjectionSourceUnresolved),
                Is.True);
        }

        [Test]
        public void NullDocumentThrows()
        {
            var resolver = Resolver();
            Assert.That(
                async () => await resolver.ResolveAsync(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void NullResolverThrows()
        {
            Assert.That(
                () => new WotProjectionResolver(null!),
                Throws.ArgumentNullException);
        }

        private static async Task<WotConversionResult<WotDocument>> ResolvePredictiveAsync()
        {
            var resolver = Resolver(
                ("./01-opcua-td-pump.jsonld", PumpSourceJson),
                ("./06-anchored-paths-and-device-identity.jsonld", IdentitySourceJson));
            using WotDocument doc =
                WotDocument.Parse(Encoding.UTF8.GetBytes(PredictiveProjectionJson));
            return await resolver.ResolveAsync(doc);
        }

        private static WotProjectionResolver Resolver(
            params (string Href, string Json)[] map)
        {
            var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int ii = 0; ii < map.Length; ii++)
            {
                dictionary[map[ii].Href] = map[ii].Json;
            }
            return new WotProjectionResolver(new MapResolver(dictionary));
        }

        private static JsonElement Property(WotDocument view, string name)
        {
            return view.RootElement.GetProperty("properties").GetProperty(name);
        }

        private static IEnumerable<string> PropertyNames(JsonElement map)
        {
            foreach (JsonProperty member in map.EnumerateObject())
            {
                yield return member.Name;
            }
        }

        private static string Sha256Hex(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
#if NET6_0_OR_GREATER
            byte[] hash = SHA256.HashData(bytes);
#else
            byte[] hash;
            using (var sha = SHA256.Create())
            {
                hash = sha.ComputeHash(bytes);
            }
#endif
            var builder = new StringBuilder(hash.Length * 2);
            for (int ii = 0; ii < hash.Length; ii++)
            {
                builder.Append(hash[ii].ToString("x2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static string DigestProjection(string digest)
        {
            return """
            {
              "@context": [
                "https://www.w3.org/2022/wot/td/v1.1",
                { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
              ],
              "@type": ["Thing", "uav:projection"],
              "id": "urn:view:digest",
              "title": "Digest view",
              "uav:scenario": "http://example.com/scenario/Digest",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "security": "nosec_sc",
              "uav:projects": [
                {
                  "uav:sourceName": "pump",
                  "href": "urn:pump",
                  "type": "application/td+json",
                  "uav:sourceDigest": "$DIGEST$",
                  "uav:selectAll": true
                }
              ]
            }
            """.Replace("$DIGEST$", digest, StringComparison.Ordinal);
        }

        private static string ContextSource(string prefix, string uri)
        {
            return """
            {
              "@context": [
                "https://www.w3.org/2022/wot/td/v1.1",
                { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "$PREFIX$": "$URI$" }
              ],
              "@type": "uav:object",
              "id": "urn:src",
              "title": "Source",
              "security": "nosec_sc",
              "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
              "properties": {
                "value": {
                  "@type": "uav:variable",
                  "title": "Value",
                  "type": "number",
                  "forms": [{ "href": "urn:x", "op": ["readproperty"] }]
                }
              }
            }
            """
                .Replace("$PREFIX$", prefix, StringComparison.Ordinal)
                .Replace("$URI$", uri, StringComparison.Ordinal);
        }

        private static void AssertJsonEqual(string expected, JsonElement actual)
        {
            using JsonDocument expectedDocument = JsonDocument.Parse(expected);
            Assert.That(
                JsonEquals(expectedDocument.RootElement, actual),
                Is.True,
                () =>
                    "Expected:\n" + Canonical(expectedDocument.RootElement) +
                    "\n\nActual:\n" + Canonical(actual));
        }

        private static bool JsonEquals(JsonElement expected, JsonElement actual)
        {
            if (expected.ValueKind != actual.ValueKind)
            {
                return false;
            }
            switch (expected.ValueKind)
            {
                case JsonValueKind.Object:
                    var members = new Dictionary<string, JsonElement>(
                        StringComparer.Ordinal);
                    foreach (JsonProperty member in actual.EnumerateObject())
                    {
                        members[member.Name] = member.Value;
                    }
                    int count = 0;
                    foreach (JsonProperty member in expected.EnumerateObject())
                    {
                        count++;
                        if (!members.TryGetValue(member.Name, out JsonElement value) ||
                            !JsonEquals(member.Value, value))
                        {
                            return false;
                        }
                    }
                    return count == members.Count;
                case JsonValueKind.Array:
                    if (expected.GetArrayLength() != actual.GetArrayLength())
                    {
                        return false;
                    }
                    JsonElement.ArrayEnumerator left = expected.EnumerateArray();
                    JsonElement.ArrayEnumerator right = actual.EnumerateArray();
                    while (left.MoveNext() && right.MoveNext())
                    {
                        if (!JsonEquals(left.Current, right.Current))
                        {
                            return false;
                        }
                    }
                    return true;
                case JsonValueKind.String:
                    return string.Equals(
                        expected.GetString(), actual.GetString(), StringComparison.Ordinal);
                case JsonValueKind.Number:
                    return string.Equals(
                        expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal);
                default:
                    return true;
            }
        }

        private static string Canonical(JsonElement element)
        {
            return JsonSerializer.Serialize(element, s_indented);
        }

        private sealed class MapResolver : IWotThingResolver
        {
            public MapResolver(Dictionary<string, string> map)
            {
                m_map = map;
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                WotResolverResult result = m_map.TryGetValue(reference, out string json)
                    ? WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(json))
                    : WotResolverResult.NotFound;
                return new ValueTask<WotResolverResult>(result);
            }

            private readonly Dictionary<string, string> m_map;
        }

        private static readonly JsonSerializerOptions s_indented =
            new() { WriteIndented = true };
        private static readonly string[] s_identityMembers =
            { "serialNumber", "manufacturer", "manual" };
        private static readonly string[] s_sensorMembers = { "sensor" };
        private static readonly string[] s_nosecOnly = { "nosec_sc" };
        private static readonly string[] s_pumpComboAllOf =
            { "pump_opcua_channel_sc", "pump_opcua_authentication_sc" };

        private const string PredictiveProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "tm": "https://www.w3.org/2019/wot/tm#"
            },
            "../opc-ua-wot-binding.context.jsonld"
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:dev:opcua:view:pump-predictive-maintenance",
          "title": "Pump condition view",
          "description": "A predictive-maintenance projection view.",
          "uav:scenario": "http://example.com/scenario/PredictiveMaintenance",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "pump",
              "href": "./01-opcua-td-pump.jsonld",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:select": [
                { "uav:affordanceKind": "property" }
              ]
            },
            {
              "uav:sourceName": "identity",
              "href": "./06-anchored-paths-and-device-identity.jsonld",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true,
              "uav:namePrefix": "device"
            }
          ],
          "properties": {
            "pumpSpeed": {
              "tm:ref": "./01-opcua-td-pump.jsonld#/properties/pumpSpeed",
              "title": "Pump speed (condition signal)",
              "description": "The speed reading as a condition signal.",
              "uav:semanticId": "http://example.com/ontology/maintenance/ConditionSignal"
            }
          }
        }
        """;

        private const string PredictiveResolvedJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "tm": "https://www.w3.org/2019/wot/tm#",
              "pump": "http://example.com/demo/pump",
              "di": "http://opcfoundation.org/UA/DI/"
            },
            "../opc-ua-wot-binding.context.jsonld"
          ],
          "@type": ["Thing"],
          "id": "urn:dev:opcua:view:pump-predictive-maintenance",
          "title": "Pump condition view",
          "description": "A predictive-maintenance projection view.",
          "uav:scenario": "http://example.com/scenario/PredictiveMaintenance",
          "securityDefinitions": {
            "nosec_sc": { "scheme": "nosec" },
            "pump_opcua_sc": {
              "scheme": "combo",
              "allOf": ["pump_opcua_channel_sc", "pump_opcua_authentication_sc"]
            },
            "pump_opcua_channel_sc": {
              "scheme": "uav:channelsec",
              "uav:securityMode": "SignAndEncrypt",
              "uav:securityPolicy": "Aes256_Sha256_RsaPss"
            },
            "pump_opcua_authentication_sc": {
              "scheme": "uav:authentication",
              "uav:userIdentityToken": "UserName"
            },
            "identity_opcua_channel_sc": {
              "scheme": "uav:channelsec",
              "uav:securityMode": "SignAndEncrypt",
              "uav:securityPolicy": "Aes256_Sha256_RsaPss"
            }
          },
          "security": "nosec_sc",
          "properties": {
            "pumpSpeed": {
              "@type": "uav:variable",
              "title": "Pump speed (condition signal)",
              "description": "The speed reading as a condition signal.",
              "uav:semanticId": "http://example.com/ontology/maintenance/ConditionSignal",
              "uav:browseName": "pump:PumpSpeed",
              "uav:browsePath": "/Objects/pump:Pump/pump:PumpSpeed",
              "type": "number",
              "unit": "rpm",
              "readOnly": true,
              "observable": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=PumpSpeed",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "observeproperty"],
                  "security": ["pump_opcua_sc"]
                }
              ],
              "uav:resolvedFrom": "./01-opcua-td-pump.jsonld#/properties/pumpSpeed"
            },
            "speedSetpoint": {
              "@type": "uav:variable",
              "title": "Speed Setpoint",
              "uav:browseName": "pump:SpeedSetpoint",
              "uav:browsePath": "/Objects/pump:Pump/pump:SpeedSetpoint",
              "type": "number",
              "unit": "rpm",
              "readOnly": false,
              "observable": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=SpeedSetpoint",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "writeproperty", "observeproperty"],
                  "security": ["pump_opcua_sc"]
                }
              ],
              "uav:resolvedFrom": "./01-opcua-td-pump.jsonld#/properties/speedSetpoint"
            },
            "dischargePressure": {
              "@type": "uav:variable",
              "title": "Discharge Pressure",
              "uav:browseName": "pump:DischargePressure",
              "uav:browsePath": "/Objects/pump:Pump/pump:DischargePressure",
              "type": "number",
              "unit": "bar",
              "readOnly": true,
              "observable": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=DischargePressure",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "observeproperty"],
                  "security": ["pump_opcua_sc"]
                }
              ],
              "uav:resolvedFrom": "./01-opcua-td-pump.jsonld#/properties/dischargePressure"
            },
            "motorTemperature": {
              "@type": "uav:variable",
              "title": "Motor Temperature",
              "uav:browseName": "pump:MotorTemperature",
              "uav:browsePath": "/Objects/pump:Pump/pump:MotorTemperature",
              "type": "number",
              "unit": "Cel",
              "readOnly": true,
              "observable": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=MotorTemperature",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "observeproperty"],
                  "security": ["pump_opcua_sc"]
                }
              ],
              "uav:resolvedFrom": "./01-opcua-td-pump.jsonld#/properties/motorTemperature"
            },
            "deviceSerialNumber": {
              "@type": "uav:variable",
              "title": "Serial Number",
              "uav:browseName": "di:SerialNumber",
              "uav:browsePath": "di:Identification/di:SerialNumber",
              "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=Pump07.SerialNumber",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"],
                  "security": ["identity_opcua_channel_sc"]
                }
              ],
              "uav:resolvedFrom": "./06-anchored-paths-and-device-identity.jsonld#/properties/serialNumber"
            },
            "deviceManufacturer": {
              "@type": "uav:variable",
              "title": "Manufacturer",
              "uav:browseName": "di:Manufacturer",
              "uav:browsePath": "di:Identification/di:Manufacturer",
              "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=Pump07.Manufacturer",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"],
                  "security": ["identity_opcua_channel_sc"]
                }
              ],
              "uav:resolvedFrom": "./06-anchored-paths-and-device-identity.jsonld#/properties/manufacturer"
            },
            "deviceManual": {
              "@type": "uav:variable",
              "title": "Device Manual",
              "uav:browseName": "di:DeviceManual",
              "uav:browsePath": "di:Identification/di:DeviceManual",
              "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=Pump07.DeviceManual",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"],
                  "security": ["identity_opcua_channel_sc"]
                }
              ],
              "uav:resolvedFrom": "./06-anchored-paths-and-device-identity.jsonld#/properties/manual"
            }
          }
        }
        """;

        private const string PumpSourceJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "pump": "http://example.com/demo/pump"
            },
            "../opc-ua-wot-binding.context.jsonld"
          ],
          "@type": "uav:object",
          "id": "urn:dev:opcua:pump-01",
          "title": "Pump 01",
          "description": "Pump source.",
          "uav:browseName": "pump:Pump",
          "uav:id": "nsu=http://example.com/demo/pump;s=Pump",
          "securityDefinitions": {
            "opcua_channel_sc": {
              "scheme": "uav:channelsec",
              "uav:securityMode": "SignAndEncrypt",
              "uav:securityPolicy": "Aes256_Sha256_RsaPss"
            },
            "opcua_authentication_sc": {
              "scheme": "uav:authentication",
              "uav:userIdentityToken": "UserName"
            },
            "opcua_sc": {
              "scheme": "combo",
              "allOf": ["opcua_channel_sc", "opcua_authentication_sc"]
            }
          },
          "security": "opcua_sc",
          "base": "opc.tcp://opcuademo.com:4840",
          "properties": {
            "pumpSpeed": {
              "@type": "uav:variable",
              "title": "Pump Speed",
              "uav:browseName": "pump:PumpSpeed",
              "uav:browsePath": "/Objects/pump:Pump/pump:PumpSpeed",
              "type": "number",
              "unit": "rpm",
              "readOnly": true,
              "observable": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=PumpSpeed",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "observeproperty"]
                }
              ]
            },
            "speedSetpoint": {
              "@type": "uav:variable",
              "title": "Speed Setpoint",
              "uav:browseName": "pump:SpeedSetpoint",
              "uav:browsePath": "/Objects/pump:Pump/pump:SpeedSetpoint",
              "type": "number",
              "unit": "rpm",
              "readOnly": false,
              "observable": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=SpeedSetpoint",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "writeproperty", "observeproperty"]
                }
              ]
            },
            "dischargePressure": {
              "@type": "uav:variable",
              "title": "Discharge Pressure",
              "uav:browseName": "pump:DischargePressure",
              "uav:browsePath": "/Objects/pump:Pump/pump:DischargePressure",
              "type": "number",
              "unit": "bar",
              "readOnly": true,
              "observable": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=DischargePressure",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "observeproperty"]
                }
              ]
            },
            "motorTemperature": {
              "@type": "uav:variable",
              "title": "Motor Temperature",
              "uav:browseName": "pump:MotorTemperature",
              "uav:browsePath": "/Objects/pump:Pump/pump:MotorTemperature",
              "type": "number",
              "unit": "Cel",
              "readOnly": true,
              "observable": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=MotorTemperature",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty", "observeproperty"]
                }
              ]
            }
          },
          "actions": {
            "reset": {
              "@type": "uav:method",
              "title": "Reset",
              "uav:browseName": "pump:Reset",
              "uav:browsePath": "/Objects/pump:Pump/pump:Reset",
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=Reset",
                  "contentType": "application/octet-stream",
                  "op": ["invokeaction"]
                }
              ]
            }
          },
          "events": {
            "overTemperature": {
              "@type": "uav:eventType",
              "title": "Over Temperature",
              "uav:browseName": "pump:OverTemperatureEventType",
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=Pump",
                  "contentType": "application/octet-stream",
                  "op": ["subscribeevent"]
                }
              ]
            }
          }
        }
        """;

        private const string IdentitySourceJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "di": "http://opcfoundation.org/UA/DI/",
              "pump": "http://example.com/demo/pump"
            },
            "../opc-ua-wot-binding.context.jsonld"
          ],
          "@type": ["Thing", "uav:object"],
          "id": "urn:dev:opcua:pump-07",
          "title": "Pump 07",
          "description": "Identity source.",
          "uav:browseName": "pump:Pump",
          "uav:id": "nsu=http://example.com/demo/pump;s=Pump07",
          "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
          "securityDefinitions": {
            "opcua_channel_sc": {
              "scheme": "uav:channelsec",
              "uav:securityMode": "SignAndEncrypt",
              "uav:securityPolicy": "Aes256_Sha256_RsaPss"
            }
          },
          "security": "opcua_channel_sc",
          "base": "opc.tcp://opcuademo.com:4840",
          "properties": {
            "serialNumber": {
              "@type": "uav:variable",
              "title": "Serial Number",
              "uav:browseName": "di:SerialNumber",
              "uav:browsePath": "di:Identification/di:SerialNumber",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=Pump07.SerialNumber",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"]
                }
              ]
            },
            "manufacturer": {
              "@type": "uav:variable",
              "title": "Manufacturer",
              "uav:browseName": "di:Manufacturer",
              "uav:browsePath": "di:Identification/di:Manufacturer",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=Pump07.Manufacturer",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"]
                }
              ]
            },
            "manual": {
              "@type": "uav:variable",
              "title": "Device Manual",
              "uav:browseName": "di:DeviceManual",
              "uav:browsePath": "di:Identification/di:DeviceManual",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "/?id=nsu=http://example.com/demo/pump;s=Pump07.DeviceManual",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"]
                }
              ]
            }
          }
        }
        """;

        private const string AssetProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "ua": "http://opcfoundation.org/UA/",
              "tm": "https://www.w3.org/2019/wot/tm#"
            },
            "../opc-ua-wot-binding.context.jsonld"
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:dev:opcua:asset:pump-01",
          "title": "Pump 01 asset",
          "description": "An asset instance projection.",
          "uav:scenario": "http://example.com/scenario/AssetManagement",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "identity",
              "href": "./06-anchored-paths-and-device-identity.jsonld",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "links": [
            {
              "rel": "ua:Organizes",
              "href": "./10-group-process-data.jsonld",
              "uav:refName": "ProcessData",
              "type": "application/td+json"
            },
            {
              "rel": "ua:Organizes",
              "href": "./11-group-condition-data.jsonld",
              "uav:refName": "ConditionData",
              "type": "application/td+json"
            }
          ]
        }
        """;

        private const string AssetResolvedJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "ua": "http://opcfoundation.org/UA/",
              "tm": "https://www.w3.org/2019/wot/tm#",
              "di": "http://opcfoundation.org/UA/DI/",
              "pump": "http://example.com/demo/pump"
            },
            "../opc-ua-wot-binding.context.jsonld"
          ],
          "@type": ["Thing"],
          "id": "urn:dev:opcua:asset:pump-01",
          "title": "Pump 01 asset",
          "description": "An asset instance projection.",
          "uav:scenario": "http://example.com/scenario/AssetManagement",
          "security": "nosec_sc",
          "links": [
            {
              "rel": "ua:Organizes",
              "href": "./10-group-process-data.jsonld",
              "uav:refName": "ProcessData",
              "type": "application/td+json"
            },
            {
              "rel": "ua:Organizes",
              "href": "./11-group-condition-data.jsonld",
              "uav:refName": "ConditionData",
              "type": "application/td+json"
            }
          ],
          "securityDefinitions": {
            "nosec_sc": { "scheme": "nosec" },
            "identity_opcua_channel_sc": {
              "scheme": "uav:channelsec",
              "uav:securityMode": "SignAndEncrypt",
              "uav:securityPolicy": "Aes256_Sha256_RsaPss"
            }
          },
          "properties": {
            "serialNumber": {
              "@type": "uav:variable",
              "title": "Serial Number",
              "uav:browseName": "di:SerialNumber",
              "uav:browsePath": "di:Identification/di:SerialNumber",
              "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=Pump07.SerialNumber",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"],
                  "security": ["identity_opcua_channel_sc"]
                }
              ],
              "uav:resolvedFrom": "./06-anchored-paths-and-device-identity.jsonld#/properties/serialNumber"
            },
            "manufacturer": {
              "@type": "uav:variable",
              "title": "Manufacturer",
              "uav:browseName": "di:Manufacturer",
              "uav:browsePath": "di:Identification/di:Manufacturer",
              "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=Pump07.Manufacturer",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"],
                  "security": ["identity_opcua_channel_sc"]
                }
              ],
              "uav:resolvedFrom": "./06-anchored-paths-and-device-identity.jsonld#/properties/manufacturer"
            },
            "manual": {
              "@type": "uav:variable",
              "title": "Device Manual",
              "uav:browseName": "di:DeviceManual",
              "uav:browsePath": "di:Identification/di:DeviceManual",
              "uav:browsePathAnchor": "nsu=http://example.com/demo/pump;s=Pump07",
              "type": "string",
              "readOnly": true,
              "forms": [
                {
                  "href": "opc.tcp://opcuademo.com:4840/?id=nsu=http://example.com/demo/pump;s=Pump07.DeviceManual",
                  "contentType": "application/octet-stream",
                  "op": ["readproperty"],
                  "security": ["identity_opcua_channel_sc"]
                }
              ],
              "uav:resolvedFrom": "./06-anchored-paths-and-device-identity.jsonld#/properties/manual"
            }
          }
        }
        """;

        private const string MinimalPumpJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:pump",
          "title": "Pump",
          "securityDefinitions": {
            "opcua_channel_sc": {
              "scheme": "uav:channelsec",
              "uav:securityMode": "SignAndEncrypt",
              "uav:securityPolicy": "Aes256_Sha256_RsaPss"
            }
          },
          "security": "opcua_channel_sc",
          "base": "opc.tcp://opcuademo.com:4840",
          "properties": {
            "pumpSpeed": {
              "@type": "uav:variable",
              "title": "Pump Speed",
              "uav:browseName": "pump:PumpSpeed",
              "type": "number",
              "forms": [
                {
                  "href": "/?id=nsu=urn:pump;s=PumpSpeed",
                  "op": ["readproperty"]
                }
              ]
            }
          }
        }
        """;

        private const string FirstWinsProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:firstwins",
          "title": "First wins",
          "uav:scenario": "http://example.com/scenario/FirstWins",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "pump",
              "href": "urn:pump",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ],
          "properties": {
            "pumpSpeed": {
              "tm:ref": "urn:pump#/properties/pumpSpeed",
              "title": "Enumerated wins"
            }
          }
        }
        """;

        private const string TwoTypedPropertiesJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "ex": "http://example.com/ont/"
            }
          ],
          "@type": "uav:object",
          "id": "urn:sensors",
          "title": "Sensors",
          "security": "nosec_sc",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "properties": {
            "sensor": {
              "@type": ["uav:variable", "ex:Sensor"],
              "title": "Sensor",
              "type": "number",
              "forms": [{ "href": "urn:x", "op": ["readproperty"] }]
            },
            "plain": {
              "@type": ["uav:variable"],
              "title": "Plain",
              "type": "number",
              "forms": [{ "href": "urn:y", "op": ["readproperty"] }]
            }
          }
        }
        """;

        private const string TypeTokenProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            {
              "uav": "http://opcfoundation.org/UA/WoT-Binding/",
              "ex": "http://example.com/ont/",
              "tm": "https://www.w3.org/2019/wot/tm#"
            }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:typed",
          "title": "Typed",
          "uav:scenario": "http://example.com/scenario/Typed",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "sensors",
              "href": "urn:sensors",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:select": [
                { "@type": ["uav:variable", "ex:Sensor"] }
              ]
            }
          ]
        }
        """;

        private const string ProjectionRoutedJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:projrouted",
          "title": "Projection routed",
          "uav:scenario": "http://example.com/scenario/ProjectionRouted",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "pump",
              "href": "urn:pump",
              "type": "application/td+json",
              "uav:routing": "projection"
            }
          ],
          "properties": {
            "speed": {
              "tm:ref": "urn:pump#/properties/pumpSpeed",
              "title": "Gateway speed",
              "forms": [
                { "href": "https://gateway.example/pump/speed", "op": ["readproperty"] }
              ]
            }
          }
        }
        """;

        private const string NoSecurityPumpJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:pump",
          "title": "Pump",
          "base": "opc.tcp://opcuademo.com:4840",
          "properties": {
            "pumpSpeed": {
              "@type": "uav:variable",
              "title": "Pump Speed",
              "type": "number",
              "forms": [
                { "href": "/?id=nsu=urn:pump;s=PumpSpeed", "op": ["readproperty"] }
              ]
            }
          }
        }
        """;

        private const string NoSecurityProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:nosec",
          "title": "No security",
          "uav:scenario": "http://example.com/scenario/NoSecurity",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "pump",
              "href": "urn:pump",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        private const string NoBasePumpJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/" }
          ],
          "@type": "uav:object",
          "id": "urn:pump",
          "title": "Pump",
          "security": "nosec_sc",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "properties": {
            "pumpSpeed": {
              "@type": "uav:variable",
              "title": "Pump Speed",
              "type": "number",
              "forms": [
                { "href": "/read/pumpSpeed", "op": ["readproperty"] }
              ]
            }
          }
        }
        """;

        private const string NoBaseProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:nobase",
          "title": "No base",
          "uav:scenario": "http://example.com/scenario/NoBase",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "pump",
              "href": "https://things.example/pump.td.jsonld",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        private const string TwoSourceProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:view:twosource",
          "title": "Two source",
          "uav:scenario": "http://example.com/scenario/TwoSource",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "a",
              "href": "urn:a",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            },
            {
              "uav:sourceName": "b",
              "href": "urn:b",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;

        private const string SelfProjectionJson = """
        {
          "@context": [
            "https://www.w3.org/2022/wot/td/v1.1",
            { "uav": "http://opcfoundation.org/UA/WoT-Binding/", "tm": "https://www.w3.org/2019/wot/tm#" }
          ],
          "@type": ["Thing", "uav:projection"],
          "id": "urn:self",
          "title": "Self",
          "uav:scenario": "http://example.com/scenario/Loop",
          "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
          "security": "nosec_sc",
          "uav:projects": [
            {
              "uav:sourceName": "self",
              "href": "urn:self",
              "type": "application/td+json",
              "uav:routing": "source",
              "uav:selectAll": true
            }
          ]
        }
        """;
    }
}
