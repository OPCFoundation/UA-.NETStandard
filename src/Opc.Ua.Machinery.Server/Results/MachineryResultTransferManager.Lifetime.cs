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
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Server.Results
{
    internal sealed partial class MachineryResultTransferManager
    {
        /// <summary>
        /// Stops admission, unregisters session cleanup, and joins every owned generation and removal.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            List<DownloadSlot>? slots = null;
            lock (m_gate)
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    slots = [.. m_slots.Values];
                    foreach (DownloadSlot slot in slots)
                    {
                        slot.Closing = true;
                    }
                }
            }
            if (slots != null)
            {
                m_sessionRegistration.Dispose();
                try
                {
                    await CleanupSlotsAsync(slots).ConfigureAwait(false);
                    m_disposalCompleted.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    m_disposalCompleted.TrySetException(ex);
                }
            }
            await m_disposalCompleted.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the live session outside the transfer gate; admission rechecks its closing flag inside the gate.
        /// </summary>
        private ISession? FindLiveSession(NodeId sessionId)
        {
            ISessionManager? sessions = m_manager.SystemContext.Server.SessionManager;
            if (sessionId.IsNull || sessions == null)
            {
                return null;
            }
            foreach (ISession session in sessions.GetSessions())
            {
                if (session.Id == sessionId && !session.IsClosing && !session.HasExpired)
                {
                    return session;
                }
            }
            throw new ServiceResultException(StatusCodes.BadSessionClosed,
                "An ended session cannot acquire a result-transfer handle.");
        }

        /// <summary>
        /// Joins removal of only this session's files, including generation and file-close work already in progress.
        /// </summary>
        private ValueTask OnSessionClosingAsync(NodeId sessionId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            var slots = new List<DownloadSlot>();
            lock (m_gate)
            {
                foreach (DownloadSlot slot in m_slots.Values)
                {
                    if (!sessionId.IsNull && slot.OwnerSessionId == sessionId)
                    {
                        slot.Closing = true;
                        slots.Add(slot);
                    }
                }
            }
            // Once the owner is closing, cancellation must not abandon its address-space mutations.
            return CleanupSlotsAsync(slots);
        }

        /// <summary>
        /// Sweeps inactive reservations before a new download, awaiting deletion before releasing capacity.
        /// </summary>
        private async ValueTask ReclaimExpiredHandlesAsync()
        {
            var expired = new List<DownloadSlot>();
            DateTimeOffset now = m_timeProvider.GetUtcNow();
            lock (m_gate)
            {
                foreach (DownloadSlot slot in m_slots.Values)
                {
                    if (now - slot.LastActivity > m_options.ResultTransferTimeout)
                    {
                        slot.Closing = true;
                        expired.Add(slot);
                    }
                }
            }
            await CleanupSlotsAsync(expired).ConfigureAwait(false);
            foreach (DownloadSlot slot in expired)
            {
                m_logger.ResultTransferHandleExpired(slot.Handle);
            }
        }

        /// <summary>
        /// Starts every cleanup before awaiting any and preserves all failures.
        /// </summary>
        private async ValueTask CleanupSlotsAsync(List<DownloadSlot> slots)
        {
            var tasks = new Task[slots.Count];
            for (int ii = 0; ii < slots.Count; ii++)
            {
                tasks[ii] = CleanupSlotAsync(slots[ii], retryFailure: true).AsTask();
            }
            Task completion = Task.WhenAll(tasks);
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch
            {
                throw completion.Exception!;
            }
        }

        /// <summary>
        /// Claims one removal attempt or joins its completion without holding a gate across external code.
        /// </summary>
        private async ValueTask CleanupSlotAsync(DownloadSlot slot, bool retryFailure = false)
        {
            bool ownsCleanup = false;
            TaskCompletionSource<bool> completion;
            lock (m_gate)
            {
                slot.Closing = true;
                if (slot.CleanupCompleted == null ||
                    (retryFailure && !slot.Removed && slot.CleanupCompleted.Task.IsFaulted))
                {
                    slot.CleanupCompleted = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    ownsCleanup = true;
                }
                completion = slot.CleanupCompleted;
            }
            if (ownsCleanup)
            {
                var errors = new List<Exception>();
                try
                {
                    slot.Shutdown.Cancel();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
                try
                {
                    // Registration is never abandoned: a late completion must be removed before close returns.
                    await slot.GenerationCompleted.Task.ConfigureAwait(false);
                    await DetachFileAsync(slot).ConfigureAwait(false);
                    lock (m_gate)
                    {
                        m_slots.Remove(slot.Handle);
                        slot.Removed = true;
                    }
                    slot.Shutdown.Dispose();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
                if (errors.Count != 0)
                {
                    completion.TrySetException(new AggregateException("Result-file cleanup failed.", errors));
                }
                else
                {
                    completion.TrySetResult(true);
                }
            }
            await completion.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the complete staged tree through the normal node-manager lifecycle, even after partial failure.
        /// </summary>
        private async ValueTask DetachFileAsync(DownloadSlot slot)
        {
            FileState? file = slot.FileObject;
            if (file == null)
            {
                return;
            }
            if (slot.FileNodes == null)
            {
                slot.FileNodes = [];
                CollectFileNodes(file, slot.FileNodes);
            }
            List<Exception>? errors = null;
            foreach (NodeId nodeId in slot.FileNodes)
            {
                try
                {
                    // Unpublished staging is rolled back without a model-change event.
                    // Published files use the normal deletion event and resource lifecycle.
                    await m_manager.DeleteNodeAsync(
                        m_manager.SystemContext,
                        nodeId,
                        emitModelChange: slot.Published,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.FailedToRemoveTransientResultFile(ex, nodeId);
                    (errors ??= []).Add(ex);
                }
            }
            if (errors != null)
            {
                // Retain ownership and capacity so shutdown or a later sweep can retry failed cleanup.
                throw new AggregateException("Transient result nodes could not all be removed.", errors);
            }
            lock (m_gate)
            {
                m_transfer.RemoveChild(file);
                slot.DetachFile();
            }
        }

        /// <summary>
        /// Captures descendants before deletion can remove their links, allowing all cleanup attempts to run.
        /// </summary>
        private void CollectFileNodes(NodeState node, List<NodeId> nodes)
        {
            if (!node.NodeId.IsNull)
            {
                nodes.Add(node.NodeId);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_manager.SystemContext, children);
            foreach (BaseInstanceState child in children)
            {
                CollectFileNodes(child, nodes);
            }
        }

        /// <summary>The nonblocking, runtime-owned session cleanup registration.</summary>
        private readonly IDisposable m_sessionRegistration;

        /// <summary>The shared shutdown completion, including all admitted generation and cleanup.</summary>
        private readonly TaskCompletionSource<bool> m_disposalCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
