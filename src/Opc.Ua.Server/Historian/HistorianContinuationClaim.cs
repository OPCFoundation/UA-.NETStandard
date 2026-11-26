// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Owns a claimed history continuation until it is retired, replaced, or restored to the session.
    /// </summary>
    internal sealed class HistorianContinuationClaim : IAsyncDisposable
    {
        /// <summary>
        /// Associates claimed continuation state with the session store used to save or restore it.
        /// </summary>
        public HistorianContinuationClaim(
            ISessionContinuationPoints continuationPoints,
            HistorianContinuationState state,
            ILogger? logger)
        {
            m_continuationPoints = continuationPoints ??
                throw new ArgumentNullException(nameof(continuationPoints));
            m_state = state ?? throw new ArgumentNullException(nameof(state));
            m_logger = logger;
        }

        /// <summary>
        /// Gets the claimed state while the claim remains active.
        /// </summary>
        public HistorianContinuationState State =>
            m_state ?? throw new ObjectDisposedException(
                nameof(HistorianContinuationClaim));

        /// <summary>
        /// Gets whether the claim still owns its original continuation.
        /// </summary>
        internal bool IsActive => m_state is not null;

        /// <summary>
        /// Saves the next continuation and retires the claimed state after persistence succeeds.
        /// </summary>
        public async ValueTask<HistorianContinuationState> CommitSuccessorAsync(
            HistorianResumeToken resumeToken,
            int? bufferedProcessedOffset,
            CancellationToken cancellationToken)
        {
            HistorianContinuationState? successor = State.CreateSuccessor(
                resumeToken,
                bufferedProcessedOffset);
            try
            {
                await m_continuationPoints!
                    .SaveHistoryAsync(successor, cancellationToken)
                    .ConfigureAwait(false);
                Retire();
                return successor;
            }
            finally
            {
                successor = null;
            }
        }

        /// <summary>
        /// Disposes the claimed state without restoring it to the session.
        /// </summary>
        public void Retire()
        {
            HistorianContinuationState? state = m_state;
            m_state = null;
            m_continuationPoints = null;
            m_logger = null;
            state?.Dispose();
        }

        /// <summary>
        /// Replaces legacy annotation state with a copy bound to the annotation property node.
        /// </summary>
        public void NormalizeLegacyAnnotationNodeId(NodeId nodeId)
        {
            HistorianContinuationState state = State;
            HistorianContinuationState normalized =
                state.CreateNormalizedAnnotationState(nodeId);
            m_state = normalized;
            state.Dispose();
        }

        // TODO: Remove this suppression when CA2000 recognizes the documented ownership transfer.
        /// <summary>
        /// Attempts to restore an uncommitted continuation and releases the claim.
        /// </summary>
        /// <exception cref="AggregateException">
        /// Both durable restoration and the fallback local save of the continuation fail.
        /// </exception>
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "SaveHistoryAsync takes ownership of the restoration copy on invocation and disposes it if the session cannot retain it.")]
        public async ValueTask DisposeAsync()
        {
            HistorianContinuationState? state = m_state;
            ISessionContinuationPoints? continuationPoints =
                m_continuationPoints;
            ILogger? logger = m_logger;
            m_state = null;
            m_continuationPoints = null;
            m_logger = null;
            if (state == null || continuationPoints == null)
            {
                return;
            }
            try
            {
                try
                {
                    HistorianContinuationState restoration = state.CreateRestorationCopy();
                    await continuationPoints.SaveHistoryAsync(
                        restoration,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception persistenceFailure) when (persistenceFailure is not OutOfMemoryException)
                {
                    try
                    {
                        continuationPoints.SaveHistory(state);
                    }
                    catch (Exception localFailure) when (localFailure is not OutOfMemoryException)
                    {
                        throw new AggregateException(
                            "Durable and local history continuation restoration failed.",
                            persistenceFailure,
                            localFailure);
                    }
                    logger?.HistoryContinuationRestoredOnlyLocally();
                    return;
                }
                state.Dispose();
            }
            finally
            {
                state = null;
                continuationPoints = null;
                logger = null;
            }
        }

        private ILogger? m_logger;
        private ISessionContinuationPoints? m_continuationPoints;
        private HistorianContinuationState? m_state;
    }

    /// <summary>
    /// Logs degraded restoration of a claimed history continuation.
    /// </summary>
    internal static partial class HistorianContinuationClaimLog
    {
        /// <summary>
        /// Logs that a continuation was restored locally after durable restoration failed.
        /// </summary>
        [LoggerMessage(
            EventId = ServerEventIds.HistorianContinuationClaim + 0,
            Level = LogLevel.Warning,
            Message = "Durable history continuation restoration failed; the continuation was restored only locally.")]
        public static partial void HistoryContinuationRestoredOnlyLocally(this ILogger logger);
    }
}
