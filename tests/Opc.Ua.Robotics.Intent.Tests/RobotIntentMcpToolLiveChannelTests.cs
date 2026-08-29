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
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Intent.Tests
{
    /// <summary>
    /// Exercises Robot Intent through the MCP tool surface against the live-channel fixture.
    /// </summary>
    [TestFixture]
    [Category("RobotIntent")]
    [Category("Integration")]
    [Category("Mcp")]
    [NonParallelizable]
    public sealed class RobotIntentMcpToolLiveChannelTests
    {
        [Test]
        public async Task LinearMoveSubmittedThroughMcpToolSurfaceMovesTheRobotPose()
        {
            await using LiveChannelFixture fixture = await LiveChannelFixture.StartAsync().ConfigureAwait(false);
            using OpcUaSessionManager sessionManager = CreateSessionManager();
            await sessionManager.ConnectAsync(
                kSessionName,
                fixture.ServerUrl,
                securityMode: null,
                securityPolicy: null,
                authType: "Anonymous",
                username: null,
                password: null,
                autoAcceptCerts: true,
                CancellationToken.None).ConfigureAwait(false);
            var robotics = new RoboticsIntentManager(sessionManager);

            ArrayOf<RobotIntentNodeLookupEntry> controllers = await RoboticsDiscoveryTools
                .ListControllersAsync(robotics, kSessionName, CancellationToken.None)
                .ConfigureAwait(false);
            RobotIntentNodeLookupEntry[] controllerEntries = controllers.ToArray()!;
            RobotIntentNodeLookupEntry controller = controllerEntries.Single(c => c.Name == "CellController");
            string controllerId = controller.NodeId.ToString();

            CommandAuthorityOutcome authority = await RoboticsControlTools
                .RequestControlAsync(robotics, controllerId, kSessionName, CancellationToken.None)
                .ConfigureAwait(false);
            IntentSubmissionResult submission = await RoboticsControlTools
                .SubmitLinearMoveAsync(
                    robotics,
                    controllerId,
                    kLinearMoveInput,
                    kSessionName,
                    CancellationToken.None)
                .ConfigureAwait(false);
            IntentOperationWaitResult completed = await WaitForCompletionThroughMcpAsync(
                robotics,
                controllerId,
                submission,
                kSessionName).ConfigureAwait(false);

            double[] movedPosition = completed.Current.CurrentPose.Position.ToArray()!;

            Assert.Multiple(() =>
            {
                Assert.That(authority.Granted, Is.True);
                Assert.That(submission.Accepted, Is.True);
                Assert.That(submission.Operation.IsNull, Is.False);
                Assert.That(completed.Completed, Is.True);
                Assert.That(completed.Result.State, Is.EqualTo(ExecutionStateEnum.Succeeded));
                Assert.That(completed.Current.CurrentPose.Position.IsNull, Is.False);
                Assert.That(movedPosition, Has.Length.EqualTo(3));
                Assert.That(movedPosition[0], Is.GreaterThan(0.0));
                Assert.That(movedPosition[1], Is.GreaterThan(0.0));
                Assert.That(movedPosition[2], Is.GreaterThan(0.0));
            });
        }

        private static async ValueTask<IntentOperationWaitResult> WaitForCompletionThroughMcpAsync(
            RoboticsIntentManager robotics,
            string controllerId,
            IntentSubmissionResult submission,
            string sessionName)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            IntentOperationWaitResult result;
            do
            {
                result = await RoboticsMonitoringTools.WaitOperationAsync(
                    robotics,
                    controllerId,
                    submission.IntentId,
                    submission.Operation.ToString(),
                    1000,
                    sessionName,
                    CancellationToken.None).ConfigureAwait(false);
                if (result.Completed)
                {
                    return result;
                }
            }
            while (DateTime.UtcNow < deadline);

            Assert.Fail("Timed out waiting for the MCP-submitted operation to complete.");
            return result;
        }

        private static OpcUaSessionManager CreateSessionManager()
        {
            ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            return new OpcUaSessionManager(
                NullLogger<OpcUaSessionManager>.Instance,
                services,
                new OpcUaClientOptions(),
                DefaultTelemetry.Create(static _ => { }));
        }

        private const string kSessionName = "mcp-live-robotics";

        private static readonly LinearMoveIntentInput kLinearMoveInput = new()
        {
            IntentId = "mcp-live-linear",
            Target = new PoseDto
            {
                Position = new PosePositionDto { X = 0.1, Y = 0.2, Z = 0.3 },
                Orientation = new QuaternionDto { W = 1.0 },
                FrameId = "world"
            },
            Constraints = new MotionConstraintsDto { CartesianSpeed = 0.05 },
            BufferMode = BufferModeEnum.Aborting,
            BlockingMode = BlockingModeEnum.None
        };

        private sealed class LiveChannelFixture : IAsyncDisposable
        {
            private LiveChannelFixture(object fixture)
            {
                m_fixture = fixture;
            }

            public string ServerUrl => (string)GetProperty("ServerUrl").GetValue(m_fixture)!;

            public static async ValueTask<LiveChannelFixture> StartAsync()
            {
                Type fixtureType = typeof(RobotIntentLiveChannelTests).GetNestedType(
                    "TestServerFixture",
                    BindingFlags.NonPublic)!;
                var fixture = Activator.CreateInstance(
                    fixtureType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: [false, false],
                    culture: CultureInfo.InvariantCulture)!;
                await InvokeValueTaskAsync(fixture, "StartAsync").ConfigureAwait(false);
                return new LiveChannelFixture(fixture);
            }

            public async ValueTask DisposeAsync()
            {
                await ((IAsyncDisposable)m_fixture).DisposeAsync().ConfigureAwait(false);
            }

            private static async ValueTask InvokeValueTaskAsync(object target, string methodName)
            {
                MethodInfo method = target.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public)!;
                var task = (ValueTask)method.Invoke(target, [])!;
                await task.ConfigureAwait(false);
            }

            private PropertyInfo GetProperty(string name)
            {
                return m_fixture.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)!;
            }

            private readonly object m_fixture;
        }
    }
}
#endif
