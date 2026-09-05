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

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Exercises the sibling-document half of the WoT Binding Section 5.1.5
    /// local context: the types the other documents of a registry snapshot
    /// project, and the names that must not resolve against them.
    /// </summary>
    [TestFixture]
    public sealed class SnapshotWotNodeResolverTests
    {
        private const string PumpNamespace = "urn:test:pump";

        /// <summary>
        /// A Thing Model projects an ObjectType, so a sibling naming it must
        /// resolve.
        /// </summary>
        [Test]
        public async Task ResolvesTypeProjectedBySiblingThingModelAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank", Tm("Tank", "i=1042")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].NodeId, Is.EqualTo("nsu=urn:test:pump;i=1042"));
            Assert.That(matches[0].NodeClass, Is.EqualTo(WotExpectedNodeClass.ObjectType));
        }

        /// <summary>
        /// A generated type identity must still be indexed by the same name a
        /// binding sees, or authored and generated Thing Models resolve
        /// differently.
        /// </summary>
        [Test]
        public async Task ResolvesGeneratedTypeProjectedBySiblingThingModelAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank", TmWithoutUavId("Tank")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(
                matches[0].NodeId,
                Is.EqualTo(Opc.Ua.Wot.WotPortableIdentity.GenerateNodeId(
                    PumpNamespace,
                    new ArrayOf<Opc.Ua.Wot.WotBrowsePathElement>(
                        new[]
                        {
                            new Opc.Ua.Wot.WotBrowsePathElement(PumpNamespace, "Tank")
                        }))),
                "The index and the conversion derive one generated identity, by the " +
                "Annex G.1 formula, from the same two inputs.");
            Assert.That(matches[0].NodeClass, Is.EqualTo(WotExpectedNodeClass.ObjectType));
        }

        /// <summary>
        /// A Thing Description projects an instance, never a type, so it must
        /// not be offered as a type-binding target.
        /// </summary>
        [Test]
        public async Task DoesNotResolveTypeToSiblingThingDescriptionAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingDescription, "tank", Td("Tank", "i=1042")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Is.Empty);
            Assert.That(
                await resolver.HoldsNamespaceAsync(PumpNamespace).ConfigureAwait(false),
                Is.False);
        }

        /// <summary>
        /// Section 5.2.1 tells a binding from an annotation by namespace, so a
        /// namespace a sibling projects into must be reported as held.
        /// </summary>
        [Test]
        public async Task HoldsNamespaceOnlyForProjectedNamespacesAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank", Tm("Tank", "i=1042")))
                .ConfigureAwait(false);

            Assert.That(
                await resolver.HoldsNamespaceAsync(PumpNamespace).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await resolver.HoldsNamespaceAsync("urn:test:other").ConfigureAwait(false),
                Is.False);
        }

        /// <summary>
        /// Every projected sibling type is an ObjectType, so a caller that
        /// requires a VariableType can never be satisfied here.
        /// </summary>
        [Test]
        public async Task DoesNotResolveWhenVariableTypeIsRequiredAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank", Tm("Tank", "i=1042")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.VariableType)
                .ConfigureAwait(false);

            Assert.That(matches, Is.Empty);
        }

        /// <summary>
        /// An ExpandedNodeId is definitive and resolves to the one sibling that
        /// projects it.
        /// </summary>
        [Test]
        public async Task ResolvesSiblingTypeByNodeIdAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank", Tm("Tank", "i=1042")))
                .ConfigureAwait(false);

            WotResolvedNode? match = await resolver
                .ResolveByNodeIdAsync("nsu=urn:test:pump;i=1042").ConfigureAwait(false);

            Assert.That(match, Is.Not.Null);
            Assert.That(match!.Value.NodeClass, Is.EqualTo(WotExpectedNodeClass.ObjectType));
            Assert.That(
                await resolver.ResolveByNodeIdAsync("nsu=urn:test:pump;i=7777")
                    .ConfigureAwait(false),
                Is.Null);
        }

        /// <summary>
        /// Two siblings projecting the same qualified name make it ambiguous.
        /// Both are reported so the caller can say so rather than pick one.
        /// </summary>
        [Test]
        public async Task ReportsEverySiblingMatchSoAmbiguityIsVisibleAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank-a", Tm("Tank", "i=1042")),
                (WoTDocumentKindEnum.ThingModel, "tank-b", Tm("Tank", "i=2042")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// A sibling that cannot be parsed contributes no name rather than
        /// failing every other lookup. Its own conversion reports why.
        /// </summary>
        [Test]
        public async Task SkipsUnparseableSiblingAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "broken", TestMaterialization.InvalidJson()),
                (WoTDocumentKindEnum.ThingModel, "tank", Tm("Tank", "i=1042")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The registry's Kind decides what is indexed, not the document's own
        /// content. A resource registered as a Thing Description is never
        /// indexed even when its bytes claim to be a Thing Model, so a party
        /// who can only submit Thing Descriptions cannot plant a type for
        /// another document to bind to.
        /// </summary>
        [Test]
        public async Task DoesNotIndexAThingDescriptionWhoseContentClaimsToBeAThingModelAsync()
        {
            var byDigest = new Dictionary<string, ByteString>(System.StringComparer.Ordinal);
            using var service = new WotRegistryService();

            // Registered as a Thing Description, but the bytes are a Thing
            // Model. Its content is present, so only the Kind check can
            // exclude it.
            ByteString disguised = ByteString.From(Tm("Tank", "i=1042"));
            byDigest[WotContentDigest.ToHex(WotContentDigest.Compute(disguised))] = disguised;
            await service.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions,
                ResourceId = "disguised",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = disguised
            }).ConfigureAwait(false);

            var resolver = new SnapshotWotNodeResolver(service.Current, byDigest);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "Tank", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Is.Empty,
                "A Thing Description must never be indexed, whatever its content claims.");
        }

        /// <summary>
        /// The index is bounded by the same document budget the rest of a
        /// conversion runs under, so a large registry cannot turn one
        /// conversion into unbounded parsing work.
        /// </summary>
        [Test]
        public async Task StopsIndexingAtTheDocumentBudgetAsync()
        {
            var byDigest = new Dictionary<string, ByteString>(System.StringComparer.Ordinal);
            using var service = new WotRegistryService();
            for (int ii = 0; ii < 6; ii++)
            {
                ByteString bytes = ByteString.From(Tm("Tank" + ii, "i=" + (1000 + ii)));
                byDigest[WotContentDigest.ToHex(WotContentDigest.Compute(bytes))] = bytes;
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = WotRegistryGroups.ThingModels,
                    ResourceId = "tank" + ii,
                    Kind = WoTDocumentKindEnum.ThingModel,
                    Content = bytes
                }).ConfigureAwait(false);
            }

            var resolver = new SnapshotWotNodeResolver(
                service.Current,
                byDigest,
                new WotNodeSetConverterOptions { MaxResolverDocuments = 2 });

            int resolved = 0;
            for (int ii = 0; ii < 6; ii++)
            {
                ArrayOf<WotResolvedNode> matches = await resolver
                    .ResolveByBrowseNameAsync(
                        PumpNamespace, "Tank" + ii, WotExpectedNodeClass.ObjectType)
                    .ConfigureAwait(false);
                resolved += matches.Count;
            }

            Assert.That(resolved, Is.EqualTo(2),
                "Indexing must stop at the configured document budget.");
        }

        /// <summary>
        /// A registry holds a companion model's ReferenceTypes as documents of
        /// their own, so a sibling naming one resolves against the snapshot
        /// before any AddressSpace is consulted (Section 5.1.5).
        /// </summary>
        [Test]
        public async Task ResolvesReferenceTypeProjectedBySiblingAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "flows", ReferenceTypeTm(
                    "FlowsInto", "i=5001", "FedFrom", symmetric: false)))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedReferenceType> forward = await resolver
                .ResolveReferenceTypesAsync(PumpNamespace, "FlowsInto")
                .ConfigureAwait(false);
            ArrayOf<WotResolvedReferenceType> inverse = await resolver
                .ResolveReferenceTypesAsync(PumpNamespace, "FedFrom")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(forward, Has.Count.EqualTo(1));
                Assert.That(forward[0].NodeId, Is.EqualTo("nsu=urn:test:pump;i=5001"));
                Assert.That(forward[0].IsForward, Is.True);
                Assert.That(inverse, Has.Count.EqualTo(1));
                Assert.That(inverse[0].NodeId, Is.EqualTo("nsu=urn:test:pump;i=5001"));
                Assert.That(
                    inverse[0].IsForward,
                    Is.False,
                    "The InverseName reads the same reference backwards.");
            });
        }

        /// <summary>
        /// A symmetric ReferenceType has one name for both directions, so it is
        /// offered once. A second entry would make every use of the name
        /// ambiguous.
        /// </summary>
        [Test]
        public async Task OffersASymmetricSiblingReferenceTypeOnceAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "connected", ReferenceTypeTm(
                    "ConnectedTo", "i=5002", inverseName: null, symmetric: true)))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(PumpNamespace, "ConnectedTo")
                .ConfigureAwait(false);

            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].IsForward, Is.True);
        }

        /// <summary>
        /// An ObjectType sibling is not a relation, so its name resolves to no
        /// ReferenceType.
        /// </summary>
        [Test]
        public async Task DoesNotOfferAnObjectTypeSiblingAsARelationAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "tank", Tm("Tank", "i=1042")))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedReferenceType> matches = await resolver
                .ResolveReferenceTypesAsync(PumpNamespace, "Tank")
                .ConfigureAwait(false);

            Assert.That(matches, Is.Empty);
        }

        /// <summary>
        /// A sibling ReferenceType is a ReferenceType, not a type-binding
        /// target, so Section 5.2.1 must not resolve an ObjectType binding to
        /// it.
        /// </summary>
        [Test]
        public async Task DoesNotOfferAReferenceTypeSiblingAsATypeBindingTargetAsync()
        {
            SnapshotWotNodeResolver resolver = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "flows", ReferenceTypeTm(
                    "FlowsInto", "i=5001", "FedFrom", symmetric: false)))
                .ConfigureAwait(false);

            ArrayOf<WotResolvedNode> matches = await resolver
                .ResolveByBrowseNameAsync(
                    PumpNamespace, "FlowsInto", WotExpectedNodeClass.ObjectType)
                .ConfigureAwait(false);

            Assert.That(matches, Is.Empty);
        }

        /// <summary>
        /// Section 5.1.5 consults the siblings of a conversion before a loaded
        /// AddressSpace, so a name both hold resolves to the sibling's
        /// ReferenceType.
        /// </summary>
        [Test]
        public async Task SiblingsSettleARelationBeforeTheAddressSpaceAsync()
        {
            SnapshotWotNodeResolver siblings = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "flows", ReferenceTypeTm(
                    "FlowsInto", "i=5001", "FedFrom", symmetric: false)))
                .ConfigureAwait(false);
            SnapshotWotNodeResolver addressSpace = await ResolverAsync(
                (WoTDocumentKindEnum.ThingModel, "flows", ReferenceTypeTm(
                    "FlowsInto", "i=9999", "FedFrom", symmetric: false)))
                .ConfigureAwait(false);

            var composite = new WotCompositeNodeResolver(siblings, addressSpace);

            ArrayOf<WotResolvedReferenceType> matches = await composite
                .ResolveReferenceTypesAsync(PumpNamespace, "FlowsInto")
                .ConfigureAwait(false);

            Assert.That(matches, Has.Count.EqualTo(1));
            Assert.That(matches[0].NodeId, Is.EqualTo("nsu=urn:test:pump;i=5001"));
        }

        /// <summary>
        /// Builds a resolver over a snapshot holding the supplied documents.
        /// </summary>
        private static async Task<SnapshotWotNodeResolver> ResolverAsync(
            params (WoTDocumentKindEnum Kind, string Id, byte[] Content)[] docs)
        {
            var byDigest = new Dictionary<string, ByteString>(System.StringComparer.Ordinal);
            using var service = new WotRegistryService();
            foreach ((WoTDocumentKindEnum kind, string id, byte[] content) in docs)
            {
                ByteString bytes = ByteString.From(content);
                byDigest[WotContentDigest.ToHex(WotContentDigest.Compute(bytes))] = bytes;
                await service.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = kind == WoTDocumentKindEnum.ThingModel
                        ? WotRegistryGroups.ThingModels
                        : WotRegistryGroups.ThingDescriptions,
                    ResourceId = id,
                    Kind = kind,
                    Content = bytes
                }).ConfigureAwait(false);
            }
            return new SnapshotWotNodeResolver(service.Current, byDigest);
        }

        private static byte[] Tm(string browseName, string identifier)
        {
            return Document("tm:ThingModel", browseName, identifier);
        }

        /// <summary>
        /// A Thing Model projecting a ReferenceType, with the second name and
        /// the Symmetric flag OPC 10000-3 gives one.
        /// </summary>
        private static byte[] ReferenceTypeTm(
            string browseName,
            string identifier,
            string? inverseName,
            bool symmetric)
        {
            var extra = new StringBuilder();
            if (inverseName is not null && !symmetric)
            {
                extra.Append("\"uav:inverseName\":\"").Append(inverseName).Append("\",");
            }
            if (symmetric)
            {
                extra.Append("\"uav:symmetric\":true,");
            }
            return Document(
                "tm:ThingModel",
                browseName,
                identifier,
                includeUavId: true,
                extraTypeToken: "uav:referenceType",
                extraMembers: extra.ToString());
        }

        private static byte[] TmWithoutUavId(string browseName)
        {
            return Document("tm:ThingModel", browseName, string.Empty, includeUavId: false);
        }

        private static byte[] Td(string browseName, string identifier)
        {
            return Document("uav:object", browseName, identifier);
        }

        private static byte[] Document(
            string typeToken,
            string browseName,
            string identifier,
            bool includeUavId = true,
            string? extraTypeToken = null,
            string? extraMembers = null)
        {
            var builder = new StringBuilder();
            builder.Append("{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",")
                .Append("{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\",")
                .Append("\"ua\":\"http://opcfoundation.org/UA/\",")
                .Append("\"pump\":\"").Append(PumpNamespace).Append("\"}],")
                .Append("\"@type\":[\"Thing\",\"").Append(typeToken).Append('"');
            if (extraTypeToken is not null)
            {
                builder.Append(",\"").Append(extraTypeToken).Append('"');
            }
            builder.Append("],")
                .Append("\"id\":\"").Append(PumpNamespace).Append("\",")
                .Append("\"title\":\"").Append(browseName).Append("\",")
                .Append("\"uav:browseName\":\"pump:").Append(browseName).Append("\",");
            if (includeUavId)
            {
                builder.Append("\"uav:id\":\"nsu=").Append(PumpNamespace).Append(';')
                    .Append(identifier).Append("\",");
            }
            if (!string.IsNullOrEmpty(extraMembers))
            {
                builder.Append(extraMembers);
            }
            builder
                .Append("\"security\":\"nosec_sc\",")
                .Append("\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}},")
                .Append("\"properties\":{\"value\":{\"type\":\"number\",")
                .Append("\"forms\":[{\"href\":\"x\"}]}}}");
            return Encoding.UTF8.GetBytes(builder.ToString());
        }
    }
}
