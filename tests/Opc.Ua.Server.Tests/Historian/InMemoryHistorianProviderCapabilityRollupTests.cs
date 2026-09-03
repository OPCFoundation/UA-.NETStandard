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
