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
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Sks;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Coverage for <see cref="PubSubNodeManager"/> and
    /// <see cref="PubSubNodeManagerFactory"/>: namespace
    /// registration, address-space initialization, method-handler
    /// binding through a mocked
    /// <see cref="IDiagnosticsNodeManager"/>, and default
    /// SecurityGroup seeding.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.5", Summary = "PublishSubscribe Object mounting")]
    [TestSpec("9.1.10", Summary = "Status.State binding")]
    public class PubSubNodeManagerTests
    {
        [Test]
        public async Task CreateAddressSpaceAsync_BindsStandardMethods()
        {
            using var harness = new Harness();

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.That(harness.Manager.AreMethodsBound, Is.True);
            Assert.That(harness.Manager.StatusBinding, Is.Not.Null);
            Assert.That(harness.Manager.StatusBinding!.StateBound, Is.True);
            Assert.That(harness.EnableMethod.OnCallMethod, Is.Not.Null);
            Assert.That(harness.DisableMethod.OnCallMethod, Is.Not.Null);
            Assert.That(harness.SetSecurityKeysMethod.OnCallMethod, Is.Not.Null);
            Assert.That(harness.AddConnectionMethod.OnCallMethod, Is.Not.Null);
            Assert.That(harness.RemoveConnectionMethod.OnCallMethod, Is.Not.Null);
        }

        [Test]
        public async Task CreateAddressSpaceAsync_WhenSksExposed_BindsSecurityKeyMethods()
        {
            using var harness = new Harness(opt =>
            {
                opt.ExposeSecurityKeyService = true;
            }, includeSks: true);

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(harness.GetSecurityKeysMethod.OnCallMethod2, Is.Not.Null);
                Assert.That(harness.GetSecurityGroupMethod.OnCallMethod, Is.Not.Null);
                Assert.That(harness.AddSecurityGroupMethod.OnCallMethod, Is.Not.Null);
                Assert.That(harness.RemoveSecurityGroupMethod.OnCallMethod, Is.Not.Null);
            });
        }

        [Test]
        public async Task CreateAddressSpaceAsync_WhenConfigMethodsDisabled_SkipsAddRemove()
        {
            using var harness = new Harness(opt =>
            {
                opt.ExposeConfigurationMethods = false;
            });

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.That(harness.SetSecurityKeysMethod.OnCallMethod, Is.Null);
            Assert.That(harness.AddConnectionMethod.OnCallMethod, Is.Null);
            Assert.That(harness.RemoveConnectionMethod.OnCallMethod, Is.Null);
            // Enable/Disable on PubSubStatusType is always bound — those
            // belong to the Status object, not the configuration set.
            Assert.That(harness.EnableMethod.OnCallMethod, Is.Not.Null);
        }

        [Test]
        public async Task CreateAddressSpaceAsync_WithDefaultSecurityGroup_SeedsGroup()
        {
            using var harness = new Harness(opt =>
            {
                opt.ExposeSecurityKeyService = true;
                opt.DefaultSecurityGroupId = "seed-grp";
                opt.DefaultSecurityPolicyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr;
                opt.DefaultKeyLifetimeMs = 60_000;
            }, includeSks: true);

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.That(((string[]?)harness.SksServer.SecurityGroupIds) ?? [], Contains.Item("seed-grp"));
        }

        [Test]
        public async Task CreateAddressSpaceAsync_WithSeededExistingGroup_DoesNotDuplicate()
        {
            using var harness = new Harness(opt =>
            {
                opt.ExposeSecurityKeyService = true;
                opt.DefaultSecurityGroupId = "preexisting";
            }, includeSks: true);
            await harness.SksServer.AddSecurityGroupAsync(new SksSecurityGroup(
                "preexisting",
                PubSubSecurityPolicyUri.PubSubAes128Ctr,
                TimeSpan.FromMinutes(1),
                2, 2, Array.Empty<PubSubSecurityKey>())).ConfigureAwait(false);

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.That(harness.SksServer.SecurityGroupIds, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task CreateAddressSpaceAsync_WithDiagnosticsExposureNone_SkipsBinding()
        {
            using var harness = new Harness(opt =>
            {
                opt.DiagnosticsExposure = PubSubDiagnosticsExposure.None;
            });

            await harness.Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);

            Assert.That(harness.Manager.StatusBinding, Is.Null);
            Assert.That(harness.Manager.AreMethodsBound, Is.True);
        }

        [Test]
        public void Constructor_NullArgs_Throw()
        {
            using var harness = new Harness();
            // The harness already produced one Manager, so use its
            // collaborators to exercise null-arg paths for the
            // public constructor.
            ApplicationConfiguration config = harness.Configuration;
            IPubSubApplication app = harness.Application;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var options = new PubSubServerOptions();

            Assert.Multiple(() =>
            {
                Assert.That(() => new PubSubNodeManager(
                    harness.MockServer.Object, config, null!, null, options, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubNodeManager(
                    harness.MockServer.Object, config, app, null, null!, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubNodeManager(
                    harness.MockServer.Object, config, app, null, options, null!),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void Factory_Create_ReturnsAttachedSyncNodeManager()
        {
            using var harness = new Harness();
            var factory = new PubSubNodeManagerFactory(
                harness.Application,
                null,
                new PubSubServerOptions(),
                NUnitTelemetryContext.Create());

            Assert.That(factory.NamespacesUris, Is.Not.Empty);
            INodeManager nm = factory.Create(harness.MockServer.Object, harness.Configuration);

            Assert.That(nm, Is.Not.Null);
            (nm as IDisposable)?.Dispose();
        }

        [Test]
        public void Factory_NullArgs_Throw()
        {
            using var harness = new Harness();
            var options = new PubSubServerOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Assert.Multiple(() =>
            {
                Assert.That(() => new PubSubNodeManagerFactory(null!, null, options, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubNodeManagerFactory(harness.Application, null, null!, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubNodeManagerFactory(harness.Application, null, options, null!),
                    Throws.ArgumentNullException);
            });
        }

        private sealed class Harness : IDisposable
        {
            public Harness(Action<PubSubServerOptions>? configure = null, bool includeSks = false)
            {
                MockServer = new Mock<IServerInternal>();
                NamespaceTable = new NamespaceTable();
                NamespaceTable.Append(Namespaces.OpcUa);
                MockServer.Setup(s => s.NamespaceUris).Returns(NamespaceTable);
                MockServer.Setup(s => s.ServerUris).Returns(new StringTable());
                MockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
                MockServer.Setup(s => s.TypeTree).Returns(new TypeTable(NamespaceTable));

                var mockMaster = new Mock<IMasterNodeManager>();
                var mockConfig = new Mock<IConfigurationNodeManager>();
                mockMaster.Setup(m => m.ConfigurationNodeManager).Returns(mockConfig.Object);
                MockServer.Setup(s => s.NodeManager).Returns(mockMaster.Object);

                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                MockServer.Setup(s => s.Telemetry).Returns(telemetry);

                m_queueFactory = new MonitoredItemQueueFactory(telemetry);
                MockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);

                m_serverSystemContext = new ServerSystemContext(MockServer.Object);
                MockServer.Setup(s => s.DefaultSystemContext).Returns(m_serverSystemContext);

                Configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MaxNotificationQueueSize = 100,
                        MaxDurableNotificationQueueSize = 200
                    }
                };

                EnableMethod = NewMethod(17407);
                DisableMethod = NewMethod(17408);
                SetSecurityKeysMethod = NewMethod(17364);
                AddConnectionMethod = NewMethod(17366);
                RemoveConnectionMethod = NewMethod(17369);
                GetSecurityKeysMethod = NewMethod(15215);
                GetSecurityGroupMethod = NewMethod(15440);
                AddSecurityGroupMethod = NewMethod(15444);
                RemoveSecurityGroupMethod = NewMethod(15447);
                StatusVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(17406u),
                    BrowseName = new QualifiedName("State")
                };

                var diagnosticsNm = new Mock<IDiagnosticsNodeManager>();
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17407u))).Returns(EnableMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17408u))).Returns(DisableMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17364u))).Returns(SetSecurityKeysMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17366u))).Returns(AddConnectionMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(17369u))).Returns(RemoveConnectionMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15215u))).Returns(GetSecurityKeysMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15440u))).Returns(GetSecurityGroupMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15444u))).Returns(AddSecurityGroupMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<MethodState>(new NodeId(15447u))).Returns(RemoveSecurityGroupMethod);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(17406u))).Returns(StatusVariable);
                diagnosticsNm.Setup(m => m.FindPredefinedNode<BaseVariableState>(It.IsAny<NodeId>()))
                    .Returns((NodeId id) => id == new NodeId(17406u) ? StatusVariable : null!);
                MockServer.Setup(s => s.DiagnosticsNodeManager).Returns(diagnosticsNm.Object);

                Application = new PubSubApplicationBuilder(NUnitTelemetryContext.Create())
                    .WithApplicationId("test-nodemanager")
                    .UseConfiguration(new PubSubConfigurationDataType
                    {
                        Connections = [],
                        PublishedDataSets = []
                    })
                    .UseAllStandardEncoders()
                    .Build();

                SksServer = new InMemoryPubSubKeyServiceServer();

                Options = new PubSubServerOptions();
                configure?.Invoke(Options);

                Manager = new PubSubNodeManager(
                    MockServer.Object,
                    Configuration,
                    Application,
                    includeSks ? SksServer : null,
                    Options,
                    telemetry);
            }

            public Mock<IServerInternal> MockServer { get; }
            public NamespaceTable NamespaceTable { get; }
            public ApplicationConfiguration Configuration { get; }
            public IPubSubApplication Application { get; }
            public InMemoryPubSubKeyServiceServer SksServer { get; }
            public PubSubServerOptions Options { get; }
            public PubSubNodeManager Manager { get; }
            public MethodState EnableMethod { get; }
            public MethodState DisableMethod { get; }
            public MethodState SetSecurityKeysMethod { get; }
            public MethodState AddConnectionMethod { get; }
            public MethodState RemoveConnectionMethod { get; }
            public MethodState GetSecurityKeysMethod { get; }
            public MethodState GetSecurityGroupMethod { get; }
            public MethodState AddSecurityGroupMethod { get; }
            public MethodState RemoveSecurityGroupMethod { get; }
            public BaseDataVariableState StatusVariable { get; }

            public void Dispose()
            {
                Manager.Dispose();
                (Application as IDisposable)?.Dispose();
                (Application as IAsyncDisposable)?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                m_queueFactory.Dispose();
            }

            private static MethodState NewMethod(uint nodeId)
            {
                return new MethodState(null)
                {
                    NodeId = new NodeId(nodeId),
                    BrowseName = new QualifiedName("M" + nodeId)
                };
            }

            private readonly MonitoredItemQueueFactory m_queueFactory;
            private readonly ServerSystemContext m_serverSystemContext;
        }
    }
}
