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

#nullable enable

using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// WoT Binding Section 5.1.4: a relative <c>uav:browsePath</c> resolves
    /// against the nearest enclosing <c>uav:browsePathAnchor</c> and, failing
    /// that, the nearest enclosing <c>uav:id</c>.
    /// </summary>
    /// <remarks>
    /// The two sources are ordered by kind and not by depth, which is the part
    /// a check written as "the root states an anchor, or this element does"
    /// gets wrong in both directions: it refuses a document that identifies the
    /// Node it describes, and it accepts nothing an intermediate scope states.
    /// The clause is also explicit that a path with neither anchor is
    /// unresolved and shall not fall back to the AddressSpace root.
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotBrowsePathAnchorTests
    {
        private const string Pump = "http://example.com/demo/pump";
        private const string RootId = "nsu=http://example.com/demo/pump;s=Pump07";
        private const string NestedId = "nsu=http://example.com/demo/pump;s=Pump07.Motor";

        /// <summary>
        /// The published anchored-paths example states the same value twice -
        /// once as the identity of the Node it describes and once as the anchor
        /// its relative paths resolve against. The identity alone is what
        /// Section 5.1.4 falls back to, so removing the anchor changes nothing
        /// about whether the paths resolve.
        /// </summary>
        [Test]
        public void TheAnchoredExampleResolvesOnItsIdentityAlone()
        {
            Assert.That(
                WotSpecExampleResolver.TryReadExample(
                    "06-anchored-paths-and-device-identity.jsonld", out byte[] bytes),
                Is.True);
            string json = Encoding.UTF8.GetString(bytes);
            Assert.That(
                json,
                Does.Contain("\"uav:browsePathAnchor\": \"" + RootId + "\""),
                "The example is the one that states both terms.");
            string withoutAnchor = json.Replace(
                "\"uav:browsePathAnchor\": \"" + RootId + "\",",
                string.Empty,
                StringComparison.Ordinal);

            using WotDocument document = WotDocument.Parse(
                Encoding.UTF8.GetBytes(withoutAnchor));
            WotConversionResult<UANodeSet> result =
                WotNodeSetConverter.ToNodeSetResult(document);

            Assert.Multiple(() =>
            {
                Assert.That(
                    withoutAnchor,
                    Does.Not.Contain("uav:browsePathAnchor"),
                    "The anchor really was removed.");
                Assert.That(withoutAnchor, Does.Contain("\"uav:id\": \"" + RootId + "\""));
                Assert.That(Unanchored(result), Is.Empty, Reasons(result));
            });
        }

        /// <summary>
        /// A document that identifies the Node it describes anchors its own
        /// relative paths; one that identifies nothing anchors none of them.
        /// </summary>
        [TestCase("\"uav:id\":\"" + RootId + "\",", true, TestName = "RootIdentity")]
        [TestCase("\"uav:browsePathAnchor\":\"" + RootId + "\",", true, TestName = "RootAnchor")]
        [TestCase("", false, TestName = "NeitherIsStated")]
        public void ARelativePathResolvesOnlyWhereItsScopeStatesAStartingNode(
            string rootTerms, bool resolves)
        {
            WotConversionResult<UANodeSet> result = Convert(
                rootTerms,
                "\"Speed\":{\"type\":\"number\",\"uav:browsePath\":\"pump:Speed\"}");

            Assert.That(Unanchored(result), resolves ? Is.Empty : Is.Not.Empty, Reasons(result));
        }

        /// <summary>
        /// An absolute path starts at the AddressSpace root wherever it is
        /// written, so it never needs an anchor and a document that states none
        /// still converts.
        /// </summary>
        [Test]
        public void AnAbsolutePathNeedsNoAnchor()
        {
            WotConversionResult<UANodeSet> result = Convert(
                string.Empty,
                "\"Speed\":{\"type\":\"number\",\"uav:browsePath\":\"/Objects/pump:Speed\"}");

            Assert.That(Unanchored(result), Is.Empty, Reasons(result));
        }

        /// <summary>
        /// The anchor is the nearest <em>enclosing</em> one, so a scope between
        /// the root and the path anchors it - which is what lets a nested
        /// definition state the short path from the Node it describes.
        /// </summary>
        [TestCase("\"uav:browsePathAnchor\":\"" + NestedId + "\",", TestName = "EnclosingAnchor")]
        [TestCase("\"uav:id\":\"" + NestedId + "\",", TestName = "EnclosingIdentity")]
        public void AnEnclosingScopeAnchorsThePathsBeneathIt(string affordanceTerms)
        {
            WotConversionResult<UANodeSet> result = Convert(
                string.Empty,
                "\"Speed\":{\"type\":\"object\"," + affordanceTerms +
                "\"properties\":{\"Rpm\":{\"type\":\"number\"," +
                "\"uav:browsePath\":\"pump:Rpm\"}}}");

            Assert.That(Unanchored(result), Is.Empty, Reasons(result));
        }

        /// <summary>
        /// An anchor stated closer to the path replaces the one it inherited,
        /// and an anchor outranks an identity wherever the two are stated,
        /// because Section 5.1.4 orders the two sources by kind rather than by
        /// depth.
        /// </summary>
        [Test]
        public void TheNearestAnchorOfEachKindWins()
        {
            var scope = default(WotAnchorScope);

            Assert.Multiple(() =>
            {
                Assert.That(scope.IsAnchored, Is.False);
                Assert.That(scope.Effective, Is.Null);

                WotAnchorScope root = scope.Enter(anchor: null, identity: RootId);
                Assert.That(root.Effective, Is.EqualTo(RootId));

                WotAnchorScope inner = root.Enter(anchor: NestedId, identity: null);
                Assert.That(
                    inner.Effective,
                    Is.EqualTo(NestedId),
                    "An anchor outranks an inherited identity.");

                WotAnchorScope anchoredRoot = scope.Enter(anchor: RootId, identity: null);
                Assert.That(
                    anchoredRoot.Enter(anchor: null, identity: NestedId).Effective,
                    Is.EqualTo(RootId),
                    "An inherited anchor outranks an identity stated closer in.");
                Assert.That(
                    anchoredRoot.Enter(anchor: NestedId, identity: NestedId).Effective,
                    Is.EqualTo(NestedId),
                    "An anchor stated closer in replaces the one inherited.");
                Assert.That(
                    root.Enter(anchor: string.Empty, identity: string.Empty).Effective,
                    Is.EqualTo(RootId),
                    "A term stated as the empty string states nothing.");
            });
        }

        /// <summary>
        /// A relative path inside a scope that states neither term is
        /// unresolved, and the report says so rather than resolving it from the
        /// AddressSpace root - which would name a different Node.
        /// </summary>
        [Test]
        public void ATrulyUnanchoredPathIsReported()
        {
            WotConversionResult<UANodeSet> result = Convert(
                string.Empty,
                "\"Speed\":{\"type\":\"object\"," +
                "\"properties\":{\"Rpm\":{\"type\":\"number\"," +
                "\"uav:browsePath\":\"pump:Rpm\"}}}");

            Assert.Multiple(() =>
            {
                Assert.That(Unanchored(result), Has.Count.EqualTo(1));
                Assert.That(
                    Unanchored(result)[0].Message,
                    Does.Contain("uav:browsePathAnchor or an enclosing uav:id"));
                Assert.That(
                    Unanchored(result)[0].Severity,
                    Is.EqualTo(WotDiagnosticSeverity.Error));
            });
        }

        /// <summary>
        /// A term states a value only where it is a non-empty string: anything
        /// else - absent, a number, an object, the empty string - says nothing,
        /// and a scope that reads it as a statement would anchor a path against
        /// a Node no one named.
        /// </summary>
        [TestCase("{\"uav:id\":\"" + RootId + "\"}", RootId, TestName = "Stated")]
        [TestCase("{}", null, TestName = "Absent")]
        [TestCase("{\"uav:id\":\"\"}", null, TestName = "Empty")]
        [TestCase("{\"uav:id\":7}", null, TestName = "NotAString")]
        [TestCase("{\"uav:id\":{}}", null, TestName = "AnObject")]
        [TestCase("7", null, TestName = "TheCarrierIsNotAnObject")]
        public void ATermStatesAValueOnlyWhereItIsANonEmptyString(
            string json, string? expected)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonNode? node = JsonNode.Parse(json);

            Assert.Multiple(() =>
            {
                Assert.That(
                    WotAnchorScope.ReadTerm(
                        parsed.RootElement, WotAnchorScope.IdentityTerm),
                    Is.EqualTo(expected));
                Assert.That(
                    node is JsonObject carrier
                        ? WotAnchorScope.ReadTerm(carrier, WotAnchorScope.IdentityTerm)
                        : null,
                    Is.EqualTo(expected),
                    "The mutable form reads the same rule.");
                Assert.That(
                    WotAnchorScope.None.Enter(parsed.RootElement).Effective,
                    Is.EqualTo(expected),
                    "Entering a scope reads the terms the same way.");
            });
        }

        private static System.Collections.Generic.List<WotDiagnostic> Unanchored(
            WotConversionResult<UANodeSet> result)
        {
            return [.. result.Diagnostics.Where(d =>
                d.Code == WotDiagnosticCode.NonPortableIdentity &&
                d.Message.Contains("no starting Node", StringComparison.Ordinal))];
        }

        private static string Reasons(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(d => d.Message));
        }

        private static WotConversionResult<UANodeSet> Convert(
            string rootTerms, string properties)
        {
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"pump\":\"" + Pump + "\"}]," +
                "\"@type\":\"uav:object\",\"id\":\"urn:dev:opcua:pump-07\"," +
                "\"title\":\"Pump\"," + rootTerms +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"properties\":{" + properties + "}}"));
            return WotNodeSetConverter.ToNodeSetResult(document);
        }
    }
}
