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
                m_messageContext,
                null,
                null);
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
            Assert.That(data.EndpointAddresses.Count(), Is.EqualTo(0));
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
        public void DiagnosticsLockIsNotNull()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.DiagnosticsLock, Is.Not.Null);
        }

        [Test]
        public void DiagnosticsEnabledReturnsFalseWhenNoDiagnosticsNodeManager()
        {
            using ServerInternalData data = CreateServerInternalData();
            Assert.That(data.DiagnosticsEnabled, Is.False);
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
            Assert.DoesNotThrow(() => data.Dispose());
        }

        [Test]
        public void DisposeCanBeCalledTwice()
        {
            ServerInternalData data = CreateServerInternalData();
            data.Dispose();
            Assert.DoesNotThrow(() => data.Dispose());
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
            Uri[] addresses = data.EndpointAddresses.ToArray();
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
    }
}
