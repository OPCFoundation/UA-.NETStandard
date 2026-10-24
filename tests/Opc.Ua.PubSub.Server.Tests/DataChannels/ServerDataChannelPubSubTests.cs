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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Server.DataChannels;
using Opc.Ua.PubSub.Tests.Transports.DataChannels;
using Opc.Ua.PubSub.Transports;
using Opc.Ua.PubSub.Transports.DataChannels;
using Opc.Ua.Server.DataChannels;
using Opc.Ua.Tests;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpEncoder = Opc.Ua.PubSub.Encoding.Uadp.UadpEncoder;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Server.Tests.DataChannels
{
    /// <summary>
    /// Tests the direct PubSub source projection and actual carrier-keyed Server accept router.
    /// </summary>
    [TestFixture]
    [CancelAfter(30000)]
    public sealed class ServerDataChannelPubSubTests
    {
        [Test]
        public void SourceProjectionPreservesExactDescriptorAndExplicitPolicy()
        {
            PubSubConnectionDataType configuration = CreateConnection(DataChannelCarrierKind.SharedUasc);
            var policy = new ConfiguredPolicy();
            DataChannelSource source = PubSubDataChannelSource.Create(configuration, policy, maximumFlows: 3);
            var registry = new DataChannelSourceRegistry();
            registry.Register(source);
            DataChannelParametersDataType parameters = DataChannelPubSubConfiguration.GetParameters(configuration);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGet("urn:test:pubsub:server", out DataChannelSource? registered), Is.True);
                Assert.That(registered, Is.SameAs(source));
                Assert.That(source.Policy, Is.SameAs(policy));
                Assert.That(source.SupportedProtocols, Is.EqualTo(new[] { parameters.Protocol }));
                Assert.That(source.SupportedDirections, Is.EqualTo(new[] { parameters.Direction }));
                Assert.That(source.SupportedDeliveryModes, Is.EqualTo(new[] { parameters.Delivery }));
                Assert.That(source.SupportedProtectionModes, Is.EqualTo(new[] { parameters.Protection }));
                Assert.That(source.ContentType, Is.EqualTo(parameters.ContentType));
                Assert.That(source.MaxMessageSize, Is.EqualTo(parameters.MaxMessageSize));
                Assert.That(source.MaxReceiveBytes, Is.EqualTo(parameters.MaxReceiveBytes));
                Assert.That(source.MaxFlows, Is.EqualTo(3));
                Assert.That(source.SourceNodeId.IsNull, Is.True);
                Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Good));
            });
            parameters.Protocol = DataChannelProfiles.OpaquePayload;
            Assert.That(source.Validate(parameters), Is.EqualTo(DataChannelResult.Unsupported));
        }

        /// <summary>
        /// Preserves complete UADP bytes on the exact configured carrier without Session authorization.
        /// </summary>
        [Test]
        public async Task RegisteredServerFlowsUseTheExactCarrierAndPayloadWithoutSessions(
            [Values] DataChannelCarrierKind carrier)
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            PubSubConnectionDataType configuration = CreateConnection(carrier);
            var policy = new ConfiguredPolicy();
            var sources = new DataChannelSourceRegistry();
            sources.Register(PubSubDataChannelSource.Create(configuration, policy));
            var budget = new DataChannelApplicationBudget();
            await using var server = new ServerDataChannelProvider(
                sources, new DataChannelOptions(), budget, TimeProvider.System, NUnitTelemetryContext.Create());
            await using ContextPair contexts = await ContextPair.CreateAsync(
                server, carrier, cancellationToken).ConfigureAwait(false);
            var flows = new ServerDataChannelPubSubFlowProvider(server);
            await using IPubSubTransport receiver = new DataChannelPubSubTransportFactory(carrier, flows)
                .Create(configuration, NUnitTelemetryContext.Create(), TimeProvider.System);
            Task opening = receiver.OpenAsync(cancellationToken).AsTask();
            await using DataChannel sender = await contexts.Client.OpenAsync(
                DataChannelPubSubConfiguration.GetParameters(configuration),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await opening.ConfigureAwait(false);
            ByteString expected = await EncodeAsync().ConfigureAwait(false);
            Assert.That(await sender.SendAsync(expected, cancellationToken: cancellationToken).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await using IAsyncEnumerator<PubSubTransportFrame> messages = receiver.ReceiveAsync(
                cancellationToken).GetAsyncEnumerator();
            Assert.That(await messages.MoveNextAsync().ConfigureAwait(false), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(receiver.IsConnected, Is.True);
                Assert.That(messages.Current.Payload.Span.SequenceEqual(expected.Span), Is.True);
                Assert.That(contexts.ClientCarrier.Payloads.Single(), Is.EqualTo(expected));
                Assert.That(contexts.Server.Channels, Has.Count.EqualTo(1));
                Assert.That(contexts.Server.Channels[0].CarrierKind, Is.EqualTo(carrier));
                Assert.That(contexts.Server.Channels[0].GetStatus().SessionId.IsNull, Is.True);
                Assert.That(policy.Accesses, Is.Not.Empty);
                Assert.That(policy.Accesses.All(access => access.Session is null), Is.True);
                Assert.That(budget.Usage.Flows, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task UafxPubSubUsesLocalServerPolicyAndStandardPubSubUascProfile()
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            PubSubConnectionDataType configuration = CreateConnection(DataChannelCarrierKind.SharedUasc);
            configuration.Address = new ExtensionObject(
                new NetworkAddressUrlDataType { Url = "opc.tcp://uafx.example:4840" });
            configuration.ReaderGroups[0].MaxNetworkMessageSize = 65536;
            var parameters = new DataChannelParametersDataType
            {
                ResourceUri = "urn:test:pubsub:server",
                Protocol = DataChannelProfiles.UadpPayload,
                ContentType = DataChannelPubSubConfiguration.UafxContentType,
                Direction = DataChannelDirection.OpenerToAcceptor,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 65536,
                MaxReceiveBytes = 262144
            };
            var policy = new ConfiguredPolicy();
            var sources = new DataChannelSourceRegistry();
            sources.Register(PubSubDataChannelSource.CreateUafx(configuration, parameters, policy));

            await using var server = new ServerDataChannelProvider(
                sources, new DataChannelOptions(), new DataChannelApplicationBudget(),
                TimeProvider.System, NUnitTelemetryContext.Create());
            await using ContextPair contexts = await ContextPair.CreateAsync(
                server, DataChannelCarrierKind.SharedUasc, cancellationToken).ConfigureAwait(false);
            var flows = new ServerDataChannelPubSubFlowProvider(server);
            var factory = new DataChannelPubSubTransportFactory(
                DataChannelCarrierKind.SharedUasc,
                flows,
                DataChannelPubSubConfiguration.UafxTransportProfile,
                _ => parameters);
            await using IPubSubTransport receiver = factory.Create(
                configuration, NUnitTelemetryContext.Create(), TimeProvider.System);
            Task opening = receiver.OpenAsync(cancellationToken).AsTask();
            await using DataChannel sender = await contexts.Client.OpenAsync(
                parameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            await opening.ConfigureAwait(false);
            ByteString expected = await EncodeAsync().ConfigureAwait(false);
            Assert.That(await sender.SendAsync(expected, cancellationToken: cancellationToken).ConfigureAwait(false),
                Is.EqualTo(DataChannelResult.Good));
            await using IAsyncEnumerator<PubSubTransportFrame> messages = receiver.ReceiveAsync(
                cancellationToken).GetAsyncEnumerator();
            Assert.That(await messages.MoveNextAsync().ConfigureAwait(false), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(receiver.TransportProfileUri,
                    Is.EqualTo(DataChannelPubSubConfiguration.UafxTransportProfile));
                Assert.That(messages.Current.Payload.Span.SequenceEqual(expected.Span), Is.True);
                Assert.That(contexts.Server.Channels, Has.Count.EqualTo(1));
                Assert.That(contexts.Server.Channels[0].CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(contexts.Server.Channels[0].GetStatus().SessionId.IsNull, Is.True);
                Assert.That(policy.Accesses, Is.Not.Empty);
                Assert.That(policy.Accesses.All(access => access.Session is null), Is.True);
                Assert.That(configuration.ConnectionProperties.IsEmpty, Is.True);
            });
        }

        [Test]
        public async Task ConfiguredUafxSourceRevocationIsScopedAcrossRemovalAndPolicyChange()
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            PubSubConnectionDataType firstConfiguration = CreateUafxConnection(
                "urn:test:pubsub:revoked", out DataChannelParametersDataType firstParameters);
            PubSubConnectionDataType secondConfiguration = CreateUafxConnection(
                "urn:test:pubsub:survivor", out DataChannelParametersDataType secondParameters);
            var policy = new ConfiguredPolicy();
            var sources = new DataChannelSourceRegistry();
            DataChannelSource firstSource = PubSubDataChannelSource.CreateUafx(
                firstConfiguration, firstParameters, policy);
            DataChannelSource secondSource = PubSubDataChannelSource.CreateUafx(
                secondConfiguration, secondParameters, policy);
            sources.Register(firstSource);
            sources.Register(secondSource);

            await using var server = new ServerDataChannelProvider(
                sources, new DataChannelOptions(), new DataChannelApplicationBudget(),
                TimeProvider.System, NUnitTelemetryContext.Create());
            await using ContextPair contexts = await ContextPair.CreateAsync(
                server, DataChannelCarrierKind.SharedUasc, cancellationToken).ConfigureAwait(false);
            await using DataChannel revoked = await contexts.Client.OpenAsync(
                firstParameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            await using DataChannel survivor = await contexts.Client.OpenAsync(
                secondParameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            DataChannel revokedReceiver = (contexts.Server.Channels.ToArray() ?? []).Single(
                channel => channel.ChannelId == revoked.ChannelId);
            DataChannel survivorReceiver = (contexts.Server.Channels.ToArray() ?? []).Single(
                channel => channel.ChannelId == survivor.ChannelId);
            var revokedState = new TaskCompletionSource<DataChannelState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            revoked.StateChanged += notification =>
            {
                if (notification.Status.State != DataChannelState.Open)
                {
                    revokedState.TrySetResult(notification.Status.State);
                }
            };

            Assert.That(sources.Unregister(firstSource.ResourceUri), Is.True);
            DataChannelState terminalState = await revokedState.Task
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            ByteString expected = await EncodeAsync().ConfigureAwait(false);
            Assert.That(await survivor.SendAsync(
                expected, cancellationToken: cancellationToken).ConfigureAwait(false), Is.EqualTo(DataChannelResult.Good));
            using DataChannelMessage received = (await survivorReceiver.ReadAsync(cancellationToken)
                .ConfigureAwait(false))!;
            sources.Register(firstSource);
            await using DataChannel reopened = await contexts.Client.OpenAsync(
                firstParameters, cancellationToken: cancellationToken).ConfigureAwait(false);
            var policyRevokedState = new TaskCompletionSource<DataChannelState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            reopened.StateChanged += notification =>
            {
                if (notification.Status.State != DataChannelState.Open)
                {
                    policyRevokedState.TrySetResult(notification.Status.State);
                }
            };
            Assert.That(sources.RevokeConfiguredFlows(firstSource.ResourceUri), Is.True);
            DataChannelState policyTerminalState = await policyRevokedState.Task
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(sources.TryGet(firstSource.ResourceUri, out DataChannelSource? registered), Is.True);
            await using DataChannel reauthorized = await contexts.Client.OpenAsync(
                firstParameters, cancellationToken: cancellationToken).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(terminalState, Is.Not.EqualTo(DataChannelState.Open));
                Assert.That(revoked.State, Is.Not.EqualTo(DataChannelState.Open));
                Assert.That(survivor.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(reopened.State, Is.Not.EqualTo(DataChannelState.Open));
                Assert.That(policyTerminalState, Is.Not.EqualTo(DataChannelState.Open));
                Assert.That(registered, Is.SameAs(firstSource));
                Assert.That(reauthorized.State, Is.EqualTo(DataChannelState.Open));
                Assert.That(contexts.Server.IsReady, Is.True);
                Assert.That(received.Payload, Is.EqualTo(expected));
                Assert.That(revokedReceiver.State, Is.Not.EqualTo(DataChannelState.Open));
            });
        }

        /// <summary>
        /// Refuses the obsolete JSON mapping without changing the admitted UADP source configuration.
        /// </summary>
        [Test]
        public void JsonPayloadIsRejectedWithoutChangingTheServerSource(
            [Values] DataChannelCarrierKind carrier)
        {
            PubSubConnectionDataType configuration = CreateConnection(carrier);
            string? originalProfile = configuration.TransportProfileUri;
            ArrayOf<KeyValuePair> originalProperties = configuration.ConnectionProperties;
            DataChannelParametersDataType parameters = DataChannelPubSubConfiguration.GetParameters(configuration);
            parameters.Protocol = DataChannelProfiles.JsonPayload;
            parameters.ContentType = "application/json";

            Assert.That(() => DataChannelPubSubConfiguration.Configure(configuration, carrier, parameters),
                Throws.TypeOf<NotSupportedException>());
            var policy = new ConfiguredPolicy();
            DataChannelSource source = PubSubDataChannelSource.Create(configuration, policy);
            DataChannelParametersDataType retained = DataChannelPubSubConfiguration.GetParameters(configuration);

            Assert.Multiple(() =>
            {
                Assert.That(configuration.TransportProfileUri, Is.EqualTo(originalProfile));
                Assert.That(configuration.ConnectionProperties, Is.EqualTo(originalProperties));
                Assert.That(retained.Protocol, Is.EqualTo(DataChannelProfiles.UadpPayload));
                Assert.That(retained.ContentType, Is.EqualTo("application/opcua+uadp"));
                Assert.That(source.SupportedProtocols, Is.EqualTo(new[] { DataChannelProfiles.UadpPayload }));
                Assert.That(source.Validate(retained), Is.EqualTo(DataChannelResult.Good));
                Assert.That(policy.Accesses, Is.Empty);
            });
        }

        [Test]
        public async Task RouterDoesNotConsumeAFlowSelectedByAnotherCarrierFactory()
        {
            CancellationToken cancellationToken = TestContext.CurrentContext.CancellationToken;
            PubSubConnectionDataType shared = CreateConnection(DataChannelCarrierKind.SharedUasc);
            PubSubConnectionDataType dedicated = CreateConnection(DataChannelCarrierKind.DedicatedUasc);
            var sources = new DataChannelSourceRegistry();
            sources.Register(PubSubDataChannelSource.Create(shared, new ConfiguredPolicy()));
            await using var server = new ServerDataChannelProvider(
                sources, new DataChannelOptions(), new DataChannelApplicationBudget(),
                TimeProvider.System, NUnitTelemetryContext.Create());
            await using ContextPair dedicatedContexts = await ContextPair.CreateAsync(
                server, DataChannelCarrierKind.DedicatedUasc, cancellationToken).ConfigureAwait(false);
            await using ContextPair sharedContexts = await ContextPair.CreateAsync(
                server, DataChannelCarrierKind.SharedUasc, cancellationToken).ConfigureAwait(false);
            var provider = new ServerDataChannelPubSubFlowProvider(server);
            Task<DataChannelPubSubFlow> waitingShared = provider.OpenAsync(
                new DataChannelPubSubFlowRequest(shared), cancellationToken).AsTask();
            await using DataChannel dedicatedSender = await dedicatedContexts.Client.OpenAsync(
                DataChannelPubSubConfiguration.GetParameters(dedicated),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            Assert.That(waitingShared.IsCompleted, Is.False);
            await using DataChannelPubSubFlow dedicatedFlow = await provider.OpenAsync(
                new DataChannelPubSubFlowRequest(dedicated), cancellationToken).ConfigureAwait(false);
            await using DataChannel sharedSender = await sharedContexts.Client.OpenAsync(
                DataChannelPubSubConfiguration.GetParameters(shared),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            await using DataChannelPubSubFlow sharedFlow = await waitingShared.ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(dedicatedFlow.CarrierKind, Is.EqualTo(DataChannelCarrierKind.DedicatedUasc));
                Assert.That(sharedFlow.CarrierKind, Is.EqualTo(DataChannelCarrierKind.SharedUasc));
                Assert.That(dedicatedFlow.Channel.GetStatus().ContextId,
                    Is.EqualTo(dedicatedSender.GetStatus().ContextId));
                Assert.That(sharedFlow.Channel.GetStatus().ContextId, Is.EqualTo(sharedSender.GetStatus().ContextId));
                Assert.That(sharedFlow.Channel.GetStatus().ContextId,
                    Is.Not.EqualTo(dedicatedFlow.Channel.GetStatus().ContextId));
            });
        }

        [Test]
        public async Task ServerTransportRegistrationRequiresAndUsesTheActualServerProvider()
        {
            var sources = new DataChannelSourceRegistry();
            await using var server = new ServerDataChannelProvider(
                sources, new DataChannelOptions(), new DataChannelApplicationBudget(),
                TimeProvider.System, NUnitTelemetryContext.Create());
            var services = new ServiceCollection();
            services.AddSingleton(server);
            services.AddOpcUa().AddPubSub(pubsub =>
                pubsub.AddServerDataChannelTransport(DataChannelCarrierKind.SharedUasc));
            await using ServiceProvider provider = services.BuildServiceProvider();
            IPubSubTransportFactory factory = provider.GetRequiredService<IPubSubTransportFactory>();
            Assert.That(factory, Is.TypeOf<DataChannelPubSubTransportFactory>());
            Assert.That(factory.TransportProfileUri, Is.EqualTo(DataChannelProfiles.SharedUasc));
            Assert.That(provider.GetRequiredKeyedService<IDataChannelPubSubFlowProvider>(
                DataChannelCarrierKind.SharedUasc), Is.TypeOf<ServerDataChannelPubSubFlowProvider>());
        }

        [TestCase(0)]
        [TestCase(129)]
        public void SourceProjectionRejectsUnboundedFlowCounts(int maximumFlows)
        {
            Assert.That(() => PubSubDataChannelSource.Create(
                CreateConnection(DataChannelCarrierKind.SharedUasc), new ConfiguredPolicy(), maximumFlows),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void IntegrationHooksRejectNullDependencies()
        {
            Assert.Multiple(() =>
            {
                Assert.That(() => new ServerDataChannelPubSubFlowProvider(null!), Throws.ArgumentNullException);
                Assert.That(() => PubSubDataChannelSource.Create(
                    CreateConnection(DataChannelCarrierKind.SharedUasc), null!), Throws.ArgumentNullException);
                Assert.That(() => PubSubDataChannelSource.Create(null!, new ConfiguredPolicy()),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// Creates a bounded UADP reader configuration for the selected carrier.
        /// </summary>
        private static PubSubConnectionDataType CreateConnection(DataChannelCarrierKind carrier)
        {
            return DataChannelPubSubConfiguration.Configure(
                new PubSubConnectionDataType
                {
                    Name = "ServerFlow",
                    ReaderGroups = [new ReaderGroupDataType()]
                },
                carrier,
                new DataChannelParametersDataType
                {
                    ResourceUri = "urn:test:pubsub:server",
                    Protocol = DataChannelProfiles.UadpPayload,
                    ContentType = "application/opcua+uadp",
                    Direction = DataChannelDirection.OpenerToAcceptor,
                    Delivery = DataChannelDeliveryMode.Ordered,
                    Protection = DataChannelProtectionMode.Confidential,
                    MaxMessageSize = 65536,
                    MaxReceiveBytes = 262144
                });
        }

        private static PubSubConnectionDataType CreateUafxConnection(
            string resourceUri,
            out DataChannelParametersDataType parameters)
        {
            parameters = new DataChannelParametersDataType
            {
                ResourceUri = resourceUri,
                Protocol = DataChannelProfiles.UadpPayload,
                ContentType = DataChannelPubSubConfiguration.UafxContentType,
                Direction = DataChannelDirection.OpenerToAcceptor,
                Delivery = DataChannelDeliveryMode.Ordered,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = 65536,
                MaxReceiveBytes = 262144
            };
            var connection = new PubSubConnectionDataType
            {
                Name = "UafxRevocation",
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.tcp://uafx.example:4840"
                }),
                ReaderGroups = [new ReaderGroupDataType { MaxNetworkMessageSize = parameters.MaxMessageSize }]
            };
            return DataChannelPubSubConfiguration.ConfigureUafx(connection, parameters);
        }

        /// <summary>
        /// Encodes a complete independent UADP message for byte-preservation assertions.
        /// </summary>
        private static async ValueTask<ByteString> EncodeAsync()
        {
            ArrayOf<DataSetField> fields = [new DataSetField { Name = "Value", Value = new Variant(42) }];
            var encoder = new UadpEncoder();
            var message = new UadpNetworkMessage
            {
                PublisherId = PublisherId.FromUInt32(70000),
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                DataSetMessages = [new UadpDataSetMessage { DataSetWriterId = 999, Fields = fields }]
            };
            var context = new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()), new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High), TimeProvider.System);
            return new ByteString(await encoder.EncodeAsync(message, context).ConfigureAwait(false));
        }

        private sealed class ConfiguredPolicy : IDataChannelSourcePolicy
        {
            public ConcurrentQueue<DataChannelSourceAccess> Accesses { get; } = new();

            public ValueTask<DataChannelResult> AuthorizeAsync(
                DataChannelSourceAccess access,
                CancellationToken cancellationToken = default)
            {
                Accesses.Enqueue(access);
                return new ValueTask<DataChannelResult>(
                    access.Session is null && access.Context?.Peer.PeerApplicationUri == "urn:test:pubsub:left"
                        ? access.Source.Validate(access.Parameters)
                        : DataChannelResult.NotAuthorized);
            }
        }

        private sealed class ContextPair : IAsyncDisposable
        {
            private ContextPair(ServerDataChannelProvider provider, DataChannelCarrierKind carrier)
            {
                ClientCarrier = new DataChannelTestPair.InProcessCarrier(carrier, true);
                m_serverCarrier = new DataChannelTestPair.InProcessCarrier(carrier, false);
                Client = new DataChannelContext(
                    ClientCarrier, new DataChannelTestPair.ResourceAuthorizer(), new DataChannelOptions(),
                    new DataChannelApplicationBudget(), TimeProvider.System, NUnitTelemetryContext.Create());
                Server = provider.Create(m_serverCarrier);
                ClientCarrier.Attach(Server);
                m_serverCarrier.Attach(Client);
            }

            public DataChannelContext Client { get; }

            public DataChannelContext Server { get; }

            public DataChannelTestPair.InProcessCarrier ClientCarrier { get; }

            public static async Task<ContextPair> CreateAsync(
                ServerDataChannelProvider provider,
                DataChannelCarrierKind carrier,
                CancellationToken cancellationToken)
            {
                var pair = new ContextPair(provider, carrier);
                try
                {
                    await Task.WhenAll(pair.Client.StartAsync(cancellationToken).AsTask(),
                        pair.Server.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);
                    return pair;
                }
                catch
                {
                    await pair.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                await Client.DisposeAsync().ConfigureAwait(false);
                await Server.DisposeAsync().ConfigureAwait(false);
                await ClientCarrier.DisposeAsync().ConfigureAwait(false);
                await m_serverCarrier.DisposeAsync().ConfigureAwait(false);
            }

            private readonly DataChannelTestPair.InProcessCarrier m_serverCarrier;
        }
    }
}
