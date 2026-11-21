// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

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
            HistorianContinuationState state)
        {
            m_continuationPoints = continuationPoints ??
                throw new ArgumentNullException(nameof(continuationPoints));
            m_state = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Gets the claimed state while the claim remains active.
        /// </summary>
        public HistorianContinuationState State =>
            m_state ?? throw new ObjectDisposedException(
                nameof(HistorianContinuationClaim));

        /// <summary>
        /// Saves the next continuation and retires the claimed state after persistence succeeds.
        /// </summary>
        public async ValueTask<HistorianContinuationState> CommitSuccessorAsync(
            HistorianResumeToken resumeToken,
            int? bufferedProcessedOffset,
            CancellationToken cancellationToken)
        {
            HistorianContinuationState successor = State.CreateSuccessor(
                resumeToken,
                bufferedProcessedOffset);
            await m_continuationPoints!
                .SaveHistoryAsync(successor, cancellationToken)
                .ConfigureAwait(false);
            Retire();
            return successor;
        }

        /// <summary>
        /// Disposes the claimed state without restoring it to the session.
        /// </summary>
        public void Retire()
        {
            HistorianContinuationState? state = m_state;
            m_state = null;
            m_continuationPoints = null;
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
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "SaveHistoryAsync takes ownership of the restoration copy on invocation and disposes it if the session cannot retain it.")]
        public async ValueTask DisposeAsync()
        {
            HistorianContinuationState? state = m_state;
            ISessionContinuationPoints? continuationPoints =
                m_continuationPoints;
            m_state = null;
            m_continuationPoints = null;
            if (state == null || continuationPoints == null)
            {
                return;
            }
            try
            {
                HistorianContinuationState restoration = state.CreateRestorationCopy();
                await continuationPoints.SaveHistoryAsync(
                    restoration,
                    CancellationToken.None).ConfigureAwait(false);
                state.Dispose();
                return;
            }
            catch
            {
                // SaveHistoryAsync owns and disposes the restoration clone
                // when durable persistence fails. Keep the claimed original
                // untouched for the local fallback.
            }

            try
            {
                continuationPoints.SaveHistory(state);
            }
            catch
            {
                // Restoration is best effort and must not replace the error
                // that caused the claim to unwind.
                state.Dispose();
            }
        }

        private ISessionContinuationPoints? m_continuationPoints;
        private HistorianContinuationState? m_state;
    }
}
