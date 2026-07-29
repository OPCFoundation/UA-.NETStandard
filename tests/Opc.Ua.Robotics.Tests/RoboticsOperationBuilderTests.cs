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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Robotics;
using Opc.Ua.Robotics.Server.Builders;

namespace Opc.Ua.Robotics.Server.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Category("Robotics")]
    public sealed class RoboticsOperationBuilderTests
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
        public async Task OperationFacetsDriveStateAndWireTaskControlReference()
        {
            SystemOperationBuilder systemOperation = null!;
            TaskControlOperationBuilder taskOperation = null!;
            MotionDeviceState motionState = null!;

            await m_fixture.CreateBuildContext()
                .AddMotionDeviceSystemAsync(NextName("Cell"), system =>
                {
                    IMotionDeviceBuilder motion = system.AddMotionDevice("Robot", robot =>
                    {
                        robot.WithCategory(MotionDeviceCategoryEnumeration.ARTICULATED_ROBOT)
                            .WithSpeedOverride(50);
                        robot.AddAxis("Axis1", axis => axis
                            .AsVirtual()
                            .WithMotionProfile(AxisMotionProfileEnumeration.ROTARY)
                            .WithActualPosition(0));
                        robot.AddPowerTrain("PowerTrain1", powerTrain =>
                            powerTrain.AddMotor("Motor1", motor => motor.WithMotorTemperature(20)));
                        motionState = robot.State;
                    });
                    system.AddSafetyState("Safety");
                    system.AddController("Controller", controller =>
                    {
                        controller.WithCurrentUser(user => user.WithLevel("Operator").WithName("alice"));
                        controller.AddSoftware("Software");
                        systemOperation = (SystemOperationBuilder)controller.AddSystemOperation(operation => operation
                            .WithTransitionReason(7)
                            .OnGetReady(GoodOperation)
                            .OnStart(GoodOperation)
                            .OnStop(GoodStop)
                            .OnStandDown(GoodOperation));
                        controller.AddTaskControl("Task", task =>
                        {
                            task.WithComponentName("Task")
                                .WithTaskProgramLoaded(false)
                                .WithTaskProgramName(string.Empty)
                                .Controls(motion);
                            taskOperation = (TaskControlOperationBuilder)task.AddTaskControlOperation(
                        operation => operation
                                .WithMotionDevicesUnderControl([motion.State.NodeId])
                                    .OnLoadByName((_, _) =>
                                new ValueTask<RoboticsProgramResult>(new RoboticsProgramResult()))
                                .OnStart(GoodOperation)
                                .OnStop(GoodStop));
                        });
                    });
                }).ConfigureAwait(false);

            await systemOperation.Machine.GetReady!.OnCallAsync!(
                m_fixture.Manager.SystemContext,
                systemOperation.Machine.GetReady,
                systemOperation.State.NodeId,
                CancellationToken.None).ConfigureAwait(false);
            await systemOperation.Machine.Start!.OnCallAsync!(
                m_fixture.Manager.SystemContext,
                systemOperation.Machine.Start,
                systemOperation.State.NodeId,
                CancellationToken.None).ConfigureAwait(false);
            await systemOperation.Machine.Stop!.OnCallAsync!(
                m_fixture.Manager.SystemContext,
                systemOperation.Machine.Stop,
                systemOperation.State.NodeId,
                0,
                CancellationToken.None).ConfigureAwait(false);
            await taskOperation.Machine.LoadByName!.OnCallAsync!(
                m_fixture.Manager.SystemContext,
                taskOperation.Machine.LoadByName,
                taskOperation.State.NodeId,
                "Program",
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(systemOperation.Machine.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            Assert.That(
                systemOperation.Machine.CurrentState.Number!.Value,
                Is.EqualTo(2u));
            Assert.That(systemOperation.Machine.LastTransition!.Value.Text, Is.EqualTo("ExecutingToReady"));
            Assert.That(
                systemOperation.Machine.LastTransition.Number!.Value,
                Is.EqualTo(5u));
            Assert.That(systemOperation.Machine.LastTransitionReason!.Value, Is.EqualTo(7));
            Assert.That(taskOperation.Machine.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            Assert.That(
                taskOperation.Machine.CurrentState.Number!.Value,
                Is.EqualTo(2u));
            Assert.That(taskOperation.Machine.LastTransition!.Value.Text, Is.EqualTo("IdleToReady"));
            Assert.That(
                taskOperation.Machine.LastTransition.Number!.Value,
                Is.EqualTo(2u));
            Assert.That(motionState.TaskControlReference!.Value, Is.EqualTo(taskOperation.State.NodeId));
            Assert.That(taskOperation.State.MotionDevicesUnderControl!.Value.Span[0], Is.EqualTo(motionState.NodeId));
        }

        private static ValueTask<ServiceResult> GoodOperation(
            RoboticsOperationContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        private static ValueTask<ServiceResult> GoodStop(
            RoboticsStopRequest request,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        private string NextName(string prefix)
        {
            return $"{prefix}{++m_nameCounter}";
        }
    }
}
