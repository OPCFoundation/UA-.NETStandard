// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.FileSystem;

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
    /// check, validation-before-apply, and the client-facing
    /// <c>UpdateId</c>/<c>ConfirmUpdate</c> lifecycle. Providers that require
    /// confirmation own the durable pending state through
    /// <see cref="IRecoverableApplicationConfigurationFileProvider"/>.
    /// </para>
    /// <para>
    /// Only the asynchronous Method handlers are registered because the
    /// <see cref="ConfigurationNodeManager"/> that hosts this node dispatches
    /// through the async call path; no synchronous-over-asynchronous shim is
    /// installed.
    /// </para>
    /// </remarks>
    // The gate remains available after shutdown so late handler calls can observe m_stopped and return their
    // established service status instead of throwing ObjectDisposedException.
    // TODO: Move admission checks ahead of the gate so it can be safely disposed.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2213:Disposable fields should be disposed",
        Justification = "Disposing the gate changes post-shutdown handler errors and can race nonblocking disposal.")]
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
        /// <param name="operations">
        /// Optional deployment admission for deferred apply and revert effects.
        /// </param>
        public ApplicationConfigurationFile(
            ApplicationConfigurationFileState node,
            IApplicationConfigurationFileProvider provider,
            SecureAccess readAccess,
            SecureAccess writeAccess,
            ITelemetryContext telemetry,
            IPushConfigurationTransactionCoordinator? coordinator,
            TimeProvider timeProvider,
            double activityTimeout,
            IApplicationConfigurationPushOperations? operations = null)
        {
            m_node = node ?? throw new ArgumentNullException(nameof(node));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_readAccess = readAccess ?? throw new ArgumentNullException(nameof(readAccess));
            m_writeAccess = writeAccess ?? throw new ArgumentNullException(nameof(writeAccess));
            m_logger = telemetry.CreateLogger<ApplicationConfigurationFile>();
            m_backgroundWork = new BackgroundTaskScope(nameof(ApplicationConfigurationFile), telemetry);
            m_coordinator = coordinator;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_activityTimeout = activityTimeout;
            m_operations = operations;

            node.Open!.OnCallAsync = OpenAsync;
            node.Read!.OnCallAsync = ReadAsync;
            node.Write!.OnCallAsync = WriteAsync;
            node.Close!.OnCallAsync = CloseAsync;
            node.SetPosition!.OnCallAsync = SetPositionAsync;
            node.GetPosition!.OnCallAsync = GetPositionAsync;
            node.CloseAndUpdate!.OnCallAsync = CloseAndUpdateAsync;
            node.ConfirmUpdate!.OnCallAsync = ConfirmUpdateAsync;
            node.Size!.OnReadValueAsync += static (_, _, _, _, _) =>
                new ValueTask<AttributeReadResult>(new AttributeReadResult(
                    StatusCodes.BadNotSupported,
                    Variant.Null,
                    StatusCodes.BadNotSupported,
                    DateTimeUtc.Now));
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
            bool wasWriting = false;
            lock (m_lock)
            {
                foreach (PendingOpen pending in m_openReservations)
                {
                    if (pending.SessionId.Equals(sessionId))
                    {
                        pending.SessionClosed = true;
                    }
                }

                List<uint> closingHandles = [];
                foreach ((uint handleId, OpenFileHandle handle) in m_handles)
                {
                    if (Utils.IsEqual(handle.SessionId, sessionId))
                    {
                        closingHandles.Add(handleId);
                    }
                }

                foreach (uint handleId in closingHandles)
                {
                    if (m_handles.TryGetValue(handleId, out OpenFileHandle? handle))
                    {
                        wasWriting |= handle.IsWriting;
                        CloseOpenHandleNoLock(handleId);
                    }
                }

                RestartActivityTimerNoLock();
                if (wasWriting && !m_updateInProgress)
                {
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                }
            }
        }

        /// <summary>
        /// Reports whether this attachment still owns file work that cannot be transferred.
        /// </summary>
        internal bool HasOutstandingAttachmentWork
        {
            get
            {
                lock (m_lock)
                {
                    return m_handles.Count != 0 || m_pendingOpens != 0 || m_updateInProgress ||
                        m_deferredApplyPending || m_pendingUpdateId.Guid != Guid.Empty || m_pendingRevertCts != null;
                }
            }
        }

        /// <summary>
        /// Stops deferred work and waits for active configuration updates before runtime release.
        /// </summary>
        public async ValueTask StopAsync()
        {
            Task pendingOpens;
            lock (m_lock)
            {
                m_stopped = true;
                pendingOpens = m_openDrain?.Task ?? Task.CompletedTask;
            }
            await pendingOpens.ConfigureAwait(false);
            CancelPendingRevert();
            await m_backgroundWork.DisposeAsync().ConfigureAwait(false);
            await m_updateGate.WaitAsync().ConfigureAwait(false);
            m_updateGate.Release();
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
                if (m_handles.Count == 0 || m_updateInProgress || generation != m_activityGeneration)
                {
                    return;
                }

                wasWriting = m_writeHandle != 0;
                DiscardAllOpenHandlesNoLock();
                if (wasWriting && !m_updateInProgress && !m_deferredApplyPending)
                {
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                }
            }

            m_logger.ConfigurationFileAutoClosedAfterInactivity(m_activityTimeout);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                m_stopped = true;
            }
            // Signal only: Dispose is synchronous. The revert window stops at
            // its delay as soon as the token trips.
            m_backgroundWork.Dispose();

            CancelPendingRevert();
            bool wasWriting;
            lock (m_lock)
            {
                // Supersede the current activity generation so a callback still
                // queued by the timer disposed here cannot run against the
                // disposed handler.
                m_activityGeneration++;
                m_activityTimer?.Dispose();
                m_activityTimer = null;
                wasWriting = m_writeHandle != 0;
                DiscardAllOpenHandlesNoLock();
                if (wasWriting && !m_updateInProgress && !m_deferredApplyPending)
                {
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                }
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
            else if (mode == ((byte)OpenFileMode.Read | (byte)OpenFileMode.Write))
            {
                m_writeAccess(context);
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
            bool writerFlagSet = false;
            bool handleAdded = false;
            bool admissionReserved = false;
            var reservation = new PendingOpen(GetSessionId(context));
            try
            {
                ServiceResult? openConflict = null;
                lock (m_lock)
                {
                    if (m_stopped)
                    {
                        openConflict = StatusCodes.BadServerHalted;
                    }
                    else if (isWriteMode &&
                        (m_handles.Count != 0 || m_pendingReaders != 0 || m_writeOpenPending || m_updateInProgress))
                    {
                        openConflict = StatusCodes.BadNotWritable;
                    }
                    else if (!isWriteMode && (m_writeHandle != 0 || m_writeOpenPending || m_updateInProgress))
                    {
                        openConflict = StatusCodes.BadNotReadable;
                    }
                    else if (m_handles.Count + m_pendingReaders >= ushort.MaxValue)
                    {
                        openConflict = StatusCodes.BadTooManyOperations;
                    }
                    else
                    {
                        admissionReserved = true;
                        m_openReservations.Add(reservation);
                        if (m_pendingOpens++ == 0)
                        {
                            m_openDrain = new TaskCompletionSource<bool>(
                                TaskCreationOptions.RunContinuationsAsynchronously);
                        }
                        if (isWriteMode)
                        {
                            m_writeOpenPending = true;
                        }
                        else
                        {
                            m_pendingReaders++;
                        }
                    }
                }

                if (openConflict != null)
                {
                    return FailedOpen(openConflict.StatusCode);
                }

                if (isWriteMode && m_coordinator != null)
                {
                    writerFlagSet = true;
                    NodeId sessionId = reservation.SessionId;
                    if (m_coordinator is IPushConfigurationFileWriteCoordinator fileWriteCoordinator)
                    {
                        fileWriteCoordinator.OpenConfigurationFileWrite(m_node.NodeId, sessionId);
                    }
                    else
                    {
                        m_coordinator.ValidateSessionCanParticipate(sessionId);
                        m_coordinator.SetTrustListWriteOpen(m_node.NodeId, true);
                    }
                }

                int maxByteStringLength = Math.Min(
                    FileObjectState.GetServerReadLimit(context),
                    checked((int)kMaxConfigurationFileSize));
                if (maxByteStringLength <= 0)
                {
                    return FailedOpen(StatusCodes.BadEncodingLimitsExceeded);
                }

                ByteString current = await m_provider.ReadConfigurationAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                byte[] buffer = current.IsNull ? [] : current.ToArray();
                if (buffer.LongLength > kMaxConfigurationFileSize)
                {
                    return FailedOpen(StatusCodes.BadEncodingLimitsExceeded);
                }

                if (isWriteMode)
                {
                    strm = new MemoryStream(Math.Max(kDefaultCapacity, buffer.Length));
                    strm.Write(buffer, 0, buffer.Length);
                    strm.Position = 0;
                }
                else
                {
                    strm = new MemoryStream(buffer, writable: false);
                }

                ServiceResult? finalConflict = null;
                uint fileHandle = 0;
                lock (m_lock)
                {
                    if (m_stopped)
                    {
                        finalConflict = StatusCodes.BadServerHalted;
                    }
                    else if (reservation.SessionClosed)
                    {
                        finalConflict = StatusCodes.BadSessionIdInvalid;
                    }
                    else if (isWriteMode && (m_handles.Count != 0 || m_updateInProgress))
                    {
                        finalConflict = StatusCodes.BadNotWritable;
                    }
                    else if (!isWriteMode && (m_writeHandle != 0 || m_writeOpenPending || m_updateInProgress))
                    {
                        finalConflict = StatusCodes.BadNotReadable;
                    }
                    else if (m_handles.Count >= ushort.MaxValue)
                    {
                        finalConflict = StatusCodes.BadTooManyOperations;
                    }
                    else
                    {
                        MemoryStream stream = strm;
                        fileHandle = NextFileHandleNoLock();
                        m_handles.Add(fileHandle, new OpenFileHandle(reservation.SessionId, stream, isWriteMode));
                        handleAdded = true;
                        if (isWriteMode)
                        {
                            m_writeHandle = fileHandle;
                            m_writeOpenPending = false;
                        }
                        m_node.OpenCount!.Value = (ushort)m_handles.Count;
                        RestartActivityTimerNoLock();
                    }
                }

                if (finalConflict != null)
                {
                    return FailedOpen(finalConflict.StatusCode);
                }

                return new OpenMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    FileHandle = fileHandle
                };
            }
            finally
            {
                if (!handleAdded)
                {
                    strm?.Dispose();
                }
                if (admissionReserved)
                {
                    lock (m_lock)
                    {
                        m_openReservations.Remove(reservation);
                        try
                        {
                            if (isWriteMode)
                            {
                                m_writeOpenPending = false;
                                if (!handleAdded && writerFlagSet)
                                {
                                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                                }
                            }
                            else
                            {
                                m_pendingReaders--;
                            }
                        }
                        finally
                        {
                            if (--m_pendingOpens == 0)
                            {
                                m_openDrain!.TrySetResult(true);
                            }
                        }
                    }
                }
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

                if (length <= 0)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument,
                        Data = default
                    });
                }

                int limit = GetMaxByteStringLength(context);
                if (limit <= 0)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadEncodingLimitsExceeded,
                        Data = default
                    });
                }

                MemoryStream stream = m_handles[fileHandle].Stream;
                int count = (int)Math.Min(Math.Min(length, limit), stream.Length - stream.Position);
                byte[] buffer = new byte[count];
                int bytesRead = stream.Read(buffer, 0, count);
                data = ByteString.From(buffer.AsSpan(0, bytesRead));
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

                OpenFileHandle handle = m_handles[fileHandle];
                if (!handle.IsWriting)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidState
                    });
                }

                int limit = GetMaxByteStringLength(context);
                if (limit <= 0 || data.Length > limit ||
                    Math.Max(handle.Stream.Length, handle.Stream.Position + data.Length) > kMaxConfigurationFileSize)
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
                    handle.Stream.Write(bytes, 0, bytes.Length);
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

                bool wasWriting = CloseOpenHandleNoLock(fileHandle);
                RestartActivityTimerNoLock();
                if (wasWriting && !m_updateInProgress)
                {
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                }
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

                MemoryStream stream = m_handles[fileHandle].Stream;
                if (position > (ulong)stream.Length)
                {
                    return new ValueTask<SetPositionMethodStateResult>(new SetPositionMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
                }

                stream.Position = (long)position;
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

                position = (ulong)m_handles[fileHandle].Stream.Position;
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

            await m_updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool ownsWriteReservation = false;
            try
            {
                lock (m_lock)
                {
                    if (m_stopped)
                    {
                        return new ConfigurationFileCloseAndUpdateMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadServerHalted
                        };
                    }
                    if (ValidateHandleNoLock(context, fileHandle) == null &&
                        m_handles.TryGetValue(fileHandle, out OpenFileHandle? handle) &&
                        handle.IsWriting)
                    {
                        m_updateInProgress = true;
                        ownsWriteReservation = true;
                    }
                }

                return await CloseAndUpdateCoreAsync(
                    context,
                    method,
                    objectId,
                    fileHandle,
                    versionToUpdate,
                    targets,
                    revertAfterTime,
                    restartDelayTime,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (ownsWriteReservation)
                    {
                        lock (m_lock)
                        {
                            if (!m_deferredApplyPending)
                            {
                                m_updateInProgress = false;
                                if (!m_handles.ContainsKey(fileHandle))
                                {
                                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                                }
                            }
                            RestartActivityTimerNoLock();
                        }
                    }
                }
                finally
                {
                    m_updateGate.Release();
                }
            }
        }

        /// <summary>
        /// Validates and applies a completed ConfigurationFile upload.
        /// </summary>
        private async ValueTask<ConfigurationFileCloseAndUpdateMethodStateResult> CloseAndUpdateCoreAsync(
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

                OpenFileHandle handle = m_handles[fileHandle];
                if (!handle.IsWriting)
                {
                    return FailedUpdate(StatusCodes.BadInvalidState, targets);
                }

                // §7.8.5.2 Targets: "There must be at least one target."
                if (targets.Count == 0)
                {
                    return FailedUpdate(StatusCodes.BadInvalidArgument, targets);
                }

                proposed = ByteString.From(handle.Stream.ToArray());
            }

            IRecoverableApplicationConfigurationFileProvider? recoverableProvider =
                m_provider as IRecoverableApplicationConfigurationFileProvider;
            if (m_provider.RequiresConfirmation && recoverableProvider == null)
            {
                CloseWriteHandle(fileHandle);
                return FailedUpdate(StatusCodes.BadNotSupported, targets);
            }

            uint oldVersion = m_provider.CurrentVersion;
            ArrayOf<Variant> inputArguments =
            [
                fileHandle,
                versionToUpdate,
                Variant.FromStructure(targets),
                revertAfterTime,
                restartDelayTime
            ];

            // §7.8.5.2: the VersionToUpdate must match the CurrentVersion.
            if (versionToUpdate != m_provider.CurrentVersion)
            {
                CloseWriteHandle(fileHandle);
                return FailedUpdate(StatusCodes.BadInvalidState, targets);
            }

            // A deferred (RestartDelayTime) apply has not changed the
            // CurrentVersion yet, so the version check above cannot detect it:
            // a second update would silently race the scheduled one.
            bool deferredApplyPending;
            lock (m_lock)
            {
                deferredApplyPending = m_deferredApplyPending;
            }

            if (deferredApplyPending)
            {
                CloseWriteHandle(fileHandle);
                return FailedUpdate(StatusCodes.BadInvalidState, targets);
            }

            // §7.8.5.2 validation before apply: an invalid configuration is
            // rejected before any change is made (no partial update). A
            // target-aware provider receives the Targets ("Contents of the
            // file which are not referenced by a target are ignored").
            var targetProvider = m_provider as IApplicationConfigurationFileTargetProvider;
            ApplicationConfigurationUpdatePlan? plan = null;
            try
            {
                if (targetProvider != null)
                {
                    plan = await targetProvider.ValidateConfigurationAsync(proposed, targets, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await m_provider.ValidateConfigurationAsync(proposed, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (ServiceResultException ex)
            {
                m_logger.ConfigurationFileUpdateRejectedDuringValidation(ex);
                CloseWriteHandle(fileHandle);
                ReportConfigurationUpdatedAuditEvent(
                    context, objectId, method.NodeId, inputArguments, ex.StatusCode, oldVersion, oldVersion);
                return FailedUpdate(ex.StatusCode, targets);
            }

            // §7.8.5.2 UpdateResults: "If any element is not Good then no
            // changes are applied and the Method return code is Uncertain."
            if (plan != null && HasBadTargetResult(plan.TargetResults, targets.Count))
            {
                CloseWriteHandle(fileHandle);
                ReportConfigurationUpdatedAuditEvent(
                    context, objectId, method.NodeId, inputArguments, StatusCodes.Uncertain, oldVersion, oldVersion);
                return new ConfigurationFileCloseAndUpdateMethodStateResult
                {
                    ServiceResult = StatusCodes.Uncertain,
                    UpdateResults = plan.TargetResults.Count == targets.Count
                        ? plan.TargetResults
                        : FailureResults(StatusCodes.BadInternalError, targets),
                    NewVersion = 0,
                    UpdateId = Uuid.Empty
                };
            }

            // §7.8.5.2 RestartDelayTime: "How long the Server should wait
            // before applying the configuration changes if applying the
            // configuration changes will interrupt active Sessions." The
            // response is returned first and the apply runs afterwards.
            if (targetProvider != null && plan!.InterruptsSessions && restartDelayTime > 0)
            {
                return await ScheduleDeferredApplyAsync(
                    context,
                    objectId,
                    method.NodeId,
                    inputArguments,
                    targetProvider,
                    proposed,
                    targets,
                    fileHandle,
                    plan,
                    oldVersion,
                    revertAfterTime,
                    restartDelayTime,
                    recoverableProvider,
                    cancellationToken).ConfigureAwait(false);
            }

            Uuid updateId = Uuid.Empty;
            DateTime? revertAtUtc = null;
            if (recoverableProvider != null && m_provider.RequiresConfirmation)
            {
                updateId = Uuid.NewUuid();
                DateTime applyAtUtc = m_timeProvider.GetUtcNow().UtcDateTime;
                revertAtUtc = GetRevertAtUtc(applyAtUtc, revertAfterTime);
                try
                {
                    await recoverableProvider.BeginUpdateAsync(
                        updateId,
                        proposed,
                        targets,
                        applyAtUtc,
                        revertAtUtc,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await RevertPendingUpdateIfCreatedAsync(recoverableProvider, updateId).ConfigureAwait(false);
                    CloseWriteHandle(fileHandle);
                    StatusCode statusCode = GetConfigurationUpdateFailureStatus(ex);
                    ReportConfigurationUpdatedAuditEvent(
                        context, objectId, method.NodeId, inputArguments, statusCode, oldVersion, oldVersion);
                    return FailedUpdate(statusCode, targets);
                }
            }

            // An atomic provider failure leaves the active configuration unchanged.
            // Clear the staged record so restart recovery cannot apply a rejected update.
            try
            {
                if (recoverableProvider != null && m_provider.RequiresConfirmation)
                {
                    await recoverableProvider.ApplyPendingUpdateAsync(updateId, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (targetProvider != null)
                {
                    await targetProvider.ApplyConfigurationAsync(proposed, targets, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await m_provider.ApplyConfigurationAsync(proposed, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.ConfigurationFileUpdateFailedWhileApplying(ex);
                if (recoverableProvider != null && m_provider.RequiresConfirmation)
                {
                    await RevertPendingUpdateAsync(recoverableProvider, updateId).ConfigureAwait(false);
                }
                CloseWriteHandle(fileHandle);
                StatusCode statusCode = GetConfigurationUpdateFailureStatus(ex);
                ReportConfigurationUpdatedAuditEvent(
                    context, objectId, method.NodeId, inputArguments, statusCode, oldVersion, oldVersion);
                return FailedUpdate(statusCode, targets);
            }

            uint newVersion = m_provider.CurrentVersion;
            if (m_provider.RequiresConfirmation)
            {
                ScheduleRevert(updateId, revertAtUtc, context);
            }

            RefreshVersionNodes(context);

            CloseWriteHandle(fileHandle);

            // §7.8.5.2: "If auditing is supported, the Server shall generate
            // the ConfigurationUpdatedAuditEventType".
            ReportConfigurationUpdatedAuditEvent(
                context, objectId, method.NodeId, inputArguments, StatusCodes.Good, oldVersion, newVersion);

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

        /// <summary>
        /// Completes a <c>CloseAndUpdate</c> whose apply interrupts active
        /// Sessions: the response is returned now and the validated targets are
        /// applied once <paramref name="restartDelayTime"/> has elapsed
        /// (§7.8.5.2). The revert window, when confirmation is required, runs
        /// from the response as the Client is told to reconnect after
        /// <c>RestartDelayTime</c> but no later than
        /// <c>RestartDelayTime + RevertAfterTime</c>.
        /// </summary>
        private async ValueTask<ConfigurationFileCloseAndUpdateMethodStateResult> ScheduleDeferredApplyAsync(
            ISystemContext context,
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            IApplicationConfigurationFileTargetProvider targetProvider,
            ByteString proposed,
            ArrayOf<ConfigurationUpdateTargetType> targets,
            uint fileHandle,
            ApplicationConfigurationUpdatePlan plan,
            uint oldVersion,
            double revertAfterTime,
            double restartDelayTime,
            IRecoverableApplicationConfigurationFileProvider? recoverableProvider,
            CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                m_deferredApplyPending = true;
            }

            Uuid updateId = Uuid.Empty;
            DateTime? revertAtUtc = null;
            if (m_provider.RequiresConfirmation)
            {
                updateId = Uuid.NewUuid();
                DateTime applyAtUtc = m_timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(restartDelayTime);
                revertAtUtc = GetRevertAtUtc(applyAtUtc, revertAfterTime);
                try
                {
                    await recoverableProvider!.BeginUpdateAsync(
                        updateId,
                        proposed,
                        targets,
                        applyAtUtc,
                        revertAtUtc,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lock (m_lock)
                    {
                        m_deferredApplyPending = false;
                        m_updateInProgress = false;
                        m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                    }
                    await RevertPendingUpdateIfCreatedAsync(recoverableProvider!, updateId).ConfigureAwait(false);
                    CloseWriteHandle(fileHandle);
                    StatusCode statusCode = GetConfigurationUpdateFailureStatus(ex);
                    ReportConfigurationUpdatedAuditEvent(
                        context, objectId, methodId, inputArguments, statusCode, oldVersion, oldVersion);
                    return FailedUpdate(statusCode, targets);
                }

                ScheduleRevert(updateId, revertAtUtc, context);
            }

            CloseWriteHandle(fileHandle);

            bool scheduled = m_backgroundWork.Run("DeferredApply", async ct =>
            {
                try
                {
                    try
                    {
                        await m_timeProvider.Delay(TimeSpan.FromMilliseconds(restartDelayTime), ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    await ExecuteDeferredAsync(ApplyAsync, ct).ConfigureAwait(false);

                    async ValueTask ApplyAsync()
                    {
                        try
                        {
                            if (recoverableProvider != null && m_provider.RequiresConfirmation)
                            {
                                await recoverableProvider.ApplyPendingUpdateAsync(updateId, ct).ConfigureAwait(false);
                            }
                            else
                            {
                                await targetProvider.ApplyConfigurationAsync(proposed, targets, ct)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            m_logger.ConfigurationFileUpdateFailedWhileApplying(ex);
                            if (recoverableProvider != null && m_provider.RequiresConfirmation)
                            {
                                await RevertPendingUpdateAsync(recoverableProvider, updateId).ConfigureAwait(false);
                            }
                            else
                            {
                                AbandonPendingRevert(updateId);
                            }
                            StatusCode failure = ex is ServiceResultException sre
                                ? sre.StatusCode
                                : StatusCodes.BadInternalError;
                            ReportConfigurationUpdatedAuditEvent(
                                context, objectId, methodId, inputArguments, failure, oldVersion, oldVersion);
                            return;
                        }

                        uint appliedVersion = m_provider.CurrentVersion;
                        RefreshVersionNodes(context);
                        ReportConfigurationUpdatedAuditEvent(
                            context, objectId, methodId, inputArguments, StatusCodes.Good, oldVersion, appliedVersion);
                        m_logger.ConfigurationFileUpdatedToVersion(
                            appliedVersion,
                            m_provider.RequiresConfirmation ? "is" : "not");
                    }
                }
                finally
                {
                    lock (m_lock)
                    {
                        m_deferredApplyPending = false;
                        m_updateInProgress = false;
                        m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                    }
                }
            });

            if (!scheduled)
            {
                // Shutting down: nothing will be applied.
                if (recoverableProvider != null && m_provider.RequiresConfirmation)
                {
                    await RevertPendingUpdateAsync(recoverableProvider, updateId).ConfigureAwait(false);
                }
                else
                {
                    AbandonPendingRevert(updateId);
                }
                lock (m_lock)
                {
                    m_deferredApplyPending = false;
                }
                return FailedUpdate(StatusCodes.BadShutdown, targets);
            }

            m_logger.ConfigurationFileApplyDeferred(restartDelayTime);

            return new ConfigurationFileCloseAndUpdateMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                UpdateResults = SuccessResults(targets),
                NewVersion = plan.NewVersion,
                UpdateId = updateId
            };
        }

        /// <summary>
        /// Drops the revert scheduled for <paramref name="updateId"/> when the
        /// update it guards was never applied.
        /// </summary>
        private void AbandonPendingRevert(Uuid updateId)
        {
            if (updateId.Guid == Guid.Empty)
            {
                return;
            }

            bool matches;
            lock (m_lock)
            {
                matches = m_pendingUpdateId.Guid == updateId.Guid;
                if (matches)
                {
                    m_pendingUpdateId = Uuid.Empty;
                }
            }

            if (matches)
            {
                CancelPendingRevert();
            }
        }

        private static bool HasBadTargetResult(ArrayOf<StatusCode> results, int targetCount)
        {
            if (results.Count == 0)
            {
                return false;
            }

            if (results.Count != targetCount)
            {
                // The provider broke the contract; treat it as a rejection
                // rather than applying an update of unknown scope.
                return true;
            }

            for (int i = 0; i < results.Count; i++)
            {
                if (!StatusCode.IsGood(results[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Maps a provider exception to the service status returned by CloseAndUpdate.
        /// </summary>
        private static StatusCode GetConfigurationUpdateFailureStatus(Exception exception)
        {
            return exception switch
            {
                ServiceResultException serviceResultException => serviceResultException.StatusCode,
                OperationCanceledException => StatusCodes.BadRequestCancelledByClient,
                _ => StatusCodes.BadInternalError
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

            await m_updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_lock)
                {
                    if (m_stopped)
                    {
                        return new ConfigurationFileConfirmUpdateMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadServerHalted
                        };
                    }
                    if (m_pendingUpdateId.Guid == Guid.Empty ||
                        m_pendingUpdateId.Guid != updateId.Guid)
                    {
                        return new ConfigurationFileConfirmUpdateMethodStateResult
                        {
                            ServiceResult = StatusCodes.BadInvalidArgument
                        };
                    }
                }

                if (m_provider is IRecoverableApplicationConfigurationFileProvider recoverableProvider)
                {
                    await recoverableProvider.ConfirmPendingUpdateAsync(updateId, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await m_provider.ConfirmUpdateAsync(cancellationToken).ConfigureAwait(false);
                }

                lock (m_lock)
                {
                    if (m_pendingUpdateId.Guid == updateId.Guid)
                    {
                        m_pendingUpdateId = Uuid.Empty;
                    }
                }
                CancelPendingRevert();
                RefreshVersionNodes(context);

                m_logger.ConfigurationFileUpdateConfirmed(updateId.Guid);

                return new ConfigurationFileConfirmUpdateMethodStateResult
                {
                    ServiceResult = ServiceResult.Good
                };
            }
            finally
            {
                m_updateGate.Release();
            }
        }

        private ServiceResult? ValidateHandleNoLock(ISystemContext context, uint fileHandle)
        {
            if (!m_handles.TryGetValue(fileHandle, out OpenFileHandle? handle))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Invalid file handle.");
            }

            if (context is ISessionSystemContext session &&
                !handle.SessionId.Equals(session.SessionId))
            {
                return ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Session not authorized for this ConfigurationFile handle.");
            }

            return null;
        }

        /// <summary>
        /// Closes one handle and updates the address-space OpenCount.
        /// </summary>
        private bool CloseOpenHandleNoLock(uint fileHandle)
        {
            if (!m_handles.TryGetValue(fileHandle, out OpenFileHandle? handle))
            {
                return false;
            }

            m_handles.Remove(fileHandle);
            handle.Stream.Dispose();
            if (handle.IsWriting)
            {
                m_writeHandle = 0;
            }
            m_node.OpenCount!.Value = (ushort)m_handles.Count;
            if (m_handles.Count == 0)
            {
                StopActivityTimerNoLock();
            }
            return handle.IsWriting;
        }

        private void DiscardAllOpenHandlesNoLock()
        {
            foreach (OpenFileHandle handle in m_handles.Values)
            {
                handle.Stream.Dispose();
            }
            m_handles.Clear();
            m_writeHandle = 0;
            m_writeOpenPending = false;
            m_node.OpenCount!.Value = 0;
            StopActivityTimerNoLock();
        }

        /// <summary>
        /// Invalidates and disposes the current inactivity timer.
        /// </summary>
        private void StopActivityTimerNoLock()
        {
            m_activityGeneration++;
            m_activityTimer?.Dispose();
            m_activityTimer = null;
        }

        /// <summary>
        /// Discards the open write handle while retaining its transaction
        /// reservation until the update completes.
        /// </summary>
        private void CloseWriteHandle(uint fileHandle)
        {
            lock (m_lock)
            {
                // The update awaited the provider: a newer Open may have
                // opened a different handle in the meantime and must survive.
                if (!m_handles.TryGetValue(fileHandle, out OpenFileHandle? handle) || !handle.IsWriting)
                {
                    return;
                }

                CloseOpenHandleNoLock(fileHandle);
                RestartActivityTimerNoLock();
            }
        }

        private void RestartActivityTimerNoLock()
        {
            if (m_activityTimeout <= 0 || m_handles.Count == 0)
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

        /// <summary>
        /// Reports the ConfigurationUpdatedAuditEventType (OPC 10000-12
        /// §7.8.5.8) for a CloseAndUpdate that reached validation or apply.
        /// </summary>
        private void ReportConfigurationUpdatedAuditEvent(
            ISystemContext context,
            NodeId objectId,
            NodeId methodId,
            ArrayOf<Variant> inputArguments,
            StatusCode statusCode,
            uint oldVersion,
            uint newVersion)
        {
            try
            {
                var e = new ConfigurationUpdatedAuditEventState(null);

                var message = new TranslationInfo(
                    "ConfigurationUpdatedAuditEvent",
                    "en-US",
                    $"ConfigurationUpdatedAuditEvent result is: {statusCode.ToString(null, System.Globalization.CultureInfo.InvariantCulture)}");

                e.Initialize(
                    context,
                    null,
                    EventSeverity.Min,
                    new LocalizedText(message),
                    StatusCode.IsGood(statusCode),
                    DateTime.UtcNow);

                // §7.8.5.8: SourceNode is the Node that owns the configuration
                // (usually the parent of the ConfigurationFile Object) and
                // SourceName is the BrowseName of that owner.
                NodeState? owner = m_node.Parent;
                NodeId sourceNode = owner?.NodeId ?? objectId;
                QualifiedName sourceName = owner?.BrowseName ?? m_node.BrowseName;
                e.SetChildValue(context, BrowseNames.SourceNode, sourceNode, false);
                e.SetChildValue(context, BrowseNames.SourceName, sourceName.Name ?? string.Empty, false);
                e.SetChildValue(context, BrowseNames.LocalTime, TimeZoneDataType.Local, false);
                e.SetChildValue(context, BrowseNames.MethodId, methodId, false);
                e.SetChildValue(context, BrowseNames.InputArguments, inputArguments, false);
                e.SetChildValue(context, BrowseNames.OldVersion, oldVersion, false);
                e.SetChildValue(context, BrowseNames.NewVersion, newVersion, false);

                m_node.ReportEvent(context, e);
            }
            catch (Exception ex)
            {
                m_logger.ErrorWhileReportingConfigurationUpdatedAuditEvent(ex);
            }
        }

        private void RefreshVersionNodes(ISystemContext context)
        {
            m_node.CurrentVersion?.Value = m_provider.CurrentVersion;
            m_node.LastUpdateTime?.Value = new DateTimeUtc(m_provider.LastUpdateTime);
            m_node.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Recovers a durable update before the ConfigurationFile node is served.
        /// </summary>
        /// <param name="context">The server system context used to refresh the node values.</param>
        /// <param name="cancellationToken">A token used to cancel recovery.</param>
        public async ValueTask RecoverPendingUpdateAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            if (m_provider is not IRecoverableApplicationConfigurationFileProvider recoverableProvider)
            {
                return;
            }

            PendingApplicationConfigurationUpdate? pending =
                await recoverableProvider.GetPendingUpdateAsync(cancellationToken).ConfigureAwait(false);
            if (pending is not PendingApplicationConfigurationUpdate update)
            {
                return;
            }

            if (update.UpdateId.Guid == Guid.Empty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The ConfigurationFile provider returned an empty pending update identifier.");
            }

            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            if (update.RevertAtUtc is DateTime revertAtUtc && revertAtUtc <= now)
            {
                await recoverableProvider.RevertPendingUpdateAsync(update.UpdateId, cancellationToken)
                    .ConfigureAwait(false);
                RefreshVersionNodes(context);
                m_logger.ConfigurationFileUpdateNotConfirmedReverted(update.UpdateId.Guid);
                return;
            }

            lock (m_lock)
            {
                m_pendingUpdateId = update.UpdateId;
            }
            ScheduleRevert(update.UpdateId, update.RevertAtUtc, context);

            if (update.IsApplied)
            {
                RefreshVersionNodes(context);
                return;
            }

            if (update.ApplyAtUtc > now)
            {
                bool scheduled = m_backgroundWork.Run("RecoveredDeferredApply", async ct =>
                {
                    try
                    {
                        await m_timeProvider.Delay(update.ApplyAtUtc - m_timeProvider.GetUtcNow().UtcDateTime, ct)
                            .ConfigureAwait(false);
                        await ExecuteDeferredAsync(ApplyRecoveredAsync, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        m_logger.ConfigurationFileUpdateFailedWhileApplying(ex);
                    }

                    async ValueTask ApplyRecoveredAsync()
                    {
                        await m_updateGate.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            try
                            {
                                await recoverableProvider.ApplyPendingUpdateAsync(update.UpdateId, ct)
                                    .ConfigureAwait(false);
                                RefreshVersionNodes(context);
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException)
                            {
                                m_logger.ConfigurationFileUpdateFailedWhileApplying(ex);
                                await RevertPendingUpdateAsync(recoverableProvider, update.UpdateId)
                                    .ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            m_updateGate.Release();
                        }
                    }
                });
                if (!scheduled)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadShutdown,
                        "The ConfigurationFile provider has a pending update, but recovery could not be scheduled.");
                }
                return;
            }

            await recoverableProvider.ApplyPendingUpdateAsync(update.UpdateId, cancellationToken).ConfigureAwait(false);
            RefreshVersionNodes(context);
        }

        private void ScheduleRevert(
            Uuid updateId,
            DateTime? revertAtUtc,
            ISystemContext context)
        {
            CancelPendingRevert();

            CancellationTokenSource? cts = revertAtUtc.HasValue ? new CancellationTokenSource() : null;
            lock (m_lock)
            {
                m_pendingUpdateId = updateId;
                m_pendingRevertCts = cts;
            }

            if (revertAtUtc is not DateTime deadline)
            {
                return;
            }

            CancellationToken revertToken = cts!.Token;
            m_backgroundWork.Run("RevertWindow", async _ =>
            {
                try
                {
                    TimeSpan remaining = deadline - m_timeProvider.GetUtcNow().UtcDateTime;
                    if (remaining > TimeSpan.Zero)
                    {
                        await m_timeProvider.Delay(remaining, revertToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        revertToken.ThrowIfCancellationRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    await ExecuteDeferredAsync(RevertAsync, revertToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (revertToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    m_logger.ConfigurationFileRevertFailed(ex);
                }

                async ValueTask RevertAsync()
                {
                    await m_updateGate.WaitAsync(revertToken).ConfigureAwait(false);
                    try
                    {
                        lock (m_lock)
                        {
                            if (m_pendingUpdateId.Guid != updateId.Guid)
                            {
                                return;
                            }
                        }
                        if (m_provider is IRecoverableApplicationConfigurationFileProvider recoverableProvider)
                        {
                            await recoverableProvider.RevertPendingUpdateAsync(updateId, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await m_provider.RevertUpdateAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        lock (m_lock)
                        {
                            if (m_pendingUpdateId.Guid == updateId.Guid)
                            {
                                m_pendingUpdateId = Uuid.Empty;
                                m_pendingRevertCts = null;
                            }
                        }
                        cts.Dispose();
                        RefreshVersionNodes(context);
                        m_logger.ConfigurationFileUpdateNotConfirmedReverted(updateId.Guid);
                    }
                    finally
                    {
                        m_updateGate.Release();
                    }
                }
            });
        }

        /// <summary>
        /// Rechecks deployment admission after a timer expires, without retaining it during the delay.
        /// </summary>
        private ValueTask ExecuteDeferredAsync(Func<ValueTask> operation, CancellationToken cancellationToken)
        {
            return m_operations == null ? operation() : m_operations.ExecuteAsync(operation, cancellationToken);
        }

        /// <summary>
        /// Reverts a staged update and clears its in-memory revert schedule.
        /// </summary>
        private async ValueTask RevertPendingUpdateAsync(
            IRecoverableApplicationConfigurationFileProvider provider,
            Uuid updateId)
        {
            try
            {
                await provider.RevertPendingUpdateAsync(updateId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.ConfigurationFileRevertFailed(ex);
            }
            finally
            {
                AbandonPendingRevert(updateId);
            }
        }

        /// <summary>
        /// Reverts a pending transaction only when staging retained the supplied identifier.
        /// </summary>
        private async ValueTask RevertPendingUpdateIfCreatedAsync(
            IRecoverableApplicationConfigurationFileProvider provider,
            Uuid updateId)
        {
            try
            {
                PendingApplicationConfigurationUpdate? pending =
                    await provider.GetPendingUpdateAsync(CancellationToken.None).ConfigureAwait(false);
                if (pending is PendingApplicationConfigurationUpdate existing &&
                    existing.UpdateId.Guid == updateId.Guid)
                {
                    await RevertPendingUpdateAsync(provider, updateId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                m_logger.ConfigurationFileRevertFailed(ex);
            }
        }

        /// <summary>
        /// Calculates the UTC deadline for a confirmation-required update.
        /// </summary>
        private static DateTime? GetRevertAtUtc(DateTime applyAtUtc, double revertAfterTime)
        {
            return revertAfterTime > 0
                ? applyAtUtc.AddMilliseconds(revertAfterTime)
                : null;
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

        /// <summary>
        /// Creates an unsuccessful Open result without publishing a file handle.
        /// </summary>
        private static OpenMethodStateResult FailedOpen(StatusCode statusCode)
        {
            return new OpenMethodStateResult
            {
                ServiceResult = statusCode,
                FileHandle = 0
            };
        }

        private static ArrayOf<StatusCode> SuccessResults(ArrayOf<ConfigurationUpdateTargetType> targets)
        {
            if (targets.Count == 0)
            {
                return [];
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
                return [];
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
        /// Allocates a nonzero file handle not currently in use.
        /// </summary>
        private uint NextFileHandleNoLock()
        {
            do
            {
                m_nextFileHandle++;
            }
            while (m_nextFileHandle == 0 || m_handles.ContainsKey(m_nextFileHandle));

            return m_nextFileHandle;
        }

        /// <summary>
        /// Gets the minimum effective server, file, and ConfigurationFile transfer limit.
        /// </summary>
        private int GetMaxByteStringLength(ISystemContext context)
        {
            int limit = Math.Min(
                FileObjectState.GetServerReadLimit(context),
                checked((int)kMaxConfigurationFileSize));
            uint fileLimit = m_node.MaxByteStringLength?.Value ?? 0;
            if (fileLimit > 0)
            {
                limit = Math.Min(limit, fileLimit > int.MaxValue ? int.MaxValue : (int)fileLimit);
            }
            return limit;
        }

        /// <summary>
        /// Captures session ownership until an admitted provider read publishes or abandons its handle.
        /// </summary>
        private sealed class PendingOpen(NodeId sessionId)
        {
            /// <summary>
            /// Gets the session that owns the admitted open.
            /// </summary>
            public NodeId SessionId { get; } = sessionId;

            /// <summary>
            /// Gets or sets whether that session closed before handle publication.
            /// </summary>
            public bool SessionClosed { get; set; }
        }

        /// <summary>
        /// Holds one Session's stream and access mode for an open file handle.
        /// </summary>
        private sealed class OpenFileHandle
        {
            /// <summary>
            /// Initializes an open handle.
            /// </summary>
            /// <param name="sessionId">The owning Session.</param>
            /// <param name="stream">The stream used for file operations.</param>
            /// <param name="isWriting">Whether the handle is open for writing.</param>
            public OpenFileHandle(NodeId sessionId, MemoryStream stream, bool isWriting)
            {
                SessionId = sessionId;
                Stream = stream;
                IsWriting = isWriting;
            }

            /// <summary>
            /// Gets the owning Session identifier.
            /// </summary>
            public NodeId SessionId { get; }

            /// <summary>
            /// Gets the stream associated with the file handle.
            /// </summary>
            public MemoryStream Stream { get; }

            /// <summary>
            /// Gets whether the handle is open for writing.
            /// </summary>
            public bool IsWriting { get; }
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

        /// <summary>The owning deployment's admission for timer-triggered provider effects.</summary>
        private readonly IApplicationConfigurationPushOperations? m_operations;
        private readonly SecureAccess m_readAccess;
        private readonly SecureAccess m_writeAccess;
        private readonly ILogger m_logger;
        private readonly BackgroundTaskScope m_backgroundWork;

        /// <summary>
        /// Serializes configuration apply, confirmation and reversion operations.
        /// </summary>
        private readonly SemaphoreSlim m_updateGate = new(1, 1);
        private readonly IPushConfigurationTransactionCoordinator? m_coordinator;
        private readonly TimeProvider m_timeProvider;
        private readonly double m_activityTimeout;
        private readonly Dictionary<uint, OpenFileHandle> m_handles = [];

        /// <summary>The next candidate handle identifier.</summary>
        private uint m_nextFileHandle;

        /// <summary>The active write handle, or zero when no writer is open.</summary>
        private uint m_writeHandle;

        /// <summary>Whether a write open is waiting for its provider read.</summary>
        private bool m_writeOpenPending;

        /// <summary>
        /// Session ownership for provider reads that have not finished handle admission.
        /// </summary>
        private readonly HashSet<PendingOpen> m_openReservations = [];

        /// <summary>
        /// Admitted readers whose provider content has not yet produced an open handle.
        /// </summary>
        private int m_pendingReaders;

        /// <summary>
        /// Admitted provider reads that shutdown must drain before runtime release.
        /// </summary>
        private int m_pendingOpens;

        /// <summary>
        /// Completion of the current admitted-open group.
        /// </summary>
        private TaskCompletionSource<bool>? m_openDrain;

        /// <summary>
        /// Whether shutdown or disposal has closed new handle admission.
        /// </summary>
        private bool m_stopped;

        /// <summary>Whether a ConfigurationFile update owns the writer reservation.</summary>
        private bool m_updateInProgress;
        private ITimer? m_activityTimer;
        private long m_activityGeneration;
        private Uuid m_pendingUpdateId;
        private CancellationTokenSource? m_pendingRevertCts;
        private bool m_deferredApplyPending;
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

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 7, Level = LogLevel.Error,
            Message = "Error while reporting the ConfigurationUpdatedAuditEvent.")]
        public static partial void ErrorWhileReportingConfigurationUpdatedAuditEvent(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = ServerEventIds.ApplicationConfigurationFile + 8, Level = LogLevel.Information,
            Message = "ConfigurationFile update interrupts active Sessions; applying after the RestartDelayTime of {Delay} ms.")]
        public static partial void ConfigurationFileApplyDeferred(this ILogger logger, double delay);
    }
}
