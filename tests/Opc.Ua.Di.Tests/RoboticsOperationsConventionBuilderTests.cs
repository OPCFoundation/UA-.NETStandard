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
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Operations;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Category("Robotics")]
    public sealed class RoboticsOperationsConventionBuilderTests
    {
        private RoboticsServerFixture m_fixture = null!;
        private int m_nameCounter;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new RoboticsServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task RegisteredHandlersMaterializeOnlySelectedMethodsWithMetadata()
        {
            IRoboticsOperationsBuilder operations = null!;

            await BuildRobotAsync(builder =>
            {
                operations = builder.AddOperations("Operations", builder.BuildContext.InstanceNamespaceIndex, op => op
                    .OnMoveTo((_, _) => new ValueTask<RoboticsOperationResult>(RoboticsOperationResult.Good))
                    .OnGrasp((_, _) => new ValueTask<RoboticsOperationResult>(RoboticsOperationResult.Good)));
            }).ConfigureAwait(false);

            Assert.That(operations.State, Is.Not.Null);
            Assert.That(FindMethod(operations.State!, "MoveTo"), Is.Not.Null);
            Assert.That(FindMethod(operations.State!, "Grasp"), Is.Not.Null);
            Assert.That(FindMethod(operations.State!, "MoveJ"), Is.Null);
            MethodState moveTo = FindMethod(operations.State!, "MoveTo")!;
            Assert.That(moveTo.InputArguments, Is.Not.Null);
            Assert.That(moveTo.OutputArguments, Is.Not.Null);
            Assert.That(moveTo.InputArguments!.Value[0].Name, Is.EqualTo("TargetFrame"));
            Assert.That(moveTo.OutputArguments!.Value[0].Name, Is.EqualTo("StatusCode"));
        }

        [Test]
        public void StandardNamespaceIndexIsRejected()
        {
            IRoboticsBuildContext context = m_fixture.CreateBuildContext();
            ushort roboticsNamespaceIndex =
                (ushort)context.Context.NamespaceUris.GetIndex(Opc.Ua.Robotics.Namespaces.Robotics);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await context.AddMotionDeviceSystemAsync(NextName("Cell"), system =>
                {
                    AddRequiredTopology(system, motion =>
                        motion.AddOperations("Operations", roboticsNamespaceIndex, _ => { }));
                }).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task MoveToReturnsHandlerResultAndFailureStatus()
        {
            IRoboticsOperationsBuilder operations = null!;

            await BuildRobotAsync(builder =>
            {
                operations = builder.AddOperations("Operations", builder.BuildContext.InstanceNamespaceIndex, op => op
                    .OnMoveTo((_, _) => new ValueTask<RoboticsOperationResult>(
                        new RoboticsOperationResult(new ServiceResult(StatusCodes.BadInvalidArgument), "no"))));
            }).ConfigureAwait(false);

            MethodState moveTo = FindMethod(operations.State!, "MoveTo")!;
            var outputs = ResultOutputs();
            ServiceResult result = await moveTo.OnCallMethod2Async!(
                m_fixture.Manager.SystemContext,
                moveTo,
                operations.State!.NodeId,
                [Structure(new ThreeDFrame()), Variant.Null, Variant.Null, Variant.Null],
                outputs,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(outputs[1].TryGetValue(out string message), Is.True);
            Assert.That(message, Is.EqualTo("no"));
        }

        private async Task BuildRobotAsync(Action<IMotionDeviceBuilder> configureMotion)
        {
            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("Cell"), system => AddRequiredTopology(system, configureMotion))
                .ConfigureAwait(false);
        }

        private static void AddRequiredTopology(
            IMotionDeviceSystemBuilder system,
            Action<IMotionDeviceBuilder> configureMotion)
        {
            system.AddMotionDevice("Robot", motion =>
            {
                motion.WithCategory(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT)
                    .WithSpeedOverride(50);
                motion.AddAxis("Axis1", axis => axis
                    .AsVirtual()
                    .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                    .WithActualPosition(0));
                motion.AddPowerTrain("PowerTrain1", powerTrain =>
                    powerTrain.AddMotor("Motor1", motor => motor.WithMotorTemperature(20)));
                configureMotion(motion);
            });
            system.AddSafetyState("Safety");
            system.AddController("Controller", controller =>
            {
                controller.WithCurrentUser(user => user.WithLevel("Operator").WithName("alice"));
                controller.AddSoftware("Software");
                controller.AddTaskControl("Task", task => task
                    .WithComponentName("Task")
                    .WithTaskProgramLoaded(false)
                    .WithTaskProgramName(string.Empty));
            });
        }

        private static MethodState? FindMethod(BaseObjectState operations, string name)
        {
            var children = new List<BaseInstanceState>();
            operations.GetChildren(null!, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is MethodState method && method.BrowseName.Name == name)
                {
                    return method;
                }
            }
            return null;
        }

        private static List<Variant> ResultOutputs()
        {
            return [Variant.Null, Variant.Null, Variant.Null];
        }

        private static Variant Structure(IEncodeable value)
        {
            return new Variant(new ExtensionObject(value));
        }

        private string NextName(string prefix)
        {
            return $"{prefix}{++m_nameCounter}";
        }
    }
}
