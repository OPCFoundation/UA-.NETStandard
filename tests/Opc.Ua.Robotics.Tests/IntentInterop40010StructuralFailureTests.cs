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

using Moq;
using NUnit.Framework;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Tests;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Verifies structural failures for the OPC 40010 interoperability facet.
    /// </summary>
    [TestFixture]
    public class IntentInterop40010StructuralFailureTests
    {
        [Test]
        public void Interop40010FacetRejectsBlankTaskProgramName()
        {
            SystemContext context = CreateSystemContext();
            MotionDeviceSystemState motionDeviceSystem = CreateMotionDeviceSystemWithSafety();
            AddTaskProgram(motionDeviceSystem, "   ", includeParameterSet: true);
            IntentControllerState controller = CreateIntentController(context);
            AddPublishedProgram(context, controller, "weld");

            Bind(context, motionDeviceSystem, controller);

            Assert.That(RobotIntentFacetCalculator.Compute(controller).ToArray(),
                Does.Not.Contain("RI-Interop-40010"));
        }

        [Test]
        public void Interop40010FacetRejectsTaskControlWithoutParameterSet()
        {
            SystemContext context = CreateSystemContext();
            MotionDeviceSystemState motionDeviceSystem = CreateMotionDeviceSystemWithSafety();
            AddTaskProgram(motionDeviceSystem, "weld", includeParameterSet: false);
            IntentControllerState controller = CreateIntentController(context);
            AddPublishedProgram(context, controller, "weld");

            Bind(context, motionDeviceSystem, controller);

            Assert.That(RobotIntentFacetCalculator.Compute(controller).ToArray(),
                Does.Not.Contain("RI-Interop-40010"));
        }

        [Test]
        public void Interop40010FacetRejectsBlankPublishedProgramId()
        {
            SystemContext context = CreateSystemContext();
            MotionDeviceSystemState motionDeviceSystem = CreateMotionDeviceSystemWithSafety();
            AddTaskProgram(motionDeviceSystem, "weld", includeParameterSet: true);
            IntentControllerState controller = CreateIntentController(context);
            AddPublishedProgram(context, controller, "   ");

            Bind(context, motionDeviceSystem, controller);

            Assert.That(RobotIntentFacetCalculator.Compute(controller).ToArray(),
                Does.Not.Contain("RI-Interop-40010"));
        }

        private static SystemContext CreateSystemContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            return new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                EncodeableFactory = messageContext.Factory
            };
        }

        private static MotionDeviceSystemState CreateMotionDeviceSystemWithSafety()
        {
            var motionDeviceSystem = new MotionDeviceSystemState(null)
            {
                NodeId = new NodeId("motion", 2),
                BrowseName = new QualifiedName("Motion", 2)
            };
            var safetyState = new SafetyStateState(motionDeviceSystem)
            {
                BrowseName = new QualifiedName("Safety", 2)
            };
            var parameterSet = new BaseObjectState(safetyState)
            {
                BrowseName = new QualifiedName("ParameterSet", 0)
            };
            parameterSet.AddChild(new BaseDataVariableState(parameterSet)
            {
                BrowseName = new QualifiedName(BrowseNames.OperationalMode, 0),
                Value = new Variant((int)OperationalModeEnumeration.AUTOMATIC)
            });
            safetyState.AddChild(parameterSet);
            motionDeviceSystem.AddChild(safetyState);
            return motionDeviceSystem;
        }

        private static IntentControllerState CreateIntentController(SystemContext context)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                context,
                new NodeId("intent", 2),
                new QualifiedName("Intent", 2),
                new LocalizedText("Intent"),
                true);
            controller.OperationalMode!.Value = OperationalModeEnum.Automatic;
            controller.Capabilities!.SupportedIntents!.Value = new[]
            {
                new IntentCapabilityDataType
                {
                    IntentType = ExpandedNodeId.ToNodeId(
                        global::Opc.Ua.RobotIntent.DataTypeIds.WaitIntentDataType,
                        context.NamespaceUris),
                    SupportedBufferModes = new[] { BufferModeEnum.Aborting }.ToArrayOf()
                }
            }.ToArrayOf();
            return controller;
        }

        private static void AddTaskProgram(
            MotionDeviceSystemState motionDeviceSystem,
            string programName,
            bool includeParameterSet)
        {
            var taskControl = new TaskControlState(motionDeviceSystem)
            {
                BrowseName = new QualifiedName("TaskControl", 2)
            };
            if (includeParameterSet)
            {
                var parameterSet = new BaseObjectState(taskControl)
                {
                    BrowseName = new QualifiedName("ParameterSet", 0)
                };
                parameterSet.AddChild(new BaseDataVariableState(parameterSet)
                {
                    BrowseName = new QualifiedName(BrowseNames.TaskProgramName, 0),
                    Value = new Variant(programName)
                });
                taskControl.AddChild(parameterSet);
            }
            motionDeviceSystem.AddChild(taskControl);
        }

        private static void AddPublishedProgram(
            SystemContext context,
            IntentControllerState controller,
            string programId)
        {
            controller.AddPrograms(context);
            ProgramState program = OpcUaRobotIntentExtensions.CreateInstanceOfProgramType(
                context,
                controller.Programs!,
                new QualifiedName("Program", 2));
            program.CreateOrReplaceProgramId(context, null);
            program.ProgramId!.Value = programId;
            controller.Programs!.AddChild(program);
        }

        private static void Bind(
            SystemContext context,
            MotionDeviceSystemState motionDeviceSystem,
            IntentControllerState controller)
        {
            var buildContext = new Mock<IRoboticsBuildContext>(MockBehavior.Strict);
            buildContext.SetupGet(static build => build.Context).Returns(context);
            var motionBuilder = new Mock<IMotionDeviceSystemBuilder>(MockBehavior.Strict);
            motionBuilder.SetupGet(static builder => builder.BuildContext).Returns(buildContext.Object);
            motionBuilder.SetupGet(static builder => builder.State).Returns(motionDeviceSystem);
            var intentBuilder = new Mock<IIntentControllerBuilder>(MockBehavior.Strict);
            intentBuilder.SetupGet(static builder => builder.State).Returns(controller);
            motionBuilder.Object.HasIntentController(intentBuilder.Object);
        }
    }
}

