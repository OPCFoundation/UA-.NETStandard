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

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// WoT Binding Section 5.2.1 names a type in either or both of two forms
    /// and defines a table of outcomes for the combinations. Section 5.1.5
    /// resolves both against a local context. These pin the table.
    /// </summary>
    [TestFixture]
    public sealed class WotTypeBindingResolutionTests
    {
        private const string PumpNamespace = "urn:test:pump";
        private const string TankTypeId = "nsu=urn:test:pump;i=1042";
        private const string OtherTypeId = "nsu=urn:test:pump;i=9999";

        [Test]
        public async Task ANameThatResolvesUniquelyBindsToThatTypeAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
        }

        /// <summary>
        /// An unresolved binding fails rather than falling back, because a
        /// silently mistyped node is worse than a reported failure.
        /// </summary>
        [Test]
        public async Task ANameInAHeldNamespaceThatResolvesToNothingIsReportedAsync()
        {
            var resolver = new StubResolver(PumpNamespace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding),
                Is.True);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType),
                "The node must not be bound to a guess.");
        }

        /// <summary>
        /// A binding is told from an annotation by namespace, not by whether
        /// the lookup succeeds, so a name in a namespace nothing holds stays an
        /// annotation and is not reported.
        /// </summary>
        [Test]
        public async Task ANameInAnUnheldNamespaceIsAnAnnotationNotABindingAsync()
        {
            var resolver = new StubResolver(PumpNamespace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"saref:TemperatureSensor\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding),
                Is.False,
                "A namespace the local context does not hold cannot have been meant as a type.");
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        [Test]
        public async Task AnAmbiguousNameWithNothingToSettleItIsInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId), Node(OtherTypeId)] }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
        }

        /// <summary>
        /// The link settles an ambiguous name, exactly as `uav:refId` settles
        /// an ambiguous `rel`.
        /// </summary>
        [Test]
        public async Task TheLinkSettlesAnAmbiguousNameAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId), Node(OtherTypeId)] },
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.False);
        }

        /// <summary>
        /// A name that resolves to nothing while the identifier resolves is a
        /// mistake in the name, not a shorthand for the identifier.
        /// </summary>
        [Test]
        public async Task ANameResolvingToNothingWhileTheLinkResolvesIsInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        [Test]
        public async Task TwoFormsResolvingToDifferentNodesAreInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(OtherTypeId)] },
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
        }

        [Test]
        public async Task TwoFormsAgreeingBindToThatTypeAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] },
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
        }

        /// <summary>
        /// A Thing Description projects an Object, so a VariableType is the
        /// wrong NodeClass for it.
        /// </summary>
        [Test]
        public async Task AResolvedTypeOfTheWrongNodeClassIsInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName =
                {
                    ["TankType"] =
                        [new WotResolvedNode(TankTypeId, WotExpectedNodeClass.VariableType)]
                }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
        }

        /// <summary>
        /// The sibling documents of the conversion are consulted before the
        /// AddressSpace, so a set of documents authored together resolves to
        /// itself.
        /// </summary>
        [Test]
        public async Task SiblingsWinOverTheAddressSpaceAsync()
        {
            var siblings = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] }
            };
            var addressSpace = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(OtherTypeId)] }
            };
            var composite = new WotCompositeNodeResolver(siblings, addressSpace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, composite).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"),
                "The sibling documents are the first part of the local context.");
        }

        [Test]
        public async Task TheAddressSpaceIsTheFallbackAsync()
        {
            var siblings = new StubResolver(PumpNamespace);
            var addressSpace = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(OtherTypeId)] }
            };
            var composite = new WotCompositeNodeResolver(siblings, addressSpace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, composite).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=9999"));
        }

        private static WotResolvedNode Node(string nodeId)
        {
            return new WotResolvedNode(nodeId, WotExpectedNodeClass.ObjectType);
        }

        private static string TypeDefinitionOf(UANodeSet nodeSet)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObject);
            return root.References!.First(r =>
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal)).Value!;
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertAsync(
            string typeToken,
            string? link,
            IWotNodeResolver resolver)
        {
            string links = link is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"" + link + "\"}]";

            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"saref\":\"https://saref.etsi.org/core/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"," + typeToken + "]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                links + "}");

            using WotDocument document = WotDocument.Parse(json);
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);
        }

        /// <summary>
        /// Stands in for one part of the Section 5.1.5 local context.
        /// </summary>
        private sealed class StubResolver(string heldNamespace) : IWotNodeResolver
        {
            public Dictionary<string, List<WotResolvedNode>> ByName { get; } = [];

            public Dictionary<string, WotResolvedNode> ByNodeId { get; } = [];

            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, heldNamespace, StringComparison.Ordinal));
            }

            public ValueTask<IReadOnlyList<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                IReadOnlyList<WotResolvedNode> matches =
                    ByName.TryGetValue(browseName, out List<WotResolvedNode>? found)
                        ? found
                        : [];
                return new ValueTask<IReadOnlyList<WotResolvedNode>>(matches);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>(
                    ByNodeId.TryGetValue(expandedNodeId, out WotResolvedNode found)
                        ? found
                        : null);
            }
        }
    }
}
