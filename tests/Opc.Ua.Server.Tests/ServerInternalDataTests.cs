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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("ServerInternalData")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerInternalDataTests
    {
        private ServerProperties m_serverProperties;
        private ApplicationConfiguration m_configuration;
        private ServiceMessageContext m_messageContext;
        private ITelemetryContext m_telemetry;

        private static readonly string[] s_locales = ["de-DE"];

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();

            m_serverProperties = new ServerProperties
            {
                ProductName = "TestProduct",
                ProductUri = "urn:test:product",
                ManufacturerName = "TestManufacturer",
                SoftwareVersion = "1.0.0",
                BuildNumber = "100",
                BuildDate = DateTime.UtcNow
            };

            m_configuration = new ApplicationConfiguration
            {
                ApplicationUri = "urn:test:server",
                ApplicationName = "TestServer",
                ServerConfiguration = new ServerConfiguration
                {
                    BaseAddresses = [
                        "opc.tcp://localhost:4840",
                        "https://localhost:4841"
                    ],
                    ServerProfileArray = [],
                    MaxBrowseContinuationPoints = 10,
                    MaxQueryContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10,
                    MaxSessionCount = 100,
                    MaxSubscriptionCount = 100
                },
                TransportQuotas = new TransportQuotas
                {
                    MaxArrayLength = 65535,
                    MaxStringLength = 65535,
                    MaxByteStringLength = 65535
                }
            };

            m_messageContext = ServiceMessageContext.Create(m_telemetry);
        }

        private ServerInternalData CreateServerInternalData()
        {
            return new ServerInternalData(
                m_serverProperties,
                m_configuration,
                m_messageContext);
        }

        [Test]
        public void RequestManagerIsUnsetUntilTheServerObjectIsCreated()
        {
            using ServerInternalData data = CreateServerInternalData();

            // Request dispatch runs against this datastore from the moment it is published,
            // which happens before the request manager exists. Callers must therefore treat
            // the request manager as absent during that startup window.
            Assert.That(data.RequestManager, Is.Null);
        }

        [Test]
        public void ConstructorInitializesEndpointAddresses()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.EndpointAddresses, Is.Not.Null);
            Assert.That(data.EndpointAddresses.Count(), Is.EqualTo(2));
        }

        [Test]
        public void ConstructorInitializesMessageContext()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.MessageContext, Is.Not.Null);
            Assert.That(data.MessageContext, Is.SameAs(m_messageContext));
        }

        [Test]
        public void ConstructorInitializesNamespaceUris()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.NamespaceUris, Is.Not.Null);
        }

        [Test]
        public void ConstructorInitializesServerUris()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.ServerUris, Is.Not.Null);
            Assert.That(data.ServerUris.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ConstructorAddsApplicationUriToServerUris()
        {
            using ServerInternalData data = CreateServerInternalData();
            string appUri = data.ServerUris.GetString(0);
            Assert.That(appUri, Is.EqualTo("urn:test:server"));
        }

        [Test]
        public void ConstructorInitializesFactory()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.Factory, Is.Not.Null);
        }

        [Test]
        public void ConstructorInitializesTypeTree()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.TypeTree, Is.Not.Null);
        }

        [Test]
        public void ConstructorInitializesDefaultSystemContext()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.DefaultSystemContext, Is.Not.Null);
        }

        [Test]
        public void ConstructorFiltersInvalidBaseAddresses()
        {
            m_configuration.ServerConfiguration.BaseAddresses = [
                "opc.tcp://localhost:4840",
                "not-a-valid-uri",
                "https://localhost:4841"
            ];

            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.EndpointAddresses.Count(), Is.EqualTo(2));
        }

        [Test]
        public void ConstructorHandlesEmptyBaseAddresses()
        {
            m_configuration.ServerConfiguration.BaseAddresses = [];
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.EndpointAddresses.Count(), Is.Zero);
        }

        [Test]
        public void SetNodeManagerStoresNodeManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager.Setup(m => m.DiagnosticsNodeManager).Returns((IDiagnosticsNodeManager)null);
            mockNodeManager.Setup(m => m.ConfigurationNodeManager).Returns((IConfigurationNodeManager)null);
            mockNodeManager.Setup(m => m.CoreNodeManager).Returns((ICoreNodeManager)null);

            data.SetNodeManager(mockNodeManager.Object);

            Assert.That(data.NodeManager, Is.SameAs(mockNodeManager.Object));
        }

        [Test]
        public void FindNodeManagersYieldsAManagerOnBothFacesOnce()
        {
            using ServerInternalData data = CreateServerInternalData();

            // A manager that implements INodeManager is its own synchronous adapter, so
            // the same instance appears on both faces.
            var manager = new Mock<IAsyncNodeManager>();
            INodeManager syncFace = manager.As<INodeManager>().Object;

            data.SetNodeManager(CreateMasterNodeManager([manager.Object], [syncFace]));

            Assert.That(
                data.FindNodeManagers<IAsyncNodeManager>().ToList(),
                Is.EqualTo(new[] { manager.Object }).AsCollection);
        }

        [Test]
        public void FindNodeManagersYieldsEveryDistinctManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            var asyncOnly = new Mock<IAsyncNodeManager>();
            var syncManager = new Mock<INodeManager>();
            IAsyncNodeManager syncAsAsync = syncManager.As<IAsyncNodeManager>().Object;

            data.SetNodeManager(
                CreateMasterNodeManager([asyncOnly.Object], [syncManager.Object]));

            Assert.That(
                data.FindNodeManagers<IAsyncNodeManager>().ToList(),
                Is.EqualTo(new[] { asyncOnly.Object, syncAsAsync }).AsCollection);
        }

        [Test]
        public void FindNodeManagersReturnsEmptyWhenNoNodeManagerIsSet()
        {
            using ServerInternalData data = CreateServerInternalData();

            Assert.That(data.FindNodeManagers<IAsyncNodeManager>(), Is.Empty);
        }

        private static IMasterNodeManager CreateMasterNodeManager(
            IAsyncNodeManager[] asyncNodeManagers,
            INodeManager[] nodeManagers)
        {
            var mock = new Mock<IMasterNodeManager>();
            mock.Setup(m => m.DiagnosticsNodeManager).Returns((IDiagnosticsNodeManager)null);
            mock.Setup(m => m.ConfigurationNodeManager).Returns((IConfigurationNodeManager)null);
            mock.Setup(m => m.CoreNodeManager).Returns((ICoreNodeManager)null);
            mock.Setup(m => m.AsyncNodeManagers).Returns(asyncNodeManagers);
            mock.Setup(m => m.NodeManagers).Returns(nodeManagers);
            return mock.Object;
        }

        [Test]
        public void SetMainNodeManagerFactoryStoresFactory()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockFactory = new Mock<IMainNodeManagerFactory>();
            data.SetMainNodeManagerFactory(mockFactory.Object);

            Assert.That(data.MainNodeManagerFactory, Is.SameAs(mockFactory.Object));
        }

        [Test]
        public void SetSessionManagerStoresManagers()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockSessionManager = new Mock<ISessionManager>();
            var mockSubscriptionManager = new Mock<ISubscriptionManager>();

            data.SetSessionManager(mockSessionManager.Object, mockSubscriptionManager.Object);

            Assert.That(data.SessionManager, Is.SameAs(mockSessionManager.Object));
            Assert.That(data.SubscriptionManager, Is.SameAs(mockSubscriptionManager.Object));
        }

        [Test]
        [Category("NodeManagerLifecycle")]
        public void CloseSessionAsyncRemovesTheSessionEvenWhenTeardownThrows()
        {
            // Closing is marked on the Session and never cleared, so a Session that started
            // closing must not be left registered and serving when teardown fails.
            using ServerInternalData data = CreateServerInternalData();
            var sessionId = new NodeId(Guid.NewGuid());
            var mockSessionManager = new Mock<ISessionManager>();
            var mockSubscriptionManager = new Mock<ISubscriptionManager>();
            mockSessionManager.Setup(manager => manager.GetSessions()).Returns([]);
            data.SetSessionManager(mockSessionManager.Object, mockSubscriptionManager.Object);

            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager
                .Setup(manager => manager.SessionClosingAsync(
                    It.IsAny<OperationContext>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("teardown failed"));
            data.SetNodeManager(mockNodeManager.Object);

            Assert.That(
                async () => await data
                    .CloseSessionAsync(null!, sessionId, deleteSubscriptions: true)
                    .ConfigureAwait(false),
                Throws.InvalidOperationException);

            mockSessionManager.Verify(
                manager => manager.CloseSessionAsync(sessionId, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void SetMonitoredItemQueueFactoryStoresFactory()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockFactory = new Mock<IMonitoredItemQueueFactory>();

            data.SetMonitoredItemQueueFactory(mockFactory.Object);

            Assert.That(data.MonitoredItemQueueFactory, Is.SameAs(mockFactory.Object));
        }

        [Test]
        public void SetSubscriptionStoreStoresStore()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockStore = new Mock<ISubscriptionStore>();

            data.SetSubscriptionStore(mockStore.Object);

            Assert.That(data.SubscriptionStore, Is.SameAs(mockStore.Object));
        }

        [Test]
        public void SetAggregateManagerStoresManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            using var aggregateManager = new AggregateManager(data);

            data.SetAggregateManager(aggregateManager);

            Assert.That(data.AggregateManager, Is.SameAs(aggregateManager));
        }

        [Test]
        public void BindingAfterTheBindPhaseIsRefused()
        {
            // A subsystem swapped underneath a running server would leave every component
            // that already resolved it holding the previous instance.
            using ServerInternalData data = CreateServerInternalData();
            data.CompleteBindPhase();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => data.SetNodeManager(new Mock<IMasterNodeManager>().Object),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo((StatusCode)StatusCodes.BadInvalidState));

                Assert.That(
                    () => data.SetRoleManager(new Mock<IRoleManager>().Object),
                    Throws.TypeOf<ServiceResultException>());

                Assert.That(
                    () => data.SetSubscriptionStore(new Mock<ISubscriptionStore>().Object),
                    Throws.TypeOf<ServiceResultException>());

                Assert.That(
                    () => data.SetSessionManager(
                        new Mock<ISessionManager>().Object,
                        new Mock<ISubscriptionManager>().Object),
                    Throws.TypeOf<ServiceResultException>());
            });
        }

        [Test]
        public void TheRefusedBindNamesTheOperation()
        {
            using ServerInternalData data = CreateServerInternalData();
            data.CompleteBindPhase();

            Assert.That(
                () => data.SetMonitoredItemQueueFactory(new Mock<IMonitoredItemQueueFactory>().Object),
                Throws.TypeOf<ServiceResultException>()
                    .With.Message.Contains(nameof(ServerInternalData.SetMonitoredItemQueueFactory)));
        }

        [Test]
        public void BindingIsAllowedUntilTheBindPhaseCloses()
        {
            using ServerInternalData data = CreateServerInternalData();
            var first = new Mock<IRoleManager>();
            var second = new Mock<IRoleManager>();

            // Rebinding during startup stays legal; only binding afterwards is refused.
            data.SetRoleManager(first.Object);
            data.SetRoleManager(second.Object);

            Assert.That(data.RoleManager, Is.SameAs(second.Object));

            data.CompleteBindPhase();

            Assert.That(
                () => data.SetRoleManager(first.Object),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void CompletingTheBindPhaseTwiceIsHarmless()
        {
            using ServerInternalData data = CreateServerInternalData();

            data.CompleteBindPhase();

            Assert.That(() => data.CompleteBindPhase(), Throws.Nothing);
        }

        [Test]
        public void UpdateServerDiagnosticsInvokesTheUpdateUnderTheLock()
        {
            using ServerInternalData data = CreateServerInternalData();

            // ServerDiagnostics is only populated once the server object is created, so
            // this asserts the callback is invoked rather than inspecting the payload.
            bool invoked = false;

            data.UpdateServerDiagnostics(_ => invoked = true);

            Assert.That(invoked, Is.True);
        }

        [Test]
        public void UpdateServerDiagnosticsThrowsOnNullUpdate()
        {
            using ServerInternalData data = CreateServerInternalData();

            Assert.That(
                () => data.UpdateServerDiagnostics(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        /// <summary>
        /// The read callback behind the ServerDiagnosticsSummary node must take the same lock
        /// every writer takes through <c>UpdateServerDiagnostics</c>.
        /// </summary>
        /// <remarks>
        /// It used to lock the payload — <c>lock (ServerDiagnostics)</c> — which is a different
        /// monitor from the one the writers take, so a client's snapshot was never excluded
        /// from a concurrent writer and <c>Variant.FromStructure</c> could walk the structure
        /// mid-mutation. Reflection is used because the callback is private and is reachable
        /// in production only through the diagnostic node manager's wiring.
        /// </remarks>
        [Test]
        public async Task DiagnosticsReadBlocksWhileAWriterHoldsTheLockAsync()
        {
            using ServerInternalData data = CreateServerInternalData();

            SetServerDiagnostics(data, new ServerDiagnosticsSummaryDataType());

            using var writerEntered = new ManualResetEventSlim(false);
            using var readerStarted = new ManualResetEventSlim(false);
            using var releaseWriter = new ManualResetEventSlim(false);

            Task writer = Task.Factory.StartNew(
                () => data.UpdateServerDiagnostics(
                    _ =>
                    {
                        writerEntered.Set();
                        releaseWriter.Wait(TimeSpan.FromSeconds(30));
                    }),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            try
            {
                Assert.That(
                    writerEntered.Wait(TimeSpan.FromSeconds(30)),
                    Is.True,
                    "the writer never entered its critical section");

                // LongRunning gets a dedicated thread, so the reader cannot simply fail to be
                // scheduled behind the writer's blocked pool thread; and the timeout starts
                // only once the reader has signalled that it is about to take the lock.
                Task<Variant> reader = Task.Factory.StartNew(
                    () =>
                    {
                        readerStarted.Set();
                        return InvokeOnUpdateDiagnostics(data);
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                Assert.That(
                    readerStarted.Wait(TimeSpan.FromSeconds(30)),
                    Is.True,
                    "the reader never started");

                Assert.That(
                    reader.Wait(TimeSpan.FromMilliseconds(500)),
                    Is.False,
                    "the read callback completed while a writer held the diagnostics lock, so " +
                    "it is not taking the lock the writers take");

                releaseWriter.Set();

                Variant snapshot = await reader.ConfigureAwait(false);

                Assert.That(snapshot.IsNull, Is.False, "the read callback produced no snapshot");
            }
            finally
            {
                releaseWriter.Set();
                await writer.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The snapshot handed to a client must be detached from the live diagnostics, so a
        /// later update cannot change a value the client has already read.
        /// </summary>
        /// <remarks>
        /// Holding the lock is not on its own enough: <c>Variant.FromStructure</c> does not
        /// copy by default, and the caller reads the fields long after the lock was released.
        /// </remarks>
        [Test]
        public async Task DiagnosticsReadReturnsADetachedSnapshotAsync()
        {
            using ServerInternalData data = CreateServerInternalData();

            SetServerDiagnostics(data, new ServerDiagnosticsSummaryDataType());

            data.UpdateServerDiagnostics(diagnostics => diagnostics.RejectedSessionCount = 7);

            Variant snapshot = await Task.Run(() => InvokeOnUpdateDiagnostics(data))
                .ConfigureAwait(false);

            data.UpdateServerDiagnostics(diagnostics => diagnostics.RejectedSessionCount = 99);

            Assert.That(
                snapshot.TryGetStructure(out ServerDiagnosticsSummaryDataType read),
                Is.True);
            Assert.That(
                read.RejectedSessionCount,
                Is.EqualTo(7u),
                "a later update changed a value that had already been handed out");
        }

        private static void SetServerDiagnostics(
            ServerInternalData data,
            ServerDiagnosticsSummaryDataType diagnostics)
        {
            typeof(ServerInternalData)
                .GetProperty(
                    nameof(ServerInternalData.ServerDiagnostics),
                    BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(data, diagnostics);
        }

        private static Variant InvokeOnUpdateDiagnostics(ServerInternalData data)
        {
            MethodInfo callback = typeof(ServerInternalData).GetMethod(
                "OnUpdateDiagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            object[] arguments = [data.DefaultSystemContext, null!, Variant.Null];
            callback.Invoke(data, arguments);

            return (Variant)arguments[2];
        }

        [Test]
        public void DiagnosticsEnabledReturnsFalseWhenNoDiagnosticsNodeManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.DiagnosticsEnabled, Is.False);
        }

        [Test]
        public void ServerContextDefaultSystemContextIsTheServerSystemContext()
        {
            using ServerInternalData data = CreateServerInternalData();

            Assert.That(
                ((IServerContext)data).DefaultSystemContext,
                Is.SameAs(data.DefaultSystemContext));
        }

        [Test]
        public void CreateSystemContextCarriesTheSessionIdentityAndLocales()
        {
            using ServerInternalData data = CreateServerInternalData();

            var sessionId = new NodeId(Guid.NewGuid());
            var identity = new UserIdentity(new AnonymousIdentityToken());

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.Identity).Returns(identity);
            session.Setup(s => s.PreferredLocales).Returns(s_locales);

            ServerSystemContext created = data.CreateSystemContext(session.Object);

            Assert.That(created, Is.Not.SameAs(data.DefaultSystemContext));
            Assert.That(created.PreferredLocales.ToArray(), Is.EqualTo(s_locales));
            Assert.That(created.NamespaceUris, Is.SameAs(data.NamespaceUris));
        }

        [Test]
        public void CreateSystemContextThrowsOnNullSession()
        {
            using ServerInternalData data = CreateServerInternalData();

            Assert.That(
                () => data.CreateSystemContext(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void FindPredefinedNodeReturnsNullBeforeTheDiagnosticsNodeManagerExists()
        {
            using ServerInternalData data = CreateServerInternalData();

            // The datastore is published before the node managers are bound, so callers
            // must tolerate an address space that is not there yet.
            Assert.That(data.FindPredefinedNode<BaseObjectState>(ObjectIds.Server), Is.Null);
        }

        [Test]
        public void TelemetryReturnsMessageContextTelemetry()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.Telemetry, Is.EqualTo(m_messageContext.Telemetry));
        }

        [Test]
        public void IsRunningReturnsFalseWhenStatusNotInitialized()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.IsRunning, Is.False);
        }

        [Test]
        public void DefaultAuditContextReturnsNonNull()
        {
            using ServerInternalData data = CreateServerInternalData();
            ISystemContext auditContext = data.DefaultAuditContext;
            Assert.That(auditContext, Is.Not.Null);
        }

        [Test]
        public void DefaultAuditContextReturnsCopy()
        {
            using ServerInternalData data = CreateServerInternalData();
            ISystemContext context1 = data.DefaultAuditContext;
            ISystemContext context2 = data.DefaultAuditContext;
            Assert.That(context1, Is.Not.SameAs(context2));
        }

        [Test]
        public void NodeManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.NodeManager, Is.Null);
        }

        [Test]
        public void SessionManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.SessionManager, Is.Null);
        }

        [Test]
        public void SubscriptionManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.SubscriptionManager, Is.Null);
        }

        [Test]
        public void EventManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.EventManager, Is.Null);
        }

        [Test]
        public void ResourceManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.ResourceManager, Is.Null);
        }

        [Test]
        public void RequestManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.RequestManager, Is.Null);
        }

        [Test]
        public void AggregateManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.AggregateManager, Is.Null);
        }

        [Test]
        public void CoreNodeManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.CoreNodeManager, Is.Null);
        }

        [Test]
        public void DiagnosticsNodeManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.DiagnosticsNodeManager, Is.Null);
        }

        [Test]
        public void ConfigurationNodeManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.ConfigurationNodeManager, Is.Null);
        }

        [Test]
        public void MonitoredItemQueueFactoryIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.MonitoredItemQueueFactory, Is.Null);
        }

        [Test]
        public void SubscriptionStoreIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.SubscriptionStore, Is.Null);
        }

        [Test]
        public void ServerObjectIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.ServerObject, Is.Null);
        }

        [Test]
        public void ServerDiagnosticsIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.ServerDiagnostics, Is.Null);
        }

        [Test]
        public void DisposeDoesNotThrowWhenPropertiesAreNull()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.DoesNotThrow(data.Dispose);
        }

        [Test]
        public void DisposeCanBeCalledTwice()
        {
            ServerInternalData data = CreateServerInternalData();
            data.Dispose();
            Assert.DoesNotThrow(data.Dispose);
        }

        [Test]
        public void SetNodeManagerStoresDiagnosticsNodeManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockDiag = new Mock<IDiagnosticsNodeManager>();
            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager.Setup(m => m.DiagnosticsNodeManager).Returns(mockDiag.Object);
            mockNodeManager.Setup(m => m.ConfigurationNodeManager).Returns((IConfigurationNodeManager)null);
            mockNodeManager.Setup(m => m.CoreNodeManager).Returns((ICoreNodeManager)null);

            data.SetNodeManager(mockNodeManager.Object);

            Assert.That(data.DiagnosticsNodeManager, Is.SameAs(mockDiag.Object));
        }

        [Test]
        public void SetNodeManagerStoresCoreNodeManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockCore = new Mock<ICoreNodeManager>();
            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager.Setup(m => m.DiagnosticsNodeManager).Returns((IDiagnosticsNodeManager)null);
            mockNodeManager.Setup(m => m.ConfigurationNodeManager).Returns((IConfigurationNodeManager)null);
            mockNodeManager.Setup(m => m.CoreNodeManager).Returns(mockCore.Object);

            data.SetNodeManager(mockNodeManager.Object);

            Assert.That(data.CoreNodeManager, Is.SameAs(mockCore.Object));
        }

        [Test]
        public void SetNodeManagerStoresConfigurationNodeManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockConfig = new Mock<IConfigurationNodeManager>();
            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager.Setup(m => m.DiagnosticsNodeManager).Returns((IDiagnosticsNodeManager)null);
            mockNodeManager.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
            mockNodeManager.Setup(m => m.CoreNodeManager).Returns((ICoreNodeManager)null);

            data.SetNodeManager(mockNodeManager.Object);

            Assert.That(data.ConfigurationNodeManager, Is.SameAs(mockConfig.Object));
        }

        [Test]
        public void AuditingIsFalseByDefault()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.Auditing, Is.False);
        }

        [Test]
        public void ModellingRulesManagerIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.ModellingRulesManager, Is.Null);
        }

        [Test]
        public void MainNodeManagerFactoryIsNullBeforeSetup()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.MainNodeManagerFactory, Is.Null);
        }

        [Test]
        public void EndpointAddressesParsesValidUrls()
        {
            using ServerInternalData data = CreateServerInternalData();
            Uri[] addresses = [.. data.EndpointAddresses];
            Assert.That(addresses[0].ToString(), Does.Contain("localhost:4840"));
            Assert.That(addresses[1].ToString(), Does.Contain("localhost:4841"));
        }

        [Test]
        public void ReportEventDoesNotThrowWhenServerObjectIsNull()
        {
            using ServerInternalData data = CreateServerInternalData();
            var mockFilterTarget = new Mock<IFilterTarget>();
            Assert.DoesNotThrow(() => data.ReportEvent(mockFilterTarget.Object));
        }

        [Test]
        public void ReportAuditEventDoesNothingWhenAuditingDisabled()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.DoesNotThrow(() => data.ReportAuditEvent(data.DefaultSystemContext, null));
        }

        [Test]
        public async Task DisposeAsyncCompletesAsync()
        {
            ServerInternalData data = CreateServerInternalData();

            await data.DisposeAsync().ConfigureAwait(false);

            Assert.That(data.RequestManager, Is.Null);
        }

        [Test]
        public void DisposeReleasesManagedResourcesOnce()
        {
            ServerInternalData data = CreateServerInternalData();
            DisposalCounts counts = ConfigureCountingDisposableState(data);

            data.Dispose();

            Assert.That(CaptureDisposedState(data), Is.All.True);
            Assert.That(counts.Total, Is.EqualTo(5));
            Assert.That(counts.SubscriptionAsync, Is.EqualTo(1));
            Assert.That(counts.SubscriptionSync, Is.Zero);
        }

        [Test]
        public async Task DisposeAndDisposeAsyncLeaveSameObservableStateAsync()
        {
            ServerInternalData syncData = CreateServerInternalData();
            ServerInternalData asyncData = CreateServerInternalData();
            ConfigureDisposableState(syncData);
            ConfigureDisposableState(asyncData);

            syncData.Dispose();
            await asyncData.DisposeAsync().ConfigureAwait(false);

            Assert.That(CaptureDisposedState(asyncData), Is.EqualTo(CaptureDisposedState(syncData)));
        }

        [Test]
        public async Task DisposeAsyncIsIdempotentAsync()
        {
            ServerInternalData data = CreateServerInternalData();

            await data.DisposeAsync().ConfigureAwait(false);

            Assert.DoesNotThrowAsync(async () => await data.DisposeAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task DisposeAfterDisposeAsyncDoesNotDisposeTwiceAsync()
        {
            ServerInternalData data = CreateServerInternalData();
            DisposalCounts counts = ConfigureCountingDisposableState(data);

            await data.DisposeAsync().ConfigureAwait(false);

            // The synchronous path shares the disposed guard with the asynchronous one, so a
            // Dispose that follows DisposeAsync must be a no-op rather than releasing a second time.
            Assert.DoesNotThrow(data.Dispose);
            Assert.That(counts.Total, Is.EqualTo(5));
            Assert.That(counts.SubscriptionAsync, Is.EqualTo(1));
            Assert.That(counts.SubscriptionSync, Is.Zero);
        }

        [Test]
        public async Task DisposeAsyncAfterDisposeDoesNotDisposeTwiceAsync()
        {
            ServerInternalData data = CreateServerInternalData();
            DisposalCounts counts = ConfigureCountingDisposableState(data);

            data.Dispose();

            Assert.DoesNotThrowAsync(async () => await data.DisposeAsync().ConfigureAwait(false));
            Assert.That(counts.Total, Is.EqualTo(5));
            Assert.That(counts.SubscriptionAsync, Is.EqualTo(1));
            Assert.That(counts.SubscriptionSync, Is.Zero);
        }

        private static void ConfigureDisposableState(ServerInternalData data)
        {
            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager.Setup(m => m.DiagnosticsNodeManager).Returns((IDiagnosticsNodeManager)null);
            mockNodeManager.Setup(m => m.ConfigurationNodeManager).Returns((IConfigurationNodeManager)null);
            mockNodeManager.Setup(m => m.CoreNodeManager).Returns((ICoreNodeManager)null);
            data.SetNodeManager(mockNodeManager.Object);

            var mockSessionManager = new Mock<ISessionManager>();
            var mockSubscriptionManager = new Mock<ISubscriptionManager>();
            mockSubscriptionManager
                .As<IAsyncDisposable>()
                .Setup(manager => manager.DisposeAsync())
                .Returns(default(ValueTask));
            data.SetSessionManager(mockSessionManager.Object, mockSubscriptionManager.Object);

            data.SetMonitoredItemQueueFactory(new Mock<IMonitoredItemQueueFactory>().Object);
            data.SetRoleManager(new Mock<IRoleManager>().Object);
        }

        private static DisposalCounts ConfigureCountingDisposableState(ServerInternalData data)
        {
            var counts = new DisposalCounts();

            var mockNodeManager = new Mock<IMasterNodeManager>();
            mockNodeManager.Setup(manager => manager.DiagnosticsNodeManager).Returns((IDiagnosticsNodeManager)null);
            mockNodeManager.Setup(manager => manager.ConfigurationNodeManager).Returns((IConfigurationNodeManager)null);
            mockNodeManager.Setup(manager => manager.CoreNodeManager).Returns((ICoreNodeManager)null);
            mockNodeManager.As<IDisposable>().Setup(manager => manager.Dispose()).Callback(() => counts.NodeManager++);
            data.SetNodeManager(mockNodeManager.Object);

            var mockSessionManager = new Mock<ISessionManager>();
            mockSessionManager.Setup(manager => manager.Dispose()).Callback(() => counts.SessionManager++);

            var mockSubscriptionManager = new Mock<ISubscriptionManager>();
            mockSubscriptionManager.Setup(manager => manager.Dispose()).Callback(() => counts.SubscriptionSync++);
            mockSubscriptionManager
                .As<IAsyncDisposable>()
                .Setup(manager => manager.DisposeAsync())
                .Callback(() => counts.SubscriptionAsync++)
                .Returns(default(ValueTask));
            data.SetSessionManager(mockSessionManager.Object, mockSubscriptionManager.Object);

            var mockQueueFactory = new Mock<IMonitoredItemQueueFactory>();
            mockQueueFactory.Setup(factory => factory.Dispose()).Callback(() => counts.MonitoredItemQueueFactory++);
            data.SetMonitoredItemQueueFactory(mockQueueFactory.Object);

            var mockRoleManager = new Mock<IRoleManager>();
            mockRoleManager.As<IDisposable>().Setup(manager => manager.Dispose()).Callback(() => counts.RoleManager++);
            data.SetRoleManager(mockRoleManager.Object);

            return counts;
        }

        private static bool[] CaptureDisposedState(ServerInternalData data)
        {
            return
            [
                data.RoleManager == null,
                data.NodeManager == null,
                data.DiagnosticsNodeManager == null,
                data.ConfigurationNodeManager == null,
                data.CoreNodeManager == null,
                data.SessionManager == null,
                data.SubscriptionManager == null,
                data.MonitoredItemQueueFactory == null,
                data.RequestManager == null
            ];
        }

        private sealed class DisposalCounts
        {
            public int Total =>
                RoleManager +
                NodeManager +
                SessionManager +
                SubscriptionSync +
                SubscriptionAsync +
                MonitoredItemQueueFactory;

            public int RoleManager { get; set; }

            public int NodeManager { get; set; }

            public int SessionManager { get; set; }

            public int SubscriptionSync { get; set; }

            public int SubscriptionAsync { get; set; }

            public int MonitoredItemQueueFactory { get; set; }
        }
    }
}
