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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.RobotIntent;
using Opc.Ua.RobotIntent.Server;
using Opc.Ua.Tests;
using RiDataTypeIds = Opc.Ua.RobotIntent.DataTypeIds;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Pins normative Robot Intent shall statements that are easy to regress silently.
    /// </summary>
    [TestFixture]
    public class IntentNormativeShallTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create(true);
            m_messageContext = ServiceMessageContext.Create(telemetry);
            m_messageContext.NamespaceUris.Append(RiNamespaces.RobotIntent);
            m_context = new SystemContext(telemetry)
            {
                NamespaceUris = m_messageContext.NamespaceUris,
                EncodeableFactory = m_messageContext.Factory
            };

            m_executor = new ScriptedExecutor();
            m_added.Clear();
        }

        [Test]
        public async Task RunningIntentContinuesWhenSubmittingSessionClosesAndAuthorityIsReleased()
        {
            var submittingSession = new NodeId("submitting-session", 1);
            var nextSession = new NodeId("next-session", 1);
            m_executor.Gate = new SemaphoreSlim(0);
            m_executor.HonourCancellation = true;
            using IntentControllerHost host = NewHost(Options(requireAuthority: true));

            Assert.That(host.RequestControl(m_context, submittingSession, out _), Is.True);
            IntentAdmission admission = host.SubmitIntent(m_context, submittingSession, Move("survives"));
            await WaitAsync(() => m_executor.Started.Contains("survives")).ConfigureAwait(false);

            host.OnSessionClosed(m_context, submittingSession);

            Assert.That(host.RequestControl(m_context, nextSession, out _), Is.True,
                "command authority must be released when the owning Session closes");
            m_executor.Gate.Release();
            IntentOperationState node = await WaitForTerminalAsync("survives").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(admission.Accepted, Is.True);
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Succeeded),
                    "outstanding intents are unaffected by authority release");
                Assert.That(m_executor.CancellationObservedCount, Is.Zero);
            });
        }

        [Test]
        public async Task OnPathCancelKeepsWireValueOneAndIsDeliveredToExecutor()
        {
            m_executor.Gate = new SemaphoreSlim(0);
            m_executor.HonourCancellation = true;
            using IntentControllerHost host = NewHost(Options());
            host.SubmitIntent(m_context, null, Move("on-path"));
            await WaitAsync(() => m_executor.Started.Contains("on-path")).ConfigureAwait(false);

            bool accepted = host.CancelIntent(m_context, null, "on-path", StopModeEnum.OnPath);

            IntentOperationState node = await WaitForTerminalAsync("on-path").ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That((int)StopModeEnum.OnPath, Is.EqualTo(1),
                    "OnPath is aligned to OPC 40010-1 and must not be renumbered");
                Assert.That(accepted, Is.True);
                Assert.That(node.ExecutionState!.Value, Is.EqualTo(ExecutionStateEnum.Cancelled));
                Assert.That(m_executor.LastStopMode, Is.EqualTo(StopModeEnum.OnPath),
                    "OnPath means the accepted cancel request reaches the robot executor");
            });
        }

        [Test]
        public void SingleBlockingModeIsAdmittedOnlyWhenTheCapabilityDeclaresSingle()
        {
            IntentControllerHostOptions options = Options();
            options.Capabilities.Clear();
            options.Capabilities.Add(new DeclaredCapability
            {
                IntentType = RiDataTypeIds.LinearMoveIntentDataType,
                SupportedBufferModes = new[] { BufferModeEnum.Aborting }.ToArrayOf(),
                SupportedBlockingModes = new[] { BlockingModeEnum.Single }.ToArrayOf()
            });
            using IntentControllerHost host = NewHost(options);

            IntentAdmission none = host.SubmitIntent(m_context, null, Move("none", BlockingModeEnum.None));
            IntentAdmission soft = host.SubmitIntent(m_context, null, Move("soft", BlockingModeEnum.Soft));
            IntentAdmission single = host.SubmitIntent(m_context, null, Move("single", BlockingModeEnum.Single));
            IntentAdmission hard = host.SubmitIntent(m_context, null, Move("hard", BlockingModeEnum.Hard));

            Assert.Multiple(() =>
            {
                Assert.That(none.Accepted, Is.False);
                Assert.That(none.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(soft.Accepted, Is.False);
                Assert.That(soft.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(single.Accepted, Is.True);
                Assert.That(hard.Accepted, Is.False);
                Assert.That(hard.Failure, Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
            });
        }

        [TearDown]
        public void TearDown()
        {
            m_executor.Gate?.Dispose();
        }

        private static IntentControllerHostOptions Options(bool requireAuthority = false)
        {
            var options = new IntentControllerHostOptions
            {
                OperationalMode = OperationalModeEnum.AutomaticExternal,
                RequireControlAuthority = requireAuthority,
                AxisCount = 6,
                MaxQueueDepth = 4
            };
            options.Accept(RiDataTypeIds.LinearMoveIntentDataType);
            return options;
        }

        private static Pose3DDataType Pose(double x = 0, double y = 0, double z = 0)
        {
            return new Pose3DDataType
            {
                FrameId = "base",
                Position = new[] { x, y, z },
                Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }
            };
        }

        private static LinearMoveIntentDataType Move(
            string id,
            BlockingModeEnum blockingMode,
            BufferModeEnum bufferMode = BufferModeEnum.Aborting)
        {
            return new LinearMoveIntentDataType
            {
                IntentId = id,
                BufferMode = bufferMode,
                BlockingMode = blockingMode,
                Target = Pose(1, 0, 0)
            };
        }

        private static LinearMoveIntentDataType Move(string id)
        {
            return Move(id, BlockingModeEnum.None);
        }

        private IntentControllerHost NewHost(IntentControllerHostOptions options)
        {
            var controller = new IntentControllerState(null);
            controller.Create(
                m_context,
                new NodeId(Guid.NewGuid().ToString(), 1),
                new QualifiedName("Controller", 1),
                new LocalizedText("Controller"),
                true);
            var host = new IntentControllerHost(
                controller,
                m_executor,
                (node, _) =>
                {
                    lock (m_addedLock)
                    {
                        m_added.Add(node);
                    }
                    return default;
                },
                options);
            host.Start(m_context);
            return host;
        }

        private async Task<IntentOperationState> WaitForTerminalAsync(string intentId)
        {
            IntentOperationState? node = null;
            await WaitAsync(() =>
            {
                node = FindOperation(intentId);
                return node?.ExecutionState?.Value is { } state && IntentOutcome.IsTerminal(state);
            }).ConfigureAwait(false);
            return node!;
        }

        private IntentOperationState? FindOperation(string intentId)
        {
            lock (m_addedLock)
            {
                return m_added
                    .OfType<IntentOperationState>()
                    .FirstOrDefault(n => n.IntentId?.Value == intentId);
            }
        }

        private static Task WaitAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            return WaitAsync(condition, "the expected condition", timeoutMs);
        }

        private static async Task WaitAsync(
            Func<bool> condition,
            string conditionDescription,
            int timeoutMs = 5000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail($"timed out waiting for {conditionDescription}");
        }

        private ServiceMessageContext m_messageContext = null!;
        private SystemContext m_context = null!;
        private ScriptedExecutor m_executor = null!;
        private readonly Lock m_addedLock = new();
        private readonly List<NodeState> m_added = [];

        private sealed class ScriptedExecutor : IIntentExecutor
        {
            public ConcurrentQueue<string> StartedQueue { get; } = new();
            public string[] Started => [.. StartedQueue];
            public SemaphoreSlim? Gate { get; set; }
            public bool HonourCancellation { get; set; }
            public StopModeEnum LastStopMode { get; private set; }
            public int CancellationObservedCount => Volatile.Read(ref m_cancellationObservedCount);

            public async ValueTask<IntentOutcome> ExecuteAsync(
                IntentExecution execution,
                CancellationToken cancellationToken)
            {
                StartedQueue.Enqueue(execution.Intent.IntentId ?? execution.IntentId);

                if (Gate != null)
                {
                    try
                    {
                        await Gate.WaitAsync(
                            HonourCancellation ? cancellationToken : CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Increment(ref m_cancellationObservedCount);
                        LastStopMode = execution.StopMode;
                        return new IntentOutcome { State = ExecutionStateEnum.Cancelled };
                    }
                }

                return IntentOutcome.Success;
            }

            public bool CanCancel(IntentExecution execution)
            {
                return true;
            }

            private int m_cancellationObservedCount;
        }
    }
}
