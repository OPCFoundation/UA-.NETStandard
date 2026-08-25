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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client.Intent
{
    /// <summary>
    /// Delegate invoked when a mission snapshot changes.
    /// </summary>
    public delegate void MissionChangedHandler(MissionSnapshot snapshot);

    /// <summary>
    /// Awaitable handle for a MissionType instance. Reconnect-safe: re-reads the
    /// mission state after a session reconnect so the caller never stalls on a
    /// completion task that will never fire.
    /// </summary>
    public sealed class MissionHandle : IAsyncDisposable
    {
        /// <summary>
        /// Creates a handle for a mission.
        /// </summary>
        public MissionHandle(RobotIntentControllerClient controller, string missionId, NodeId missionNode)
        {
            m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
            MissionId = missionId;
            MissionNode = missionNode;
            m_controller.Transport.Reconnected += OnReconnected;
        }

        /// <summary>
        /// Raised when an observed value changes or the state is re-read after reconnect.
        /// </summary>
        public event MissionChangedHandler? Changed;

        /// <summary>
        /// Gets the mission id.
        /// </summary>
        public string MissionId { get; }

        /// <summary>
        /// Gets the mission node.
        /// </summary>
        public NodeId MissionNode { get; }

        /// <summary>
        /// Gets the last known snapshot.
        /// </summary>
        public MissionSnapshot Current
        {
            get
            {
                lock (m_lock)
                {
                    return m_current;
                }
            }
            private set
            {
                lock (m_lock)
                {
                    m_current = value;
                }
            }
        }

        /// <summary>
        /// Gets a task that completes when the mission reaches a terminal execution state.
        /// </summary>
        public Task<MissionSnapshot> Completion => m_completion.Task;

        /// <summary>
        /// Starts observation by reading the initial state. Subscribe before reading
        /// to close the fast-completion race.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            m_executionStateNode = await m_controller.Transport.ResolveChildAsync(
                MissionNode,
                "ExecutionState",
                cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> nodes = [m_executionStateNode];
            m_pumpTask = PumpAsync(nodes, m_disposeCts.Token);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Re-reads the mission snapshot after reconnect or on demand.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            MissionSnapshot snapshot = await m_controller.Transport.ReadMissionSnapshotAsync(
                MissionNode,
                cancellationToken).ConfigureAwait(false);
            Apply(snapshot, fullyObserved: true);
        }

        /// <summary>
        /// Requests cancellation of the mission.
        /// </summary>
        public ValueTask<IntentCommandOutcome> CancelAsync(
            StopModeEnum stopMode,
            CancellationToken cancellationToken = default)
        {
            return m_controller.Transport.CancelMissionAsync(MissionId, stopMode, cancellationToken);
        }

        /// <summary>
        /// Waits up to the timeout for the mission to complete, returning the current state on timeout.
        /// </summary>
        public async ValueTask<MissionWaitResult> WaitForCompletionAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            if (Completion.IsCompleted)
            {
                MissionSnapshot terminal = await Completion.ConfigureAwait(false);
                return new MissionWaitResult
                {
                    Completed = true,
                    TerminalState = terminal.ExecutionState,
                    Failure = terminal.Failure,
                    FailureMessage = terminal.FailureMessage,
                    Current = terminal
                };
            }

            Task delay = timeout == Timeout.InfiniteTimeSpan
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.Delay(timeout, cancellationToken);
            Task completed = await Task.WhenAny(Completion, delay).ConfigureAwait(false);
            if (ReferenceEquals(completed, Completion))
            {
                MissionSnapshot terminal = await Completion.ConfigureAwait(false);
                return new MissionWaitResult
                {
                    Completed = true,
                    TerminalState = terminal.ExecutionState,
                    Failure = terminal.Failure,
                    FailureMessage = terminal.FailureMessage,
                    Current = terminal
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            MissionSnapshot current = Current;
            return new MissionWaitResult
            {
                Completed = false,
                TerminalState = current.ExecutionState,
                Failure = current.Failure,
                FailureMessage = current.FailureMessage,
                Current = current
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await m_disposeCts.CancelAsync().ConfigureAwait(false);
                m_controller.Transport.Reconnected -= OnReconnected;
                if (m_pumpTask != null)
                {
                    try
                    {
                        await m_pumpTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception exception)
                    {
                        m_controller.Transport.Logger.MissionSubscriptionFailed(exception, MissionId, MissionNode);
                    }
                }
            }
            finally
            {
                m_disposeCts.Dispose();
            }
        }

        private async Task PumpAsync(ArrayOf<NodeId> nodes, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (RobotIntentDataChange change in m_controller.Transport
                    .SubscribeDataChangesAsync(nodes, cancellationToken).ConfigureAwait(false))
                {
                    if (Matches(change.NodeId, m_executionStateNode) &&
                        TryGetEnumValue(change.Value, out ExecutionStateEnum state))
                    {
                        MissionSnapshot snapshot = Current with { ExecutionState = state };
                        if (Apply(snapshot, fullyObserved: false) && RobotIntentRules.IsTerminal(state))
                        {
                            await RefreshAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                m_controller.Transport.Logger.MissionSubscriptionFailed(exception, MissionId, MissionNode);
            }
        }

        private bool Apply(MissionSnapshot snapshot, bool fullyObserved)
        {
            bool completed = false;
            lock (m_lock)
            {
                m_current = snapshot;
                if (RobotIntentRules.IsTerminal(snapshot.ExecutionState) && fullyObserved)
                {
                    completed = m_completion.TrySetResult(snapshot);
                }
            }
            Changed?.Invoke(snapshot);
            return !fullyObserved && RobotIntentRules.IsTerminal(snapshot.ExecutionState);
        }

        private void OnReconnected()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            _ = RefreshAfterReconnectAsync();
        }

        private async Task RefreshAfterReconnectAsync()
        {
            try
            {
                await RefreshAsync(m_disposeCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_disposeCts.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                m_controller.Transport.Logger.MissionSubscriptionFailed(exception, MissionId, MissionNode);
            }
        }

        private static bool Matches(NodeId observed, NodeId expected)
        {
            return !expected.IsNull && observed == expected;
        }

        private static bool TryGetEnumValue<TEnum>(
            Variant value, out TEnum result) where TEnum : struct, Enum
        {
            if (value.TryGetValue(out int intValue))
            {
                result = EnumHelper.Int32ToEnum<TEnum>(intValue);
                return true;
            }
            if (value.TryGetValue(out TEnum enumValue))
            {
                result = enumValue;
                return true;
            }
            result = default;
            return false;
        }

        private readonly RobotIntentControllerClient m_controller;
        private readonly Lock m_lock = new();
        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly TaskCompletionSource<MissionSnapshot> m_completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private MissionSnapshot m_current = new();
        private NodeId m_executionStateNode = NodeId.Null;
        private Task? m_pumpTask;
        private int m_disposed;
    }

    internal static partial class MissionHandleLog
    {
        [LoggerMessage(
            EventId = RobotIntentClientEventIds.MissionSubscriptionFailed,
            Level = LogLevel.Warning,
            Message = "Robot Intent mission subscription failed. MissionId={MissionId}, MissionNode={MissionNode}.")]
        public static partial void MissionSubscriptionFailed(
            this ILogger logger,
            Exception exception,
            string missionId,
            NodeId missionNode);
    }
}
