// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="ServerRedundancyStartupTask"/> and
    /// <see cref="ServerRedundancyController"/> subtype materialization of
    /// <c>Server.ServerRedundancy</c>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class ServerRedundancyStartupTaskTests
    {
        [Test]
        public void ConstructorThrowsOnNullController()
        {
            Assert.That(() => new ServerRedundancyStartupTask(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void ControllerConstructorThrowsOnNullOptions()
        {
            Assert.That(() => new ServerRedundancyController(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void OptionsUseSingleServerDefaults()
        {
            var options = new ServerRedundancyOptions();

            Assert.That(options.Mode, Is.EqualTo(RedundancySupport.None));
            Assert.That(options.PeerServerUris, Is.Empty);
            Assert.That(options.CurrentServerId, Is.Not.Empty);
            Assert.That(options.PeerStatusLifetime, Is.GreaterThan(options.PeerStatusUpdateInterval));
        }

        [Test]
        public void GetTypeDefinitionIdMapsModes()
        {
            Assert.That(ServerRedundancyController.GetTypeDefinitionId(RedundancySupport.None),
                Is.EqualTo(ObjectTypeIds.ServerRedundancyType));
            Assert.That(ServerRedundancyController.GetTypeDefinitionId(RedundancySupport.Transparent),
                Is.EqualTo(ObjectTypeIds.TransparentRedundancyType));
            Assert.That(ServerRedundancyController.GetTypeDefinitionId(RedundancySupport.Hot),
                Is.EqualTo(ObjectTypeIds.NonTransparentRedundancyType));
            Assert.That(ServerRedundancyController.GetTypeDefinitionId(RedundancySupport.Cold),
                Is.EqualTo(ObjectTypeIds.NonTransparentRedundancyType));
        }

        [Test]
        public void OnServerStartedThrowsOnNullServer()
        {
            var task = new ServerRedundancyStartupTask(
                new ServerRedundancyController(new ServerRedundancyOptions()));

            Assert.That(
                async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedNoOpsWhenServerObjectMissingAsync()
        {
            var task = new ServerRedundancyStartupTask(
                new ServerRedundancyController(new ServerRedundancyOptions()));
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.ServerObject).Returns((ServerObjectState)null!);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            server.VerifyGet(s => s.ServerObject, Times.Once);
        }

        [Test]
        public async Task NoneModeKeepsBaseTypeWithoutSubtypeChildrenAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var task = new ServerRedundancyStartupTask(
                new ServerRedundancyController(new ServerRedundancyOptions()));

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);

            ServerRedundancyState redundancy = loaded.Server.Object.ServerObject.ServerRedundancy!;
            Assert.That(redundancy.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.ServerRedundancyType));
            Assert.That(redundancy, Is.Not.InstanceOf<TransparentRedundancyState>());
            Assert.That(redundancy, Is.Not.InstanceOf<NonTransparentRedundancyState>());
            Assert.That(redundancy.RedundancySupport!.Value, Is.EqualTo(RedundancySupport.None));
            Assert.That(HasChild(loaded, redundancy, BrowseNames.CurrentServerId), Is.False);
            Assert.That(HasChild(loaded, redundancy, BrowseNames.ServerUriArray), Is.False);
        }

        [Test]
        public async Task TransparentModePromotesToTransparentSubtypeAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new ServerRedundancyOptions
            {
                Mode = RedundancySupport.Transparent,
                CurrentServerId = "replica-a"
            };
            options.PeerServerUris.Add("urn:peer-a");
            var task = new ServerRedundancyStartupTask(new ServerRedundancyController(options));

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);

            ServerRedundancyState redundancy = loaded.Server.Object.ServerObject.ServerRedundancy!;
            Assert.That(redundancy.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.TransparentRedundancyType));
            Assert.That(redundancy, Is.InstanceOf<TransparentRedundancyState>());

            var transparent = (TransparentRedundancyState)redundancy;
            Assert.That(transparent.CurrentServerId, Is.Not.Null);
            Assert.That(transparent.CurrentServerId!.Value, Is.EqualTo("replica-a"));
            Assert.That(transparent.CurrentServerId.NodeId,
                Is.EqualTo(VariableIds.Server_ServerRedundancy_CurrentServerId));
            Assert.That(transparent.RedundantServerArray!.Value[0].ServerId, Is.EqualTo("urn:peer-a"));
            Assert.That(HasChild(loaded, redundancy, BrowseNames.CurrentServerId), Is.True);
            Assert.That(HasChild(loaded, redundancy, BrowseNames.ServerUriArray), Is.False);
        }

        [Test]
        public async Task NonTransparentModePromotesToNonTransparentSubtypeAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new ServerRedundancyOptions
            {
                Mode = RedundancySupport.Hot
            };
            options.PeerServerUris.Add("urn:peer-a");
            options.PeerServerUris.Add("urn:peer-b");
            var task = new ServerRedundancyStartupTask(new ServerRedundancyController(options));

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);

            ServerRedundancyState redundancy = loaded.Server.Object.ServerObject.ServerRedundancy!;
            Assert.That(redundancy.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.NonTransparentRedundancyType));
            Assert.That(redundancy, Is.InstanceOf<NonTransparentRedundancyState>());

            var nonTransparent = (NonTransparentRedundancyState)redundancy;
            Assert.That(nonTransparent.ServerUriArray, Is.Not.Null);
            Assert.That(nonTransparent.ServerUriArray!.Value, Is.EqualTo(["urn:peer-a", "urn:peer-b"]));
            Assert.That(nonTransparent.ServerUriArray.NodeId,
                Is.EqualTo(VariableIds.Server_ServerRedundancy_ServerUriArray));
            Assert.That(nonTransparent.RedundantServerArray!.Value[0].ServerId, Is.EqualTo("urn:peer-a"));
            Assert.That(nonTransparent.RedundantServerArray.Value[1].ServerId, Is.EqualTo("urn:peer-b"));
            AssertPeerStatus(loaded, 0, "urn:peer-a", ServerState.Unknown, 0);
            AssertPeerStatus(loaded, 1, "urn:peer-b", ServerState.Unknown, 0);
            Assert.That(HasChild(loaded, redundancy, BrowseNames.ServerUriArray), Is.True);
            Assert.That(HasChild(loaded, redundancy, BrowseNames.CurrentServerId), Is.False);
        }

        [TestCase(RedundancySupport.Hot)]
        [TestCase(RedundancySupport.Transparent)]
        public async Task PeerArrayReflectsLiveServerStatusAndExpiresSilentPeersAsync(RedundancySupport mode)
        {
            const string serverA = "urn:server:a";
            const string serverB = "urn:server:b";
            bool transparent = mode == RedundancySupport.Transparent;
            string peer = transparent ? "replica-b" : serverB;
            string keyPrefix = transparent
                ? PeerStatusStartupTask.TransparentKeyPrefix
                : PeerStatusStartupTask.KeyPrefix;
            using LoadedDiagnosticsServer loadedA = await CreateLoadedServerAsync(transparent ? serverB : serverA);
            using LoadedDiagnosticsServer loadedB = await CreateLoadedServerAsync(serverB);
            using var store = new InMemorySharedKeyValueStore();
            using var random = RandomNumberGenerator.Create();
            byte[] key = new byte[32];
            random.GetBytes(key);
            using var protector = new AesCbcHmacRecordProtector(key);
            var time = new FakeTimeProvider();
            var optionsA = new ServerRedundancyOptions { Mode = mode, CurrentServerId = "replica-a" };
            optionsA.PeerServerUris.Add(peer);
            optionsA.PeerServerUris.Add("urn:missing");
            var optionsB = new ServerRedundancyOptions { Mode = mode, CurrentServerId = "replica-b" };
            using var controllerA = new ServerRedundancyController(optionsA);
            using var controllerB = new ServerRedundancyController(optionsB);
            await controllerA.AttachAsync(loadedA.Server.Object);
            await controllerB.AttachAsync(loadedB.Server.Object);
            await using var taskA = new PeerStatusStartupTask(controllerA, store, protector, time);
            await using var taskB = new PeerStatusStartupTask(controllerB, store, protector, time);

            loadedB.Server.SetupGet(s => s.CurrentState).Returns(ServerState.Running);
            loadedB.Server.Object.ServerObject.ServiceLevel!.Value = 175;
            await taskB.RefreshAsync(loadedB.Server.Object, NullLogger.Instance, default);
            await taskA.OnServerStartedAsync(loadedA.Server.Object);
            AssertPeerStatus(loadedA, 0, peer, ServerState.Running, 175);
            AssertPeerStatus(loadedA, 1, "urn:missing", ServerState.Unknown, 0);

            // A valid record for one peer identifier cannot be reused under another peer's key.
            (bool found, ByteString value) = await store.TryGetAsync(keyPrefix + peer);
            Assert.That(found, Is.True);
            await store.SetAsync(keyPrefix + "urn:missing", value);

            loadedB.Server.SetupGet(s => s.CurrentState).Returns(ServerState.Suspended);
            loadedB.Server.Object.ServerObject.ServiceLevel!.Value = 0;
            await taskB.RefreshAsync(loadedB.Server.Object, NullLogger.Instance, default);
            time.Advance(optionsA.PeerStatusUpdateInterval);
            await WaitForPeerStateAsync(loadedA, ServerState.Suspended);
            AssertPeerStatus(loadedA, 0, peer, ServerState.Suspended, 0);
            AssertPeerStatus(loadedA, 1, "urn:missing", ServerState.Unknown, 0);

            await taskA.DisposeAsync();
            time.Advance(optionsA.PeerStatusLifetime);
            await using var refreshA = new PeerStatusStartupTask(controllerA, store, protector, time);
            await refreshA.RefreshAsync(loadedA.Server.Object, NullLogger.Instance, default);
            AssertPeerStatus(loadedA, 0, peer, ServerState.Unknown, 0);
            AssertPeerStatus(loadedA, 1, "urn:missing", ServerState.Unknown, 0);
        }

        [Test]
        public async Task ChangeModeAtRuntimeSwapsSubtypeAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new ServerRedundancyOptions();
            options.PeerServerUris.Add("urn:peer-a");
            var controller = new ServerRedundancyController(options);
            var task = new ServerRedundancyStartupTask(controller);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            Assert.That(loaded.Server.Object.ServerObject.ServerRedundancy!.TypeDefinitionId,
                Is.EqualTo(ObjectTypeIds.ServerRedundancyType));

            // None -> Transparent
            options.CurrentServerId = "replica-a";
            await controller.ChangeModeAsync(RedundancySupport.Transparent).ConfigureAwait(false);

            Assert.That(controller.Mode, Is.EqualTo(RedundancySupport.Transparent));
            ServerRedundancyState afterTransparent = loaded.Server.Object.ServerObject.ServerRedundancy!;
            Assert.That(afterTransparent, Is.InstanceOf<TransparentRedundancyState>());
            Assert.That(((TransparentRedundancyState)afterTransparent).CurrentServerId!.Value,
                Is.EqualTo("replica-a"));
            Assert.That(HasChild(loaded, afterTransparent, BrowseNames.CurrentServerId), Is.True);

            // Transparent -> Hot: subtype swaps, CurrentServerId child gone, ServerUriArray present
            await controller.ChangeModeAsync(RedundancySupport.Hot).ConfigureAwait(false);

            ServerRedundancyState afterHot = loaded.Server.Object.ServerObject.ServerRedundancy!;
            Assert.That(afterHot, Is.InstanceOf<NonTransparentRedundancyState>());
            Assert.That(((NonTransparentRedundancyState)afterHot).ServerUriArray!.Value,
                Is.EqualTo(["urn:peer-a"]));
            Assert.That(HasChild(loaded, afterHot, BrowseNames.ServerUriArray), Is.True);
            Assert.That(HasChild(loaded, afterHot, BrowseNames.CurrentServerId), Is.False);
            Assert.That(
                loaded.Manager.FindPredefinedNode<PropertyState<string>>(
                    VariableIds.Server_ServerRedundancy_CurrentServerId),
                Is.Null);
        }

        private static bool HasChild(
            LoadedDiagnosticsServer loaded,
            ServerRedundancyState redundancy,
            string browseName)
        {
            var children = new List<BaseInstanceState>();
            redundancy.GetChildren(loaded.Server.Object.DefaultSystemContext, children);
            return children.Exists(child => child.BrowseName.Name == browseName);
        }

        private static void AssertPeerStatus(
            LoadedDiagnosticsServer loaded, int index, string uri, ServerState state, byte level)
        {
            RedundantServerDataType peer = loaded.Server.Object.ServerObject
                .ServerRedundancy!.RedundantServerArray!.Value[index];
            Assert.That(peer.ServerId, Is.EqualTo(uri));
            Assert.That(peer.ServerState, Is.EqualTo(state));
            Assert.That(peer.ServiceLevel, Is.EqualTo(level));
        }

        private static async Task WaitForPeerStateAsync(LoadedDiagnosticsServer loaded, ServerState state)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (loaded.Server.Object.ServerObject.ServerRedundancy!.RedundantServerArray!.Value[0].ServerState !=
                state)
            {
                await Task.Delay(10, timeout.Token);
            }
        }

        private static async Task<LoadedDiagnosticsServer> CreateLoadedServerAsync(string? uri = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://opcfoundation.org/UA/");
            var serverUris = new StringTable();
            if (uri != null)
            {
                serverUris.Append(uri);
            }
            var typeTree = new TypeTable(namespaceUris);
            var messageContext = new ServiceMessageContext(telemetry, EncodeableFactory.Create())
            {
                NamespaceUris = namespaceUris,
                ServerUris = serverUris
            };

            var server = new Mock<IServerInternal>();
            var coreNodeManager = new Mock<ICoreNodeManager>();
            coreNodeManager.Setup(m => m.ImportNodesAsync(
                It.IsAny<ISystemContext>(),
                It.IsAny<IList<NodeState>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            var masterNodeManager = new Mock<IMasterNodeManager>();
            masterNodeManager.Setup(m => m.RemoveReferencesAsync(
                It.IsAny<List<LocalReference>>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());

            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(typeTree);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.CoreNodeManager).Returns(coreNodeManager.Object);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());

            var context = new ServerSystemContext(server.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(context);

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration()
            };
            var manager = new DiagnosticsNodeManager(server.Object, configuration, NullLogger.Instance);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ServerObjectState serverObject = manager.FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            server.Setup(s => s.ServerObject).Returns(serverObject);
            server.Setup(s => s.DiagnosticsNodeManager).Returns(manager);
            server.Setup(s => s.FindNodeManagers<IPredefinedNodeSubtypeReplacer>()).Returns([manager]);
            return new LoadedDiagnosticsServer(server, manager);
        }

        private sealed class LoadedDiagnosticsServer : IDisposable
        {
            public LoadedDiagnosticsServer(
                Mock<IServerInternal> server,
                DiagnosticsNodeManager manager)
            {
                Server = server;
                Manager = manager;
            }

            public Mock<IServerInternal> Server { get; }

            public DiagnosticsNodeManager Manager { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }
    }
}
