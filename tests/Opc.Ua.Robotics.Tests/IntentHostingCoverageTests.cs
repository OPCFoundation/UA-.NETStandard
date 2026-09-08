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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Robotics.Server.Hosting;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Covers Robot Intent server hosting and standalone node-manager seams.
    /// </summary>
    [TestFixture]
    public class IntentHostingCoverageTests
    {
        [Test]
        public void ServerOptionsValidateDefaultsAndRejectBlankValues()
        {
            var options = new RobotIntentServerOptions();

            Assert.Multiple(() =>
            {
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:opcfoundation:robot-intent:instances"));
                Assert.That(options.SpecificationVersion, Is.EqualTo("0.1.0"));
                Assert.That(() => options.Validate(), Throws.Nothing);
                Assert.That(
                    () => new RobotIntentServerOptions { InstanceNamespaceUri = " " }.Validate(),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("InstanceNamespaceUri"));
                Assert.That(
                    () => new RobotIntentServerOptions { SpecificationVersion = string.Empty }.Validate(),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("SpecificationVersion"));
            });
        }

        [Test]
        public async Task RejectingExecutorReportsMissingRegistration()
        {
            var executor = new RobotIntentRejectingExecutor();

            IntentOutcome outcome = await executor.ExecuteAsync(null!, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.State, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(outcome.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(outcome.Message, Is.EqualTo("No Robot Intent executor is registered."));
                Assert.That(executor.CanCancel(null!), Is.True);
            });
        }

        [Test]
        public void ModelProviderLoadsNodesAndGuardsArguments()
        {
            var provider = new RobotIntentModelProvider();
            SystemContext context = CreateSystemContext();
            var nodes = new NodeStateCollection();

            provider.AddPredefinedNodes(nodes, context);

            Assert.Multiple(() =>
            {
                Assert.That(provider.Order, Is.EqualTo(int.MinValue));
                Assert.That(provider.NamespaceUris.ToArray(), Does.Contain(global::Opc.Ua.RobotIntent.Namespaces.RobotIntent));
                Assert.That(nodes, Has.Count.GreaterThan(0));
                Assert.That(
                    () => provider.AddPredefinedNodes(null!, context),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("nodes"));
                Assert.That(
                    () => provider.AddPredefinedNodes(nodes, null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("context"));
            });
        }

        [Test]
        public void HostingExtensionsRegisterServicesAndOptions()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });

            Assert.That(
                () => builder
                    .AddRobotIntent(options =>
                    {
                        options.InstanceNamespaceUri = "urn:test:intent";
                        options.SpecificationVersion = "1.2.3";
                    })
                    .AddRobotIntentExecutor<CompletingExecutor>()
                    .ConfigureRobotIntent(_ => { })
                    .ConfigureRobotIntent(async (context, cancellationToken) =>
                    {
                        await context.AddIntentControllerAsync(
                            "ConfiguredAsync",
                            controller => controller.Accepts<WaitIntentDataType>(),
                            cancellationToken).ConfigureAwait(false);
                    }),
                Throws.Nothing);

            using ServiceProvider provider = services.BuildServiceProvider();
            RobotIntentServerOptions options = provider.GetRequiredService<IOptions<RobotIntentServerOptions>>().Value;
            IIntentExecutor[] executors = [.. provider.GetServices<IIntentExecutor>()];

            Assert.Multiple(() =>
            {
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:test:intent"));
                Assert.That(options.SpecificationVersion, Is.EqualTo("1.2.3"));
                Assert.That(provider.GetServices<IRobotIntentModelProvider>(), Has.Exactly(1).Items);
                Assert.That(provider.GetServices<IServerStartupTask>(), Has.Exactly(1).Items);
                Assert.That(provider.GetRequiredService<IRobotIntentPostSetupRunner>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<RobotIntentNodeManagerFactory>(), Is.Not.Null);
                Assert.That(executors.Any(static executor => executor is RobotIntentRejectingExecutor), Is.True);
                Assert.That(executors.Any(static executor => executor is CompletingExecutor), Is.True);
            });
        }

        [Test]
        public void HostingExtensionsBindOptionsFromConfigurationAndGuardNulls()
        {
            var values = new Dictionary<string, string?>
            {
                ["OpcUa:RobotIntent:InstanceNamespaceUri"] = "urn:configured:intent",
                ["OpcUa:RobotIntent:SpecificationVersion"] = "9.8.7"
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });

            builder.Services.Configure<RobotIntentServerOptions>(configuration.GetSection("OpcUa:RobotIntent"));
            builder.AddRobotIntent();

            using ServiceProvider provider = services.BuildServiceProvider();
            RobotIntentServerOptions options = provider.GetRequiredService<IOptions<RobotIntentServerOptions>>().Value;

            Assert.Multiple(() =>
            {
                Assert.That(options.InstanceNamespaceUri, Is.EqualTo("urn:configured:intent"));
                Assert.That(options.SpecificationVersion, Is.EqualTo("9.8.7"));
                Assert.That(
                    () => ((IOpcUaServerBuilder)null!).AddRobotIntent(),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("builder"));
                Assert.That(
                    () => ((IOpcUaServerBuilder)null!).AddRobotIntentExecutor<CompletingExecutor>(),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("builder"));
                Assert.That(
                    () => builder.ConfigureRobotIntent((Action<IRobotIntentBuildContext>)null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("configure"));
                Assert.That(
                    () => ((IOpcUaServerBuilder)null!).ConfigureRobotIntent(
                        static (context, cancellationToken) => default),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("builder"));
                Assert.That(
                    () => builder.ConfigureRobotIntent(
                        (Func<IRobotIntentBuildContext, CancellationToken, ValueTask>)null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("configure"));
            });
        }

        [Test]
        public async Task NodeManagerCreatesRootRegistersEncodeablesAndStartsHostsIdempotently()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            await fixture.StartAsync(new RobotIntentServerOptions
            {
                InstanceNamespaceUri = "urn:test:intent:root",
                SpecificationVersion = "5.0.0"
            }, new ControllerSetupRunner()).ConfigureAwait(false);
            RobotIntentNodeManager manager = fixture.Manager;

            manager.StartIntentControllerHosts();
            IntentControllerHost host = manager.GetIntentControllerHost(fixture.Runner.Controller!.NodeId);

            Assert.Multiple(() =>
            {
                Assert.That(manager.Root, Is.Not.Null);
                Assert.That(manager.Root.Controllers, Is.Not.Null);
                Assert.That(manager.Root.SpecificationVersion!.Value, Is.EqualTo("5.0.0"));
                Assert.That(fixture.ExternalReferences.ContainsKey(global::Opc.Ua.ObjectIds.Server), Is.True);
                Assert.That(manager.IntentControllerHosts.Count, Is.EqualTo(1));
                Assert.That(host, Is.SameAs(fixture.Runner.Builder!.Host));
                Assert.That(manager.SystemContext.EncodeableFactory.TryGetEncodeableType(
                    new Pose3DDataType().BinaryEncodingId,
                    out _), Is.True);
                Assert.That(
                    () => manager.GetIntentControllerHost(NodeId.Null),
                    Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("controllerNodeId"));
                Assert.That(
                    () => manager.GetIntentControllerHost(new NodeId("missing", 2)),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
            });
        }

        [Test]
        public async Task NodeManagerDisposesWithoutStartAndRootRequiresAddressSpace()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            await fixture.CreateServerAsync().ConfigureAwait(false);
            RobotIntentNodeManager manager = fixture.CreateManager(new RobotIntentServerOptions(), null);

            Assert.That(
                () => _ = manager.Root,
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(manager, Is.InstanceOf<IAsyncDisposable>());

            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task NodeManagerCompletesBaseDisposeAfterSynchronousDeferredHostShutdown()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            await fixture.CreateServerAsync().ConfigureAwait(false);
            RobotIntentNodeManager manager = fixture.CreateManager(new RobotIntentServerOptions(), null);
            using var executor = new BlockingExecutor();
            SystemContext context = CreateSystemContext();
            var controller = new IntentControllerState(null);
            controller.Create(
                context,
                new NodeId("DeferredController", 1),
                new QualifiedName("DeferredController", 1),
                new LocalizedText("DeferredController"),
                true);
            var options = new IntentControllerHostOptions
            {
                OperationalMode = OperationalModeEnum.AutomaticExternal,
                RequireControlAuthority = false,
                AxisCount = 6,
                MaxQueueDepth = 4,
                ExecutorShutdownTimeoutMs = 50
            };
            options.Accept(global::Opc.Ua.RobotIntent.DataTypeIds.LinearMoveIntentDataType);
            var host = new IntentControllerHost(
                controller,
                executor,
                (_, _) => default,
                options);
            manager.RegisterIntentControllerHost(host);
            host.Start(context);

            IntentAdmission admission = host.SubmitIntent(context, null, Move("hung"));
            Assert.That(admission.Accepted, Is.True, admission.Message);
            await WaitUntilAsync(() => executor.Started.Task.IsCompleted).ConfigureAwait(false);

            manager.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(host.IsShutdownDeferred, Is.True);
                if (!host.ResourcesDisposed)
                {
                    Assert.That(manager.BaseDisposeStarted, Is.False);
                }
            });

            executor.Release();
            await WaitUntilAsync(() => manager.BaseDisposeStarted).ConfigureAwait(false);

            Assert.That(host.ResourcesDisposed, Is.True);
        }

        [Test]
        public async Task FactoryCreatesManagerAndContextResolvesServices()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            await fixture.CreateServerAsync().ConfigureAwait(false);
            var factory = new RobotIntentNodeManagerFactory();

            IAsyncNodeManager created = await factory.CreateAsync(
                fixture.Server.CurrentInstance,
                fixture.Configuration).ConfigureAwait(false);
            var manager = (RobotIntentNodeManager)created;
            var references = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(references).ConfigureAwait(false);
            var services = new ServiceCollection();
            services.AddSingleton<IIntentExecutor, CompletingExecutor>();
            using ServiceProvider provider = services.BuildServiceProvider();
            var context = new RobotIntentBuildContext(
                manager,
                manager.Root,
                new RobotIntentServerOptions(),
                CancellationToken.None,
                provider);

            Assert.Multiple(() =>
            {
                Assert.That(factory.NamespacesUris.ToArray(), Does.Contain(global::Opc.Ua.RobotIntent.Namespaces.RobotIntent));
                Assert.That(factory.NamespacesUris.ToArray(),
                    Does.Contain(new RobotIntentServerOptions().InstanceNamespaceUri));
                Assert.That(context.GetRequiredService<IIntentExecutor>(), Is.TypeOf<CompletingExecutor>());
                Assert.That(context.TryGetService(out IIntentExecutor? executor), Is.True);
                Assert.That(executor, Is.TypeOf<CompletingExecutor>());
            });

            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DirectBuildContextGuardsInvalidConstructionAndMissingServices()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            await fixture.StartAsync(new RobotIntentServerOptions(), new ControllerSetupRunner(false))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new RobotIntentBuildContext(
                        null!,
                        fixture.Manager.Root,
                        new RobotIntentServerOptions(),
                        CancellationToken.None),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("manager"));
                Assert.That(
                    () => new RobotIntentBuildContext(
                        fixture.Manager,
                        null!,
                        new RobotIntentServerOptions(),
                        CancellationToken.None),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("root"));
                Assert.That(
                    () => new RobotIntentBuildContext(
                        fixture.Manager,
                        fixture.Manager.Root,
                        null!,
                        CancellationToken.None),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("options"));
                Assert.That(
                    () => new RobotIntentBuildContext(
                        fixture.Manager,
                        fixture.Manager.Root,
                        new RobotIntentServerOptions { InstanceNamespaceUri = "urn:not-registered" },
                        CancellationToken.None),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadConfigurationError));
            });

            IRobotIntentBuildContext context = fixture.Manager.CreateRobotIntentBuildContext();

            Assert.Multiple(() =>
            {
                Assert.That(context.Manager, Is.SameAs(fixture.Manager));
                Assert.That(context.Root, Is.SameAs(fixture.Manager.Root));
                Assert.That(context.InstanceNamespaceIndex, Is.GreaterThan(0));
                Assert.That(context.Nodes, Is.Not.Null);
                Assert.That(
                    () => context.GetRequiredService<IIntentExecutor>(),
                    Throws.InvalidOperationException);
                Assert.That(
                    () => ((IRobotIntentBuildContext)null!).AddIntentControllerAsync(
                        "Controller",
                        controller => controller.Accepts<WaitIntentDataType>()),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("context"));
                Assert.That(
                    () => context.AddIntentControllerAsync("Controller", null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("configure"));
            });
        }

        [Test]
        public async Task NodeManagerCreatesStableNodeIdsAndNormalizesProviders()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            var options = new RobotIntentServerOptions { InstanceNamespaceUri = "urn:test:factory" };
            await fixture.StartAsync(options, new ControllerSetupRunner(false)).ConfigureAwait(false);
            BaseDataVariableState child = new BaseDataVariableState(fixture.Manager.Root)
            {
                SymbolicName = "Child",
                NodeId = NodeId.Null
            };

            NodeId childId = fixture.Manager.New(fixture.Manager.SystemContext, child);
            var standalone = new BaseObjectState(null)
            {
                NodeId = NodeId.Null,
                BrowseName = new QualifiedName("Standalone", fixture.Manager.Root.BrowseName.NamespaceIndex)
            };
            NodeId generated = fixture.Manager.New(fixture.Manager.SystemContext, standalone);
            var existing = new BaseObjectState(null)
            {
                NodeId = new NodeId("existing", fixture.Manager.Root.BrowseName.NamespaceIndex)
            };

            Assert.Multiple(() =>
            {
                Assert.That(
                    childId.IdentifierAsString,
                    Is.EqualTo($"{fixture.Manager.Root.NodeId.IdentifierAsString}_Child"));
                Assert.That(generated.IsNull, Is.False);
                Assert.That(fixture.Manager.New(fixture.Manager.SystemContext, existing), Is.EqualTo(existing.NodeId));
                Assert.That(RobotIntentNodeManager.NormalizeProviders(default).Count, Is.EqualTo(1));
                Assert.That(
                    RobotIntentNodeManager.GetNamespaceUris(default, null!).ToArray(),
                    Does.Contain(options.InstanceNamespaceUri).Or.Contain(new RobotIntentServerOptions().InstanceNamespaceUri));
            });
        }

        [Test]
        public void SafetySnapshotPositionalParametersMapToNamedProperties()
        {
            var reason = new LocalizedText("stopped");
            var snapshot = new RobotIntentSafetySnapshot(
                SafeMotionFunctionEnum.Sls,
                true,
                false,
                true,
                0.5,
                false,
                reason);

            Assert.Multiple(() =>
            {
                Assert.That(snapshot.ActiveFunction, Is.EqualTo(SafeMotionFunctionEnum.Sls));
                Assert.That(snapshot.EmergencyStopActive, Is.True);
                Assert.That(snapshot.ProtectiveStopActive, Is.False);
                Assert.That(snapshot.SafeSpeedLimitActive, Is.True);
                Assert.That(snapshot.SafeSpeedLimit, Is.EqualTo(0.5));
                Assert.That(snapshot.SafetyControllerOk, Is.False);
                Assert.That(snapshot.LastStopReason, Is.EqualTo(reason));
            });
        }

        [Test]
        public async Task NodeManagerRejectsEmptyAndDuplicateControllerConfigurations()
        {
            await using IntentServerFixture emptyFixture = new IntentServerFixture();
            await emptyFixture.StartAsync(new RobotIntentServerOptions(), new ControllerSetupRunner(false))
                .ConfigureAwait(false);

            Assert.That(emptyFixture.Manager.IntentControllerHosts, Is.Empty);

            await using IntentServerFixture duplicateFixture = new IntentServerFixture();
            var duplicateRunner = new DuplicateControllerSetupRunner();

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await duplicateFixture.StartAsync(new RobotIntentServerOptions(), duplicateRunner)
                    .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public async Task IntentBuilderCreatesAddressSpaceGraphAndOptionalMethods()
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder full = await context.AddIntentControllerAsync(
                    "Full",
                    controller =>
                    {
                        controller
                            .WithOperationalMode(OperationalModeEnum.ManualReducedSpeed)
                            .WithReady(false)
                            .WithMaxQueueDepth(9);
                        IIntentFrameBuilder baseFrame = controller.AddFrame(
                            "Base",
                            "base",
                            FrameRoleEnum.Base,
                            Pose());
                        IIntentFrameBuilder tcpFrame = controller.AddFrame(
                            "Tcp",
                            "tcp",
                            FrameRoleEnum.Tool,
                            Pose(),
                            frame => frame.WithParent(baseFrame));
                        controller.AddTool("Gripper", tcpFrame, true);
                        controller.AddTool("Spare", tcpFrame);
                        controller.AddLocation("Bin", Pose(), location => location.WithOccupancy(true, 4));
                        controller.AddAxis("Axis0", 0, AxisKindEnum.Revolute);
                        controller.AddAxis("Axis1", 1, AxisKindEnum.Prismatic);
                        controller.AddOutput("ReadySignal", global::Opc.Ua.DataTypeIds.Boolean, Variant.From(true));
                        controller.AddProgram("MainProgram", "main");
                        controller.AddRealTimeChannel(
                            "FastChannel",
                            "fast",
                            RealTimeTransportEnum.OpcUaFx,
                            "udp://239.0.0.1:4840");
                        controller.WithSafetyState(new StaticSafetySource());
                        controller.WithDescription(description => description
                            .WithKinematicChain(new[]
                            {
                                new KinematicJointDataType
                                {
                                    AxisId = "Axis0",
                                    Kind = AxisKindEnum.Revolute,
                                    OriginTransform = Pose(),
                                    AxisVector = s_doubleValues1.ToArrayOf()
                                }
                            }.ToArrayOf())
                            .WithLimits(1.2, 3.4, 5.6, 7.8));
                        controller.State.Capabilities!.MissionsSupported!.Value = true;
                        controller.State.Capabilities.MissionHorizonSupported!.Value = true;
                        controller.State.Capabilities.BlendingSupported!.Value = true;
                        controller.State.Capabilities.MaxTrajectoryPoints!.Value = 128;
                        controller.Accepts<JointMoveIntentDataType>(
                            pauseSupported: true,
                            retrySupported: true);
                        controller.Accepts<TrajectoryIntentDataType>(pauseSupported: false);
                        controller.Accepts<ForceIntentDataType>();
                    },
                    cancellationToken).ConfigureAwait(false);
                IIntentControllerBuilder minimal = await context.AddIntentControllerAsync(
                    "Minimal",
                    controller => controller.Accepts<WaitIntentDataType>(
                        pauseSupported: false,
                        retrySupported: false),
                    cancellationToken).ConfigureAwait(false);
                return new object[] { full, minimal };
            });

            await fixture.StartAsync(new RobotIntentServerOptions(), runner).ConfigureAwait(false);

            var full = (IIntentControllerBuilder)runner.Results![0];
            var minimal = (IIntentControllerBuilder)runner.Results[1];
            IntentControllerState state = full.State;
            NodeId frameParent = NodeId.Create(
                global::Opc.Ua.RobotIntent.ReferenceTypes.HasFrameParent,
                global::Opc.Ua.RobotIntent.Namespaces.RobotIntent,
                fixture.Manager.SystemContext.NamespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(state.OperationalMode!.Value, Is.EqualTo(OperationalModeEnum.ManualReducedSpeed));
                Assert.That(state.Ready!.Value, Is.False);
                Assert.That(state.MaxQueueDepth!.Value, Is.EqualTo(9));
                Assert.That(state.Capabilities!.AxisCount!.Value, Is.GreaterThanOrEqualTo(2));
                Assert.That(state.Capabilities.SupportedIntents!.Value.Count, Is.EqualTo(3));
                Assert.That(state.Capabilities.TrajectorySupported!.Value, Is.True);
                Assert.That(state.Capabilities.ForceControlSupported!.Value, Is.True);
                Assert.That(state.SubmitMission, Is.Not.Null);
                Assert.That(state.CancelMission, Is.Not.Null);
                Assert.That(state.UpdateMission, Is.Not.Null);
                Assert.That(state.OpenRealTimeChannel, Is.Not.Null);
                Assert.That(state.CloseRealTimeChannel, Is.Not.Null);
                Assert.That(state.Pause, Is.Not.Null);
                Assert.That(state.Resume, Is.Not.Null);
                Assert.That(state.Retry, Is.Not.Null);
                Assert.That(Child(state.Frames!, "Tcp").ReferenceExists(
                    frameParent,
                    false,
                    Child(state.Frames!, "Base").NodeId), Is.True);
                Assert.That(((ToolState)Child(state.Tools!, "Gripper")).Fitted!.Value, Is.True);
                Assert.That(((LocationState)Child(state.Locations!, "Bin")).Capacity!.Value, Is.EqualTo(4));
                Assert.That(((OutputSignalState)Child(state.Outputs!, "ReadySignal")).Value!.DataType,
                    Is.EqualTo(global::Opc.Ua.DataTypeIds.Boolean));
                Assert.That(((ProgramState)Child(state.Programs!, "MainProgram")).ProgramId!.Value, Is.EqualTo("main"));
                Assert.That(state.Description!.KinematicChain!.Value.Count, Is.EqualTo(1));
                Assert.That(state.Description.ReachRadius!.Value, Is.EqualTo(1.2));
                Assert.That(state.SafetyState!.SafetyControllerOk!.Value, Is.True);
                Assert.That(full.Host, Is.SameAs(fixture.Manager.GetIntentControllerHost(state.NodeId)));
                Assert.That(full.ComputeFacets().ToArray(), Does.Contain("RI-Trajectory"));
                Assert.That(minimal.State.Retry, Is.Null);
            });
        }

        [TestCase("NoCapabilities")]
        [TestCase("WrongTcpFrameRole")]
        [TestCase("DuplicateFittedTool")]
        [TestCase("MissingAxisIndex")]
        [TestCase("UnsupportedBufferModes")]
        public async Task IntentBuilderRejectsInvalidConfigurations(string scenario)
        {
            await using IntentServerFixture fixture = new IntentServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    scenario,
                    controller => ConfigureInvalid(controller, scenario),
                    cancellationToken).ConfigureAwait(false);
                return new object[] { builder };
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await fixture.StartAsync(new RobotIntentServerOptions(), runner).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(scenario == "WrongTcpFrameRole" || scenario == "DuplicateFittedTool" ||
                    scenario == "UnsupportedBufferModes"
                        ? StatusCodes.BadInvalidArgument
                        : StatusCodes.BadConfigurationError).Or.EqualTo(StatusCodes.BadInvalidState));
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = Opc.Ua.Tests.NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(global::Opc.Ua.RobotIntent.Namespaces.RobotIntent);
            messageContext.Factory.Builder.AddOpcUaRobotIntent().Commit();
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        private static Pose3DDataType Pose()
        {
            return new Pose3DDataType
            {
                FrameId = "world",
                Position = s_doubleValues2.ToArrayOf(),
                Orientation = s_doubleValues3.ToArrayOf()
            };
        }

        private static LinearMoveIntentDataType Move(string intentId)
        {
            return new LinearMoveIntentDataType
            {
                IntentId = intentId,
                BufferMode = BufferModeEnum.Aborting,
                Target = Pose()
            };
        }

        private static BaseInstanceState Child(NodeState parent, string browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            BaseInstanceState? child = children.FirstOrDefault(node => node.BrowseName.Name == browseName);
            Assert.That(child, Is.Not.Null, browseName);
            return child!;
        }

        private static async Task WaitUntilAsync(Func<bool> predicate)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!predicate())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("Timed out waiting for condition.");
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        private static void ConfigureInvalid(IIntentControllerBuilder controller, string scenario)
        {
            if (scenario == "NoCapabilities")
            {
                return;
            }
            if (scenario == "WrongTcpFrameRole")
            {
                IIntentFrameBuilder baseFrame = controller.AddFrame("Base", "base", FrameRoleEnum.Base, Pose());
                controller.AddTool("InvalidTool", baseFrame);
                return;
            }
            if (scenario == "DuplicateFittedTool")
            {
                IIntentFrameBuilder tcp = controller.AddFrame("Tcp", "tcp", FrameRoleEnum.Tool, Pose());
                controller.AddTool("First", tcp, true);
                controller.AddTool("Second", tcp, true);
                return;
            }
            if (scenario == "MissingAxisIndex")
            {
                controller.AddAxis("Axis1", 1, AxisKindEnum.Revolute);
                controller.Accepts<JointMoveIntentDataType>();
                return;
            }
            controller.Accepts<WaitIntentDataType>(
                supportedBufferModes: new[] { BufferModeEnum.Buffered }.ToArrayOf());
        }

        private sealed class IntentServerFixture : IAsyncDisposable
        {
            public StandardServer Server { get; private set; } = null!;

            public ApplicationConfiguration Configuration => m_fixture!.Config;

            public RobotIntentNodeManager Manager { get; private set; } = null!;

            public Dictionary<NodeId, IList<IReference>> ExternalReferences { get; } = [];

            public ControllerSetupRunner Runner { get; private set; } = null!;

            public async Task CreateServerAsync()
            {
                m_fixture = new ServerFixture<StandardServer>(
                    telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                Server = await m_fixture.StartAsync().ConfigureAwait(false);
            }

            public RobotIntentNodeManager CreateManager(
                RobotIntentServerOptions options,
                IRobotIntentPostSetupRunner? runner)
            {
                Manager = new RobotIntentNodeManager(
                    Server.CurrentInstance,
                    Configuration,
                    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                    options,
                    runner);
                return Manager;
            }

            public async Task StartAsync(RobotIntentServerOptions options, ControllerSetupRunner runner)
            {
                Runner = runner;
                await CreateServerAsync().ConfigureAwait(false);
                CreateManager(options, runner);
                await Manager.CreateAddressSpaceAsync(ExternalReferences).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync().ConfigureAwait(false);
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }

            private ServerFixture<StandardServer>? m_fixture;
        }

        private class ControllerSetupRunner : IRobotIntentPostSetupRunner
        {
            public ControllerSetupRunner(bool addController = true)
            {
                m_addController = addController;
            }

            public IntentControllerState? Controller { get; private set; }

            public IIntentControllerBuilder? Builder { get; private set; }

            public virtual async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                if (!m_addController)
                {
                    return;
                }
                var robotIntentManager = (RobotIntentNodeManager)manager;
                IRobotIntentBuildContext context = robotIntentManager.CreateRobotIntentBuildContext(
                    new CompletingExecutor(),
                    cancellationToken);
                Builder = await context.AddIntentControllerAsync(
                    "Controller",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                Controller = Builder.State;
            }

            private readonly bool m_addController;
        }

        private sealed class DuplicateControllerSetupRunner : ControllerSetupRunner
        {
            public override async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                var robotIntentManager = (RobotIntentNodeManager)manager;
                IRobotIntentBuildContext context = robotIntentManager.CreateRobotIntentBuildContext(
                    new CompletingExecutor(),
                    cancellationToken);
                await context.AddIntentControllerAsync(
                    "Duplicate",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                await context.AddIntentControllerAsync(
                    "Duplicate",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private sealed class DelegateSetupRunner : ControllerSetupRunner
        {
            public DelegateSetupRunner(Func<IRobotIntentBuildContext, CancellationToken, ValueTask<object[]>> configure)
            {
                m_configure = configure;
            }

            public object[]? Results { get; private set; }

            public override async ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                var robotIntentManager = (RobotIntentNodeManager)manager;
                IRobotIntentBuildContext context = robotIntentManager.CreateRobotIntentBuildContext(
                    new CompletingExecutor(),
                    cancellationToken);
                Results = await m_configure(context, cancellationToken).ConfigureAwait(false);
            }

            private readonly Func<IRobotIntentBuildContext, CancellationToken, ValueTask<object[]>> m_configure;
        }

        private sealed class StaticSafetySource : IRobotIntentSafetySource
        {
            public ValueTask<RobotIntentSafetySnapshot> ReadAsync(CancellationToken cancellationToken)
            {
                return new ValueTask<RobotIntentSafetySnapshot>(new RobotIntentSafetySnapshot(
                    SafeMotionFunctionEnum.Ss1,
                    false,
                    false,
                    true,
                    0.25,
                    true,
                    new LocalizedText("ok")));
            }
        }

        [SuppressMessage(
            "Performance",
            "CA1812:Avoid uninstantiated internal classes",
            Justification = "Instantiated by DI generic registration in hosting tests; TODO: remove if CA1812 tracks DI.")]
        private sealed class CompletingExecutor : IIntentExecutor
        {
            public ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }
        }

        private sealed class BlockingExecutor : IIntentExecutor, IDisposable
        {
            public TaskCompletionSource<bool> Started { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                Started.TrySetResult(true);
                await m_gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                return IntentOutcome.Success;
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }

            public void Release()
            {
                m_gate.Release();
            }

            public void Dispose()
            {
                m_gate.Dispose();
            }

            private readonly SemaphoreSlim m_gate = new(0);
        }

        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 1.0];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues3 = [0.0, 0.0, 0.0, 1.0];
    }
}
