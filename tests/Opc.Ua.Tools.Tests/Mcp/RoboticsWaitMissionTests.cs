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

#if NET10_0
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests the bounded mission wait: it observes the published mission node
    /// through the core mission handle, never polls the mission list, never
    /// retries, and always disposes its handle.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsWaitMissionTests
    {
        [Test]
        public async Task WaitReturnsCurrentStateOnTimeoutWithoutListPolling()
        {
            var transport = new RecordingRobotIntentTransport
            {
                MissionSnapshot = new MissionSnapshot
                {
                    MissionId = "m1",
                    MissionNode = new NodeId("Missions/m1", 2),
                    ExecutionState = ExecutionStateEnum.Executing,
                    CurrentStepId = "s2",
                    ReleasedStepCount = 3
                }
            };
            var controller = new RobotIntentControllerClient(transport);

            MissionWaitResult result = await RoboticsMonitoringTools.WaitMissionCoreAsync(
                controller,
                "m1",
                "ns=2;s=Missions/m1",
                1,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.False);
                Assert.That(result.Current.CurrentStepId, Is.EqualTo("s2"));
                Assert.That(result.Current.ReleasedStepCount, Is.EqualTo(3u));
                Assert.That(transport.ListMissionsCallCount, Is.Zero);
                Assert.That(transport.SubmitMissionCallCount, Is.Zero);
                Assert.That(transport.RequestControlCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task WaitReportsTerminalMissionAsCompleted()
        {
            var transport = new RecordingRobotIntentTransport
            {
                MissionSnapshot = new MissionSnapshot
                {
                    MissionId = "m1",
                    MissionNode = new NodeId("Missions/m1", 2),
                    ExecutionState = ExecutionStateEnum.Failed,
                    Failure = IntentFailureEnum.SafetyLimitExceeded,
                    FailureMessage = new LocalizedText("safe speed limit active")
                }
            };
            var controller = new RobotIntentControllerClient(transport);

            MissionWaitResult result = await RoboticsMonitoringTools.WaitMissionCoreAsync(
                controller,
                "m1",
                "ns=2;s=Missions/m1",
                50,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.True);
                Assert.That(result.TerminalState, Is.EqualTo(ExecutionStateEnum.Failed));
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.SafetyLimitExceeded));
                Assert.That(result.FailureMessage.Text, Is.EqualTo("safe speed limit active"));
            });
        }

        [Test]
        public async Task NonPositiveTimeoutPerformsSingleRefresh()
        {
            var transport = new RecordingRobotIntentTransport
            {
                MissionSnapshot = new MissionSnapshot
                {
                    MissionId = "m1",
                    MissionNode = new NodeId("Missions/m1", 2),
                    ExecutionState = ExecutionStateEnum.Queued
                }
            };
            var controller = new RobotIntentControllerClient(transport);

            MissionWaitResult result = await RoboticsMonitoringTools.WaitMissionCoreAsync(
                controller,
                "m1",
                "ns=2;s=Missions/m1",
                0,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Completed, Is.False);
                Assert.That(result.Current.ExecutionState, Is.EqualTo(ExecutionStateEnum.Queued));
                Assert.That(transport.ReadMissionSnapshotCallCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(transport.ListMissionsCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task WaitSubscribesExactlyOncePerCall()
        {
            var transport = new RecordingRobotIntentTransport
            {
                MissionSnapshot = new MissionSnapshot
                {
                    MissionId = "m1",
                    MissionNode = new NodeId("Missions/m1", 2),
                    ExecutionState = ExecutionStateEnum.Executing
                }
            };
            var controller = new RobotIntentControllerClient(transport);

            _ = await RoboticsMonitoringTools.WaitMissionCoreAsync(
                controller, "m1", "ns=2;s=Missions/m1", 1, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(transport.SubscribeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void TimeoutBeyondTheBoundIsRejected()
        {
            var controller = new RobotIntentControllerClient(new RecordingRobotIntentTransport());

            Assert.That(
                () => RoboticsMonitoringTools.WaitMissionCoreAsync(
                    controller, "m1", "ns=2;s=Missions/m1", 600001, CancellationToken.None),
                Throws.ArgumentException.With.Message.Contains("timeoutMs"));
        }

        [Test]
        public void MissingMissionIdIsRejected()
        {
            var controller = new RobotIntentControllerClient(new RecordingRobotIntentTransport());

            Assert.That(
                () => RoboticsMonitoringTools.WaitMissionCoreAsync(
                    controller, "   ", "ns=2;s=Missions/m1", 10, CancellationToken.None),
                Throws.ArgumentException);
        }

        [Test]
        public void InvalidMissionNodeIdIsRejected()
        {
            var controller = new RobotIntentControllerClient(new RecordingRobotIntentTransport());

            Assert.That(
                () => RoboticsMonitoringTools.WaitMissionCoreAsync(
                    controller, "m1", "not a node id", 10, CancellationToken.None),
                Throws.InstanceOf<System.Exception>());
        }
    }
}
#endif
