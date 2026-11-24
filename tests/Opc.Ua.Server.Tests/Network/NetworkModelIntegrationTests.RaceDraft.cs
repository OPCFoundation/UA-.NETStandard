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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Network;
using Opc.Ua.Server.Network;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Network
{
    /// <summary>
    /// Holds the uncommitted between-browse diagnostic.
    /// </summary>
    public sealed partial class NetworkModelIntegrationTests
    {
        /// <summary>
        /// Removes a returned child before discovery issues its next browse.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task DiscoveryDoesNotLeakUnknownWhenInterfaceDisappearsBetweenBrowses(
            CancellationToken cancellationToken)
        {
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeId removedChild = NodeId.Null;
            bool capturedParentBrowse = false;
            bool confirmedRemoval = false;
            bool observedUnknownTypeBrowse = false;
            var session = new Mock<ISessionClient>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(m_session.MessageContext);
            session.Setup(value => value.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? header, bool release, ArrayOf<ByteString> points, CancellationToken ct) =>
                    m_session.BrowseNextAsync(header, release, points, ct));
            session.Setup(value => value.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, ViewDescription?, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    async (header, view, maximum, requests, ct) =>
                    {
                        BrowseResponse response = await m_session.BrowseAsync(header, view, maximum, requests, ct)
                            .ConfigureAwait(false);
                        Assert.That(requests.Count, Is.EqualTo(1));
                        BrowseDescription request = requests[0];
                        if (request.NodeId == ObjectIds.NetworkInterfaces &&
                            request.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences)
                        {
                            Assert.That(capturedParentBrowse, Is.False);
                            Assert.That(response.Results.Count, Is.EqualTo(1));
                            BrowseResult result = response.Results[0];
                            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
                            Assert.That(result.References.Count, Is.GreaterThan(0));
                            ReferenceDescription child = result.References[0];
                            Assert.That(child.BrowseName.Name, Is.EqualTo("eth0").Or.EqualTo("eth0.2"));
                            removedChild = ExpandedNodeId.ToNodeId(
                                child.NodeId, m_session.MessageContext.NamespaceUris);
                            Assert.That(removedChild.IsNull, Is.False);
                            TestContext.Progress.WriteLine(
                                $"[NetworkRaceDraft] Parent browse returned {removedChild}.");
                            capturedParentBrowse = true;
                            m_providerBoundary.AfterDelivery = current =>
                            {
                                if (current.Revision == snapshot.Revision + 1)
                                {
                                    applied.TrySetResult(true);
                                }
                            };
                            m_provider.Publish(snapshot with
                            {
                                Timestamp = snapshot.Timestamp.AddSeconds(1),
                                Interfaces = []
                            });
                            await applied.Task.WaitAsync(ct).ConfigureAwait(false);
                            ReadResponse removed = await m_session.ReadAsync(
                                null, 0, TimestampsToReturn.Neither,
                                [new ReadValueId { NodeId = removedChild, AttributeId = Attributes.NodeClass }], ct)
                                .ConfigureAwait(false);
                            Assert.That(removed.Results.Count, Is.EqualTo(1));
                            Assert.That(removed.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                            TestContext.Progress.WriteLine(
                                $"[NetworkRaceDraft] Removed child {removedChild} returned BadNodeIdUnknown.");
                            confirmedRemoval = true;
                        }
                        else if (capturedParentBrowse && request.NodeId == removedChild &&
                            request.ReferenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                        {
                            observedUnknownTypeBrowse = response.Results.Count == 1 &&
                                response.Results[0].StatusCode == StatusCodes.BadNodeIdUnknown;
                            if (response.Results.Count == 1)
                            {
                                TestContext.Progress.WriteLine(
                                    $"[NetworkRaceDraft] Type browse for {removedChild}: " +
                                    $"{response.Results[0].StatusCode}.");
                            }
                        }
                        return response;
                    });
            var client = new NetworkModelClient(session.Object, NUnitTelemetryContext.Create());
            try
            {
                ArrayOf<NodeId> discovered = await client.DiscoverInterfacesAsync(cancellationToken)
                    .ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(capturedParentBrowse, Is.True);
                    Assert.That(confirmedRemoval, Is.True);
                    Assert.That(discovered.Count, Is.Zero);
                });
            }
            catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(capturedParentBrowse, Is.True);
                    Assert.That(removedChild.IsNull, Is.False);
                    Assert.That(confirmedRemoval, Is.True);
                    Assert.That(observedUnknownTypeBrowse, Is.True);
                });
                throw;
            }
            finally
            {
                m_providerBoundary.AfterDelivery = null;
            }
        }
    }
}
