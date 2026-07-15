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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Implements the OPC 10000-12 §7.10.20 <c>ApplicationConfigurationFileType</c>
    /// (a §7.8.5.1 <c>ConfigurationFileType</c>) read/update flow on the
    /// <c>ServerConfiguration.ConfigurationFile</c> Object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler wires the inherited <c>FileType</c> Methods (Open, Read,
    /// Write, Close, SetPosition, GetPosition) and the
    /// <c>ConfigurationFileType</c> Methods (CloseAndUpdate, ConfirmUpdate) on
    /// the supplied node and delegates the actual configuration access to an
    /// <see cref="IApplicationConfigurationFileProvider"/>. It owns the FileType
    /// stream lifecycle, single-writer/owner-Session validation, the
    /// SecurityAdmin/authenticated-channel checks, transaction exclusion
    /// against the shared PushManagement transaction coordinator
    /// (§7.10.2/§7.10.20), the <c>VersionToUpdate</c>/<c>CurrentVersion</c>
    /// check, validation-before-apply with atomic rollback, and the
    /// <c>UpdateId</c>/<c>ConfirmUpdate</c> revert lifecycle.
    /// </para>
    /// <para>
    /// Only the asynchronous Method handlers are registered because the
    /// <see cref="ConfigurationNodeManager"/> that hosts this node dispatches
    /// through the async call path; no synchronous-over-asynchronous shim is
    /// installed.
    /// </para>
    /// </remarks>
    public sealed class ApplicationConfigurationFile : IDisposable
    {
        private const int kDefaultCapacity = 64 * 1024;

        /// <summary>
        /// Maximum configuration file size (bytes) accepted for a read or a
        /// write. Guards against unbounded memory use by a misbehaving Client.
        /// </summary>
        private const long kMaxConfigurationFileSize = 16 * 1024 * 1024;

        /// <summary>
        /// Delegate used to validate access to the configuration file. Throws a
        /// <see cref="ServiceResultException"/> when access is denied.
        /// </summary>
        /// <param name="context">The calling system context.</param>
        public delegate void SecureAccess(ISystemContext context);

        /// <summary>
        /// Initializes the handler and wires the FileType and
        /// ConfigurationFileType Method handlers on <paramref name="node"/>.
        /// </summary>
        /// <param name="node">The configuration file node to bind.</param>
        /// <param name="provider">The configuration provider to delegate to.</param>
        /// <param name="readAccess">
        /// Validates read access (SecurityAdmin over an authenticated,
        /// encrypted SecureChannel).
        /// </param>
        /// <param name="writeAccess">
        /// Validates update access (SecurityAdmin over an authenticated
        /// SecureChannel), used by Write/CloseAndUpdate/ConfirmUpdate.
        /// </param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="coordinator">
        /// The shared PushManagement transaction coordinator used to enforce
        /// transaction exclusion, or <see langword="null"/> when transactions
        /// are not coordinated.
        /// </param>
        /// <param name="timeProvider">The time provider for revert scheduling.</param>
        /// <param name="activityTimeout">
        /// The <c>ActivityTimeout</c> in milliseconds; the file is
        /// automatically closed (and any changes discarded) if no Method is
        /// called within this window after <c>Open</c>. Non-positive disables
        /// the auto-close.
        /// </param>
        public ApplicationConfigurationFile(
            ApplicationConfigurationFileState node,
            IApplicationConfigurationFileProvider provider,
            SecureAccess readAccess,
            SecureAccess writeAccess,
            ITelemetryContext telemetry,
            IPushConfigurationTransactionCoordinator? coordinator,
            TimeProvider timeProvider,
            double activityTimeout)
        {
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_readAccess = readAccess ?? throw new ArgumentNullException(nameof(readAccess));
            m_writeAccess = writeAccess ?? throw new ArgumentNullException(nameof(writeAccess));
            m_logger = telemetry.CreateLogger<ApplicationConfigurationFile>();
            m_coordinator = coordinator;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_activityTimeout = activityTimeout;

            node.Open!.OnCallAsync = OpenAsync;
            node.Read!.OnCallAsync = ReadAsync;
            node.Write!.OnCallAsync = WriteAsync;
            node.Close!.OnCallAsync = CloseAsync;
            node.SetPosition!.OnCallAsync = SetPositionAsync;
            node.GetPosition!.OnCallAsync = GetPositionAsync;
            node.CloseAndUpdate!.OnCallAsync = CloseAndUpdateAsync;
            node.ConfirmUpdate!.OnCallAsync = ConfirmUpdateAsync;
        }

        /// <summary>
        /// Closes this file's open read/write handle if it is currently owned
        /// by <paramref name="sessionId"/>. Called by
        /// <see cref="ConfigurationNodeManager.SessionClosingAsync"/> so an
        /// abandoned Session does not leave the file permanently open.
        /// </summary>
        /// <param name="sessionId">The closing Session.</param>
        public void NotifySessionClosing(NodeId sessionId)
        {
            lock (m_lock)
            {
                if (m_sessionId.IsNull || !Utils.IsEqual(m_sessionId, sessionId))
                {
                    return;
                }

                DiscardOpenHandleNoLock();
            }

            m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
        }

        /// <summary>
        /// Discards the open handle when the <c>ActivityTimeout</c> elapses with
        /// no intervening Method call (§7.8.5.1). Any buffered changes are lost.
        /// </summary>
        /// <remarks>
        /// Exposed for tests as an explicit inactivity-expiry hook. It is routed
        /// through the same generation-aware path the timer callback uses so a
        /// test cannot bypass the guard that prevents a superseded or a
        /// different Session's timer from closing a refreshed handle.
        /// </remarks>
        internal void ExpireForInactivity()
        {
            long generation;
            lock (m_lock)
            {
                generation = m_activityGeneration;
            }

            OnActivityTimerExpired(generation);
        }

        /// <summary>
        /// Handles an <c>ActivityTimeout</c> firing. The open handle is only
        /// discarded when <paramref name="generation"/> still matches the
        /// current activity generation and a handle is open, so a callback
        /// queued by a superseded or disposed timer - including one armed for a
        /// previous Session or before the handle was refreshed - is ignored
        /// instead of closing a live handle.
        /// </summary>
        /// <param name="generation">
        /// The activity generation captured when the firing timer was armed.
        /// </param>
        private void OnActivityTimerExpired(long generation)
        {
            bool wasWriting;
            lock (m_lock)
            {
                if (m_sessionId.IsNull || generation != m_activityGeneration)
                {
                    return;
                }

                wasWriting = m_writing;
                DiscardOpenHandleNoLock();
            }

            if (wasWriting)
            {
                m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
            }

            m_logger.ConfigurationFileAutoClosedAfterInactivity(m_activityTimeout);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancelPendingRevert();
            lock (m_lock)
            {
                // Supersede the current activity generation so a callback still
                // queued by the timer disposed here cannot run against the
                // disposed handler.
                m_activityGeneration++;
                m_activityTimer?.Dispose();
                m_activityTimer = null;
                m_strm?.Dispose();
                m_strm = null;
                m_sessionId = default;
            }
        }

        private async ValueTask<OpenMethodStateResult> OpenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            CancellationToken cancellationToken)
        {
            bool isWriteMode;
            if (mode == (byte)OpenFileMode.Read)
            {
                m_readAccess(context);
                isWriteMode = false;
            }
            else if (mode == (byte)(OpenFileMode.Read | OpenFileMode.Write))
            {
                m_writeAccess(context);

                // §7.10.20: opening for writing is rejected with
                // Bad_TransactionPending when another Session owns the active
                // PushManagement transaction.
                m_coordinator?.ValidateSessionCanParticipate(GetSessionId(context));
                isWriteMode = true;
            }
            else
            {
                // §7.8.5.1: Open shall not support modes other than Read (0x01)
                // and Read + Write (0x03).
                return new OpenMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "The ConfigurationFile only supports Read (0x01) and Read+Write (0x03) open modes."),
                    FileHandle = 0
                };
            }

            MemoryStream? strm = null;
            try
            {
                if (isWriteMode)
                {
                    strm = new MemoryStream(kDefaultCapacity);
                }
                else
                {
                    ByteString current = await m_provider.ReadConfigurationAsync(cancellationToken)
                        .ConfigureAwait(false);
                    byte[] buffer = current.IsNull ? [] : current.ToArray();
                    strm = new MemoryStream(buffer, writable: false);
                }

                uint fileHandle;
                lock (m_lock)
                {
                    // Last open always wins, matching the sibling TrustList
                    // FileType handler, so an abandoned handle cannot block a
                    // fresh open.
                    if (!m_sessionId.IsNull)
                    {
                        DiscardOpenHandleNoLock();
                    }

                    m_sessionId = GetSessionId(context);
                    fileHandle = ++m_fileHandle;
                    m_strm = strm;
                    m_writing = isWriteMode;
                    m_totalBytesProcessed = 0;
                    m_node.OpenCount!.Value = 1;
                    RestartActivityTimerNoLock();
                }

                // Cleared unconditionally before being set so an evicted
                // previous write-open never leaves a stale entry behind.
                m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                if (isWriteMode)
                {
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, true);
                }

                return new OpenMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    FileHandle = fileHandle
                };
            }
            catch
            {
                strm?.Dispose();
                throw;
            }
        }

        private ValueTask<ReadMethodStateResult> ReadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            CancellationToken cancellationToken)
        {
            m_readAccess(context);

            ByteString data;
            lock (m_lock)
            {
                ServiceResult? access = ValidateHandleNoLock(context, fileHandle);
                if (access != null)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = access,
                        Data = default
                    });
                }

                if (length < 0)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument,
                        Data = default
                    });
                }

                // Overflow-safe cumulative bound check: m_totalBytesProcessed is
                // always <= kMaxConfigurationFileSize (never negative), so the
                // subtraction cannot underflow/overflow. length is a
                // non-negative int (checked above) widened to long for the
                // comparison, so int.MaxValue cannot overflow it either. This
                // rejects an oversized request *before* allocating the buffer,
                // rather than allocating and truncating afterwards.
                long remaining = kMaxConfigurationFileSize - m_totalBytesProcessed;
                if (length > remaining)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Configuration read of {0} bytes would exceed the maximum allowed size of {1} bytes.",
                            length,
                            kMaxConfigurationFileSize),
                        Data = default
                    });
                }

                byte[] buffer = new byte[length];
                int bytesRead = m_strm!.Read(buffer, 0, length);
                data = ByteString.From(buffer)[..bytesRead];
                m_totalBytesProcessed += bytesRead;
                RestartActivityTimerNoLock();
            }

            return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Data = data
            });
        }

        private ValueTask<WriteMethodStateResult> WriteAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ByteString data,
            CancellationToken cancellationToken)
        {
            m_writeAccess(context);

            lock (m_lock)
            {
                ServiceResult? access = ValidateHandleNoLock(context, fileHandle);
                if (access != null)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = access
                    });
                }

                if (!m_writing)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    });
                }

                if (m_totalBytesProcessed + data.Length > kMaxConfigurationFileSize)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Configuration exceeds the maximum allowed size of {0} bytes.",
                            kMaxConfigurationFileSize)
                    });
                }

                if (!data.IsNull && data.Length > 0)
                {
                    byte[] bytes = data.ToArray();
                    m_strm!.Write(bytes, 0, bytes.Length);
                    m_totalBytesProcessed += data.Length;
                }
                RestartActivityTimerNoLock();
            }

            return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ValueTask<CloseMethodStateResult> CloseAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            m_readAccess(context);

            bool wasWriting;
            lock (m_lock)
            {
                ServiceResult? access = ValidateHandleNoLock(context, fileHandle);
                if (access != null)
                {
                    return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
                    {
                        ServiceResult = access
                    });
                }

                wasWriting = m_writing;
                DiscardOpenHandleNoLock();
            }

            if (wasWriting)
            {
                m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
            }

            return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ValueTask<SetPositionMethodStateResult> SetPositionAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ulong position,
            CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                ServiceResult? access = ValidateHandleNoLock(context, fileHandle);
                if (access != null)
                {
                    return new ValueTask<SetPositionMethodStateResult>(new SetPositionMethodStateResult
                    {
                        ServiceResult = access
                    });
                }

                if (position > (ulong)m_strm!.Length)
                {
                    return new ValueTask<SetPositionMethodStateResult>(new SetPositionMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
                }

                m_strm.Position = (long)position;
                RestartActivityTimerNoLock();
            }

            return new ValueTask<SetPositionMethodStateResult>(new SetPositionMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ValueTask<GetPositionMethodStateResult> GetPositionAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            ulong position;
            lock (m_lock)
            {
                ServiceResult? access = ValidateHandleNoLock(context, fileHandle);
                if (access != null)
                {
                    return new ValueTask<GetPositionMethodStateResult>(new GetPositionMethodStateResult
                    {
                        ServiceResult = access,
                        Position = 0
                    });
                }

                position = (ulong)m_strm!.Position;
                RestartActivityTimerNoLock();
            }

            return new ValueTask<GetPositionMethodStateResult>(new GetPositionMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Position = position
            });
        }

        private async ValueTask<ConfigurationFileCloseAndUpdateMethodStateResult> CloseAndUpdateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            uint versionToUpdate,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            double revertAfterTime,
            double restartDelayTime,
            CancellationToken cancellationToken)
        {
            m_writeAccess(context);

            NodeId sessionId = GetSessionId(context);

            // §7.10.20: changes queued on another Session block the update.
            // Bad_ChangesPending is not defined by this stack; the semantically
            // equivalent Bad_TransactionPending (also used by Open with Write)
            // is returned instead.
            if (m_coordinator != null)
            {
                try
                {
                    m_coordinator.ValidateSessionCanParticipate(sessionId);
                }
                catch (ServiceResultException ex)
                {
                    return FailedUpdate(ex.StatusCode, targets);
                }
            }

            ByteString proposed;
            lock (m_lock)
            {
                ServiceResult? access = ValidateHandleNoLock(context, fileHandle);
                if (access != null)
                {
                    return FailedUpdate(access.StatusCode, targets);
                }

                if (!m_writing)
                {
                    return FailedUpdate(StatusCodes.BadInvalidState, targets);
                }

                proposed = ByteString.From(m_strm!.ToArray());
            }

            // §7.8.5.2: the VersionToUpdate must match the CurrentVersion.
            if (versionToUpdate != m_provider.CurrentVersion)
            {
                CloseWriteHandle();
                return FailedUpdate(StatusCodes.BadInvalidState, targets);
            }

            // §7.8.5.2 validation before apply: an invalid configuration is
            // rejected before any change is made (no partial update).
            try
            {
                await m_provider.ValidateConfigurationAsync(proposed, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                m_logger.ConfigurationFileUpdateRejectedDuringValidation(ex);
                CloseWriteHandle();
                return FailedUpdate(ex.StatusCode, targets);
            }

            // §7.8.5.2 atomic apply: on failure the provider leaves the active
            // configuration unchanged, so there is nothing to roll back.
            try
            {
                await m_provider.ApplyConfigurationAsync(proposed, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                m_logger.ConfigurationFileUpdateFailedWhileApplying(ex);
                CloseWriteHandle();
                return FailedUpdate(ex.StatusCode, targets);
            }

            uint newVersion = m_provider.CurrentVersion;
            Uuid updateId = Uuid.Empty;
            if (m_provider.RequiresConfirmation)
            {
                updateId = Uuid.NewUuid();
                ScheduleRevert(updateId, restartDelayTime, revertAfterTime);
            }

            RefreshVersionNodes(context);

            CloseWriteHandle();

            m_logger.ConfigurationFileUpdatedToVersion(
                newVersion,
                m_provider.RequiresConfirmation ? "is" : "not");

            return new ConfigurationFileCloseAndUpdateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                UpdateResults = SuccessResults(targets),
                NewVersion = newVersion,
                UpdateId = updateId
            };
        }

        private async ValueTask<ConfigurationFileConfirmUpdateMethodStateResult> ConfirmUpdateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            Uuid updateId,
            CancellationToken cancellationToken)
        {
            m_writeAccess(context);

            lock (m_lock)
            {
                if (m_pendingUpdateId.Guid == Guid.Empty ||
                    m_pendingUpdateId.Guid != updateId.Guid)
                {
                    return new ConfigurationFileConfirmUpdateMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    };
                }

                m_pendingUpdateId = Uuid.Empty;
            }

            CancelPendingRevert();

            await m_provider.ConfirmUpdateAsync(cancellationToken).ConfigureAwait(false);

            m_logger.ConfigurationFileUpdateConfirmed(updateId.Guid);

            return new ConfigurationFileConfirmUpdateMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            };
        }

        private ServiceResult? ValidateHandleNoLock(ISystemContext context, uint fileHandle)
        {
            if (m_sessionId.IsNull)
            {
                return StatusCodes.BadInvalidState;
            }

            if (context is ISessionSystemContext session &&
                !m_sessionId.IsNull &&
                !m_sessionId.Equals(session.SessionId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Session not authorized for this ConfigurationFile handle.");
            }

            if (m_fileHandle != fileHandle)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Invalid file handle.");
            }

            return null;
        }

        private void DiscardOpenHandleNoLock()
        {
            m_sessionId = default;
            m_writing = false;
            m_strm?.Dispose();
            m_strm = null;
            m_totalBytesProcessed = 0;
            m_node.OpenCount!.Value = 0;
            // Supersede the current activity generation so a callback already
            // queued by the timer disposed here cannot close a handle that is
            // later reopened for the same or a different Session.
            m_activityGeneration++;
            m_activityTimer?.Dispose();
            m_activityTimer = null;
        }

        /// <summary>
        /// Discards the open write handle and clears the coordinator's
        /// write-open flag so a blocked <c>ApplyChanges</c> can proceed. Used
        /// by <c>CloseAndUpdate</c> for both the successful commit and the
        /// terminal version/validation/apply failures.
        /// </summary>
        private void CloseWriteHandle()
        {
            bool wasWriting;
            lock (m_lock)
            {
                wasWriting = m_writing;
                DiscardOpenHandleNoLock();
            }

            if (wasWriting)
            {
                m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
            }
        }

        private void RestartActivityTimerNoLock()
        {
            if (m_activityTimeout <= 0 || m_sessionId.IsNull)
            {
                return;
            }

            // Advance the generation and capture it in the immutable timer state
            // so a callback queued by the timer superseded here is recognised as
            // stale and ignored once this newer timer is armed.
            long generation = ++m_activityGeneration;
            m_activityTimer?.Dispose();
            m_activityTimer = m_timeProvider.CreateTimer(
                static state =>
                {
                    var activityState = (ActivityTimerState)state!;
                    activityState.Owner.OnActivityTimerExpired(activityState.Generation);
                },
                new ActivityTimerState(this, generation),
                TimeSpan.FromMilliseconds(m_activityTimeout),
                Timeout.InfiniteTimeSpan);
        }

        private void RefreshVersionNodes(ISystemContext context)
        {
            if (m_node.CurrentVersion != null)
            {
                m_node.CurrentVersion.Value = m_provider.CurrentVersion;
            }
            if (m_node.LastUpdateTime != null)
            {
                m_node.LastUpdateTime.Value = new DateTimeUtc(m_provider.LastUpdateTime);
            }
            m_node.ClearChangeMasks(context, includeChildren: true);
        }

        private void ScheduleRevert(Uuid updateId, double restartDelayTime, double revertAfterTime)
        {
            CancelPendingRevert();

            double totalMs = Math.Max(0, restartDelayTime) + Math.Max(0, revertAfterTime);
            var cts = new CancellationTokenSource();
            lock (m_lock)
            {
                m_pendingUpdateId = updateId;
                m_pendingRevertCts = cts;
            }

            if (totalMs <= 0)
            {
                // No revert window: the update stands until explicitly
                // confirmed or overwritten; nothing to schedule.
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await m_timeProvider.Delay(TimeSpan.FromMilliseconds(totalMs), cts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                bool stillPending;
                lock (m_lock)
                {
                    stillPending = m_pendingUpdateId.Guid == updateId.Guid;
                    if (stillPending)
                    {
                        m_pendingUpdateId = Uuid.Empty;
                    }
                }

                if (!stillPending)
                {
                    return;
                }

                try
                {
                    await m_provider.RevertUpdateAsync(CancellationToken.None).ConfigureAwait(false);
                    m_logger.ConfigurationFileUpdateNotConfirmedReverted(updateId.Guid);
                }
                catch (Exception ex)
                {
                    m_logger.ConfigurationFileRevertFailed(ex);
                }
            });
        }

        private void CancelPendingRevert()
        {
            CancellationTokenSource? cts;
            lock (m_lock)
            {
                cts = m_pendingRevertCts;
                m_pendingRevertCts = null;
            }

            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // already disposed
                }
                cts.Dispose();
            }
        }

        private static ConfigurationFileCloseAndUpdateMethodStateResult FailedUpdate(
            StatusCode statusCode,
            ArrayOf<ConfigurationUpdateTargetType> targets)
        {
            return new ConfigurationFileCloseAndUpdateMethodStateResult
            {
                ServiceResult = statusCode,
                UpdateResults = FailureResults(statusCode, targets),
                NewVersion = 0,
                UpdateId = Uuid.Empty
            };
        }

        private static ArrayOf<StatusCode> SuccessResults(ArrayOf<ConfigurationUpdateTargetType> targets)
        {
            if (targets.Count == 0)
            {
                return ArrayOf<StatusCode>.Empty;
            }

            var results = new StatusCode[targets.Count];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = StatusCodes.Good;
            }
            return ArrayOf.Wrapped(results);
        }

        private static ArrayOf<StatusCode> FailureResults(
            StatusCode statusCode,
            ArrayOf<ConfigurationUpdateTargetType> targets)
        {
            if (targets.Count == 0)
            {
                return ArrayOf<StatusCode>.Empty;
            }

            var results = new StatusCode[targets.Count];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = statusCode;
            }
            return ArrayOf.Wrapped(results);
        }

        private static NodeId GetSessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        /// <summary>
        /// Immutable state passed to the inactivity timer callback so the
        /// callback can recognise (and ignore) a firing from a superseded or
        /// disposed timer by comparing the captured generation against the
        /// current one.
        /// </summary>
        private sealed class ActivityTimerState
        {
            public ActivityTimerState(ApplicationConfigurationFile owner, long generation)
            {
                Owner = owner;
                Generation = generation;
            }

            public ApplicationConfigurationFile Owner { get; }

            public long Generation { get; }
        }

        private readonly Lock m_lock = new();
        private readonly ApplicationConfigurationFileState m_node;
        private readonly IApplicationConfigurationFileProvider m_provider;
        private readonly SecureAccess m_readAccess;
        private readonly SecureAccess m_writeAccess;
        private readonly ILogger m_logger;
        private readonly IPushConfigurationTransactionCoordinator? m_coordinator;
        private readonly TimeProvider m_timeProvider;
        private readonly double m_activityTimeout;
        private NodeId m_sessionId;
        private uint m_fileHandle;
        private MemoryStream? m_strm;
        private bool m_writing;
        private long m_totalBytesProcessed;
        private ITimer? m_activityTimer;
        private long m_activityGeneration;
        private Uuid m_pendingUpdateId;
        private CancellationTokenSource? m_pendingRevertCts;
    }

    internal static partial class ApplicationConfigurationFileLog
    {
        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 0, Level = LogLevel.Information,
            Message = "ConfigurationFile auto-closed after inactivity timeout of {Timeout} ms.")]
        public static partial void ConfigurationFileAutoClosedAfterInactivity(this ILogger logger, double timeout);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 1, Level = LogLevel.Warning,
            Message = "ConfigurationFile update rejected during validation.")]
        public static partial void ConfigurationFileUpdateRejectedDuringValidation(
            this ILogger logger,
            Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 2, Level = LogLevel.Error,
            Message = "ConfigurationFile update failed while applying; no changes applied.")]
        public static partial void ConfigurationFileUpdateFailedWhileApplying(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 3, Level = LogLevel.Information,
            Message = "ConfigurationFile updated to version {Version}; confirmation {Confirm} required.")]
        public static partial void ConfigurationFileUpdatedToVersion(
            this ILogger logger,
            uint version,
            string confirm);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 4, Level = LogLevel.Information,
            Message = "ConfigurationFile update {UpdateId} confirmed.")]
        public static partial void ConfigurationFileUpdateConfirmed(this ILogger logger, Guid updateId);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 5, Level = LogLevel.Warning,
            Message = "ConfigurationFile update {UpdateId} was not confirmed in time and was reverted.")]
        public static partial void ConfigurationFileUpdateNotConfirmedReverted(this ILogger logger, Guid updateId);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 6, Level = LogLevel.Error,
            Message = "ConfigurationFile revert of unconfirmed update failed.")]
        public static partial void ConfigurationFileRevertFailed(this ILogger logger, Exception ex);
    }
}
