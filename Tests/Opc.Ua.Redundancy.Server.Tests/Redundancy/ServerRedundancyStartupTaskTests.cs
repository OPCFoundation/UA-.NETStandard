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
    /// Unit tests for <see cref="ServerRedundancyStartupTask"/> guard paths.
    /// The live node population path is exercised by the hosted-server
    /// end-to-end test.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class ServerRedundancyStartupTaskTests
    {
        [Test]
        public void ConstructorThrowsOnNullOptions()
        {
            Assert.That(() => new ServerRedundancyStartupTask(null!), Throws.ArgumentNullException);
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
        public void OnServerStartedThrowsOnNullServer()
        {
            var task = new ServerRedundancyStartupTask(new ServerRedundancyOptions());

            Assert.That(async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false), Throws.ArgumentNullException);
        }

        [Test]
        public async Task OnServerStartedNoOpsWhenServerObjectMissingAsync()
        {
            var task = new ServerRedundancyStartupTask(new ServerRedundancyOptions());
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.ServerObject).Returns((ServerObjectState)null!);

            await task.OnServerStartedAsync(server.Object).ConfigureAwait(false);

            server.VerifyGet(s => s.ServerObject, Times.Once);
        }

        [Test]
        public async Task OnServerStartedLeavesSubtypeMembersAbsentForNoneAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var task = new ServerRedundancyStartupTask(new ServerRedundancyOptions());

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);

            PropertyState<string> currentServerId =
                loaded.Manager.FindPredefinedNode<PropertyState<string>>(
                    VariableIds.Server_ServerRedundancy_CurrentServerId);
            PropertyState<ArrayOf<string>> serverUriArray =
                loaded.Manager.FindPredefinedNode<PropertyState<ArrayOf<string>>>(
                    VariableIds.Server_ServerRedundancy_ServerUriArray);
            Assert.That(currentServerId, Is.Not.Null);
            Assert.That(loaded.Server.Object.ServerObject.ServerRedundancy!.TypeDefinitionId,
                Is.EqualTo(ServerRedundancyTypeId));
            Assert.That(currentServerId!.NodeId, Is.EqualTo(VariableIds.Server_ServerRedundancy_CurrentServerId));
            Assert.That(currentServerId.Value, Is.Null);
            Assert.That(serverUriArray, Is.Not.Null);
            Assert.That(serverUriArray!.NodeId, Is.EqualTo(VariableIds.Server_ServerRedundancy_ServerUriArray));
            Assert.That(serverUriArray.Value, Is.Empty);
        }

        [Test]
        public async Task OnServerStartedAddsCurrentServerIdForTransparentModeAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new ServerRedundancyOptions
            {
                Mode = RedundancySupport.Transparent,
                CurrentServerId = "replica-a"
            };
            options.PeerServerUris.Add("urn:peer-a");
            var task = new ServerRedundancyStartupTask(options);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);

            PropertyState<string> currentServerId =
                loaded.Manager.FindPredefinedNode<PropertyState<string>>(
                    VariableIds.Server_ServerRedundancy_CurrentServerId);
            PropertyState<ArrayOf<RedundantServerDataType>> redundantServerArray =
                loaded.Manager.FindPredefinedNode<PropertyState<ArrayOf<RedundantServerDataType>>>(
                    VariableIds.Server_ServerRedundancy_RedundantServerArray);
            Assert.That(currentServerId!.Value, Is.EqualTo("replica-a"));
            Assert.That(loaded.Server.Object.ServerObject.ServerRedundancy!.TypeDefinitionId,
                Is.EqualTo(TransparentRedundancyTypeId));
            Assert.That(currentServerId.NodeId, Is.EqualTo(VariableIds.Server_ServerRedundancy_CurrentServerId));
            Assert.That(redundantServerArray!.Value[0].ServerId, Is.EqualTo("urn:peer-a"));
        }

        [Test]
        public async Task OnServerStartedAddsServerUriArrayForNonTransparentModeAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new ServerRedundancyOptions
            {
                Mode = RedundancySupport.Hot
            };
            options.PeerServerUris.Add("urn:peer-a");
            options.PeerServerUris.Add("urn:peer-b");
            var task = new ServerRedundancyStartupTask(options);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);

            PropertyState<ArrayOf<string>> serverUriArray =
                loaded.Manager.FindPredefinedNode<PropertyState<ArrayOf<string>>>(
                    VariableIds.Server_ServerRedundancy_ServerUriArray);
            PropertyState<ArrayOf<RedundantServerDataType>> redundantServerArray =
                loaded.Manager.FindPredefinedNode<PropertyState<ArrayOf<RedundantServerDataType>>>(
                    VariableIds.Server_ServerRedundancy_RedundantServerArray);
            Assert.That(serverUriArray!.Value, Is.EqualTo(["urn:peer-a", "urn:peer-b"]));
            Assert.That(loaded.Server.Object.ServerObject.ServerRedundancy!.TypeDefinitionId,
                Is.EqualTo(NonTransparentRedundancyTypeId));
            Assert.That(serverUriArray.NodeId, Is.EqualTo(VariableIds.Server_ServerRedundancy_ServerUriArray));
            Assert.That(redundantServerArray!.Value[0].ServerId, Is.EqualTo("urn:peer-a"));
            Assert.That(redundantServerArray.Value[1].ServerId, Is.EqualTo("urn:peer-b"));
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

        private static readonly NodeId ServerRedundancyTypeId = new(2034);
        private static readonly NodeId TransparentRedundancyTypeId = new(2036);
        private static readonly NodeId NonTransparentRedundancyTypeId = new(2039);
    }
}
