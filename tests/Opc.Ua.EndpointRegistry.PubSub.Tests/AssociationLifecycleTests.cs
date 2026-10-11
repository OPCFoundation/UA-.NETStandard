/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.PubSub.Tests
{
    public sealed partial class PubSubBindingServerTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedBindingStartupDetachesObserversAndInvalidatesAssociationsAsync(bool timerFailure)
        {
            int schemaCalls = 0;
            var schemas = new Mock<IPubSubBindingSchemaProvider>();
            schemas.Setup(provider => provider.FindSchema(
                It.IsAny<DataSetMetaDataKey>(), It.IsAny<DataSetMetaDataType>()))
                .Returns((DataSetMetaDataKey key, DataSetMetaDataType metadata) =>
                {
                    schemaCalls++;
                    if (!timerFailure)
                    {
                        throw new InvalidOperationException("Injected initial activation failure.");
                    }
                    return Server.Schemas.FindSchema(key, metadata);
                });
            var clock = new Mock<TimeProvider>();
            clock.Setup(provider => provider.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object>(),
                It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Throws(new InvalidOperationException("Injected timer creation failure."));
            string prefix = "startup-feedback-" + Guid.NewGuid().ToString("N");
            await using var candidate = new EndpointRegistryPubSubBinding(Server.Registry, Server.PubSub,
                m_session!.MessageContext, m_telemetry!, new EndpointRegistryPubSubBindingOptions
                {
                    SurfaceLocalPublishers = false,
                    LocalIdentifierPrefix = prefix + "-local-",
                    RemoteIdentifierPrefix = prefix + "-remote-",
                    Schemas = schemas.Object,
                    TimeProvider = clock.Object
                });
            try
            {
                await Binding.RetiringAsync(CancellationToken.None).ConfigureAwait(false);
                InvalidOperationException error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await candidate.StartAsync().ConfigureAwait(false))!;
                Assert.That(error.Message, Is.EqualTo(timerFailure
                    ? "Injected timer creation failure." : "Injected initial activation failure."));
                Assert.That(schemaCalls, Is.GreaterThan(0));
                Assert.That(candidate.Snapshots, Is.Empty);
                Assert.That(() => Host.AddObserver(candidate), Throws.Nothing,
                    "Failed startup must not retain the registry observer.");
                Host.RemoveObserver(candidate);
                int callsAfterFailure = schemaCalls;
                await Server.Application.ReplaceConfigurationAsync(Server.Application.GetConfiguration())
                    .ConfigureAwait(false);
                Assert.That(schemaCalls, Is.EqualTo(callsAfterFailure),
                    "A failed binding must not receive subsequent view activations.");
                Assert.That(async () => await candidate.StartAsync().ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                await Binding.ActivatedAsync(Server.PubSub.ConfigurationView!, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

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
        public async Task ProtocolAuthorizationAlternativesRequireExplicitBindingApprovalAsync()
        {
            string xid = "/endpoints/ordinary-authorization-" + Guid.NewGuid().ToString("N");
            Assert.That((await Host.WriteAsync(new RegistryWriteRequestDataType
            {
                TargetXid = xid,
                Definition = OrdinaryEndpoint(xid)
            }).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
            try
            {
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo)
                    .ConfigureAwait(false)).Count, Is.EqualTo(2));
                Assert.That((await Host.PatchAsync(xid, ByteString.From(
                    """
                    {"protocoloptions":{"authorization":[
                        {"type":"Plain","authorityuri":"https://identity.example.test"}]}}
                    """u8), 1).ConfigureAwait(false)).StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo)
                    .ConfigureAwait(false)).Count, Is.EqualTo(1));
                Assert.That(Server.AuthorizationChecks, Is.GreaterThan(0));
                Server.AuthorizeBindings = true;
                await Binding.RefreshAsync().ConfigureAwait(false);
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo)
                    .ConfigureAwait(false)).Count, Is.EqualTo(2));
                Server.AuthorizeBindings = false;
                await Binding.RefreshAsync().ConfigureAwait(false);
                Assert.That((await BrowseAsync(GroupTarget().Node.NodeId, ReferenceTypeIds.PublishesTo)
                    .ConfigureAwait(false)).Count, Is.EqualTo(1));
            }
            finally
            {
                Server.AuthorizeBindings = false;
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
