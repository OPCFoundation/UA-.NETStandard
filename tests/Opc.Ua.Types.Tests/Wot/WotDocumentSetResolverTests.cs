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
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Verifies resolver-enabled set conversion without changing local precedence or dropping diagnostics.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotDocumentSetResolverTests
    {
        /// <summary>
        /// References already supplied by the set never reach an external resolver.
        /// </summary>
        [Test]
        public async Task SetLocalDocumentWinsOverExternalResolverAsync()
        {
            using var set = new WotDocumentSet("root",
            [
                new WotDocumentSetEntry("root", Document("Root", link: "child")),
                new WotDocumentSetEntry("child", Document("Child"))
            ]);
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, resolver.Object, new WotExternalSchemaResolver()).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAObjectType>().Select(node => node.BrowseName),
                Is.EquivalentTo(s_expectedLocalNames));
            UAObjectType root = result.Value.Items!.OfType<UAObjectType>().Single(node => node.BrowseName == "1:Root");
            Assert.That(root.References!.Any(reference =>
                reference.IsForward && reference.Value == "ns=1;s=Child"), Is.True);
            resolver.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Missing set-local references can be satisfied by the explicitly supplied resolver.
        /// </summary>
        [Test]
        public async Task ExternalResolverSuppliesMissingSetReferenceAsync()
        {
            using var set = new WotDocumentSet("root",
                [new WotDocumentSetEntry("root", Document("Root", link: "external"))]);
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.Setup(value => value.ResolveThingAsync(
                "external", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(
                    Encoding.UTF8.GetBytes("""{"uav:id":"nsu=urn:wot-set;s=External"}""")));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, resolver.Object, new WotExternalSchemaResolver()).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().Single();
            Assert.That(root.References!.Any(reference =>
                reference.IsForward && reference.Value == "ns=1;s=External"), Is.True);
            resolver.Verify(value => value.ResolveThingAsync(
                "external", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        /// <summary>
        /// Schema diagnostics survive both the single-document fast path and the multi-document merge.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExternalSchemaFailureIsPreservedAcrossSetConversionAsync(bool multiple)
        {
            ArrayOf<WotDocumentSetEntry> entries = multiple
                ? [new WotDocumentSetEntry("root", Document("Root", schema: "urn:schema:speed")),
                    new WotDocumentSetEntry("other", Document("Other"))]
                : [new WotDocumentSetEntry("root", Document("Root", schema: "urn:schema:speed"))];
            using var set = new WotDocumentSet("root", entries);
            var schemas = new Mock<IWotSchemaResolver>(MockBehavior.Strict);
            schemas.Setup(value => value.ResolveSchemaAsync(
                "urn:schema:speed", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(
                    Encoding.UTF8.GetBytes("""{"type":"string","uav:mapToType":"i=12"}"""), "application/schema+json"));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, NullWotResolver.Instance, new WotExternalSchemaResolver(schemas.Object)).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ExternalSchemaIncompatible &&
                diagnostic.Severity == WotDiagnosticSeverity.Error), Is.True, Describe(result));
            schemas.Verify(value => value.ResolveSchemaAsync(
                "urn:schema:speed", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }

        /// <summary>
        /// The resolver fallback also serves references of a multi-document set through the shared merge path.
        /// </summary>
        [Test]
        public async Task ExternalResolverSuppliesMissingReferenceInMultiDocumentSetAsync()
        {
            using var set = new WotDocumentSet("root",
            [
                new WotDocumentSetEntry("root", Document("Root", link: "external")),
                new WotDocumentSetEntry("other", Document("Other"))
            ]);
            var resolver = new Mock<IWotThingResolver>(MockBehavior.Strict);
            resolver.Setup(value => value.ResolveThingAsync(
                "external", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(
                    Encoding.UTF8.GetBytes("""{"uav:id":"nsu=urn:wot-set;s=External"}""")));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, resolver.Object, new WotExternalSchemaResolver()).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Describe(result));
            UAObjectType root = result.Value!.Items!.OfType<UAObjectType>().Single(node => node.BrowseName == "1:Root");
            Assert.That(root.References!.Any(reference =>
                reference.IsForward && reference.Value == "ns=1;s=External"), Is.True);
        }

        /// <summary>
        /// The resolver-free entry point and an explicitly empty resolver agree on outcome and diagnostics.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LocalOnlyAndNullResolverEntryPointsAgreeAsync(bool multiple)
        {
            ArrayOf<WotDocumentSetEntry> entries = multiple
                ? [new WotDocumentSetEntry("root", Document("Root", link: "child")),
                    new WotDocumentSetEntry("child", Document("Child"))]
                : [new WotDocumentSetEntry("root", Document("Root"))];
            using var set = new WotDocumentSet("root", entries);

            WotConversionResult<UANodeSet> local = await WotNodeSetConverter.ToNodeSetAsync(set)
                .ConfigureAwait(false);
            WotConversionResult<UANodeSet> explicitNull = await WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, NullWotResolver.Instance, new WotExternalSchemaResolver()).ConfigureAwait(false);

            Assert.That(explicitNull.Success, Is.EqualTo(local.Success), Describe(explicitNull));
            Assert.That(local.Success, Is.True, Describe(local));
            Assert.That(
                explicitNull.Diagnostics.Select(diagnostic => diagnostic.Code),
                Is.EqualTo(local.Diagnostics.Select(diagnostic => diagnostic.Code)));
            Assert.That(Summarize(explicitNull), Is.EqualTo(Summarize(local)));
            Assert.That(Summarize(local), Is.Not.Empty);
            if (multiple)
            {
                Assert.That(Summarize(local).Any(line => line.Contains("ns=1;s=Child", StringComparison.Ordinal)),
                    Is.True);
            }
        }

        /// <summary>
        /// Lists every converted node with its identity and ordered references for output equality checks.
        /// </summary>
        private static string[] Summarize(WotConversionResult<UANodeSet> result)
        {
            if (result.Value?.Items is null)
            {
                return [];
            }
            return [.. result.Value.Items.Select(node =>
                node.NodeId + "|" + node.BrowseName + "|" + string.Join(
                    ",", (node.References ?? []).Select(reference =>
                        reference.ReferenceType + (reference.IsForward ? ">" : "<") + reference.Value)))];
        }

        /// <summary>
        /// Null explicit resolvers are rejected instead of silently falling back to no resolution.
        /// </summary>
        [Test]
        public void ExplicitResolverArgumentsCannotBeNull()
        {
            using var set = new WotDocumentSet("root", [new WotDocumentSetEntry("root", Document("Root"))]);

            Assert.That(() => WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, null!, new WotExternalSchemaResolver()), Throws.ArgumentNullException);
            Assert.That(() => WotNodeSetConverter.ToNodeSetWithResolversAsync(
                set, NullWotResolver.Instance, null!), Throws.ArgumentNullException);
        }

        /// <summary>
        /// Constructs a portable minimal model with one optional link or external schema.
        /// </summary>
        private static WotDocument Document(string name, string? link = null, string? schema = null)
        {
            JsonObject root = JsonNode.Parse("""
                {
                  "@context":[{"uav":"http://opcfoundation.org/UA/WoT-Binding/",
                    "ua":"http://opcfoundation.org/UA/","test":"urn:wot-set",
                    "tm":"https://www.w3.org/2019/wot/tm#"}],
                  "@type":["tm:ThingModel","uav:objectType"],
                  "security":"none",
                  "securityDefinitions":{"none":{"scheme":"nosec"}}
                }
                """)!.AsObject();
            root["title"] = name;
            root["uav:browseName"] = "test:" + name;
            root["uav:id"] = "nsu=urn:wot-set;s=" + name;
            if (link is not null)
            {
                root["links"] = new JsonArray(new JsonObject
                {
                    ["rel"] = "ua:HasComponent",
                    ["href"] = link,
                    ["uav:refId"] = "i=47"
                });
            }
            if (schema is not null)
            {
                root["properties"] = new JsonObject
                {
                    ["Speed"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["uav:mapToType"] = "i=11",
                        ["uav:externalSchema"] = schema
                    }
                };
            }
            return WotDocument.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        /// <summary>
        /// Presents only diagnostics useful when an assertion fails.
        /// </summary>
        private static string Describe(WotConversionResult<UANodeSet> result)
        {
            return string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }

        /// <summary>Exact local type identities retained by the set conversion.</summary>
        private static readonly string[] s_expectedLocalNames = ["1:Root", "1:Child"];
    }
}
