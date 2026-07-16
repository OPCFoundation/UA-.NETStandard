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
    /// Unit tests for <see cref="RequestServerStateChangeStartupTask"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RequestServerStateChangeStartupTaskTests
    {
        [Test]
        public void ConstructorThrowsOnNullOptions()
        {
            Assert.That(
                () => new RequestServerStateChangeStartupTask((RequestServerStateChangeOptions)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void OnServerStartedThrowsOnNullServer()
        {
            var task = new RequestServerStateChangeStartupTask();

            Assert.That(async () => await task.OnServerStartedAsync(null!).ConfigureAwait(false), Throws.ArgumentNullException);
        }

        [Test]
        public async Task RequestShutdownPublishesMaintenanceAndEstimatedReturnTimeAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            Mock<IServiceLevelController> controller = new();
            RequestServerStateChangeStartupTask task = CreateTask(controller.Object);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            DateTimeUtc estimatedReturnTime = DateTimeUtc.Now;
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                estimatedReturnTime,
                10,
                new LocalizedText("maintenance"),
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(method.NodeId, Is.EqualTo(MethodIds.Server_RequestServerStateChange));
            controller.Verify(c => c.SetServiceLevel(ServiceLevels.Maintenance), Times.Once);
            PropertyState<DateTimeUtc> estimatedReturnTimeNode =
                loaded.Manager.FindPredefinedNode<PropertyState<DateTimeUtc>>(
                    VariableIds.Server_EstimatedReturnTime);
            Assert.That(estimatedReturnTimeNode.Value, Is.EqualTo(estimatedReturnTime));
        }

        [Test]
        public async Task RequestFailedPublishesNoDataWhenNoControllerIsConfiguredAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Failed,
                DateTimeUtc.Now,
                0,
                new LocalizedText("failed"),
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            PropertyState<byte> serviceLevel =
                loaded.Manager.FindPredefinedNode<PropertyState<byte>>(VariableIds.Server_ServiceLevel);
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.NoData));
        }

        [Test]
        public async Task RequestReturnsAccessDeniedWhenAdminValidationFailsAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new RequestServerStateChangeOptions
            {
                AdminAccessValidator = _ => throw new ServiceResultException(StatusCodes.BadUserAccessDenied)
            };
            var task = new RequestServerStateChangeStartupTask(options);
            PropertyState<byte> serviceLevel =
                loaded.Manager.FindPredefinedNode<PropertyState<byte>>(VariableIds.Server_ServiceLevel);
            serviceLevel.Value = ServiceLevels.Maximum;

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.Now,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.Maximum));
        }

        [Test]
        public async Task RequestUsesConfigurationNodeManagerWhenAdminValidatorIsNotConfiguredAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            RequestServerStateChangeStartupTask task = new(new RequestServerStateChangeOptions());

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.Now,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            loaded.ConfigurationNodeManager.Verify(
                manager => manager.HasApplicationSecureAdminAccess(It.IsAny<ISystemContext>()),
                Times.Once);
        }

        [Test]
        public async Task RequestSuspendedPublishesMaintenanceAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Suspended,
                DateTimeUtc.Now,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            PropertyState<byte> serviceLevel =
                loaded.Manager.FindPredefinedNode<PropertyState<byte>>(VariableIds.Server_ServiceLevel);
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.Maintenance));
        }

        [Test]
        public async Task RequestTestPublishesNoDataAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Test,
                DateTimeUtc.Now,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            PropertyState<byte> serviceLevel =
                loaded.Manager.FindPredefinedNode<PropertyState<byte>>(VariableIds.Server_ServiceLevel);
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.NoData));
        }

        [Test]
        public async Task RequestUsesCustomServiceLevelSelectorAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var options = new RequestServerStateChangeOptions
            {
                AdminAccessValidator = _ => { },
                ServiceLevelSelector = state => state == ServerState.Running ? (byte)200 : (byte)100
            };
            var task = new RequestServerStateChangeStartupTask(options);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Running,
                DateTimeUtc.Now,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            PropertyState<byte> serviceLevel =
                loaded.Manager.FindPredefinedNode<PropertyState<byte>>(VariableIds.Server_ServiceLevel);
            Assert.That(serviceLevel.Value, Is.EqualTo(200));
        }

        [Test]
        public async Task RequestUpdatesServerStatusFieldsAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            var estimatedReturnTime = new DateTimeUtc(638000000000000000);
            var reason = new LocalizedText("Planned maintenance");
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                estimatedReturnTime,
                300,
                reason,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            ServerStatusState serverStatus = loaded.Manager.FindPredefinedNode<ServerStatusState>(
                VariableIds.Server_ServerStatus);
            Assert.That(serverStatus.State!.Value, Is.EqualTo(ServerState.Shutdown));
            Assert.That(serverStatus.SecondsTillShutdown!.Value, Is.EqualTo(300));
            Assert.That(serverStatus.ShutdownReason!.Value.Text, Is.EqualTo("Planned maintenance"));
        }

        [Test]
        public async Task RequestAcceptsRestartFlagAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method =
                loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                    MethodIds.Server_RequestServerStateChange);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.Now,
                10,
                LocalizedText.Null,
                restart: true);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
        }

        private static RequestServerStateChangeStartupTask CreateTask(
            IServiceLevelController? controller = null)
        {
            return new RequestServerStateChangeStartupTask(
                new RequestServerStateChangeOptions
                {
                    AdminAccessValidator = _ => { }
                },
                controller);
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
            var configurationNodeManager = new Mock<IConfigurationNodeManager>();

            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(typeTree);
            server.Setup(s => s.MessageContext).Returns(messageContext);
            server.Setup(s => s.CoreNodeManager).Returns(coreNodeManager.Object);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.ConfigurationNodeManager).Returns(configurationNodeManager.Object);

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
            return new LoadedDiagnosticsServer(server, manager, configurationNodeManager);
        }

        private sealed class LoadedDiagnosticsServer : IDisposable
        {
            public LoadedDiagnosticsServer(
                Mock<IServerInternal> server,
                DiagnosticsNodeManager manager,
                Mock<IConfigurationNodeManager> configurationNodeManager)
            {
                Server = server;
                Manager = manager;
                ConfigurationNodeManager = configurationNodeManager;
            }

            public Mock<IServerInternal> Server { get; }

            public DiagnosticsNodeManager Manager { get; }

            public Mock<IConfigurationNodeManager> ConfigurationNodeManager { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }
    }
}
