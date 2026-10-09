/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.PubSub.Tests
{
    public sealed partial class PubSubBindingServerTests
    {
        [Test]
        public async Task OrdinaryEndpointEditsInvalidateCorrespondenceAndPublicationDoesNotAdvanceItsEpochAsync()
        {
            string xid = "/endpoints/ordinary-lifecycle-" + Guid.NewGuid().ToString("N");
            Assert.That((await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = xid,
                Definition = OrdinaryEndpoint(xid)
            }).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            try
            {
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                    Is.EqualTo(2));
                PubSubConfigurationDataType configuration = Server.Application.GetConfiguration();
                configuration.Connections[0].WriterGroups[0].PublishingInterval++;
                await Server.Application.ReplaceConfigurationAsync(configuration).ConfigureAwait(false);
                Assert.That((await ReadNativeAsync(xid).ConfigureAwait(false)).Epoch, Is.EqualTo(1),
                    "Unchanged ordinary metadata retains its exact store-owned epoch.");
                Assert.That((await Host.PatchAsync(xid,
                    ByteString.From("{\"protocoloptions\":{\"topic\":\"different/topic\"}}"u8), 1).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                    Is.EqualTo(1));
                Assert.That((await Host.PatchAsync(xid,
                    ByteString.From("{\"protocoloptions\":{\"topic\":\"opcua/json/data/2234/Line1\"}}"u8), 2).ConfigureAwait(false)).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo).ConfigureAwait(false)).Count,
                    Is.EqualTo(2));
            }
            finally
            {
                await Host.DeleteAsync(xid, 0).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task PeriodicExpiryRemovesRemoteEntriesWithoutAnyBrokerOrPollingClientAsync()
        {
            RemotePubSubObservation connection = RemoteConnection("trusted");
            await Binding.ObserveRemoteAsync(connection).ConfigureAwait(false);
            RemotePubSubObservation metadata = RemoteMetadata();
            metadata.ExpiresAt = Server.Clock.GetUtcNow().AddSeconds(10);
            await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false);
            Assert.That(RemotePaths(), Has.Count.EqualTo(1));
            var expiration = new ExpirationObserver(() => RemotePaths().Count == 0);
            Host.AddObserver(expiration);
            try
            {
                Server.Clock.Advance(TimeSpan.FromSeconds(10));
                await AwaitWithDeadlineAsync(expiration.Expired.Task).ConfigureAwait(false);
                Assert.That(RemotePaths(), Is.Empty);
                Assert.That((await Binding.ObserveRemoteAsync(metadata).ConfigureAwait(false)).Accepted, Is.False);
            }
            finally
            {
                Host.RemoveObserver(expiration);
                await ClearRemoteAsync("trusted").ConfigureAwait(false);
            }
        }

        private sealed class ExpirationObserver(Func<bool> isExpired) : IRegistryStateObserver
        {
            public TaskCompletionSource<bool> Expired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask ActivatingAsync(RegistryCommittedState state, CancellationToken cancellationToken) => default;

            public ValueTask ActivatedAsync(RegistryCommittedState state, CancellationToken cancellationToken)
            {
                if (isExpired())
                {
                    Expired.TrySetResult(true);
                }
                return default;
            }
        }
    }
}
