// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
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
        public async Task RequestRejectsRestartBecauseTheServerCannotRestartItselfAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            using RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.Now,
                10,
                LocalizedText.Null,
                restart: true);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(ReadState(loaded), Is.EqualTo(ServerState.Running));
        }

        [Test]
        public async Task RequestSuspendedIsReturnedByServerStatusReadAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            using RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Suspended,
                DateTimeUtc.MinValue,
                0,
                new LocalizedText("maintenance"),
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(ReadState(loaded), Is.EqualTo(ServerState.Suspended));
            Assert.That(loaded.Status.Value.State, Is.EqualTo(ServerState.Suspended));
            Assert.That(loaded.Server.Object.CurrentState, Is.EqualTo(ServerState.Suspended));

            Variant statusValue = ReadValue(loaded.StatusVariable, loaded);
            Assert.That(statusValue.TryGetStructure<ServerStatusDataType>(out ServerStatusDataType? status), Is.True);
            Assert.That(status!.State, Is.EqualTo(ServerState.Suspended));
            Assert.That(status.ShutdownReason.Text, Is.EqualTo("maintenance"));
        }

        [Test]
        public async Task RequestShutdownIsReturnedByServerStatusChildReadsAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            using RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.MinValue,
                300,
                new LocalizedText("Planned maintenance"),
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(ReadState(loaded), Is.EqualTo(ServerState.Shutdown));
            Assert.That(
                ReadValue(loaded.StatusVariable.SecondsTillShutdown!, loaded).TryGetValue(out uint seconds),
                Is.True);
            Assert.That(seconds, Is.EqualTo(300));
            Assert.That(
                ReadValue(loaded.StatusVariable.ShutdownReason!, loaded).TryGetValue(out LocalizedText reason),
                Is.True);
            Assert.That(reason.Text, Is.EqualTo("Planned maintenance"));
        }

        [TestCase(ServerState.Unknown)]
        [TestCase((ServerState)8)]
        [TestCase((ServerState)(-1))]
        [TestCase((ServerState)int.MaxValue)]
        public async Task RequestRejectsStateThatIsNotAllowedAsync(ServerState state)
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            Mock<IServiceLevelController> controller = new();
            using RequestServerStateChangeStartupTask task = CreateTask(controller.Object);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                state,
                DateTimeUtc.MinValue,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(ReadState(loaded), Is.EqualTo(ServerState.Running));
            controller.Verify(c => c.SetServiceLevel(It.IsAny<byte>()), Times.Never);
        }

        [Test]
        public async Task RequestRunningSetsEstimatedReturnTimeToNullAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            using RequestServerStateChangeStartupTask task = CreateTask();

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            PropertyState<DateTimeUtc> estimatedReturnTimeNode =
                loaded.Manager.FindPredefinedNode<PropertyState<DateTimeUtc>>(
                    VariableIds.Server_EstimatedReturnTime);

            ServiceResult suspend = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Suspended,
                new DateTimeUtc(638000000000000000),
                0,
                new LocalizedText("maintenance"),
                restart: false);
            Assert.That(suspend, Is.EqualTo(ServiceResult.Good));
            Assert.That(estimatedReturnTimeNode.Value, Is.EqualTo(new DateTimeUtc(638000000000000000)));

            ServiceResult resume = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Running,
                new DateTimeUtc(638000000000000000),
                0,
                new LocalizedText("ignored"),
                restart: false);

            Assert.That(resume, Is.EqualTo(ServiceResult.Good));
            Assert.That(estimatedReturnTimeNode.Value, Is.EqualTo(DateTimeUtc.MinValue));
            Assert.That(ReadState(loaded), Is.EqualTo(ServerState.Running));
            Assert.That(loaded.Status.Value.ShutdownReason.IsNullOrEmpty, Is.True);
            Assert.That(loaded.Server.Object.ServerObject.ServiceLevel!.Value, Is.EqualTo(ServiceLevels.Maximum));
        }

        [TestCase(RedundancySupport.Cold, ServiceLevels.NoData)]
        [TestCase(RedundancySupport.Warm, (byte)178)]
        public async Task RequestRunningRestoresStandbyRoleAndHealthAsync(RedundancySupport mode, byte expected)
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var clock = new FakeTimeProvider();
            await using var election = new StaticLeaderElection(false);
            byte health = ServiceLevels.Maximum;
            using var provider = new LeaderServiceLevelProvider(election, mode, getHealthServiceLevel: () => health);
            using var publisher = new ServiceLevelStartupTask(provider, clock);
            using var task = new RequestServerStateChangeStartupTask(
                new RequestServerStateChangeOptions
                {
                    AdminAccessValidator = _ => { },
                    ServiceLevelSelector = _ => ServiceLevels.Maximum,
                    TimeProvider = clock
                },
                provider);
            await publisher.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            PropertyState<byte> serviceLevel = loaded.Server.Object.ServerObject.ServiceLevel!;
            var estimate = new DateTimeUtc(638000000000000000);
            ServiceResult suspend = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Suspended,
                estimate,
                0,
                LocalizedText.Null,
                restart: false);
            Assert.That(suspend, Is.EqualTo(ServiceResult.Good));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.Maintenance));

            health = 178;
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(serviceLevel.Value, Is.EqualTo(ServiceLevels.Maintenance));

            ServiceResult resume = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Running,
                estimate,
                0,
                LocalizedText.Null,
                restart: false);
            Assert.That(resume, Is.EqualTo(ServiceResult.Good));
            Assert.That(serviceLevel.Value, Is.EqualTo(expected));
            Assert.That(loaded.Server.Object.ServerObject.EstimatedReturnTime!.Value, Is.EqualTo(DateTimeUtc.MinValue));
        }

        [Test]
        public async Task RequestIgnoresSecondsTillShutdownForStatesOtherThanShutdownAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var timeProvider = new CountingTimeProvider();
            using RequestServerStateChangeStartupTask task = CreateTask(timeProvider: timeProvider);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Suspended,
                DateTimeUtc.MinValue,
                30,
                LocalizedText.Null,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.Zero);
            Assert.That(timeProvider.ActiveTimers, Is.Zero);
        }

        [Test]
        public async Task RequestShutdownCountsSecondsTillShutdownDownAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var timeProvider = new CountingTimeProvider();
            using RequestServerStateChangeStartupTask task = CreateTask(timeProvider: timeProvider);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.MinValue,
                3,
                new LocalizedText("maintenance"),
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.EqualTo(3));

            timeProvider.Advance(TimeSpan.FromSeconds(1));
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.EqualTo(2));
            Assert.That(loaded.StatusVariable.SecondsTillShutdown!.Value, Is.EqualTo(2));

            timeProvider.Advance(TimeSpan.FromSeconds(2));
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.Zero);
            Assert.That(loaded.Status.Value.State, Is.EqualTo(ServerState.Shutdown));
            Assert.That(timeProvider.ActiveTimers, Is.Zero);
        }

        [Test]
        public async Task RequestRunningStopsPendingShutdownCountdownAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var timeProvider = new CountingTimeProvider();
            using RequestServerStateChangeStartupTask task = CreateTask(timeProvider: timeProvider);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.MinValue,
                10,
                LocalizedText.Null,
                restart: false);
            ServiceResult result = method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Running,
                DateTimeUtc.MinValue,
                0,
                LocalizedText.Null,
                restart: false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(timeProvider.ActiveTimers, Is.Zero);
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            Assert.That(ReadState(loaded), Is.EqualTo(ServerState.Running));
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.Zero);
        }

        [Test]
        public async Task CountdownDoesNotOverwriteStateRestoredByHostAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var timeProvider = new CountingTimeProvider();
            using RequestServerStateChangeStartupTask task = CreateTask(timeProvider: timeProvider);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.MinValue,
                10,
                LocalizedText.Null,
                restart: false);

            // the host restores Running after the tick read a stale Shutdown state; the
            // tick must check the state under the status lock it writes with.
            loaded.Server.Object.UpdateServerStatus(status => status.Value.State = ServerState.Running);
            loaded.Server.Setup(s => s.CurrentState).Returns(ServerState.Shutdown);
            timeProvider.Advance(TimeSpan.FromSeconds(1));

            Assert.That(loaded.Status.Value.State, Is.EqualTo(ServerState.Running));
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.EqualTo(10));
            Assert.That(timeProvider.ActiveTimers, Is.Zero);
        }

        [Test]
        public async Task DisposeStopsPendingShutdownCountdownAsync()
        {
            using LoadedDiagnosticsServer loaded = await CreateLoadedServerAsync().ConfigureAwait(false);
            var timeProvider = new CountingTimeProvider();
            RequestServerStateChangeStartupTask task = CreateTask(timeProvider: timeProvider);

            await task.OnServerStartedAsync(loaded.Server.Object).ConfigureAwait(false);
            RequestServerStateChangeMethodState method = FindMethod(loaded);
            method.OnCall!(
                loaded.Server.Object.DefaultSystemContext,
                method,
                ObjectIds.Server,
                ServerState.Shutdown,
                DateTimeUtc.MinValue,
                10,
                LocalizedText.Null,
                restart: false);
            task.Dispose();
            timeProvider.Advance(TimeSpan.FromSeconds(5));

            Assert.That(timeProvider.ActiveTimers, Is.Zero);
            Assert.That(loaded.Status.Value.SecondsTillShutdown, Is.EqualTo(10));
        }

        private static RequestServerStateChangeMethodState FindMethod(LoadedDiagnosticsServer loaded)
        {
            return loaded.Manager.FindPredefinedNode<RequestServerStateChangeMethodState>(
                MethodIds.Server_RequestServerStateChange);
        }

        /// <summary>
        /// Reads <c>ServerStatus.State</c> the way the Read service does, through the
        /// variable's value handlers rather than the node's cached <c>Value</c>.
        /// </summary>
        private static ServerState ReadState(LoadedDiagnosticsServer loaded)
        {
            Variant value = ReadValue(loaded.StatusVariable.State!, loaded);
            Assert.That(value.TryGetValue(out ServerState state), Is.True);
            return state;
        }

        private static Variant ReadValue(NodeState node, LoadedDiagnosticsServer loaded)
        {
            Variant value = Variant.Null;
            DateTimeUtc timestamp = DateTimeUtc.MinValue;
            ServiceResult result = node.ReadAttribute(
                loaded.Server.Object.DefaultSystemContext,
                Attributes.Value,
                ref value,
                ref timestamp);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            return value;
        }

        private static RequestServerStateChangeStartupTask CreateTask(
            IServiceLevelController? controller = null,
            TimeProvider? timeProvider = null)
        {
            return new RequestServerStateChangeStartupTask(
                new RequestServerStateChangeOptions
                {
                    AdminAccessValidator = _ => { },
                    TimeProvider = timeProvider ?? TimeProvider.System
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

            // back ServerStatus with the status object the server serves Reads from, as
            // ServerInternalData does.
            var statusLock = new Lock();
            var status = new ServerStatusValue(
                serverObject.ServerStatus!,
                new ServerStatusDataType { State = ServerState.Running },
                statusLock);
            server.Setup(s => s.UpdateServerStatus(It.IsAny<Action<ServerStatusValue>>()))
                .Callback((Action<ServerStatusValue> action) =>
                {
                    lock (statusLock)
                    {
                        action(status);
                    }
                });
            server.Setup(s => s.CurrentState).Returns(() =>
            {
                lock (statusLock)
                {
                    return status.Value.State;
                }
            });
            server.Setup(s => s.DiagnosticsNodeManager).Returns(manager);
            server.Setup(s => s.FindNodeManagers<IConfigurationNodeManager>())
                .Returns([configurationNodeManager.Object]);
            server.Setup(s => s.FindPredefinedNode<RequestServerStateChangeMethodState>(
                It.IsAny<NodeId>()))
                .Returns((NodeId nodeId) => manager.FindPredefinedNode<RequestServerStateChangeMethodState>(nodeId));
            return new LoadedDiagnosticsServer(server, manager, configurationNodeManager, status);
        }

        private sealed class LoadedDiagnosticsServer : IDisposable
        {
            public LoadedDiagnosticsServer(
                Mock<IServerInternal> server,
                DiagnosticsNodeManager manager,
                Mock<IConfigurationNodeManager> configurationNodeManager,
                ServerStatusValue status)
            {
                Server = server;
                Manager = manager;
                ConfigurationNodeManager = configurationNodeManager;
                Status = status;
            }

            public Mock<IServerInternal> Server { get; }

            public ServerStatusValue Status { get; }

            public ServerStatusState StatusVariable => Status.Variable;

            public DiagnosticsNodeManager Manager { get; }

            public Mock<IConfigurationNodeManager> ConfigurationNodeManager { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        /// <summary>
        /// A fake time provider that counts the timers that were created and not yet disposed.
        /// </summary>
        private sealed class CountingTimeProvider : FakeTimeProvider
        {
            public int ActiveTimers => Volatile.Read(ref m_activeTimers);

            public override ITimer CreateTimer(
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                Interlocked.Increment(ref m_activeTimers);
                return new CountedTimer(this, base.CreateTimer(callback, state, dueTime, period));
            }

            private sealed class CountedTimer : ITimer
            {
                public CountedTimer(CountingTimeProvider owner, ITimer inner)
                {
                    m_owner = owner;
                    m_inner = inner;
                }

                public bool Change(TimeSpan dueTime, TimeSpan period)
                {
                    return m_inner.Change(dueTime, period);
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) == 0)
                    {
                        Interlocked.Decrement(ref m_owner.m_activeTimers);
                    }
                    m_inner.Dispose();
                }

                public ValueTask DisposeAsync()
                {
                    Dispose();
                    return default;
                }

                private readonly CountingTimeProvider m_owner;
                private readonly ITimer m_inner;
                private int m_disposed;
            }

            private int m_activeTimers;
        }
    }
}
