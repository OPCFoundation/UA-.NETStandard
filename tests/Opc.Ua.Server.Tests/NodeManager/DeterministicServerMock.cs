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

using System;
using System.Threading;
using Moq;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Builds a deterministic mock of <see cref="IServerInternal"/> that is sufficient
    /// to construct node managers, monitored items, sampling groups and event managers
    /// without spinning up a real server.
    /// </summary>
    internal static class DeterministicServerMock
    {
        public const string TestNamespaceUri = "urn:opcfoundation:server:tests:deterministic";

        /// <summary>
        /// Creates the mock server.
        /// </summary>
        public static Mock<IServerInternal> Create(
            out MonitoredItemQueueFactory queueFactory,
            TimeProvider? timeProvider = null)
        {
            var mockServer = new Mock<IServerInternal>();
            if (timeProvider != null)
            {
                mockServer.As<ITimeProviderProvider>().SetupGet(server => server.TimeProvider).Returns(timeProvider);
            }
            var mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();
            var mockCoreNodeManager = new Mock<ICoreNodeManager>();

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(TestNamespaceUri);

            mockServer.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceTable));
            mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mockServer.Setup(s => s.NodeManager).Returns(mockMasterNodeManager.Object);
            mockServer.Setup(s => s.CoreNodeManager).Returns(mockCoreNodeManager.Object);
            mockServer.Setup(s => s.IsRunning).Returns(true);
            mockMasterNodeManager.Setup(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);
            mockMasterNodeManager.Setup(m => m.CoreNodeManager)
                .Returns(mockCoreNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            queueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);

            var serverSystemContext = new ServerSystemContext(mockServer.Object);
            mockServer.Setup(s => s.DefaultSystemContext).Returns(serverSystemContext);

            return mockServer;
        }

        /// <summary>
        /// Creates a dispatcher with its own type and factory publication owners
        /// while borrowing the running fixture's server-wide services.
        /// </summary>
        public static MasterNodeManager CreateIsolatedMasterNodeManager(
            IServerInternal template,
            ApplicationConfiguration configuration,
            ArrayOf<INodeManager> additionalManagers = default,
            ArrayOf<IAsyncNodeManager> additionalAsyncManagers = default)
        {
            _ = template ?? throw new ArgumentNullException(nameof(template));
            _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
            if (template.Factory is not EncodeableFactory factory)
            {
                throw new NotSupportedException("The fixture requires a snapshot-capable encodeable factory.");
            }

            var server = new Mock<IServerInternal>(MockBehavior.Strict);
            if (template is ITimeProviderProvider timeProvider)
            {
                server.As<ITimeProviderProvider>()
                    .SetupGet(s => s.TimeProvider).Returns(timeProvider.TimeProvider);
            }
            MasterNodeManager? manager = null;
            EncodeableFactory isolatedFactory = factory.Fork();
            TypeTable isolatedTypes = template.TypeTree.CaptureSnapshot(out _, out _);
            var messageContext = new ServiceMessageContext(template.Telemetry, isolatedFactory)
            {
                NamespaceUris = template.NamespaceUris,
                ServerUris = template.ServerUris,
                MaxStringLength = template.MessageContext.MaxStringLength,
                MaxByteStringLength = template.MessageContext.MaxByteStringLength,
                MaxArrayLength = template.MessageContext.MaxArrayLength,
                MaxMessageSize = template.MessageContext.MaxMessageSize,
                MaxEncodingNestingLevels = template.MessageContext.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = template.MessageContext.MaxDecoderRecoveries
            };
            server.SetupGet(s => s.NamespaceUris).Returns(template.NamespaceUris);
            server.SetupGet(s => s.ServerUris).Returns(template.ServerUris);
            server.SetupGet(s => s.Factory).Returns(isolatedFactory);
            server.SetupGet(s => s.TypeTree).Returns(isolatedTypes);
            server.SetupGet(s => s.MessageContext).Returns(messageContext);
            server.SetupGet(s => s.Telemetry).Returns(template.Telemetry);
            server.SetupGet(s => s.EndpointAddresses).Returns(template.EndpointAddresses);
            server.SetupGet(s => s.EventManager).Returns(template.EventManager);
            server.SetupGet(s => s.ResourceManager).Returns(template.ResourceManager);
            server.SetupGet(s => s.RequestManager).Returns(template.RequestManager);
            server.SetupGet(s => s.AggregateManager).Returns(template.AggregateManager);
            server.SetupGet(s => s.SessionManager).Returns(template.SessionManager);
            server.SetupGet(s => s.RoleManager).Returns(template.RoleManager);
            server.SetupGet(s => s.IdentityRegistry).Returns(template.IdentityRegistry);
            server.SetupGet(s => s.UserManagement).Returns(template.UserManagement);
            server.SetupGet(s => s.SubscriptionManager).Returns(template.SubscriptionManager);
            server.SetupGet(s => s.MonitoredItemQueueFactory).Returns(template.MonitoredItemQueueFactory);
            server.SetupGet(s => s.SubscriptionStore).Returns(template.SubscriptionStore);
            server.SetupGet(s => s.ServerObject).Returns(template.ServerObject);
            server.SetupGet(s => s.CurrentState).Returns(() => template.CurrentState);
            server.SetupGet(s => s.IsRunning).Returns(() => template.IsRunning);
            server.SetupGet(s => s.Auditing).Returns(() => template.Auditing);
            server.SetupGet(s => s.NodeManager).Returns(() => manager!);
            server.SetupGet(s => s.CoreNodeManager).Returns(() => manager?.CoreNodeManager!);
            server.SetupGet(s => s.ConfigurationNodeManager).Returns(() => manager?.ConfigurationNodeManager!);
            server.SetupGet(s => s.DiagnosticsNodeManager).Returns(() => manager?.DiagnosticsNodeManager!);
            server.SetupGet(s => s.MainNodeManagerFactory)
                .Returns(new MainNodeManagerFactory(configuration, server.Object));
            var systemContext = new ServerSystemContext(server.Object);
            server.SetupGet(s => s.DefaultSystemContext).Returns(systemContext);
            server.SetupGet(s => s.DefaultAuditContext).Returns(systemContext);
            server.Setup(s => s.CreateSystemContext(It.IsAny<ISession>()))
                .Returns((ISession session) => new ServerSystemContext(server.Object, session));
            server.Setup(s => s.UpdateServerDiagnostics(It.IsAny<Action<ServerDiagnosticsSummaryDataType>>()))
                .Callback((Action<ServerDiagnosticsSummaryDataType> update) => template.UpdateServerDiagnostics(update));
            server.Setup(s => s.UpdateServerStatus(It.IsAny<Action<ServerStatusValue>>()))
                .Callback((Action<ServerStatusValue> update) => template.UpdateServerStatus(update));
            server.Setup(s => s.ReportEvent(It.IsAny<IFilterTarget>()))
                .Callback((IFilterTarget notification) => template.ReportEvent(notification));
            server.Setup(s => s.ReportEvent(It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>()))
                .Callback((ISystemContext context, IFilterTarget notification) =>
                    template.ReportEvent(context, notification));
            server.Setup(s => s.ReportEventAsync(It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns((IFilterTarget notification, CancellationToken token) =>
                    template.ReportEventAsync(notification, token));
            server.Setup(s => s.ReportEventAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<IFilterTarget>(), It.IsAny<CancellationToken>()))
                .Returns((ISystemContext context, IFilterTarget notification, CancellationToken token) =>
                    template.ReportEventAsync(context, notification, token));
            server.Setup(s => s.ReportAuditEvent(It.IsAny<ISystemContext>(), It.IsAny<AuditEventState>()))
                .Callback((ISystemContext context, AuditEventState notification) =>
                    template.ReportAuditEvent(context, notification));

            manager = new MasterNodeManager(
                server.Object, configuration, null,
                additionalAsyncManagers.ToArray(), additionalManagers.ToArray());
            return manager;
        }
    }
}
