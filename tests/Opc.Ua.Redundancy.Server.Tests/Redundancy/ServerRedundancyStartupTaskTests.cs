/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
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
            Assert.That(options.PeerServiceLevel, Is.EqualTo(ServiceLevels.Maximum));
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
            Assert.That(HasChild(loaded, redundancy, BrowseNames.ServerUriArray), Is.True);
            Assert.That(HasChild(loaded, redundancy, BrowseNames.CurrentServerId), Is.False);
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

        private static async Task<LoadedDiagnosticsServer> CreateLoadedServerAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("http://opcfoundation.org/UA/");
            var serverUris = new StringTable();
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
