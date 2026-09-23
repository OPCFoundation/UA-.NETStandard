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

namespace Opc.Ua.Server.Tests.NodeManager
{
    public sealed partial class NodeManagerLifecycleTests
    {
        [Test]
        public async Task PrivateValidationRejectsUnsupportedReferencesBeforePublication()
        {
            (Mock<IAsyncNodeManager> manager, BaseObjectState node, IAsyncNodeManagerFactory factory) =
                CreateUnsupportedReferenceFactory();
            var lifecycle = (INodeManagerPublicationLifecycle)m_server.NodeManagerLifecycle;
            await using INodeManagerPublication publication =
                await lifecycle.CapturePublication().BeginAsync().ConfigureAwait(false);
            Assert.That(publication, Is.InstanceOf<INodeManagerValidationPublication>());
            var validation = (INodeManagerValidationPublication)publication;
            ArrayOf<string> namespaces = m_server.CurrentInstance.NamespaceUris.ToArrayOf();
            NodeState objects = await GetObjectsReferenceOwnerAsync(CancellationToken.None).ConfigureAwait(false);
            int inspections = 0;

            await Assert.ThatAsync(async () => await validation.ValidateAsync(
                [NodeManagerBatchChange.Add(factory)], (_, _) =>
                {
                    inspections++;
                    return default;
                }).ConfigureAwait(false), Throws.TypeOf<NotSupportedException>()).ConfigureAwait(false);

            Assert.That(inspections, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
            Assert.That(m_server.CurrentInstance.NamespaceUris.ToArrayOf(), Is.EqualTo(namespaces));
            Assert.That(objects.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(8402, 1)), Is.False);
            Assert.That(node.ReferenceExists(ReferenceTypeIds.Organizes, false, new NodeId(8402, 1)), Is.False);
            manager.Verify(value => value.AddReferencesAsync(
                It.IsAny<IDictionary<NodeId, IList<IReference>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task PrivateValidationRejectsCommitDuringInspectionAndAllowsTheNextUnit()
        {
            TrackingLifecycleNodeManager manager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kFirstRegistrationValue, created => manager = created);
            var lifecycle = (INodeManagerPublicationLifecycle)m_server.NodeManagerLifecycle;
            await using INodeManagerPublication publication =
                await lifecycle.CapturePublication().BeginAsync().ConfigureAwait(false);
            var validation = (INodeManagerValidationPublication)publication;
            ArrayOf<string> namespaces = m_server.CurrentInstance.NamespaceUris.ToArrayOf();
            int decisions = 0;

            await validation.ValidateAsync([NodeManagerBatchChange.Add(factory)], async (batch, token) =>
            {
                Assert.That(batch.Registrations.Count, Is.EqualTo(1));
                Assert.That(batch.IsCommitted, Is.False);
                await Assert.ThatAsync(async () => await batch.CommitAsync(_ =>
                {
                    decisions++;
                    return default;
                }, token).ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }).ConfigureAwait(false);

            Assert.That(decisions, Is.Zero);
            Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
            Assert.That(manager.DisposeCount, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
            Assert.That(m_server.CurrentInstance.NamespaceUris.ToArrayOf(), Is.EqualTo(namespaces));
            await using IPreparedNodeManagerBatch next = await publication.PrepareAsync(
                [NodeManagerBatchChange.Add(factory)]).ConfigureAwait(false);
            NodeManagerBatchResult committed = await next.CommitAsync(_ =>
            {
                decisions++;
                return default;
            }).ConfigureAwait(false);
            Assert.That(decisions, Is.EqualTo(1));
            Assert.That(next.IsCommitted, Is.True);
            Assert.That(lifecycle.Registrations, Is.EqualTo(committed.Registrations));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PrivateValidationFailureDisposesCandidatesAndReleasesAdmission(bool cancel)
        {
            TrackingLifecycleNodeManager manager = null;
            IAsyncNodeManagerFactory factory = CreateTrackingNodeManagementFactory(
                kFirstRegistrationValue, created => manager = created);
            var lifecycle = (INodeManagerPublicationLifecycle)m_server.NodeManagerLifecycle;
            ArrayOf<string> namespaces = m_server.CurrentInstance.NamespaceUris.ToArrayOf();
            await using (INodeManagerPublication publication =
                await lifecycle.CapturePublication().BeginAsync().ConfigureAwait(false))
            {
                var validation = (INodeManagerValidationPublication)publication;
                using var cancellation = new CancellationTokenSource();
                var failure = new InvalidOperationException("The private metadata inspection failed.");
                await Assert.ThatAsync(async () => await validation.ValidateAsync(
                    [NodeManagerBatchChange.Add(factory)], (_, _) =>
                    {
                        if (cancel)
                        {
                            cancellation.Cancel();
                            return default;
                        }
                        throw failure;
                    }, cancellation.Token).ConfigureAwait(false),
                    cancel ? Throws.InstanceOf<OperationCanceledException>() : Throws.Exception.SameAs(failure))
                    .ConfigureAwait(false);
                Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
                Assert.That(manager.DeleteAddressSpaceCount, Is.EqualTo(1));
                Assert.That(manager.DisposeCount, Is.EqualTo(1));
                Assert.That(m_server.CurrentInstance.NamespaceUris.ToArrayOf(), Is.EqualTo(namespaces));
            }
            NodeManagerRegistration added = await lifecycle.AddAsync(factory, callerContext: null)
                .ConfigureAwait(false);
            Assert.That(lifecycle.Registrations.Count, Is.EqualTo(1));
            Assert.That(lifecycle.Registrations[0], Is.SameAs(added));
        }

        [Test]
        public async Task PrivateValidationRequiresAnInspectorBeforeCreatingCandidates()
        {
            var factory = new Mock<IAsyncNodeManagerFactory>(MockBehavior.Strict);
            var lifecycle = (INodeManagerPublicationLifecycle)m_server.NodeManagerLifecycle;
            await using INodeManagerPublication publication =
                await lifecycle.CapturePublication().BeginAsync().ConfigureAwait(false);
            var validation = (INodeManagerValidationPublication)publication;

            await Assert.ThatAsync(async () => await validation.ValidateAsync(
                [NodeManagerBatchChange.Add(factory.Object)], null).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>()).ConfigureAwait(false);

            factory.Verify(value => value.CreateAsync(
                It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Assert.That(lifecycle.Registrations.IsEmpty, Is.True);
        }
    }
}
