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
    /// Awaitable handle for an IntentOperationType instance.
    /// </summary>
    public sealed class IntentOperationHandle : IAsyncDisposable
    {
        /// <summary>
        /// Creates a handle for an operation.
        /// </summary>
        public IntentOperationHandle(RobotIntentControllerClient controller, string intentId, NodeId operation)
        {
            m_controller = controller ?? throw new ArgumentNullException(nameof(controller));
            IntentId = intentId;
            Operation = operation;
            m_controller.Transport.Reconnected += OnReconnected;
        }

        /// <summary>
        /// Raised when an observed value changes or the state is re-read after reconnect.
        /// </summary>
        public event IntentOperationChangedHandler? Changed;

        /// <summary>
        /// Gets the intent id.
        /// </summary>
        public string IntentId { get; }

        /// <summary>
        /// Gets the operation node.
        /// </summary>
        public NodeId Operation { get; }

        /// <summary>
        /// Gets the last known snapshot.
        /// </summary>
        public IntentOperationSnapshot Current
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
        /// Gets a task that completes with Result when ExecutionState reaches a terminal value.
        /// </summary>
        public Task<IntentResultDataType> Completion => m_completion.Task;

        /// <summary>
        /// Starts observation, subscribing before reading the initial state to close the fast-completion race.
        /// </summary>
        public async ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            NodeId executionState = await m_controller.Transport.ResolveChildAsync(
                Operation,
                "ExecutionState",
                cancellationToken).ConfigureAwait(false);
            NodeId progress = await m_controller.Transport.ResolveChildAsync(
                Operation,
                "Progress",
                cancellationToken).ConfigureAwait(false);
            NodeId currentPose = await m_controller.Transport.ResolveChildAsync(
                Operation,
                "CurrentPose",
                cancellationToken).ConfigureAwait(false);
            NodeId resultNode = await m_controller.Transport.ResolveChildAsync(
                Operation,
                "Result",
                cancellationToken).ConfigureAwait(false);
            m_executionStateNode = executionState;
            m_progressNode = progress;
            m_currentPoseNode = currentPose;
            m_resultNode = resultNode;
            ArrayOf<NodeId> nodes = [executionState, progress, currentPose, resultNode];
            m_pumpTask = PumpAsync(nodes, m_disposeCts.Token);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Re-reads the operation after reconnect or on demand.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            IntentOperationSnapshot snapshot = await m_controller.Transport.ReadOperationSnapshotAsync(
                Operation,
                cancellationToken).ConfigureAwait(false);
            bool resultObserved = RobotIntentRules.IsTerminal(snapshot.ExecutionState) &&
                snapshot.Result.State == snapshot.ExecutionState;
            Apply(
                snapshot,
                stateObserved: true,
                progressObserved: true,
                poseObserved: true,
                resultObserved: resultObserved,
                fullyObserved: true);
        }

        /// <summary>
        /// Requests cancellation and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> CancelAsync(
            StopModeEnum stopMode,
            CancellationToken cancellationToken = default)
        {
            return m_controller.Transport.CancelIntentAsync(IntentId, stopMode, cancellationToken);
        }

        /// <summary>
        /// Requests Pause and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken cancellationToken = default)
        {
            return m_controller.Transport.PauseAsync(cancellationToken);
        }

        /// <summary>
        /// Requests Resume and returns the refusal-aware outcome.
        /// </summary>
        public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken cancellationToken = default)
        {
            return m_controller.Transport.ResumeAsync(cancellationToken);
        }

        /// <summary>
        /// Retries the intent and returns the admission outcome for the new operation.
        /// </summary>
        public ValueTask<IntentSubmissionResult> RetryAsync(CancellationToken cancellationToken = default)
        {
            return m_controller.Transport.RetryAsync(IntentId, cancellationToken);
        }

        /// <summary>
        /// Waits up to the timeout for completion, returning the current state on timeout.
        /// </summary>
        public async ValueTask<IntentOperationWaitResult> WaitForCompletionAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            if (Completion.IsCompleted)
            {
                return new IntentOperationWaitResult
                {
                    Completed = true,
                    Result = await Completion.ConfigureAwait(false),
                    Current = Current
                };
            }

            Task delay = timeout == Timeout.InfiniteTimeSpan
                ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                : Task.Delay(timeout, cancellationToken);
            Task completed = await Task.WhenAny(Completion, delay).ConfigureAwait(false);
            if (ReferenceEquals(completed, Completion))
            {
                return new IntentOperationWaitResult
                {
                    Completed = true,
                    Result = await Completion.ConfigureAwait(false),
                    Current = Current
                };
            }

            cancellationToken.ThrowIfCancellationRequested();
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return new IntentOperationWaitResult
            {
                Completed = false,
                Current = Current
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
                        m_controller.Transport.Logger.OperationSubscriptionFailed(exception, IntentId, Operation);
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
                    IntentOperationSnapshot? snapshot = null;
                    bool stateObserved = false;
                    bool progressObserved = false;
                    bool poseObserved = false;
                    bool resultObserved = false;
                    if (Matches(change.NodeId, m_executionStateNode) &&
                        TryGetEnumValue(change.Value, out ExecutionStateEnum state))
                    {
                        snapshot = new IntentOperationSnapshot { Operation = Operation, ExecutionState = state };
                        stateObserved = true;
                    }
                    else if (Matches(change.NodeId, m_progressNode) &&
                        change.Value.TryGetValue(out double progress))
                    {
                        snapshot = new IntentOperationSnapshot { Operation = Operation, Progress = progress };
                        progressObserved = true;
                    }
                    else if (Matches(change.NodeId, m_currentPoseNode) &&
                        TryGetEncodeable(change.Value, out Pose3DDataType pose))
                    {
                        snapshot = new IntentOperationSnapshot { Operation = Operation, CurrentPose = pose };
                        poseObserved = true;
                    }
                    else if (Matches(change.NodeId, m_resultNode) &&
                        change.Value.TryGetValue(out ExtensionObject extension) &&
                        extension.TryGetValue(out IEncodeable? encodeable) &&
                        encodeable is IntentResultDataType extensionResult)
                    {
                        snapshot = new IntentOperationSnapshot { Operation = Operation, Result = extensionResult };
                        resultObserved = true;
                    }
                    else if (Matches(change.NodeId, m_resultNode) &&
                        TryGetEncodeable(change.Value, out IntentResultDataType directResult))
                    {
                        snapshot = new IntentOperationSnapshot { Operation = Operation, Result = directResult };
                        resultObserved = true;
                    }
                    if (snapshot != null && Apply(
                        snapshot,
                        stateObserved,
                        progressObserved,
                        poseObserved,
                        resultObserved,
                        fullyObserved: false))
                    {
                        await RefreshAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                m_controller.Transport.Logger.OperationSubscriptionFailed(exception, IntentId, Operation);
            }
        }

        private bool Apply(
            IntentOperationSnapshot snapshot,
            bool stateObserved,
            bool progressObserved,
            bool poseObserved,
            bool resultObserved,
            bool fullyObserved)
        {
            IntentOperationSnapshot current;
            bool shouldReadResult = false;
            bool completed = false;
            lock (m_lock)
            {
                current = Merge(
                    m_current,
                    snapshot,
                    stateObserved,
                    progressObserved,
                    poseObserved,
                    resultObserved,
                    fullyObserved);
                m_current = current;
                m_resultObserved |= resultObserved;
                if (RobotIntentRules.IsTerminal(current.ExecutionState))
                {
                    if (m_resultObserved)
                    {
                        completed = m_completion.TrySetResult(current.Result);
                    }
                    else
                    {
                        shouldReadResult = true;
                    }
                }
            }
            Changed?.Invoke(current);
            if (completed)
            {
                m_controller.Transport.Logger.OperationTerminal(
                    IntentId,
                    Operation,
                    current.ExecutionState);
            }
            return shouldReadResult;
        }

        private void OnReconnected()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                return;
            }
            m_controller.Transport.Logger.OperationSubscriptionReestablished(IntentId, Operation);
            m_controller.Transport.Logger.OperationReconnectRead(IntentId, Operation);
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
                m_controller.Transport.Logger.OperationSubscriptionFailed(exception, IntentId, Operation);
            }
        }

        private IntentOperationSnapshot Merge(
            IntentOperationSnapshot current,
            IntentOperationSnapshot update,
            bool stateObserved,
            bool progressObserved,
            bool poseObserved,
            bool resultObserved,
            bool fullyObserved)
        {
            IntentOperationSnapshot merged = current with
            {
                Operation = Operation
            };
            if (!update.Operation.IsNull)
            {
                merged = merged with { Operation = update.Operation };
            }
            if (update.IntentId.Length != 0)
            {
                merged = merged with { IntentId = update.IntentId };
            }
            if (stateObserved)
            {
                if (!RobotIntentRules.IsTerminal(current.ExecutionState) ||
                    RobotIntentRules.IsTerminal(update.ExecutionState))
                {
                    merged = merged with { ExecutionState = update.ExecutionState };
                }
            }
            if (progressObserved)
            {
                merged = merged with { Progress = update.Progress };
            }
            if (poseObserved)
            {
                merged = merged with { CurrentPose = update.CurrentPose };
            }
            if (resultObserved)
            {
                merged = merged with { Result = update.Result };
            }
            // A pump update carries one changed node, so an empty MissionId or a zero QueuePosition
            // there means "not carried" rather than "cleared". A full read is authoritative for both,
            // and the server publishes QueuePosition zero precisely to say the operation left the queue.
            if (fullyObserved || update.MissionId.Length != 0)
            {
                merged = merged with { MissionId = update.MissionId };
            }
            if (fullyObserved || update.QueuePosition != 0)
            {
                merged = merged with { QueuePosition = update.QueuePosition };
            }
            return merged;
        }

        private bool TryGetEncodeable<T>(Variant value, out T result)
            where T : class, IEncodeable
        {
            result = null!;
            // TryGetValue annotates out parameters conservatively for nullable analysis.
            // TODO: remove this suppression when Variant carries precise MaybeNull annotations for encodeables.
#pragma warning disable CS8600
            if (!value.TryGetValue(out T decoded, m_controller.Transport.MessageContext) || decoded == null)
#pragma warning restore CS8600
            {
                return false;
            }
            result = decoded;
            return true;
        }

        private static bool Matches(NodeId actual, NodeId expected)
        {
            return actual.IsNull || Equals(actual, expected);
        }

        private static bool TryGetEnumValue<TEnum>(Variant value, out TEnum result)
            where TEnum : struct, Enum
        {
            if (value.TryGetValue(out TEnum typed))
            {
                result = typed;
                return true;
            }
            if (value.TryGetValue(out int intValue))
            {
                // OPC UA encodes Enumeration values as Int32 in Variants; convert explicitly to the
                // generated enum type.
                result = EnumHelper.Int32ToEnum<TEnum>(intValue);
                return true;
            }
            result = default;
            return false;
        }

        private readonly RobotIntentControllerClient m_controller;
        private readonly CancellationTokenSource m_disposeCts = new();
        private readonly Lock m_lock = new();

        private readonly TaskCompletionSource<IntentResultDataType> m_completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private Task? m_pumpTask;
        private IntentOperationSnapshot m_current = new();
        private NodeId m_executionStateNode = NodeId.Null;
        private NodeId m_progressNode = NodeId.Null;
        private NodeId m_currentPoseNode = NodeId.Null;
        private NodeId m_resultNode = NodeId.Null;
        private int m_disposed;
        private bool m_resultObserved;
    }

    internal static partial class IntentOperationHandleLog
    {
        [LoggerMessage(
            EventId = RobotIntentClientEventIds.OperationTerminal,
            Level = LogLevel.Information,
            Message = "Robot Intent operation reached terminal state. IntentId={IntentId}, Operation={Operation}, " +
                "State={State}.")]
        public static partial void OperationTerminal(
            this ILogger logger,
            string intentId,
            NodeId operation,
            ExecutionStateEnum state);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.OperationReconnectRead,
            Level = LogLevel.Debug,
            Message = "Robot Intent operation state is being re-read after reconnect. IntentId={IntentId}, " +
                "Operation={Operation}.")]
        public static partial void OperationReconnectRead(this ILogger logger, string intentId, NodeId operation);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.OperationSubscriptionReestablished,
            Level = LogLevel.Debug,
            Message = "Robot Intent operation subscription re-established after reconnect. IntentId={IntentId}, " +
                "Operation={Operation}.")]
        public static partial void OperationSubscriptionReestablished(
            this ILogger logger,
            string intentId,
            NodeId operation);

        [LoggerMessage(
            EventId = RobotIntentClientEventIds.OperationSubscriptionFailed,
            Level = LogLevel.Warning,
            Message = "Robot Intent operation subscription failed during shutdown. IntentId={IntentId}, " +
                "Operation={Operation}.")]
        public static partial void OperationSubscriptionFailed(
            this ILogger logger,
            Exception exception,
            string intentId,
            NodeId operation);
    }
}
