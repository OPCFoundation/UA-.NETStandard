// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Network;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;
using ISession = Opc.Ua.Client.ISession;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Exercises the shared Part 21 and Part 22 Resources tree through a connected session.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    [Category("Server")]
    [NonParallelizable]
    public sealed class ProvisionableDeviceResourcesIntegrationTests
    {
        /// <summary>
        /// Browses the device and provider-backed interface with either node-manager registration order.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60_000)]
        public async Task ProvisionableDeviceAndNetworkModelCoexistUnderResourcesAsync(
            bool networkFirst,
            CancellationToken cancellationToken)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaResourcesTests", Guid.NewGuid().ToString("N"));
            byte[] password = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
            var runtime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            runtime.SetupGet(device => device.IsSingleton).Returns(true);
            runtime.Setup(device => device.RequestTicketsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<string>)["signed-ticket"]);
            var provider = new ReferenceNetworkModelProvider(new NetworkModelSnapshot
            {
                Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                StatusCode = StatusCodes.Good,
                Interfaces =
                [
                    new NetworkInterfaceSnapshot("physical", "eth0")
                    {
                        AdminStatus = InterfaceAdminStatus.Up,
                        OperStatus = InterfaceOperStatus.Up,
                        Speed = 1_000_000_000
                    }
                ],
                MappingTables = [],
                Streams = []
            });
            var server = new ServerFixture<ReferenceServer>(telemetry =>
            {
                var instance = new ReferenceServer(telemetry);
                // This account is already registered with the server's user-management metadata.
                Assert.That(
                    instance.UserDatabase.ResetPassword(
                        "user1", password, Ua.UserConfigurationMask.None, string.Empty),
                    Is.True);
                var network = new NetworkModelNodeManagerFactory(provider, _ => false);
                var device = new ProvisionableDeviceNodeManagerFactory(runtime.Object);
                if (networkFirst)
                {
                    instance.AddNodeManager(network);
                    instance.AddNodeManager(device);
                }
                else
                {
                    instance.AddNodeManager(device);
                    instance.AddNodeManager(network);
                }
                return instance;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };

            try
            {
                await server.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
                server.Config.ServerConfiguration!.UserTokenPolicies =
                [
                    new UserTokenPolicy(UserTokenType.Anonymous),
                    new UserTokenPolicy(UserTokenType.UserName)
                ];
                await server.StartAsync(pkiRoot).ConfigureAwait(false);
                await using var client = new ClientFixture(NUnitTelemetryContext.Create());
                await client.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
                var serverUrl = new Uri(
                    "opc.tcp://localhost:" + server.Port.ToString(CultureInfo.InvariantCulture));
                await using ISession session = await client.ConnectAsync(
                    serverUrl,
                    SecurityPolicies.Basic256Sha256,
                    default,
                    new UserIdentity("user1", password)).ConfigureAwait(false);

                int deviceIndex = session.NamespaceUris.GetIndex(ProvisionableDeviceNodeManagerFactory.NamespaceUri);
                int networkIndex = session.NamespaceUris.GetIndex(NetworkModelNodeManager.NamespaceUri);
                Assert.That(deviceIndex, Is.GreaterThan(0));
                Assert.That(networkIndex, Is.GreaterThan(0));
                var deviceId = new NodeId(29878u);
                var singletonId = new NodeId(29879u);
                var requestTicketsId = new NodeId(29880u);
                var interfaceId = new NodeId("interface/physical", checked((ushort)networkIndex));

                BrowseResponse response = await session.BrowseAsync(
                    null,
                    null,
                    0,
                    [
                        DescribeBrowse(Ua.ObjectIds.Resources, BrowseDirection.Forward),
                        DescribeBrowse(Ua.ObjectIds.Communication, BrowseDirection.Forward),
                        DescribeBrowse(Ua.ObjectIds.NetworkInterfaces, BrowseDirection.Forward),
                        DescribeBrowse(deviceId, BrowseDirection.Inverse),
                        new BrowseDescription
                        {
                            NodeId = deviceId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    cancellationToken).ConfigureAwait(false);
                Assert.That(response.Results.Count, Is.EqualTo(5));
                for (int i = 0; i < response.Results.Count; i++)
                {
                    Assert.That(response.Results[i].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.Results[i].ContinuationPoint.IsEmpty, Is.True);
                }

                List<ReferenceDescription> resources = response.Results[0].References.ToList();
                ReferenceDescription device = FindByName(resources, "ProvisionableDevice");
                Assert.Multiple(() =>
                {
                    Assert.That(device.BrowseName, Is.EqualTo(new QualifiedName("ProvisionableDevice", 0)));
                    Assert.That(device.NodeId, Is.EqualTo((ExpandedNodeId)deviceId));
                    Assert.That(device.NodeClass, Is.EqualTo(NodeClass.Object));
                    Assert.That(device.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.Organizes));
                    Assert.That(device.IsForward, Is.True);
                    Assert.That(device.TypeDefinition, Is.EqualTo(new ExpandedNodeId(26871u)));
                });

                ReferenceDescription communication = FindByName(resources, "Communication");
                Assert.Multiple(() =>
                {
                    Assert.That(communication.BrowseName, Is.EqualTo(new QualifiedName("Communication", 0)));
                    Assert.That(communication.NodeId, Is.EqualTo((ExpandedNodeId)Ua.ObjectIds.Communication));
                    Assert.That(communication.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.Organizes));
                    Assert.That(communication.IsForward, Is.True);
                });

                ReferenceDescription interfaces = FindByName(
                    response.Results[1].References.ToList(), "NetworkInterfaces");
                Assert.Multiple(() =>
                {
                    Assert.That(interfaces.BrowseName, Is.EqualTo(new QualifiedName("NetworkInterfaces", 0)));
                    Assert.That(interfaces.NodeId, Is.EqualTo((ExpandedNodeId)Ua.ObjectIds.NetworkInterfaces));
                    Assert.That(interfaces.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.Organizes));
                    Assert.That(interfaces.IsForward, Is.True);
                });

                ReferenceDescription networkInterface = FindByName(
                    response.Results[2].References.ToList(), "eth0");
                Assert.Multiple(() =>
                {
                    Assert.That(
                        networkInterface.BrowseName,
                        Is.EqualTo(new QualifiedName("eth0", checked((ushort)networkIndex))));
                    Assert.That(networkInterface.NodeId, Is.EqualTo((ExpandedNodeId)interfaceId));
                    Assert.That(networkInterface.NodeClass, Is.EqualTo(NodeClass.Object));
                    Assert.That(networkInterface.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.Organizes));
                    Assert.That(networkInterface.IsForward, Is.True);
                    Assert.That(
                        networkInterface.TypeDefinition,
                        Is.EqualTo((ExpandedNodeId)Ua.ObjectTypeIds.IetfBaseNetworkInterfaceType));
                });

                ReferenceDescription parent = FindByName(response.Results[3].References.ToList(), "Resources");
                Assert.Multiple(() =>
                {
                    Assert.That(parent.BrowseName, Is.EqualTo(new QualifiedName("Resources", 0)));
                    Assert.That(parent.NodeId, Is.EqualTo((ExpandedNodeId)Ua.ObjectIds.Resources));
                    Assert.That(parent.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.Organizes));
                    Assert.That(parent.IsForward, Is.False);
                });

                List<ReferenceDescription> children = response.Results[4].References.ToList();
                ReferenceDescription singleton = FindByName(children, "IsSingleton");
                ReferenceDescription requestTickets = FindByName(children, "RequestTickets");
                Assert.Multiple(() =>
                {
                    Assert.That(singleton.NodeId, Is.EqualTo((ExpandedNodeId)singletonId));
                    Assert.That(singleton.BrowseName, Is.EqualTo(new QualifiedName("IsSingleton", 0)));
                    Assert.That(singleton.NodeClass, Is.EqualTo(NodeClass.Variable));
                    Assert.That(singleton.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.HasProperty));
                    Assert.That(requestTickets.NodeId, Is.EqualTo((ExpandedNodeId)requestTicketsId));
                    Assert.That(requestTickets.BrowseName, Is.EqualTo(new QualifiedName("RequestTickets", 0)));
                    Assert.That(requestTickets.NodeClass, Is.EqualTo(NodeClass.Method));
                    Assert.That(requestTickets.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.HasComponent));
                });
                ReadResponse read = await session.ReadAsync(
                    null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = singletonId, AttributeId = Attributes.Value }],
                    cancellationToken).ConfigureAwait(false);
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[0].WrappedValue.TryGetValue(out bool isSingleton), Is.True);
                Assert.That(isSingleton, Is.True);
                TranslateBrowsePathsToNodeIdsResponse translated = await session.TranslateBrowsePathsToNodeIdsAsync(
                    null,
                    [
                        new BrowsePath
                        {
                            StartingNode = Ua.ObjectIds.Resources,
                            RelativePath = new RelativePath
                            {
                                Elements =
                                [
                                    new RelativePathElement
                                    {
                                        ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                                        TargetName = new QualifiedName("ProvisionableDevice", 0)
                                    },
                                    new RelativePathElement
                                    {
                                        ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                                        TargetName = new QualifiedName("RequestTickets", 0)
                                    }
                                ]
                            }
                        }
                    ],
                    cancellationToken).ConfigureAwait(false);
                Assert.That(translated.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(translated.Results[0].Targets.Count, Is.EqualTo(1));
                Assert.That(translated.Results[0].Targets[0].TargetId, Is.EqualTo((ExpandedNodeId)requestTicketsId));
                ArrayOf<Variant> outputs = await session.CallAsync(
                    deviceId, requestTicketsId, cancellationToken).ConfigureAwait(false);
                Assert.That(outputs.Count, Is.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out ArrayOf<string> tickets), Is.True);
                Assert.That(tickets.ToArray(), Is.EqualTo(s_signedTickets));
                runtime.Verify(device => device.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                await server.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Publishes, replaces, and retires the device through one authenticated server session.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [CancelAfter(60_000)]
        public async Task ConnectedSessionAddReplaceRemoveUpdatesResourcesAndRoutesCurrentRuntimeAsync(
            CancellationToken cancellationToken)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaResourcesLifecycleTests", Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<NoListenerReferenceServer>(
                telemetry => new NoListenerReferenceServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            NoListenerReferenceServer? server = null;
            RequestHeader? requestHeader = null;
            SecureChannelContext? secureChannelContext = null;
            bool sessionActive = false;
            var retirements = new List<Task>();

            var runtime1 = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            runtime1.SetupGet(runtime => runtime.IsSingleton).Returns(true);
            runtime1.SetupSequence(runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<string>)["generation-1-ticket"])
                .ReturnsAsync((ArrayOf<string>)["generation-2-ticket"]);

            var runtime3 = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            runtime3.SetupGet(runtime => runtime.IsSingleton).Returns(true);
            runtime3.Setup(runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<string>)["generation-3-ticket"]);

            try
            {
                await fixture.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
                Assert.That(
                    await fixture.Application.CheckApplicationInstanceCertificatesAsync(
                        true, ct: cancellationToken).ConfigureAwait(false),
                    Is.True);

                server = new NoListenerReferenceServer(NUnitTelemetryContext.Create());
                await fixture.Application.StartAsync(server, cancellationToken).ConfigureAwait(false);

                (requestHeader, secureChannelContext) = await server.CreateAndActivateSessionAsync(
                    TestContext.CurrentContext.Test.Name,
                    useSecurity: false).ConfigureAwait(false);
                sessionActive = true;
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds)
                    .Returns((ArrayOf<NodeId>)[Ua.ObjectIds.WellKnownRole_AuthenticatedUser]);
                server.CurrentInstance.SessionManager.GetSession(requestHeader.AuthenticationToken)!
                    .RefreshEffectiveIdentity(identity.Object);
                DataValue fallbackRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                CallMethodResult fallbackCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(fallbackRead.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(StatusCode.IsBad(fallbackCall.StatusCode), Is.True);

                await using IPreparedChanges preparedAdd = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Add(
                            new ProvisionableDeviceNodeManagerFactory(runtime1.Object))],
                        cancellationToken)
                    .ConfigureAwait(false);
                LifecycleResult added = await preparedAdd
                    .CommitAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(added.CleanupFailure, Is.Null);
                Assert.That(added.Registrations, Has.Count.EqualTo(1));
                NodeManagerSlot slot1 = added.Registrations[0].Slot;
                retirements.Add(added.Retired);

                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: true, cancellationToken)
                    .ConfigureAwait(false);
                DataValue firstRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(firstRead.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(firstRead.WrappedValue.TryGetValue(out bool firstSingleton), Is.True);
                Assert.That(firstSingleton, Is.True);

                CallMethodResult firstCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(firstCall.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(firstCall.OutputArguments.Count, Is.EqualTo(1));
                Assert.That(firstCall.OutputArguments[0].TryGetValue(out ArrayOf<string> firstTickets), Is.True);
                Assert.That(firstTickets.ToArray(), Is.EqualTo(s_generation1Tickets));
                runtime1.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Once);

                Assert.That(
                    added.Registrations[0].TryGetRegistration(out NodeManagerRegistration? original), Is.True);
                var replacementFactory = new ProvisionableDeviceNodeManagerFactory(runtime1.Object);
                long originalRevision = server.NodeManagerLifecycle.Revision;
                Assert.That(
                    replacementFactory.QueueReplacement(server.NodeManagerLifecycle, original!, cancellationToken),
                    Is.EqualTo(StatusCodes.Good));
                while (server.NodeManagerLifecycle.Revision == originalRevision ||
                    !slot1.TryGetCurrent(out NodeManagerSlotSnapshot replacement) ||
                    replacement.Generation == original!.Generation)
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                // The successor retains the host runtime but owns a new routing generation.
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: true, cancellationToken)
                    .ConfigureAwait(false);
                DataValue replacementRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(replacementRead.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(replacementRead.WrappedValue.TryGetValue(out bool replacementSingleton), Is.True);
                Assert.That(replacementSingleton, Is.True);

                requestHeader.Timestamp = DateTimeUtc.Now;
                ushort deviceNamespace = checked((ushort)server.CurrentInstance.NamespaceUris.GetIndex(
                    ProvisionableDeviceNodeManagerFactory.NamespaceUri));
                using var writeLifetime = new RequestLifetime(cancellationToken);
                WriteResponse overrideWrite = await server.WriteAsync(
                    secureChannelContext,
                    requestHeader,
                    [
                        new WriteValue
                        {
                            NodeId = new NodeId(29879u),
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(false))
                        },
                        new WriteValue
                        {
                            NodeId = new NodeId("missing-device-node", deviceNamespace),
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(true))
                        }
                    ],
                    writeLifetime).ConfigureAwait(false);
                Assert.That(overrideWrite.Results, Has.Count.EqualTo(2));
                Assert.That(overrideWrite.Results[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(overrideWrite.Results[1], Is.EqualTo(StatusCodes.BadNodeIdUnknown));

                DataValue unchangedSingleton = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(unchangedSingleton.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(unchangedSingleton.WrappedValue.TryGetValue(out bool stillReplacementSingleton), Is.True);
                Assert.That(stillReplacementSingleton, Is.True);

                CallMethodResult replacementCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(replacementCall.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(replacementCall.OutputArguments.Count, Is.EqualTo(1));
                Assert.That(
                    replacementCall.OutputArguments[0].TryGetValue(out ArrayOf<string> replacementTickets),
                    Is.True);
                Assert.That(replacementTickets.ToArray(), Is.EqualTo(s_generation2Tickets));
                runtime1.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));

                await using IPreparedChanges preparedRemove = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Remove(slot1)],
                        cancellationToken)
                    .ConfigureAwait(false);
                LifecycleResult removed = await preparedRemove
                    .CommitAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(removed.CleanupFailure, Is.Null);
                retirements.Add(removed.Retired);
                await removed.Retired.WaitAsync(cancellationToken).ConfigureAwait(false);

                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                    .ConfigureAwait(false);
                NodeState resourcesNode = server.CurrentInstance.DiagnosticsNodeManager
                    .FindPredefinedNode<NodeState>(Ua.ObjectIds.Resources) ??
                    throw new AssertionException("The standard Resources node must remain available.");
                Assert.That(
                    resourcesNode.ReferenceExists(
                        Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)),
                    Is.False);
                DataValue removedRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(removedRead.StatusCode, Is.EqualTo(fallbackRead.StatusCode));
                Assert.That(removedRead.WrappedValue, Is.EqualTo(fallbackRead.WrappedValue));
                CallMethodResult removedCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(removedCall.StatusCode, Is.EqualTo(fallbackCall.StatusCode));
                Assert.That(removedCall.OutputArguments, Is.Empty);

                await using (IPreparedChanges abandonedFreshAdd = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Add(
                            new ProvisionableDeviceNodeManagerFactory(runtime3.Object))],
                        cancellationToken)
                    .ConfigureAwait(false))
                {
                    Assert.That(
                        resourcesNode.ReferenceExists(
                            Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)),
                        Is.False);
                    await AssertResourcesBindingAsync(
                        server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                        .ConfigureAwait(false);
                }
                Assert.That(
                    resourcesNode.ReferenceExists(
                        Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)),
                    Is.False);
                DataValue abandonedRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(abandonedRead.StatusCode, Is.EqualTo(fallbackRead.StatusCode));
                Assert.That(abandonedRead.WrappedValue, Is.EqualTo(fallbackRead.WrappedValue));
                CallMethodResult abandonedCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(abandonedCall.StatusCode, Is.EqualTo(fallbackCall.StatusCode));
                runtime3.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Never);

                NodeManagerSlot? freshSlot = null;
                try
                {
                    await using IPreparedChanges preparedFreshAdd = await server.NodeManagerLifecycle
                        .PrepareAsync(
                            [LifecycleChange.Add(
                                new ProvisionableDeviceNodeManagerFactory(runtime3.Object))],
                            cancellationToken)
                        .ConfigureAwait(false);
                    Assert.That(
                        resourcesNode.ReferenceExists(
                            Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)),
                        Is.False);
                    await AssertResourcesBindingAsync(
                        server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                        .ConfigureAwait(false);
                    LifecycleResult freshAdded = await preparedFreshAdd
                        .CommitAsync(cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    retirements.Add(freshAdded.Retired);
                    if (freshAdded.Registrations.Count == 1)
                    {
                        freshSlot = freshAdded.Registrations[0].Slot;
                    }
                    Assert.That(freshAdded.CleanupFailure, Is.Null);
                    Assert.That(freshAdded.Registrations, Has.Count.EqualTo(1));

                    await AssertResourcesBindingAsync(
                        server, requestHeader, secureChannelContext, expectedPresent: true, cancellationToken)
                        .ConfigureAwait(false);
                    DataValue freshRead = await ReadSingletonAsync(
                        server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                    Assert.That(freshRead.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(freshRead.WrappedValue.TryGetValue(out bool freshSingleton), Is.True);
                    Assert.That(freshSingleton, Is.True);

                    CallMethodResult freshCall = await RequestTicketsAsync(
                        server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                    Assert.That(freshCall.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(freshCall.OutputArguments.Count, Is.EqualTo(1));
                    Assert.That(freshCall.OutputArguments[0].TryGetValue(out ArrayOf<string> freshTickets), Is.True);
                    Assert.That(freshTickets.ToArray(), Is.EqualTo(s_generation3Tickets));
                    runtime3.Verify(
                        runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Once);
                }
                finally
                {
                    if (freshSlot is { } publishedSlot)
                    {
                        await using IPreparedChanges preparedFreshRemove = await server.NodeManagerLifecycle
                            .PrepareAsync(
                                [LifecycleChange.Remove(publishedSlot)],
                                cancellationToken)
                            .ConfigureAwait(false);
                        LifecycleResult freshRemoved = await preparedFreshRemove
                            .CommitAsync(cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        Assert.That(freshRemoved.CleanupFailure, Is.Null);
                        retirements.Add(freshRemoved.Retired);
                        await freshRemoved.Retired.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(
                    resourcesNode.ReferenceExists(
                        Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)),
                    Is.False);
                DataValue freshRemovedRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(freshRemovedRead.StatusCode, Is.EqualTo(fallbackRead.StatusCode));
                Assert.That(freshRemovedRead.WrappedValue, Is.EqualTo(fallbackRead.WrappedValue));
                CallMethodResult freshRemovedCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(freshRemovedCall.StatusCode, Is.EqualTo(fallbackCall.StatusCode));
                Assert.That(freshRemovedCall.OutputArguments, Is.Empty);

                runtime1.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
                runtime3.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                try
                {
                    if (sessionActive && server is not null && requestHeader is not null &&
                        secureChannelContext is not null)
                    {
                        requestHeader.Timestamp = DateTimeUtc.Now;
                        await server.CloseSessionAsync(
                            secureChannelContext, requestHeader, true, RequestLifetime.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        using (var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        {
                            foreach (Task retirement in retirements)
                            {
                                await retirement.WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (fixture.Application is not null)
                            {
                                await fixture.Application.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            if (Directory.Exists(pkiRoot))
                            {
                                Directory.Delete(pkiRoot, recursive: true);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Preserves a fresh device's references and routing while an older same-identifier generation drains.
        /// </summary>
        [Test]
        [Category("NodeManagerLifecycle")]
        [CancelAfter(60_000)]
        // The pinned request intentionally outlives manager retirement and is awaited in cleanup before its session
        // closes; the analyzer does not follow this task lifetime through the test's finally block.
        // TODO: Extract the pinned-call scenario into a helper with an explicit async lifetime owner.
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability",
            "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
            Justification = "The pending call is awaited before CloseSessionAsync disposes its secure-channel context.")]
        public async Task ConnectedSessionRemoveReaddPreservesResourcesAcrossPinnedRetirementAsync(
            CancellationToken cancellationToken)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaResourcesPinnedRetirementTests", Guid.NewGuid().ToString("N"));
            var fixture = new ServerFixture<NoListenerReferenceServer>(
                telemetry => new NoListenerReferenceServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            NoListenerReferenceServer? server = null;
            RequestHeader? requestHeader = null;
            SecureChannelContext? secureChannelContext = null;
            Task<CallMethodResult>? pinnedCall = null;
            var retirements = new List<Task>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<ArrayOf<string>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var oldRuntime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            oldRuntime.SetupGet(runtime => runtime.IsSingleton).Returns(true);
            oldRuntime.Setup(runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    return new ValueTask<ArrayOf<string>>(release.Task.WaitAsync(token));
                });
            var freshRuntime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            freshRuntime.SetupGet(runtime => runtime.IsSingleton).Returns(false);
            freshRuntime.Setup(runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ArrayOf<string>)["fresh-generation-ticket"]);
            var abandonedRuntime = new Mock<IProvisionableDeviceRuntime>(MockBehavior.Strict);
            abandonedRuntime.SetupGet(runtime => runtime.IsSingleton).Returns(true);

            try
            {
                await fixture.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
                Assert.That(
                    await fixture.Application.CheckApplicationInstanceCertificatesAsync(
                        true, ct: cancellationToken).ConfigureAwait(false),
                    Is.True);
                server = new NoListenerReferenceServer(NUnitTelemetryContext.Create());
                await fixture.Application.StartAsync(server, cancellationToken).ConfigureAwait(false);
                (requestHeader, secureChannelContext) = await server.CreateAndActivateSessionAsync(
                    TestContext.CurrentContext.Test.Name,
                    useSecurity: false).ConfigureAwait(false);

                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.GrantedRoleIds)
                    .Returns((ArrayOf<NodeId>)[Ua.ObjectIds.WellKnownRole_AuthenticatedUser]);
                server.CurrentInstance.SessionManager.GetSession(requestHeader.AuthenticationToken)!
                    .RefreshEffectiveIdentity(identity.Object);

                var singletonId = new NodeId(29879u);
                var methodId = new NodeId(29880u);
                (_, IAsyncNodeManager? fallbackOwner) = await server.CurrentInstance.NodeManager
                    .GetManagerHandleAsync(singletonId, cancellationToken).ConfigureAwait(false);
                Assert.That(fallbackOwner, Is.TypeOf<ReferenceServerConfigurationNodeManager>());
                DataValue fallbackRead = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                CallMethodResult fallbackCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(fallbackRead.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(StatusCode.IsBad(fallbackCall.StatusCode), Is.True);

                await using IPreparedChanges preparedOld = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Add(new ProvisionableDeviceNodeManagerFactory(oldRuntime.Object))],
                        cancellationToken).ConfigureAwait(false);
                LifecycleResult oldAdded = await preparedOld.CommitAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                retirements.Add(oldAdded.Retired);
                Assert.That(oldAdded.CleanupFailure, Is.Null);
                Assert.That(oldAdded.Registrations, Has.Count.EqualTo(1));
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: true, cancellationToken)
                    .ConfigureAwait(false);

                var pinnedHeader = new RequestHeader { AuthenticationToken = requestHeader.AuthenticationToken };
                pinnedCall = RequestTicketsAsync(server, pinnedHeader, secureChannelContext, cancellationToken);
                await entered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await using IPreparedChanges preparedRemove = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Remove(oldAdded.Registrations[0].Slot)],
                        cancellationToken).ConfigureAwait(false);
                LifecycleResult removed = await preparedRemove.CommitAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                retirements.Add(removed.Retired);
                Assert.That(removed.CleanupFailure, Is.Null);
                Assert.That(pinnedCall.IsCompleted, Is.False);
                Assert.That(removed.Retired.IsCompleted, Is.False);
                NodeState resources = server.CurrentInstance.DiagnosticsNodeManager
                    .FindPredefinedNode<NodeState>(Ua.ObjectIds.Resources) ??
                    throw new AssertionException("The standard Resources node must remain available.");
                Assert.That(resources.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.False);
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                    .ConfigureAwait(false);

                await AssertStandardFallbackAsync().ConfigureAwait(false);

                await using (IPreparedChanges abandoned = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Add(new ProvisionableDeviceNodeManagerFactory(abandonedRuntime.Object))],
                        cancellationToken).ConfigureAwait(false))
                {
                    Assert.That(pinnedCall.IsCompleted, Is.False);
                    Assert.That(removed.Retired.IsCompleted, Is.False);
                    Assert.That(resources.ReferenceExists(
                        Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.False);
                    await AssertResourcesBindingAsync(
                        server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                        .ConfigureAwait(false);
                    await AssertStandardFallbackAsync().ConfigureAwait(false);
                }
                Assert.That(pinnedCall.IsCompleted, Is.False);
                Assert.That(removed.Retired.IsCompleted, Is.False);
                Assert.That(resources.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.False);
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                    .ConfigureAwait(false);
                await AssertStandardFallbackAsync().ConfigureAwait(false);
                abandonedRuntime.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Never);

                await using IPreparedChanges preparedFresh = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Add(new ProvisionableDeviceNodeManagerFactory(freshRuntime.Object))],
                        cancellationToken).ConfigureAwait(false);
                Assert.That(resources.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.False);
                await AssertStandardFallbackAsync().ConfigureAwait(false);
                LifecycleResult freshAdded = await preparedFresh.CommitAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                retirements.Add(freshAdded.Retired);
                Assert.That(freshAdded.CleanupFailure, Is.Null);
                Assert.That(freshAdded.Registrations, Has.Count.EqualTo(1));
                Assert.That(pinnedCall.IsCompleted, Is.False);
                Assert.That(removed.Retired.IsCompleted, Is.False);
                Assert.That(resources.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.True);
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: true, cancellationToken)
                    .ConfigureAwait(false);
                DataValue beforeRetirement = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(beforeRetirement.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(beforeRetirement.WrappedValue.TryGetValue(out bool beforeSingleton), Is.True);
                Assert.That(beforeSingleton, Is.False);
                CallMethodResult beforeCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(beforeCall.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(beforeCall.OutputArguments.Count, Is.EqualTo(1));
                Assert.That(beforeCall.OutputArguments[0].TryGetValue(out ArrayOf<string> beforeTickets), Is.True);
                Assert.That(beforeTickets.ToArray(), Is.EqualTo(s_freshGenerationTickets));

                release.TrySetResult(["old-generation-ticket"]);
                CallMethodResult oldCall = await pinnedCall.ConfigureAwait(false);
                Assert.That(oldCall.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(oldCall.OutputArguments.Count, Is.EqualTo(1));
                Assert.That(oldCall.OutputArguments[0].TryGetValue(out ArrayOf<string> oldTickets), Is.True);
                Assert.That(oldTickets.ToArray(), Is.EqualTo(s_oldGenerationTickets));
                await removed.Retired.WaitAsync(cancellationToken).ConfigureAwait(false);

                Assert.That(resources.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.True);
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: true, cancellationToken)
                    .ConfigureAwait(false);
                DataValue afterRetirement = await ReadSingletonAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(afterRetirement.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(afterRetirement.WrappedValue.TryGetValue(out bool afterSingleton), Is.True);
                Assert.That(afterSingleton, Is.False);
                CallMethodResult afterCall = await RequestTicketsAsync(
                    server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                Assert.That(afterCall.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(afterCall.OutputArguments.Count, Is.EqualTo(1));
                Assert.That(afterCall.OutputArguments[0].TryGetValue(out ArrayOf<string> afterTickets), Is.True);
                Assert.That(afterTickets.ToArray(), Is.EqualTo(s_freshGenerationTickets));
                oldRuntime.Verify(runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Once);
                freshRuntime.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
                abandonedRuntime.Verify(
                    runtime => runtime.RequestTicketsAsync(It.IsAny<CancellationToken>()), Times.Never);

                await using IPreparedChanges preparedFinalRemove = await server.NodeManagerLifecycle
                    .PrepareAsync(
                        [LifecycleChange.Remove(freshAdded.Registrations[0].Slot)],
                        cancellationToken).ConfigureAwait(false);
                LifecycleResult finalRemoved = await preparedFinalRemove
                    .CommitAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                retirements.Add(finalRemoved.Retired);
                Assert.That(finalRemoved.CleanupFailure, Is.Null);
                await finalRemoved.Retired.WaitAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(resources.ReferenceExists(
                    Ua.ReferenceTypeIds.Organizes, false, new NodeId(29878u)), Is.False);
                await AssertResourcesBindingAsync(
                    server, requestHeader, secureChannelContext, expectedPresent: false, cancellationToken)
                    .ConfigureAwait(false);
                await AssertStandardFallbackAsync().ConfigureAwait(false);

                async Task AssertStandardFallbackAsync()
                {
                    (_, IAsyncNodeManager? readOwner) = await server.CurrentInstance.NodeManager
                        .GetManagerHandleAsync(singletonId, cancellationToken).ConfigureAwait(false);
                    (_, IAsyncNodeManager? callOwner) = await server.CurrentInstance.NodeManager
                        .GetManagerHandleAsync(methodId, cancellationToken).ConfigureAwait(false);
                    Assert.That(readOwner, Is.SameAs(fallbackOwner));
                    Assert.That(callOwner, Is.SameAs(fallbackOwner));
                    DataValue read = await ReadSingletonAsync(
                        server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                    Assert.That(read.StatusCode, Is.EqualTo(fallbackRead.StatusCode));
                    Assert.That(read.WrappedValue, Is.EqualTo(fallbackRead.WrappedValue));
                    CallMethodResult call = await RequestTicketsAsync(
                        server, requestHeader, secureChannelContext, cancellationToken).ConfigureAwait(false);
                    Assert.That(call.StatusCode, Is.EqualTo(fallbackCall.StatusCode));
                    Assert.That(call.OutputArguments, Is.Empty);
                }
            }
            finally
            {
                release.TrySetResult(["old-generation-ticket"]);
                try
                {
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        if (pinnedCall is not null)
                        {
                            await pinnedCall.WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (server is not null && requestHeader is not null && secureChannelContext is not null)
                            {
                                requestHeader.Timestamp = DateTimeUtc.Now;
                                await server.CloseSessionAsync(
                                    secureChannelContext, requestHeader, true, RequestLifetime.None)
                                    .ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            foreach (Task retirement in retirements)
                            {
                                await retirement.WaitAsync(cleanupTimeout.Token).ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (fixture.Application is not null)
                        {
                            await fixture.Application.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(pkiRoot))
                        {
                            Directory.Delete(pkiRoot, recursive: true);
                        }
                    }
                }
            }
        }

        private static async Task AssertResourcesBindingAsync(
            NoListenerReferenceServer server,
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            bool expectedPresent,
            CancellationToken cancellationToken)
        {
            const uint deviceId = 29878;
            requestHeader.Timestamp = DateTimeUtc.Now;
            using var requestLifetime = new RequestLifetime(cancellationToken);
            BrowseResponse response = await server.BrowseAsync(
                secureChannelContext,
                requestHeader,
                null,
                0,
                [DescribeBrowse(Ua.ObjectIds.Resources, BrowseDirection.Forward)],
                requestLifetime).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            BrowseResult result = response.Results[0];
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.ContinuationPoint.IsEmpty, Is.True);
            List<ReferenceDescription> references = result.References.ToList();
            QualifiedName expectedBrowseName = new("ProvisionableDevice", 0);
            ExpandedNodeId expectedTarget = new(deviceId);
            List<ReferenceDescription> byName = references
                .Where(reference => reference.BrowseName == expectedBrowseName)
                .ToList();
            List<ReferenceDescription> byTarget = references
                .Where(reference => reference.NodeId == expectedTarget)
                .ToList();

            Assert.That(byName, Has.Count.EqualTo(expectedPresent ? 1 : 0));
            Assert.That(byTarget, Has.Count.EqualTo(expectedPresent ? 1 : 0));
            if (!expectedPresent)
            {
                return;
            }

            ReferenceDescription device = byName[0];
            Assert.Multiple(() =>
            {
                Assert.That(device.NodeId, Is.EqualTo(expectedTarget));
                Assert.That(device.BrowseName, Is.EqualTo(expectedBrowseName));
                Assert.That(device.NodeClass, Is.EqualTo(NodeClass.Object));
                Assert.That(device.ReferenceTypeId, Is.EqualTo(Ua.ReferenceTypeIds.Organizes));
                Assert.That(device.IsForward, Is.True);
                Assert.That(device.TypeDefinition, Is.EqualTo(new ExpandedNodeId(26871u)));
            });
        }

        private static async Task<DataValue> ReadSingletonAsync(
            NoListenerReferenceServer server,
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken)
        {
            requestHeader.Timestamp = DateTimeUtc.Now;
            using var requestLifetime = new RequestLifetime(cancellationToken);
            ReadResponse response = await server.ReadAsync(
                secureChannelContext,
                requestHeader,
                0,
                TimestampsToReturn.Neither,
                [
                    new ReadValueId
                    {
                        NodeId = new NodeId(29879u),
                        AttributeId = Attributes.Value
                    }
                ],
                requestLifetime).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            return response.Results[0];
        }

        private static async Task<CallMethodResult> RequestTicketsAsync(
            NoListenerReferenceServer server,
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            CancellationToken cancellationToken)
        {
            requestHeader.Timestamp = DateTimeUtc.Now;
            using var requestLifetime = new RequestLifetime(cancellationToken);
            CallResponse response = await server.CallAsync(
                secureChannelContext,
                requestHeader,
                [
                    new CallMethodRequest
                    {
                        ObjectId = new NodeId(29878u),
                        MethodId = new NodeId(29880u),
                        InputArguments = []
                    }
                ],
                requestLifetime).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            return response.Results[0];
        }

        /// <summary>
        /// Uses real StandardServer internals while deliberately creating no listening service hosts.
        /// </summary>
        private sealed class NoListenerReferenceServer : ReferenceServer
        {
            public NoListenerReferenceServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            protected override ValueTask<ServiceHostInitializationResult> InitializeServiceHostsAsync(
                ApplicationConfiguration configuration,
                ITransportBindingRegistry bindingFactory,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var serverDescription = new ApplicationDescription
                {
                    ApplicationUri = configuration.ApplicationUri,
                    ProductUri = configuration.ProductUri,
                    ApplicationName = new LocalizedText(configuration.ApplicationName),
                    ApplicationType = ApplicationType.Server
                };
                var endpoint = new EndpointDescription
                {
                    EndpointUrl = configuration.ServerConfiguration!.BaseAddresses[0],
                    Server = serverDescription,
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    TransportProfileUri = Profiles.UaTcpTransport,
                    UserIdentityTokens =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "anonymous",
                            TokenType = UserTokenType.Anonymous,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ]
                };
                return new ValueTask<ServiceHostInitializationResult>(
                    new ServiceHostInitializationResult([], serverDescription, [endpoint]));
            }
        }

        /// <summary>
        /// Requests complete Organizes references in the specified direction.
        /// </summary>
        private static BrowseDescription DescribeBrowse(NodeId nodeId, BrowseDirection direction)
        {
            return new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = direction,
                ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                IncludeSubtypes = false,
                ResultMask = (uint)BrowseResultMask.All
            };
        }

        /// <summary>
        /// Requires exactly one reference with the requested browse-name text.
        /// </summary>
        private static ReferenceDescription FindByName(List<ReferenceDescription> references, string name)
        {
            List<ReferenceDescription> matches = references
                .Where(reference => reference.BrowseName.Name == name)
                .ToList();
            Assert.That(
                matches,
                Has.Count.EqualTo(1),
                $"Expected one {name} reference; found " +
                    string.Join(", ", matches.Select(reference => reference.NodeId)));
            return matches[0];
        }

        /// <summary>The ticket returned by the signing identity provider.</summary>
        private static readonly string[] s_signedTickets = ["signed-ticket"];

        /// <summary>The ticket returned by the first runtime generation.</summary>
        private static readonly string[] s_generation1Tickets = ["generation-1-ticket"];

        /// <summary>The ticket returned by the replacement runtime generation.</summary>
        private static readonly string[] s_generation2Tickets = ["generation-2-ticket"];

        /// <summary>The ticket returned by the third runtime generation.</summary>
        private static readonly string[] s_generation3Tickets = ["generation-3-ticket"];

        /// <summary>The ticket returned by a newly registered runtime generation.</summary>
        private static readonly string[] s_freshGenerationTickets = ["fresh-generation-ticket"];

        /// <summary>The ticket returned by the retired runtime generation.</summary>
        private static readonly string[] s_oldGenerationTickets = ["old-generation-ticket"];
    }
}
