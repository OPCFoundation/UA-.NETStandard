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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.Robotics.Server.Hosting;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using RiAxisState = Opc.Ua.RobotIntent.AxisState;
using RiDataTypes = Opc.Ua.RobotIntent.DataTypes;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Tests Robot Intent facet computation.
    /// </summary>
    [TestFixture]
    public class IntentFacetBuilderTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            var messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
            m_controller = new IntentControllerState(null);
            m_controller.Create(
                m_context,
                new NodeId("Controller", 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
        }

        [Test]
        public void ComputeFacetsForMinimalControllerClaimsBaseOnly()
        {
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.WaitIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Contain("RI-Base"));
            Assert.That(facets, Does.Contain("RI-Wait"));
            Assert.That(facets, Does.Not.Contain("RI-Trajectory"));
        }

        [Test]
        public void ComputeFacetsRequiresCapabilityAndAddressSpaceEvidence()
        {
            AddAxis("Axis0", 0);
            m_controller.Capabilities!.AxisCount!.Value = 1;
            m_controller.Capabilities.TrajectorySupported!.Value = true;
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.JointMoveIntentDataType),
                Capability(RiDataTypes.TrajectoryIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Contain("RI-Motion-Joint"));
            Assert.That(facets, Does.Contain("RI-Trajectory"));
            Assert.That(facets, Does.Not.Contain("RI-Motion-Linear"));
        }

        [Test]
        public void ComputeFacetsClaimsOnlySatisfiedProcessFacets()
        {
            AddAxis("Axis0", 0);
            AddAxis("Axis1", 1);
            m_controller.Capabilities!.AxisCount!.Value = 2;
            AddCompleteDescription("Axis0", "Axis1");
            AddTool("Tool0", true);
            AddTool("Tool1", true);
            AddLocation("Location0");
            m_controller.AddOutputs(m_context);
            m_controller.AddPrograms(m_context);
            AddRealTimeChannel("FastChannel");
            m_controller.AddOpenRealTimeChannel(m_context);
            m_controller.AddCloseRealTimeChannel(m_context);
            m_controller.Capabilities!.RealTimeChannelsSupported!.Value = true;
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.GraspIntentDataType),
                Capability(RiDataTypes.ReleaseIntentDataType),
                Capability(RiDataTypes.PickIntentDataType),
                Capability(RiDataTypes.PlaceIntentDataType),
                Capability(RiDataTypes.ToolChangeIntentDataType),
                Capability(RiDataTypes.SetOutputIntentDataType),
                Capability(RiDataTypes.CallProgramIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Contain("RI-Grasp"));
                Assert.That(facets, Does.Contain("RI-PickPlace"));
                Assert.That(facets, Does.Contain("RI-ToolChange"));
                Assert.That(facets, Does.Contain("RI-Output"));
                Assert.That(facets, Does.Contain("RI-Program"));
                Assert.That(facets, Does.Not.Contain("RI-Safety"));
                Assert.That(facets, Does.Contain("RI-Description"));
                Assert.That(facets, Does.Contain("RI-RealTimeChannel"));
            });
        }

        [Test]
        public void ComputeFacetsRejectsRealTimeChannelWithEmptyFolder()
        {
            m_controller.AddRealTimeChannels(m_context);
            m_controller.AddOpenRealTimeChannel(m_context);
            m_controller.AddCloseRealTimeChannel(m_context);
            m_controller.Capabilities!.RealTimeChannelsSupported!.Value = true;

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-RealTimeChannel"));
        }

        [Test]
        public void ComputeFacetsRejectsRealTimeChannelWithoutMethods()
        {
            AddRealTimeChannel("FastChannel");
            m_controller.AddOpenRealTimeChannel(m_context);
            m_controller.Capabilities!.RealTimeChannelsSupported!.Value = true;

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-RealTimeChannel"));
        }

        [Test]
        public void ComputeFacetsRequiresEveryIntentNamedByFacet()
        {
            AddTool("Tool0", true);
            AddLocation("Location0");
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.GraspIntentDataType),
                Capability(RiDataTypes.PickIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Not.Contain("RI-Grasp"));
                Assert.That(facets, Does.Not.Contain("RI-PickPlace"));
            });
        }

        [Test]
        public void ComputeFacetsClaimsQueueBlendingAndMissionHorizonOnlyWhenRequirementsAreSatisfied()
        {
            m_controller.MaxQueueDepth!.Value = 4;
            m_controller.Capabilities!.MissionsSupported!.Value = true;
            m_controller.Capabilities.MissionHorizonSupported!.Value = true;
            m_controller.Capabilities.BlendingSupported!.Value = true;
            m_controller.AddSubmitMission(m_context);
            m_controller.AddCancelMission(m_context);
            m_controller.AddUpdateMission(m_context);
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(
                    RiDataTypes.LinearMoveIntentDataType,
                    [
                        BufferModeEnum.Aborting,
                        BufferModeEnum.Buffered,
                        BufferModeEnum.BlendingLow,
                        BufferModeEnum.BlendingPrevious,
                        BufferModeEnum.BlendingNext,
                        BufferModeEnum.BlendingHigh
                    ])
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Contain("RI-Queue"));
                Assert.That(facets, Does.Contain("RI-Blending"));
                Assert.That(facets, Does.Contain("RI-Mission"));
                Assert.That(facets, Does.Contain("RI-Mission-Horizon"));
            });
        }

        [Test]
        public void ComputeFacetsRejectsQueueWithoutBufferedMode()
        {
            m_controller.MaxQueueDepth!.Value = 4;
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.WaitIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Queue"));
        }

        [Test]
        public void ComputeFacetsRejectsBlendingWithoutAllBlendingModes()
        {
            m_controller.Capabilities!.BlendingSupported!.Value = true;
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(
                    RiDataTypes.LinearMoveIntentDataType,
                    [
                        BufferModeEnum.Aborting,
                        BufferModeEnum.BlendingLow,
                        BufferModeEnum.BlendingPrevious,
                        BufferModeEnum.BlendingNext
                    ])
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Blending"));
        }

        [Test]
        public void ComputeFacetsRejectsMissionHorizonWithoutUpdateMission()
        {
            m_controller.Capabilities!.MissionsSupported!.Value = true;
            m_controller.Capabilities.MissionHorizonSupported!.Value = true;
            m_controller.AddSubmitMission(m_context);
            m_controller.AddCancelMission(m_context);

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Contain("RI-Mission"));
                Assert.That(facets, Does.Not.Contain("RI-Mission-Horizon"));
            });
        }

        [Test]
        public void ComputeFacetsRejectsGraspWithoutToolTcpFrame()
        {
            AddTool("Tool0");
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.GraspIntentDataType),
                Capability(RiDataTypes.ReleaseIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Grasp"));
        }

        [Test]
        public void ComputeFacetsRejectsGraspWhenTcpFrameIsNotToolRole()
        {
            AddTool("Tool0", true, FrameRoleEnum.Base);
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.GraspIntentDataType),
                Capability(RiDataTypes.ReleaseIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Grasp"));
        }

        [Test]
        public void ComputeFacetsRejectsDescriptionWithoutKinematicChainCoveringEveryAxis()
        {
            AddAxis("Axis0", 0);
            AddAxis("Axis1", 1);
            m_controller.Capabilities!.AxisCount!.Value = 2;
            AddCompleteDescription("Axis0");

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Description"));
        }

        [Test]
        public void ComputeFacetsRejectsDescriptionWithoutKinematicChainNode()
        {
            AddAxis("Axis0", 0);
            m_controller.Capabilities!.AxisCount!.Value = 1;
            AddCompleteDescription("Axis0");
            m_controller.Description!.KinematicChain = null;

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Description"));
        }

        [Test]
        public void ComputeFacetsRejectsDescriptionWithoutReachRadiusNode()
        {
            AddAxis("Axis0", 0);
            m_controller.Capabilities!.AxisCount!.Value = 1;
            AddCompleteDescription("Axis0");
            m_controller.Description!.ReachRadius = null;

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Description"));
        }

        [Test]
        public void ComputeFacetsRejectsDescriptionWithoutPayloadLimitNode()
        {
            AddAxis("Axis0", 0);
            m_controller.Capabilities!.AxisCount!.Value = 1;
            AddCompleteDescription("Axis0");
            m_controller.Description!.PayloadLimit = null;

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Description"));
        }

        [Test]
        public void ComputeFacetsRejectsDescriptionWithoutMaxCartesianSpeedNode()
        {
            AddAxis("Axis0", 0);
            m_controller.Capabilities!.AxisCount!.Value = 1;
            AddCompleteDescription("Axis0");
            m_controller.Description!.MaxCartesianSpeed = null;

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Description"));
        }

        [Test]
        public void ComputeFacetsRejectsSafetyWithoutBoundSource()
        {
            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Safety"));
        }

        [Test]
        public async Task RegisteredControllerPublishesSafetyFacetWhenSafetySourceIsBound()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "SafetyBound",
                    controller => controller
                        .WithSafetyState(new StaticSafetySource())
                        .Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                return [builder.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);
            var controller = (IntentControllerState)runner.Results![0];

            string[] facets = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);

            Assert.That(facets, Does.Contain("RI-Safety"));
        }

        [Test]
        public void ComputeFacetsRejectsInterop40010WhenControllerOnlyHasInverseHasIntentControllerReference()
        {
            AddIntentControllerOfReference();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Interop-40010"));
        }

        [Test]
        public void ComputeFacetsRejectsInterop40010WithoutInverseHasIntentControllerReference()
        {
            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Interop-40010"));
        }

        [Test]
        public void ComputeFacetsRejectsToolChangeWithSingleTool()
        {
            AddTool("Tool0", true);
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.ToolChangeIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-ToolChange"));
        }

        [Test]
        public void ComputeFacetsRejectsSurfaceFinishWithoutForceFacet()
        {
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.SurfaceFinishIntentDataType),
                Capability(RiDataTypes.ForceIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Process-SurfaceFinish"));
        }

        [TestCase(RiDataTypes.ArcWeldIntentDataType, "RI-Process-ArcWeld")]
        [TestCase(RiDataTypes.SpotWeldIntentDataType, "RI-Process-SpotWeld")]
        [TestCase(RiDataTypes.DispenseIntentDataType, "RI-Process-Dispense")]
        [TestCase(RiDataTypes.FastenIntentDataType, "RI-Process-Fasten")]
        public void ComputeFacetsClaimsProcessFacetOnlyForDeclaredProcessIntent(uint dataType, string facet)
        {
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(dataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Contain(facet));
                Assert.That(facets, Does.Not.Contain("RI-Process-Palletise"));
            });
        }

        [Test]
        public void ComputeFacetsRejectsPalletiseWithoutLocationPattern()
        {
            m_controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.PalletiseIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.That(facets, Does.Not.Contain("RI-Process-Palletise"));
        }

        [Test]
        public void ComputeFacetsRejectsDeclaredCapabilityWithoutRequiredEvidence()
        {
            m_controller.Capabilities!.TrajectorySupported!.Value = true;
            m_controller.Capabilities.AxisCount!.Value = 2;
            AddAxis("Axis0", 0);
            m_controller.Capabilities.SupportedIntents!.Value = new[]
            {
                Capability(RiDataTypes.JointMoveIntentDataType),
                Capability(RiDataTypes.TrajectoryIntentDataType),
                Capability(RiDataTypes.ForceIntentDataType)
            }.ToArrayOf();

            string[] facets = [.. RobotIntentFacetCalculator.Compute(m_controller)];

            Assert.Multiple(() =>
            {
                Assert.That(facets, Does.Not.Contain("RI-Motion-Joint"));
                Assert.That(facets, Does.Contain("RI-Trajectory"));
                Assert.That(facets, Does.Not.Contain("RI-Force"));
            });
        }

        [Test]
        public async Task RegisteredControllerPublishesComputedSupportedFacets()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "Published",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                return [builder.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);
            var controller = (IntentControllerState)runner.Results![0];

            string[] published = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);
            string[] computed = [.. RobotIntentFacetCalculator.Compute(controller)];

            Assert.That(published, Is.EqualTo(computed));
        }

        [Test]
        public async Task RegisteredControllerReadSupportedFacetsRejectsInteropWhenRawReferenceIsAttachedAfterRegistration()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "PublishedInterop",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                return [builder.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);
            var controller = (IntentControllerState)runner.Results![0];

            string[] beforeReference = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);

            controller.AddReference(
                HasIntentControllerReferenceTypeId(fixture.Manager.SystemContext.NamespaceUris),
                true,
                new NodeId("MotionDeviceSystem", 2));

            string[] published = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);
            string[] computed = [.. RobotIntentFacetCalculator.Compute(controller)];

            Assert.Multiple(() =>
            {
                Assert.That(beforeReference, Does.Not.Contain("RI-Interop-40010"));
                Assert.That(published, Does.Not.Contain("RI-Interop-40010"));
                Assert.That(published, Is.EqualTo(computed));
            });
        }

        [Test]
        public async Task RegisteredControllerReadSupportedFacetsRemovesToolChangeWhenToolIsRemovedAfterRegistration()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "ToolChange",
                    controller =>
                    {
                        IIntentFrameBuilder tool0Frame = controller.AddFrame(
                            "Tool0Frame",
                            "tool0",
                            FrameRoleEnum.Tool,
                            Pose());
                        IIntentFrameBuilder tool1Frame = controller.AddFrame(
                            "Tool1Frame",
                            "tool1",
                            FrameRoleEnum.Tool,
                            Pose());
                        controller.AddTool("Tool0", tool0Frame);
                        controller.AddTool("Tool1", tool1Frame);
                        controller.Accepts<ToolChangeIntentDataType>();
                    },
                    cancellationToken).ConfigureAwait(false);
                return [builder.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);
            var controller = (IntentControllerState)runner.Results![0];

            string[] beforeRemoval = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);
            var tools = new List<BaseInstanceState>();
            controller.Tools!.GetChildren(null!, tools);
            Assert.That(tools, Has.Count.EqualTo(2));
            controller.Tools.RemoveChild(tools[0]);
            string[] afterRemoval = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);
            string[] computed = [.. RobotIntentFacetCalculator.Compute(controller)];

            Assert.Multiple(() =>
            {
                Assert.That(beforeRemoval, Does.Contain("RI-ToolChange"));
                Assert.That(afterRemoval, Does.Not.Contain("RI-ToolChange"));
                Assert.That(afterRemoval, Is.EqualTo(computed));
            });
        }

        [Test]
        public async Task RegisteredControllersAlwaysPublishBaseFacet()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder minimal = await context.AddIntentControllerAsync(
                    "Minimal",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                IIntentControllerBuilder motion = await context.AddIntentControllerAsync(
                    "Motion",
                    controller =>
                    {
                        controller.AddAxis("Axis0", 0, AxisKindEnum.Revolute);
                        controller.Accepts<JointMoveIntentDataType>();
                    },
                    cancellationToken).ConfigureAwait(false);
                IIntentControllerBuilder process = await context.AddIntentControllerAsync(
                    "Process",
                    controller =>
                    {
                        IIntentFrameBuilder frame = controller.AddFrame(
                            "ToolFrame",
                            "tool",
                            FrameRoleEnum.Tool,
                            Pose());
                        controller.AddTool("Tool0", frame);
                        controller.Accepts<ToolChangeIntentDataType>();
                    },
                    cancellationToken).ConfigureAwait(false);
                return [minimal.State, motion.State, process.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);

            foreach (IntentControllerState controller in runner.Results!)
            {
                string[] facets = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);

                Assert.That(facets, Does.Contain("RI-Base"));
            }
        }

        [Test]
        public async Task RegisteredControllerDoesNotPublishUnsatisfiedFacet()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "SingleTool",
                    controller =>
                    {
                        IIntentFrameBuilder frame = controller.AddFrame(
                            "ToolFrame",
                            "tool",
                            FrameRoleEnum.Tool,
                            Pose());
                        controller.AddTool("Tool0", frame);
                        controller.Accepts<ToolChangeIntentDataType>();
                    },
                    cancellationToken).ConfigureAwait(false);
                return [builder.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);
            var controller = (IntentControllerState)runner.Results![0];

            string[] facets = await ReadSupportedFacetsAsync(controller).ConfigureAwait(false);

            Assert.That(facets, Does.Not.Contain("RI-ToolChange"));
        }

        [Test]
        public async Task RegisteredControllerPublishesSupportedFacetsNodeShape()
        {
            await using var fixture = new IntentFacetServerFixture();
            var runner = new DelegateSetupRunner(async (context, cancellationToken) =>
            {
                IIntentControllerBuilder builder = await context.AddIntentControllerAsync(
                    "Shape",
                    controller => controller.Accepts<WaitIntentDataType>(),
                    cancellationToken).ConfigureAwait(false);
                return [builder.State];
            });
            await fixture.StartAsync(runner).ConfigureAwait(false);
            var controller = (IntentControllerState)runner.Results![0];
            PropertyState<ArrayOf<string>> supportedFacets = controller.Capabilities!.SupportedFacets!;
            ServiceResult writeResult = supportedFacets.WriteAttribute(
                null!,
                Attributes.Value,
                NumericRange.Null,
                new DataValue(Variant.From(s_stringValues1.ToArrayOf())));

            Assert.Multiple(() =>
            {
                Assert.That(supportedFacets.DataType, Is.EqualTo(global::Opc.Ua.DataTypeIds.String));
                Assert.That(supportedFacets.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                Assert.That(writeResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotWritable));
            });
        }

        private void AddAxis(string browseName, uint index)
        {
            RiAxisState axis = OpcUaRobotIntentExtensions.CreateInstanceOfAxisType(
                m_context,
                m_controller.Axes!,
                new QualifiedName(browseName, 1));
            axis.CreateOrReplaceAxisId(m_context, null);
            axis.AxisId!.Value = browseName;
            axis.Index!.Value = index;
            axis.Kind!.Value = AxisKindEnum.Revolute;
            m_controller.Axes!.AddChild(axis);
        }

        private void AddTool(
            string browseName,
            bool withTcpFrame = false,
            FrameRoleEnum tcpFrameRole = FrameRoleEnum.Tool)
        {
            ToolState tool = OpcUaRobotIntentExtensions.CreateInstanceOfToolType(
                m_context,
                m_controller.Tools!,
                new QualifiedName(browseName, 1));
            if (withTcpFrame)
            {
                CoordinateFrameState tcpFrame = AddFrame($"{browseName}Tcp", tcpFrameRole);
                tool.CreateOrReplaceTcpFrame(m_context, null);
                tool.TcpFrame!.Value = tcpFrame.NodeId;
            }
            m_controller.Tools!.AddChild(tool);
        }

        private CoordinateFrameState AddFrame(string browseName, FrameRoleEnum role)
        {
            CoordinateFrameState frame = OpcUaRobotIntentExtensions.CreateInstanceOfCoordinateFrameType(
                m_context,
                m_controller.Frames!,
                new QualifiedName(browseName, 1));
            frame.CreateOrReplaceFrameId(m_context, null);
            frame.CreateOrReplaceRole(m_context, null);
            frame.CreateOrReplaceTransform(m_context, null);
            frame.FrameId!.Value = browseName;
            frame.Role!.Value = role;
            frame.Transform!.Value = Pose();
            m_controller.Frames!.AddChild(frame);
            return frame;
        }

        private void AddLocation(string browseName)
        {
            LocationState location = OpcUaRobotIntentExtensions.CreateInstanceOfLocationType(
                m_context,
                m_controller.Locations!,
                new QualifiedName(browseName, 1));
            m_controller.Locations!.AddChild(location);
        }

        private void AddRealTimeChannel(string browseName)
        {
            m_controller.AddRealTimeChannels(m_context);
            RealTimeChannelState channel = OpcUaRobotIntentExtensions.CreateInstanceOfRealTimeChannelType(
                m_context,
                m_controller.RealTimeChannels!,
                new QualifiedName(browseName, 1));
            channel.CreateOrReplaceChannelId(m_context, null);
            channel.ChannelId!.Value = browseName;
            m_controller.RealTimeChannels!.AddChild(channel);
        }

        private void AddIntentControllerOfReference()
        {
            m_controller.AddReference(
                HasIntentControllerReferenceTypeId(),
                true,
                new NodeId("MotionDeviceSystem", 2));
        }

        private void AddCompleteDescription(params string[] axisIds)
        {
            m_controller.AddDescription(m_context);
            m_controller.Description!.CreateOrReplaceKinematicChain(m_context, null);
            m_controller.Description.CreateOrReplaceReachRadius(m_context, null);
            m_controller.Description.CreateOrReplacePayloadLimit(m_context, null);
            m_controller.Description.CreateOrReplaceMaxCartesianSpeed(m_context, null);
            var chain = new KinematicJointDataType[axisIds.Length];
            for (int ii = 0; ii < axisIds.Length; ii++)
            {
                chain[ii] = new KinematicJointDataType
                {
                    AxisId = axisIds[ii],
                    Kind = AxisKindEnum.Revolute,
                    OriginTransform = Pose(),
                    AxisVector = s_doubleValues1.ToArrayOf()
                };
            }
            m_controller.Description.KinematicChain!.Value = chain.ToArrayOf();
            m_controller.Description.ReachRadius!.Value = 1.0;
            m_controller.Description.PayloadLimit!.Value = 1.0;
            m_controller.Description.MaxCartesianSpeed!.Value = 1.0;
        }

        private IntentCapabilityDataType Capability(uint dataType, BufferModeEnum[]? supportedBufferModes = null)
        {
            return new IntentCapabilityDataType
            {
                IntentType = NodeId.Create(dataType, RiNamespaces.RobotIntent, m_context.NamespaceUris),
                SupportedBufferModes = (supportedBufferModes ?? [BufferModeEnum.Aborting]).ToArrayOf()
            };
        }

        private NodeId HasIntentControllerReferenceTypeId()
        {
            return HasIntentControllerReferenceTypeId(m_context.NamespaceUris);
        }

        private static NodeId HasIntentControllerReferenceTypeId(NamespaceTable namespaceUris)
        {
            return NodeId.Create(
                global::Opc.Ua.RobotIntent.ReferenceTypes.HasIntentController,
                RiNamespaces.RobotIntent,
                namespaceUris);
        }

        private static async Task<string[]> ReadSupportedFacetsAsync(IntentControllerState controller)
        {
            PropertyState<ArrayOf<string>> supportedFacets = controller.Capabilities!.SupportedFacets!;
            (ServiceResult result, DataValue value) = await supportedFacets.ReadAttributeAsync(
                null!,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue()).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsBad(result), Is.False);
                Assert.That(StatusCode.IsBad(value.StatusCode), Is.False);
            });
            return [.. value.WrappedValue.GetStringArray()];
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

        private sealed class IntentFacetServerFixture : IAsyncDisposable
        {
            public RobotIntentNodeManager Manager => m_manager!;

            public async Task StartAsync(ControllerSetupRunner runner)
            {
                m_fixture = new ServerFixture<StandardServer>(
                    telemetry => new StandardServer(telemetry))
                {
                    AutoAccept = true,
                    SecurityNone = true
                };
                StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
                m_manager = new RobotIntentNodeManager(
                    server.CurrentInstance,
                    m_fixture.Config,
                    new IRobotIntentModelProvider[] { new RobotIntentModelProvider() },
                    new RobotIntentServerOptions(),
                    runner);
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await m_manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (m_manager != null)
                {
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                }
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }

            private ServerFixture<StandardServer>? m_fixture;
            private RobotIntentNodeManager? m_manager;
        }

        private class ControllerSetupRunner : IRobotIntentPostSetupRunner
        {
            public virtual ValueTask RunAsync(
                AsyncCustomNodeManager manager,
                RobotIntentRootState root,
                RobotIntentServerOptions options,
                CancellationToken cancellationToken)
            {
                return new ValueTask();
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
                    new TestIntentExecutor(),
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
                    SafeMotionFunctionEnum.None,
                    false,
                    false,
                    false,
                    0.0,
                    true,
                    LocalizedText.Null));
            }
        }

        private SystemContext m_context = null!;
        private IntentControllerState m_controller = null!;

        private static readonly string[] s_stringValues1 = ["RI-Fake"];
        private static readonly double[] s_doubleValues1 = [0.0, 0.0, 1.0];
        private static readonly double[] s_doubleValues2 = [0.0, 0.0, 0.0];
        private static readonly double[] s_doubleValues3 = [0.0, 0.0, 0.0, 1.0];
    }
}
