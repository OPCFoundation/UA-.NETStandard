/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PlasticsRubber.Runtime.Tests
{
    public sealed partial class CommonBindingTests
    {
        /// <summary>
        /// An unloaded owner is rejected rather than silently creating a second model or manager.
        /// </summary>
        [Test]
        public async Task RuntimeRequiresExistingOwnerPolicyAndLoadedCommonModelAsync()
        {
            await using PortFreeRuntimeOwner owner = CreateRuntimeOwner(loadCommon: false);
            Assert.Throws<ArgumentNullException>(() => new PlasticsRubberRuntime(null!, AllowAccess()));
            Assert.Throws<ArgumentNullException>(() => new PlasticsRubberRuntime(owner, null!));
            ServiceResultException? error = Assert.Throws<ServiceResultException>(
                () => new PlasticsRubberRuntime(owner, AllowAccess()));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(owner.FindPredefinedNode(
                ExpandedNodeId.ToNodeId(ObjectTypeIds.StartDeviceType, owner.SystemContext.NamespaceUris)), Is.Null);
        }

        /// <summary>
        /// Runtime forwarding retains the owner context and policy and never fabricates device status.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RuntimeDispatchUsesExistingOwnerContextAndExplicitPolicyAsync(bool deny)
        {
            await using PortFreeRuntimeOwner owner = CreateRuntimeOwner();
            StartDeviceState node = CreateStartDevice();
            node.Status!.Value = StartEnumeration.READY_TO_START;
            var provider = new Mock<IStartDeviceProvider>(MockBehavior.Strict);
            var policy = new Mock<IPlasticsRubberAccessPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                It.Is<PlasticsRubberRequestContext>(request =>
                    ReferenceEquals(request.SystemContext, owner.SystemContext) && request.TargetId == node.NodeId),
                node.StartRequest!.NodeId, It.IsAny<CancellationToken>()))
                .Returns((PlasticsRubberRequestContext _, NodeId _, CancellationToken _) =>
                    deny ? throw new ServiceResultException(StatusCodes.BadUserAccessDenied) : default);
            if (!deny)
            {
                provider.Setup(value => value.StartAsync(
                    It.Is<PlasticsRubberRequestContext>(request =>
                        ReferenceEquals(request.SystemContext, owner.SystemContext) && request.TargetId == node.NodeId),
                    It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            }
            var runtime = new PlasticsRubberRuntime(owner, policy.Object);
            StartDeviceBinding binding = await runtime.BindStartDeviceAsync(node,
                provider.Object).ConfigureAwait(false);

            ServiceResult result = await node.StartRequest!.CallAsync(
                owner.SystemContext, node.NodeId, [], [], []).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(runtime.Manager, Is.SameAs(owner));
                Assert.That(runtime.AccessPolicy, Is.SameAs(policy.Object));
                Assert.That(binding.Node, Is.SameAs(node));
                Assert.That(result.StatusCode, Is.EqualTo(deny ? StatusCodes.BadUserAccessDenied : StatusCodes.Good));
                Assert.That(node.Status.Value, Is.EqualTo(StartEnumeration.READY_TO_START));
                Assert.That(node.StartRequest.Executable, Is.True);
            });
            policy.VerifyAll();
            provider.Verify(value => value.StartAsync(
                It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<CancellationToken>()),
                deny ? Times.Never() : Times.Once());
            await owner.DisposeAsync().ConfigureAwait(false);
            Assert.That(node.StartRequest.Executable, Is.False);
            Assert.That(node.StopRequest!.Executable, Is.False);
        }

        /// <summary>
        /// Each runtime adapter transfers the exact selected binding to its existing owner's lifetime.
        /// </summary>
        [TestCase("start")]
        [TestCase("maintenance")]
        [TestCase("monitored")]
        [TestCase("closed-loop")]
        [TestCase("configuration")]
        [TestCase("machine-status")]
        [TestCase("production")]
        [TestCase("active-job")]
        [TestCase("jobs")]
        [TestCase("cyclic-configuration")]
        public async Task RuntimeAdoptsSelectedBindingsAndReleasesTheirMethodsWithOwnerAsync(string capability)
        {
            await using PortFreeRuntimeOwner owner = CreateRuntimeOwner();
            var runtime = new PlasticsRubberRuntime(owner, AllowAccess());
            MethodState method;
            NodeState original;
            NodeState bound;
            switch (capability)
            {
                case "start":
                {
                    StartDeviceState node = CreateStartDevice();
                    original = node;
                    bound = (await runtime.BindStartDeviceAsync(
                        node, Mock.Of<IStartDeviceProvider>()).ConfigureAwait(false)).Node;
                    method = node.StartRequest!;
                    break;
                }
                case "maintenance":
                {
                    MaintenanceState node = m_context.CreateInstanceOfMaintenanceType(Parent(),
                        new QualifiedName("Maintenance", 1));
                    original = node;
                    bound = (await runtime.BindMaintenanceAsync(
                        node, Mock.Of<IMaintenanceProvider>()).ConfigureAwait(false)).Node;
                    method = node.Reset!;
                    break;
                }
                case "monitored":
                {
                    MonitoredParameterState node = CreateSettingsNode(controlled: false);
                    node.AddResetMonitoring(m_context);
                    original = node;
                    bound = (await runtime.BindMonitoredParameterAsync(
                        node, Mock.Of<IMonitoredParameterProvider>()).ConfigureAwait(false)).Node;
                    method = node.ResetMonitoring!;
                    break;
                }
                case "closed-loop":
                {
                    ClosedLoopControlState node = CreateClosedLoopSettings();
                    original = node;
                    bound = (await runtime.BindClosedLoopControlAsync(
                        node, Mock.Of<IClosedLoopControlProvider>()).ConfigureAwait(false)).Node;
                    method = node.AutoTuningOn!;
                    break;
                }
                case "configuration":
                {
                    MachineConfigurationState node = m_context.CreateInstanceOfMachineConfigurationType(Parent(),
                        new QualifiedName("Configuration", 1));
                    node.AddGetPage(m_context);
                    original = node;
                    bound = (await runtime.BindMachineConfigurationAsync(
                        node, Mock.Of<IMachineConfigurationProvider>()).ConfigureAwait(false)).Node;
                    method = node.GetPage!;
                    break;
                }
                case "machine-status":
                {
                    MachineStatusState node = m_context.CreateInstanceOfMachineStatusType(Parent(),
                        new QualifiedName("Status", 1));
                    node.AddActivateSleepMode(m_context);
                    original = node;
                    bound = (await runtime.BindMachineStatusAsync(
                        node, Mock.Of<IMachineStatusProvider>()).ConfigureAwait(false)).Node;
                    method = node.ActivateSleepMode!;
                    break;
                }
                case "production":
                {
                    ProductionControlState node = m_context.CreateInstanceOfProductionControlType(Parent(),
                        new QualifiedName("Production", 1));
                    original = node;
                    bound = (await runtime.BindProductionControlAsync(
                        node, Mock.Of<IProductionControlProvider>()).ConfigureAwait(false)).Node;
                    method = node.EnableAutomaticRun!;
                    break;
                }
                case "active-job":
                {
                    ActiveJobValuesState node = m_context.CreateInstanceOfActiveJobValuesType(Parent(),
                        new QualifiedName("ActiveJob", 1));
                    original = node;
                    bound = (await runtime.BindActiveJobAsync(
                        node, Mock.Of<IActiveJobProvider>()).ConfigureAwait(false)).Node;
                    method = node.InterruptJob!;
                    break;
                }
                case "jobs":
                {
                    JobsState node = CreateJobExchange();
                    original = node;
                    bound = (await runtime.BindJobListsAsync(
                        node, Mock.Of<IJobListProvider>()).ConfigureAwait(false)).Node;
                    method = node.SendJobList!;
                    break;
                }
                case "cyclic-configuration":
                {
                    CyclicJobInformationState node = CreateCyclicConfigurationNode();
                    original = node;
                    bound = (await runtime.BindCyclicJobInformationAsync(
                        node, Mock.Of<ICyclicJobConfigurationProvider>()).ConfigureAwait(false)).Node;
                    method = node.SetCyclicJobData!;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(capability));
            }
            Assert.That(bound, Is.SameAs(original));
            Assert.That(method.Executable, Is.True);
            Assert.That(method.UserExecutable, Is.True);

            await owner.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(method.Executable, Is.False, "An unadopted binding would remain executable.");
                Assert.That(method.UserExecutable, Is.False);
                Assert.That(bound, Is.SameAs(original), "Ownership release must not replace the selected node.");
            });
        }

        private PortFreeRuntimeOwner CreateRuntimeOwner(bool loadCommon = true)
        {
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.NamespaceUris).Returns(m_context.NamespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.Factory).Returns(m_context.EncodeableFactory);
            server.SetupGet(value => value.TypeTree).Returns((TypeTable)m_context.TypeTable);
            var context = new ServerSystemContext(server.Object);
            server.SetupGet(value => value.DefaultSystemContext).Returns(context);
            return new PortFreeRuntimeOwner(server.Object, loadCommon);
        }

        /// <summary>
        /// Loads the real embedded common model into a DI owner without starting a server or transport.
        /// </summary>
        private sealed class PortFreeRuntimeOwner : DiNodeManager
        {
            internal PortFreeRuntimeOwner(IServerInternal server, bool loadCommon)
                : base(server, new ApplicationConfiguration { ApplicationUri = "urn:plastics-runtime-tests:instances" })
            {
                if (loadCommon)
                {
                    var nodes = new NodeStateCollection();
                    new PlasticsRubberModelProvider().AddPredefinedNodes(nodes, SystemContext);
                    foreach (NodeState node in nodes)
                    {
                        PredefinedNodes[node.NodeId] = node;
                    }
                }
            }

            protected override async ValueTask DisposeAsyncCore()
            {
                await this.DisposeMachineryResourcesAsync().ConfigureAwait(false);
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }
    }
}
