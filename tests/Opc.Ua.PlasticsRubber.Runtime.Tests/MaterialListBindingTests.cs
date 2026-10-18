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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.PlasticsRubber.Server;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    /// <summary>
    /// Verifies native catalogue topology, typed wire methods and authoritative replay.
    /// </summary>
    [TestFixture]
    public sealed class MaterialListBindingTests
    {
        /// <summary>
        /// Generated add/remove methods publish actual MaterialType children with stable identity and units.
        /// </summary>
        [Test]
        public async Task GeneratedWireMethodsPublishNativeMaterialsAsync()
        {
            await using var fixture = new CommonRuntimeServerFixture();
            var provider = new MaterialProvider();
            MaterialListBinding? binding = null;
            await fixture.StartAsync(async server =>
            {
                MaterialListState node = CreateList(server);
                binding = new MaterialListBinding(server.Manager, node, provider, AllowAccess());
                await server.RegisterAsync(node, binding).ConfigureAwait(false);
                await binding.PublishAsync(provider.Snapshot).ConfigureAwait(false);
            }).ConfigureAwait(false);
            MaterialListState list = binding!.Node;
            var client = new MaterialListTypeClient(fixture.Session, list.NodeId,
                fixture.Session.MessageContext.Telemetry);

            await client.AddMaterialAsync("polymer-a", new LocalizedText("Polymer A"), 950).ConfigureAwait(false);
            MaterialState material = RequireMaterial(fixture, list, 1);
            NodeId identity = material.NodeId;
            DataValue density = await fixture.Session.ReadValueAsync(material.Density!.NodeId).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(material.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    ObjectTypeIds.MaterialType, fixture.Session.NamespaceUris)));
                Assert.That(density.WrappedValue.TryGetValue(out double value), Is.True);
                Assert.That(value, Is.EqualTo(950));
                Assert.That(density.SourceTimestamp, Is.EqualTo(provider.Snapshot.SourceTimestamp));
                Assert.That(material.Density.EngineeringUnits!.Value.UnitId,
                    Is.EqualTo(list.DensityUnit!.Value.UnitId));
                Assert.That(list.NodeVersion!.Value, Is.EqualTo("1"));
                Assert.That((provider.LastContext as ISessionSystemContext)?.SessionId,
                    Is.EqualTo(fixture.Session.SessionId));
            });

            await binding.PublishAsync(provider.Snapshot).ConfigureAwait(false);
            Assert.That(RequireMaterial(fixture, list, 1).NodeId, Is.EqualTo(identity));
            await client.RemoveMaterialByIdAsync("polymer-a").ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode(identity), Is.Null);
            Assert.That(list.NodeVersion!.Value, Is.EqualTo("2"));
        }

        /// <summary>
        /// Bad density and in-use removal fail without changing the committed catalogue.
        /// </summary>
        [Test]
        public async Task InvalidOrInUseMutationPreservesCatalogueAsync()
        {
            await using var fixture = new CommonRuntimeServerFixture();
            var provider = new MaterialProvider();
            MaterialListBinding? binding = null;
            await fixture.StartAsync(async server =>
            {
                MaterialListState node = CreateList(server);
                binding = new MaterialListBinding(server.Manager, node, provider, AllowAccess());
                await server.RegisterAsync(node, binding).ConfigureAwait(false);
            }).ConfigureAwait(false);
            var client = new MaterialListTypeClient(
                fixture.Session, binding!.Node.NodeId, fixture.Session.MessageContext.Telemetry);
            ServiceResultException? error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.AddMaterialAsync("invalid", new LocalizedText("Invalid"), double.NaN)
                    .ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(provider.Snapshot.Materials.IsEmpty, Is.True);

            await client.AddMaterialAsync("in-use", new LocalizedText("In use"), 1000).ConfigureAwait(false);
            provider.InUse = "in-use";
            error = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.RemoveMaterialByIdAsync("in-use").ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(RequireMaterial(fixture, binding.Node, 1).Id!.Value, Is.EqualTo("in-use"));
            Assert.That(binding.Node.NodeVersion!.Value, Is.EqualTo("1"));
        }

        /// <summary>
        /// Stale revisions and conflicting replay fail before changing live values or NodeVersion.
        /// </summary>
        [Test]
        public async Task StaleAndConflictingSnapshotsAreRejectedAsync()
        {
            await using var fixture = new CommonRuntimeServerFixture();
            var provider = new MaterialProvider();
            MaterialListBinding? binding = null;
            await fixture.StartAsync(async server =>
            {
                MaterialListState node = CreateList(server);
                binding = new MaterialListBinding(server.Manager, node, provider, AllowAccess());
                await server.RegisterAsync(node, binding).ConfigureAwait(false);
                await binding.PublishAsync(new MaterialListSnapshot(5, Time(5),
                    [new MaterialListEntry(1, "polymer", new LocalizedText("Polymer"), 900)])).ConfigureAwait(false);
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await binding!.PublishAsync(new MaterialListSnapshot(4, Time(4), [])).ConfigureAwait(false));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await binding!.PublishAsync(new MaterialListSnapshot(5, Time(5),
                    [new MaterialListEntry(1, "polymer", new LocalizedText("Polymer"), 901)])).ConfigureAwait(false));
            Assert.That(RequireMaterial(fixture, binding!.Node, 1).Density!.Value, Is.EqualTo(900));
            Assert.That(binding.Node.NodeVersion!.Value, Is.EqualTo("5"));
        }

        /// <summary>
        /// Material slots are bounded stable identities, not arbitrary collection indexes.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(1000)]
        public void InvalidSlotIsRejected(int slot)
        {
            var snapshot = new MaterialListSnapshot(1, Time(1),
                [new MaterialListEntry(slot, "id", new LocalizedText("Material"), 900)]);
            Assert.Throws<ArgumentException>(() => snapshot.Validate());
        }

        /// <summary>
        /// Duplicate identifiers may not appear in different slots.
        /// </summary>
        [Test]
        public void DuplicateIdentifierIsRejected()
        {
            var snapshot = new MaterialListSnapshot(1, Time(1),
            [
                new MaterialListEntry(1, "duplicate", new LocalizedText("Material"), 900),
                new MaterialListEntry(2, "duplicate", new LocalizedText("Material"), 950)
            ]);
            Assert.Throws<ArgumentException>(() => snapshot.Validate());
        }

        /// <summary>
        /// Builds the actual standard list with native optional mutation methods and declared units.
        /// </summary>
        private static MaterialListState CreateList(CommonRuntimeServerFixture fixture)
        {
            MaterialListState list = fixture.Manager.SystemContext.CreateInstanceOfMaterialListType(
                fixture.Root, new QualifiedName("Materials", fixture.Manager.MachineryInstanceNamespaceIndex));
            list.AddAddMaterial(fixture.Manager.SystemContext);
            list.AddRemoveMaterialById(fixture.Manager.SystemContext);
            list.DensityUnit!.Value = new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4934993,
                DisplayName = new LocalizedText("kg/m3"),
                Description = new LocalizedText("kilogram per cubic metre")
            };
            list.EventNotifier = EventNotifiers.SubscribeToEvents;
            return list;
        }

        /// <summary>
        /// Resolves a real material rather than matching an untyped object name.
        /// </summary>
        private static MaterialState RequireMaterial(CommonRuntimeServerFixture fixture, MaterialListState list,
            int number)
        {
            return list.FindChild(fixture.Manager.SystemContext,
                new QualifiedName("Material_" + number.ToString("D3",
                    System.Globalization.CultureInfo.InvariantCulture),
                    list.NodeId.NamespaceIndex)) as MaterialState ??
                throw new InvalidOperationException("The native material was not published.");
        }

        /// <summary>
        /// Provides an explicit test policy; runtime registration never adds an implicit permissive policy.
        /// </summary>
        private static IPlasticsRubberAccessPolicy AllowAccess()
        {
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return policy.Object;
        }

        /// <summary>
        /// Produces fixed, deterministic committed source timestamps.
        /// </summary>
        private static DateTimeUtc Time(ulong revision)
        {
            return (DateTimeUtc)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(revision);
        }

        /// <summary>
        /// Deterministic test authority with explicit in-use and duplicate checks.
        /// </summary>
        private sealed class MaterialProvider : IMaterialListProvider
        {
            /// <summary>
            /// Gets the complete committed state.
            /// </summary>
            internal MaterialListSnapshot Snapshot => new(m_revision, Time(m_revision), m_materials.ToArrayOf());

            /// <summary>
            /// Gets or sets the material protected by an active job.
            /// </summary>
            internal string? InUse { get; set; }

            /// <summary>
            /// Gets the original service context last observed by the provider.
            /// </summary>
            internal ISystemContext? LastContext { get; private set; }

            /// <inheritdoc/>
            public ValueTask<MaterialListSnapshot> ReadAsync(
                PlasticsRubberRequestContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastContext = context.SystemContext;
                return new ValueTask<MaterialListSnapshot>(Snapshot);
            }

            /// <inheritdoc/>
            public ValueTask<MaterialListSnapshot> AddAsync(
                PlasticsRubberRequestContext context, string id, LocalizedText name, double density,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastContext = context.SystemContext;
                if (m_materials.Exists(value => string.Equals(value.Id, id, StringComparison.Ordinal)))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNodeIdExists, "Duplicate material.");
                }
                m_materials.Add(new MaterialListEntry(++m_nextNumber, id, name, density));
                m_revision++;
                return new ValueTask<MaterialListSnapshot>(Snapshot);
            }

            /// <inheritdoc/>
            public ValueTask<MaterialListSnapshot> RemoveAsync(
                PlasticsRubberRequestContext context, string id, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastContext = context.SystemContext;
                if (string.Equals(InUse, id, StringComparison.Ordinal))
                {
                    throw ServiceResultException.Create(StatusCodes.BadInvalidState, "The material is in use.");
                }
                int index = m_materials.FindIndex(value => string.Equals(value.Id, id, StringComparison.Ordinal));
                if (index < 0)
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound, "The material does not exist.");
                }
                m_materials.RemoveAt(index);
                m_revision++;
                return new ValueTask<MaterialListSnapshot>(Snapshot);
            }

            /// <summary>
            /// The committed native material descriptions.
            /// </summary>
            private readonly List<MaterialListEntry> m_materials = [];

            /// <summary>
            /// The persistent slot sequence.
            /// </summary>
            private int m_nextNumber;

            /// <summary>
            /// The authority's committed revision.
            /// </summary>
            private ulong m_revision;
        }
    }
}
