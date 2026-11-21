// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

namespace Opc.Ua.Server.Tests.Historian
{
    /// <summary>
    /// Validates that <see cref="InMemoryHistorianProvider.GetCapabilitiesAsync"/>
    /// called with <see cref="NodeId.Null"/> (the provider-wide rollup used
    /// by the <c>HistoryServerCapabilities</c> diagnostics rollup) returns a
    /// conservative union of the capabilities actually advertised by
    /// registered nodes, rather than blindly returning
    /// <see cref="InMemoryHistorianOptions.DefaultCapabilities"/>.
    /// </summary>
    [TestFixture]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class InMemoryHistorianProviderCapabilityRollupTests
    {
        private const ushort NamespaceIndex = 1;

        /// <summary>
        /// Verifies that provider-wide capability lookup reports no capabilities when no nodes are registered.
        /// </summary>
        [Test]
        public async Task NullNodeRollupWithNoRegisteredNodesReturnsNoCapabilitiesAsync()
        {
            // Even though the configured default template is ReadWrite,
            // nothing has actually been registered yet, so the rollup for
            // NodeId.Null must not claim any capability.
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { DefaultCapabilities = HistorianNodeCapabilities.ReadWrite });

            HistorianNodeCapabilities rollup =
                await provider.GetCapabilitiesAsync(NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(rollup.ReadRawData, Is.False);
            Assert.That(rollup.ReadModifiedData, Is.False);
            Assert.That(rollup.ReadAtTime, Is.False);
            Assert.That(rollup.ReadProcessedData, Is.False);
            Assert.That(rollup.InsertData, Is.False);
            Assert.That(rollup.ReplaceData, Is.False);
            Assert.That(rollup.UpdateData, Is.False);
            Assert.That(rollup.DeleteRaw, Is.False);
            Assert.That(rollup.DeleteAtTime, Is.False);
            Assert.That(rollup.InsertAnnotation, Is.False);
            Assert.That(rollup.ReadEventHistory, Is.False);
            Assert.That(rollup.ReadStructuredData, Is.False);
            Assert.That(rollup.ServerTimestampSupported, Is.False);
        }

        /// <summary>
        /// Verifies that provider-wide capability lookup combines flags across registered nodes.
        /// </summary>
        [Test]
        public async Task NullNodeRollupUnionsFlagsAcrossRegisteredNodesAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            var readOnlyNode = new NodeId("read-only", NamespaceIndex);
            var writableNode = new NodeId("writable", NamespaceIndex);

            provider.Register(
                readOnlyNode,
                new HistorianNodeCapabilities
                {
                    ReadRawData = true,
                    InsertData = false,
                    DeleteRaw = false
                });
            provider.Register(
                writableNode,
                new HistorianNodeCapabilities
                {
                    ReadRawData = true,
                    InsertData = true,
                    DeleteRaw = true
                });

            HistorianNodeCapabilities rollup =
                await provider.GetCapabilitiesAsync(NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(rollup.ReadRawData, Is.True);
            Assert.That(rollup.InsertData, Is.True, "Union must include the writable node's InsertData=true.");
            Assert.That(rollup.DeleteRaw, Is.True, "Union must include the writable node's DeleteRaw=true.");
            // Nothing registered advertises annotations, events, or structured data.
            Assert.That(rollup.InsertAnnotation, Is.False);
            Assert.That(rollup.ReadEventHistory, Is.False);
            Assert.That(rollup.ReadStructuredData, Is.False);
        }

        /// <summary>
        /// Verifies that read-only registrations do not cause provider-wide lookup to overadvertise capabilities.
        /// </summary>
        [Test]
        public async Task NullNodeRollupDoesNotOverAdvertiseWhenAllRegisteredNodesAreReadOnlyAsync()
        {
            // Regression test for the over-advertisement bug: a provider
            // whose registered nodes are all read-only must not report
            // write capabilities for NodeId.Null just because the
            // (unused) DefaultCapabilities template happens to be
            // ReadWrite.
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { DefaultCapabilities = HistorianNodeCapabilities.ReadWrite });
            var nodeId = new NodeId("read-only-only", NamespaceIndex);
            provider.Register(nodeId, HistorianNodeCapabilities.ReadOnly);

            HistorianNodeCapabilities rollup =
                await provider.GetCapabilitiesAsync(NodeId.Null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(rollup.ReadRawData, Is.True);
            Assert.That(rollup.InsertData, Is.False);
            Assert.That(rollup.ReplaceData, Is.False);
            Assert.That(rollup.UpdateData, Is.False);
            Assert.That(rollup.DeleteRaw, Is.False);
            Assert.That(rollup.DeleteAtTime, Is.False);
            Assert.That(rollup.InsertAnnotation, Is.False);
            Assert.That(rollup.ReadEventHistory, Is.False);
            Assert.That(rollup.ReadStructuredData, Is.False);
        }

        /// <summary>
        /// Verifies that the None capability preset disables every capability flag.
        /// </summary>
        [Test]
        public void NoneStaticPresetHasEveryFlagFalse()
        {
            HistorianNodeCapabilities none = HistorianNodeCapabilities.None;

            Assert.That(none.ReadRawData, Is.False);
            Assert.That(none.ReadModifiedData, Is.False);
            Assert.That(none.ReadAtTime, Is.False);
            Assert.That(none.ReadProcessedData, Is.False);
            Assert.That(none.InsertData, Is.False);
            Assert.That(none.SupportsAnyUpdate, Is.False);
            Assert.That(none.SupportsAnyEventUpdate, Is.False);
            Assert.That(none.SupportsAnyStructuredUpdate, Is.False);
        }

        /// <summary>
        /// Verifies that an unregistered non-null node still uses the provider's default capabilities.
        /// </summary>
        [Test]
        public async Task NonNullUnregisteredNodeStillFallsBackToDefaultCapabilitiesAsync()
        {
            // The NodeId.Null rollup behavior is specific to the
            // provider-wide query; per-node queries for a node that was
            // never registered are unaffected and keep returning the
            // configured default template.
            using var provider = new InMemoryHistorianProvider(
                new InMemoryHistorianOptions { DefaultCapabilities = HistorianNodeCapabilities.ReadWrite });

            HistorianNodeCapabilities caps = await provider
                .GetCapabilitiesAsync(new NodeId("never-registered", NamespaceIndex), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(caps, Is.EqualTo(HistorianNodeCapabilities.ReadWrite));
        }

        /// <summary>
        /// Verifies that ordinary data registration does not advertise event or structured-data facets.
        /// </summary>
        [Test]
        public async Task DefaultDataRegistrationDoesNotClaimEventOrStructuredFacetsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            provider.Register(
                new NodeId("data", NamespaceIndex));

            HistorianNodeCapabilities rollup = await provider
                .GetCapabilitiesAsync(
                    NodeId.Null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(rollup.InsertData, Is.True);
            Assert.That(rollup.ReadEventHistory, Is.False);
            Assert.That(rollup.SupportsAnyEventUpdate, Is.False);
            Assert.That(rollup.ReadStructuredData, Is.False);
            Assert.That(rollup.SupportsAnyStructuredUpdate, Is.False);
        }

        /// <summary>
        /// Verifies that structured-data registration advertises only structured-data facets.
        /// </summary>
        [Test]
        public async Task StructuredRegistrationOnlyClaimsStructuredFacetsAsync()
        {
            using var provider = new InMemoryHistorianProvider();
            provider.RegisterStructured(
                new NodeId("structured", NamespaceIndex),
                KeyValuePairStructuredDataKeySelector.Instance);

            HistorianNodeCapabilities rollup = await provider
                .GetCapabilitiesAsync(
                    NodeId.Null,
                    CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(rollup.ReadStructuredData, Is.True);
            Assert.That(rollup.SupportsAnyStructuredUpdate, Is.True);
            Assert.That(rollup.InsertData, Is.False);
            Assert.That(rollup.ReadProcessedData, Is.False);
            Assert.That(rollup.ReadEventHistory, Is.False);
            Assert.That(rollup.SupportsAnyEventUpdate, Is.False);
        }
    }
}
