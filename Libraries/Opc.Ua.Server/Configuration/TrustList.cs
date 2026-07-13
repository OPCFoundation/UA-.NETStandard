/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The implementation of a server trustlist.
    /// </summary>
    public class TrustList
    {
        private const int kDefaultTrustListCapacity = 1 * 1024 * 1024;

        /// <summary>
        /// The default resource-protection safety ceiling (1&#160;MiB) used to
        /// bound the actually-enforced TrustList size when no explicit ceiling
        /// is supplied and the advertised <c>MaxTrustListSize</c> is 0
        /// (unlimited per OPC 10000-12 §8.4.5) or exceeds the ceiling. See
        /// <see cref="ComputeEffectiveMaxTrustListSize"/>.
        /// </summary>
        internal const int DefaultMaxTrustListSizeSafetyCeiling = 1 * 1024 * 1024;

        /// <summary>
        /// Initializes the trustlist for non-transactional (legacy,
        /// immediate-apply) hosting.
        /// </summary>
        /// <remarks>
        /// This legacy overload preserves the historical enforcement exactly: a
        /// configured finite <paramref name="maxTrustListSize"/> is honored
        /// as-is (never clamped) and a value of 0 falls back to the
        /// <see cref="DefaultMaxTrustListSizeSafetyCeiling"/>. To bound an
        /// unlimited or oversized advertised limit with a separate
        /// resource-protection ceiling, use the overload that takes an explicit
        /// <c>maxTrustListSizeSafetyCeiling</c>.
        /// </remarks>
        public TrustList(
            TrustListState node,
            CertificateStoreIdentifier trustedListStore,
            CertificateStoreIdentifier issuerListStore,
            SecureAccess readAccess,
            SecureAccess writeAccess,
            ITelemetryContext telemetry,
            int maxTrustListSize = 0)
            : this(
                node,
                trustedListStore,
                issuerListStore,
                readAccess,
                writeAccess,
                telemetry,
                coordinator: null,
                maxTrustListSize)
        {
        }

        /// <summary>
        /// Initializes the trustlist so its <c>CloseAndUpdate</c>,
        /// <c>AddCertificate</c>, and <c>RemoveCertificate</c> handlers
        /// stage their store mutations through the shared PushManagement
        /// transaction <paramref name="coordinator"/> (OPC 10000-12
        /// §§7.10.2-7.10.11) instead of applying them immediately. The
        /// existing constructor remains available for non-transactional
        /// (legacy, immediate-apply) hosting.
        /// </summary>
        /// <remarks>
        /// This legacy overload preserves the historical enforcement exactly
        /// (see the non-transactional overload). Use the overload that takes an
        /// explicit <c>maxTrustListSizeSafetyCeiling</c> to bound an unlimited
        /// or oversized advertised limit for resource protection.
        /// </remarks>
        public TrustList(
            TrustListState node,
            CertificateStoreIdentifier trustedListStore,
            CertificateStoreIdentifier issuerListStore,
            SecureAccess readAccess,
            SecureAccess writeAccess,
            ITelemetryContext telemetry,
            IPushConfigurationTransactionCoordinator? coordinator,
            int maxTrustListSize = 0)
            : this(
                node,
                trustedListStore,
                issuerListStore,
                readAccess,
                writeAccess,
                telemetry,
                coordinator,
                maxTrustListSize,
                maxTrustListSize > 0 ? maxTrustListSize : DefaultMaxTrustListSizeSafetyCeiling)
        {
        }

        /// <summary>
        /// Initializes the trustlist with an explicit OPC 10000-12 §8.4.5
        /// advertised <paramref name="maxTrustListSize"/> (0 = unlimited) and a
        /// separate resource-protection
        /// <paramref name="maxTrustListSizeSafetyCeiling"/>. The
        /// actually-enforced limit is
        /// <see cref="ComputeEffectiveMaxTrustListSize"/> of the two, so an
        /// unlimited or oversized advertised value is bounded honestly by the
        /// ceiling instead of a hidden default.
        /// </summary>
        public TrustList(
            TrustListState node,
            CertificateStoreIdentifier trustedListStore,
            CertificateStoreIdentifier issuerListStore,
            SecureAccess readAccess,
            SecureAccess writeAccess,
            ITelemetryContext telemetry,
            IPushConfigurationTransactionCoordinator? coordinator,
            int maxTrustListSize,
            int maxTrustListSizeSafetyCeiling)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<TrustList>();
            m_node = node;
            m_trustedStore = trustedListStore;
            m_issuerStore = issuerListStore;
            m_readAccess = readAccess;
            m_writeAccess = writeAccess;
            m_coordinator = coordinator;
            m_effectiveMaxTrustListSize = ComputeEffectiveMaxTrustListSize(
                maxTrustListSize,
                maxTrustListSizeSafetyCeiling);

            // Register both sync and async handlers per MethodState. The async path is
            // preferred (and is what the in-tree containers — ConfigurationNodeManager
            // and ApplicationsNodeManager — dispatch through). The sync OnCall handlers
            // are compat shims for legacy CustomNodeManager2 subclasses that host this
            // TrustList but do not implement ICallAsyncNodeManager; those subclasses
            // dispatch through MethodState.Call (sync) which would otherwise return
            // BadNotImplemented because OnCallAsync is not consulted on the sync path.
            node.Open!.OnCall = new OpenMethodStateMethodCallHandler(Open);
            node.Open.OnCallAsync = new OpenMethodStateMethodAsyncCallHandler(OpenAsync);
            node.OpenWithMasks!.OnCall
                = new OpenWithMasksMethodStateMethodCallHandler(OpenWithMasks);
            node.OpenWithMasks.OnCallAsync
                = new OpenWithMasksMethodStateMethodAsyncCallHandler(OpenWithMasksAsync);
            node.Read!.OnCall = new ReadMethodStateMethodCallHandler(Read);
            node.Read.OnCallAsync = new ReadMethodStateMethodAsyncCallHandler(ReadAsync);
            node.Write!.OnCall = new WriteMethodStateMethodCallHandler(Write);
            node.Write.OnCallAsync = new WriteMethodStateMethodAsyncCallHandler(WriteAsync);
            node.Close!.OnCall = new CloseMethodStateMethodCallHandler(Close);
            node.Close.OnCallAsync = new CloseMethodStateMethodAsyncCallHandler(CloseAsync);
            node.CloseAndUpdate!.OnCall
                = new CloseAndUpdateMethodStateMethodCallHandler(CloseAndUpdate);
            node.CloseAndUpdate.OnCallAsync
                = new CloseAndUpdateMethodStateMethodAsyncCallHandler(CloseAndUpdateAsync);
            node.AddCertificate!.OnCall
                = new AddCertificateMethodStateMethodCallHandler(AddCertificate);
            node.AddCertificate.OnCallAsync
                = new AddCertificateMethodStateMethodAsyncCallHandler(AddCertificateAsync);
            node.RemoveCertificate!.OnCall
                = new RemoveCertificateMethodStateMethodCallHandler(RemoveCertificate);
            node.RemoveCertificate.OnCallAsync
                = new RemoveCertificateMethodStateMethodAsyncCallHandler(RemoveCertificateAsync);
        }

        /// <summary>
        /// The effective, actually-enforced maximum TrustList size in bytes.
        /// Always a positive, finite value. This is the honest limit the server
        /// advertises through <c>ServerConfiguration.MaxTrustListSize</c> and
        /// enforces on Read/Write/CloseAndUpdate/AddCertificate.
        /// </summary>
        internal int EffectiveMaxTrustListSize => m_effectiveMaxTrustListSize;

        /// <summary>
        /// Computes the effective, actually-enforced maximum TrustList size (in
        /// bytes) from the OPC 10000-12 §8.4.5 advertised
        /// <paramref name="maxTrustListSize"/> (0 = "no protocol limit" /
        /// unlimited) and the resource-protection
        /// <paramref name="maxTrustListSizeSafetyCeiling"/>. The result is
        /// always a positive, finite value so the server never silently
        /// advertises "unlimited" while enforcing a hidden cap:
        /// <list type="bullet">
        /// <item>advertised max 0 (unlimited) → the safety ceiling;</item>
        /// <item>advertised max above the safety ceiling → the safety ceiling;</item>
        /// <item>finite advertised max at or below the ceiling → the advertised max.</item>
        /// </list>
        /// A non-positive <paramref name="maxTrustListSizeSafetyCeiling"/> falls
        /// back to <see cref="DefaultMaxTrustListSizeSafetyCeiling"/>.
        /// </summary>
        /// <param name="maxTrustListSize">
        /// The advertised <c>MaxTrustListSize</c> (0 = unlimited).
        /// </param>
        /// <param name="maxTrustListSizeSafetyCeiling">
        /// The resource-protection hard cap in bytes.
        /// </param>
        internal static int ComputeEffectiveMaxTrustListSize(
            int maxTrustListSize,
            int maxTrustListSizeSafetyCeiling)
        {
            int safetyCeiling = maxTrustListSizeSafetyCeiling > 0
                ? maxTrustListSizeSafetyCeiling
                : DefaultMaxTrustListSizeSafetyCeiling;

            // OPC 10000-12 §8.4.5: MaxTrustListSize == 0 means "no protocol
            // limit" (unlimited). The server still bounds actual enforcement by
            // the resource-protection safety ceiling.
            if (maxTrustListSize <= 0)
            {
                return safetyCeiling;
            }

            // Finite advertised max: honor it, but never above the ceiling.
            return Math.Min(maxTrustListSize, safetyCeiling);
        }

        /// <summary>
        /// Delegate to validate the access to the trust list.
        /// </summary>
        /// <param name="context">System context</param>
        /// <param name="trustedStore">the path to identify the trustList</param>
        public delegate void SecureAccess(
            ISystemContext context,
            CertificateStoreIdentifier trustedStore);

        /// <summary>
        /// Closes this TrustList's open read/write handle if it is
        /// currently owned by <paramref name="sessionId"/>. Called by
        /// <see cref="ConfigurationNodeManager.SessionClosingAsync"/> so an
        /// abandoned Session does not leave the TrustList permanently
        /// open for writing.
        /// </summary>
        internal void NotifySessionClosing(NodeId sessionId)
        {
            lock (m_lock)
            {
                if (m_sessionId.IsNull || !Utils.IsEqual(m_sessionId, sessionId))
                {
                    return;
                }

                m_sessionId = default;
                m_strm?.Dispose();
                m_strm = null;
                m_node.OpenCount!.Value = 0;
            }

            m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
        }

        /// <summary>
        /// Extracts the owning Session's NodeId from <paramref name="context"/>,
        /// or <see cref="NodeId.Null"/> when the context is not
        /// Session-bound.
        /// </summary>
        private static NodeId GetSessionId(ISystemContext context)
        {
            return (context as ISessionSystemContext)?.SessionId ?? NodeId.Null;
        }

        private ServiceResult Open(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            ref uint fileHandle)
        {
            OpenMethodStateResult result = OpenAsync(
                context,
                method,
                objectId,
                mode,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            fileHandle = result.FileHandle;
            return result.ServiceResult;
        }

        private ValueTask<OpenMethodStateResult> OpenAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            byte mode,
            CancellationToken cancellationToken)
        {
            return OpenCoreAsync(
                context,
                method,
                objectId,
                (OpenFileMode)mode,
                TrustListMasks.All,
                cancellationToken);
        }

        private ServiceResult OpenWithMasks(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint masks,
            ref uint fileHandle)
        {
            OpenWithMasksMethodStateResult result = OpenWithMasksAsync(
                context,
                method,
                objectId,
                masks,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            fileHandle = result.FileHandle;
            return result.ServiceResult;
        }

        private async ValueTask<OpenWithMasksMethodStateResult> OpenWithMasksAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint masks,
            CancellationToken cancellationToken)
        {
            OpenMethodStateResult result = await OpenCoreAsync(
                context,
                method,
                objectId,
                OpenFileMode.Read,
                (TrustListMasks)masks,
                cancellationToken).ConfigureAwait(false);

            return new OpenWithMasksMethodStateResult
            {
                ServiceResult = result.ServiceResult,
                FileHandle = result.FileHandle
            };
        }

        private async ValueTask<OpenMethodStateResult> OpenCoreAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            OpenFileMode mode,
            TrustListMasks masks,
            CancellationToken cancellationToken)
        {
            bool isWriteMode;
            if (mode == OpenFileMode.Read)
            {
                HasSecureReadAccess(context);
                isWriteMode = false;
            }
            else if ((int)mode == ((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting))
            {
                HasSecureWriteAccess(context);
                // Per OPC UA Part 12 §7.10.2, opening a TrustList for
                // writing participates in owner-Session validation: a
                // different Session's active transaction rejects this
                // Open with BadTransactionPending.
                m_coordinator?.ValidateSessionCanParticipate(GetSessionId(context));
                isWriteMode = true;
            }
            else
            {
                return new OpenMethodStateResult
                {
                    ServiceResult = StatusCodes.BadNotWritable,
                    FileHandle = 0
                };
            }

            uint fileHandle = 0;
            MemoryStream? strm = null;

            try
            {
                var trustList = new TrustListDataType { SpecifiedLists = (uint)masks };

                ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open trusted certificate store.");
                    }

                    if (((int)masks & (int)TrustListMasks.TrustedCertificates) != 0)
                    {
                        using CertificateCollection certificates = await store.EnumerateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        trustList.TrustedCertificates = trustList.TrustedCertificates.AddItems(
                            certificates
                                .Select(certificate => certificate.RawData.ToByteString()));
                    }

                    if (((int)masks & (int)TrustListMasks.TrustedCrls) != 0)
                    {
                        X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        trustList.TrustedCrls = trustList.TrustedCrls.AddItems(
                             crls.Select(crl => crl.RawData.ToByteString()));
                    }
                }
                finally
                {
                    store.Dispose();
                }

                store = m_issuerStore.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open issuer certificate store.");
                    }

                    if (((int)masks & (int)TrustListMasks.IssuerCertificates) != 0)
                    {
                        using CertificateCollection certificates = await store.EnumerateAsync(cancellationToken)
                            .ConfigureAwait(false);
                        trustList.IssuerCertificates = trustList.IssuerCertificates.AddItems(certificates
                            .Select(certificate => certificate.RawData.ToByteString()));
                    }

                    if (((int)masks & (int)TrustListMasks.IssuerCrls) != 0)
                    {
                        X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        trustList.IssuerCrls = trustList.IssuerCrls.AddItems(crls
                            .Select(crl => crl.RawData.ToByteString()));
                    }
                }
                finally
                {
                    store.Dispose();
                }

                if (mode == OpenFileMode.Read)
                {
                    strm = EncodeTrustListData(context, trustList);
                }
                else
                {
                    // Pre-size the write buffer, but never above the effective
                    // limit (the Write handler rejects anything beyond it) nor
                    // above the historical 1 MiB hint, so a large safety ceiling
                    // does not translate into a large up-front allocation.
                    strm = new MemoryStream(
                        Math.Min(m_effectiveMaxTrustListSize, kDefaultTrustListCapacity));
                }

                lock (m_lock)
                {
                    if (!m_sessionId.IsNull)
                    {
                        // to avoid deadlocks, last open always wins
                        m_sessionId = default;
                        m_strm?.Dispose();
                        m_strm = null;
                        m_node.OpenCount!.Value = 0;
                    }

                    m_sessionId = (context as ISessionSystemContext)?.SessionId ?? default;
                    fileHandle = ++m_fileHandle;
                    m_totalBytesProcessed = 0; // Reset counter for new file operation
                    m_strm = strm;
                    m_node.OpenCount!.Value = 1;
                }

                // Cleared unconditionally (idempotent) before being set so
                // an evicted previous open never leaves a stale "open for
                // writing" entry behind.
                m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                if (isWriteMode)
                {
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, true);
                }
            }
            catch
            {
                strm?.Dispose();
                throw;
            }

            return new OpenMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                FileHandle = fileHandle
            };
        }

        private ServiceResult Read(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            ref ByteString data)
        {
            ReadMethodStateResult result = ReadAsync(
                context,
                method,
                objectId,
                fileHandle,
                length,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            data = result.Data;
            return result.ServiceResult;
        }

        private ValueTask<ReadMethodStateResult> ReadAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            int length,
            CancellationToken cancellationToken)
        {
            HasSecureReadAccess(context);

            ByteString data;

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != null! &&
                    !m_sessionId.Equals(session.SessionId))
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadUserAccessDenied,
                            "Session not authorized"),
                        Data = default
                    });
                }

                if (m_fileHandle != fileHandle)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "Invalid file handle"),
                        Data = default
                    });
                }

                // Reject a negative requested length before any allocation
                // (a client-supplied value flows straight into the buffer
                // size below).
                if (length < 0)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "Invalid length"),
                        Data = default
                    });
                }

                // Overflow-safe cumulative bound: m_totalBytesProcessed is a
                // long, so promoting the int length keeps the addition in long
                // range and cannot wrap. Enforced against the effective,
                // actually-advertised limit.
                if (m_totalBytesProcessed + length > m_effectiveMaxTrustListSize)
                {
                    return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Trust list size exceeds maximum allowed size of {0} bytes",
                            m_effectiveMaxTrustListSize),
                        Data = default
                    });
                }

                byte[] buffer = new byte[length];
                int bytesRead = m_strm!.Read(buffer, 0, length);
                Debug.Assert(bytesRead >= 0);
                data = ByteString.From(buffer)[..bytesRead];

                m_totalBytesProcessed += bytesRead;
            }

            return new ValueTask<ReadMethodStateResult>(new ReadMethodStateResult
            {
                ServiceResult = ServiceResult.Good,
                Data = data
            });
        }

        private ServiceResult Write(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ByteString data)
        {
            WriteMethodStateResult result = WriteAsync(
                context,
                method,
                objectId,
                fileHandle,
                data,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private ValueTask<WriteMethodStateResult> WriteAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ByteString data,
            CancellationToken cancellationToken)
        {
            HasSecureWriteAccess(context);

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != null! &&
                    !m_sessionId.Equals(session.SessionId))
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadUserAccessDenied
                    });
                }

                if (m_fileHandle != fileHandle)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
                }

                // Overflow-safe cumulative bound: m_totalBytesProcessed is a
                // long, so promoting the int data.Length keeps the addition in
                // long range and cannot wrap. Enforced against the effective,
                // actually-advertised limit before the payload is buffered.
                if (m_totalBytesProcessed + data.Length > m_effectiveMaxTrustListSize)
                {
                    return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "Trust list size exceeds maximum allowed size of {0} bytes",
                            m_effectiveMaxTrustListSize)
                    });
                }

                m_strm!.Write(data.ToArray(), 0, data.Length);
                m_totalBytesProcessed += data.Length;
            }

            return new ValueTask<WriteMethodStateResult>(new WriteMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ServiceResult Close(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle)
        {
            CloseMethodStateResult result = CloseAsync(
                context,
                method,
                objectId,
                fileHandle,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private ValueTask<CloseMethodStateResult> CloseAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            HasSecureReadAccess(context);

            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != null! &&
                    !m_sessionId.Equals(session.SessionId))
                {
                    return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadUserAccessDenied
                    });
                }

                if (m_fileHandle != fileHandle)
                {
                    return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument
                    });
                }

                m_sessionId = default;
                m_strm?.Dispose();
                m_strm = null;
                m_node.OpenCount!.Value = 0;
            }

            m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);

            return new ValueTask<CloseMethodStateResult>(new CloseMethodStateResult
            {
                ServiceResult = ServiceResult.Good
            });
        }

        private ServiceResult CloseAndUpdate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            ref bool restartRequired)
        {
            CloseAndUpdateMethodStateResult result = CloseAndUpdateAsync(
                context,
                method,
                objectId,
                fileHandle,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            restartRequired = result.ApplyChangesRequired;
            return result.ServiceResult;
        }

        private async ValueTask<CloseAndUpdateMethodStateResult> CloseAndUpdateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint fileHandle,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> inputParameters = [fileHandle];
            m_node.ReportTrustListUpdateRequestedAuditEvent(
                context,
                objectId,
                "Method/CloseAndUpdate",
                method.NodeId,
                inputParameters,
                m_logger);
            HasSecureWriteAccess(context);

            NodeId sessionId = GetSessionId(context);
            if (m_coordinator != null)
            {
                try
                {
                    m_coordinator.ValidateSessionCanParticipate(sessionId);
                }
                catch (ServiceResultException ex)
                {
                    m_node.ReportTrustListUpdatedAuditEvent(
                        context, objectId, "Method/CloseAndUpdate", method.NodeId, inputParameters,
                        ex.StatusCode, m_logger);
                    return new CloseAndUpdateMethodStateResult
                    {
                        ServiceResult = ex.StatusCode,
                        ApplyChangesRequired = false
                    };
                }
            }

            MemoryStream? strm;
            lock (m_lock)
            {
                if (context is ISessionSystemContext session &&
                    m_sessionId != null! &&
                    !m_sessionId.Equals(session.SessionId))
                {
                    return new CloseAndUpdateMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadUserAccessDenied,
                        ApplyChangesRequired = false
                    };
                }

                if (m_fileHandle != fileHandle)
                {
                    return new CloseAndUpdateMethodStateResult
                    {
                        ServiceResult = StatusCodes.BadInvalidArgument,
                        ApplyChangesRequired = false
                    };
                }

                strm = m_strm;
            }

            ServiceResult result = StatusCodes.Good;
            CertificateCollection? issuerCertificates = null;
            CertificateCollection? trustedCertificates = null;
            X509CRLCollection? issuerCrls = null;
            X509CRLCollection? trustedCrls = null;
            int masks = (int)TrustListMasks.None;
            try
            {
                TrustListDataType trustList = DecodeTrustListData(
                    context, strm!, m_effectiveMaxTrustListSize);
                masks = (int)trustList.SpecifiedLists;

                // test integrity of all CRLs
                if ((masks & (int)TrustListMasks.IssuerCertificates) != 0)
                {
                    issuerCertificates = [];
                    foreach (ByteString cert in trustList.IssuerCertificates)
                    {
                        using var certificate = Certificate.FromRawData(cert);
                        issuerCertificates.Add(certificate);
                    }
                }
                if ((masks & (int)TrustListMasks.IssuerCrls) != 0)
                {
                    issuerCrls = [];
                    foreach (ByteString crl in trustList.IssuerCrls)
                    {
                        issuerCrls.Add(new X509CRL(crl.ToArray()));
                    }
                }
                if ((masks & (int)TrustListMasks.TrustedCertificates) != 0)
                {
                    trustedCertificates = [];
                    foreach (ByteString cert in trustList.TrustedCertificates)
                    {
                        using var certificate = Certificate.FromRawData(cert);
                        trustedCertificates.Add(certificate);
                    }
                }
                if ((masks & (int)TrustListMasks.TrustedCrls) != 0)
                {
                    trustedCrls = [];
                    foreach (ByteString crl in trustList.TrustedCrls)
                    {
                        trustedCrls.Add(new X509CRL(crl.ToArray()));
                    }
                }
            }
            catch
            {
                result = StatusCodes.BadCertificateInvalid;
            }

            if (!ServiceResult.IsGood(result) || m_coordinator == null)
            {
                // Decode/validation failure, or legacy non-transactional
                // hosting: apply immediately (or report the failure), as
                // before.
                try
                {
                    if (ServiceResult.IsGood(result))
                    {
                        int updateMasks = (int)TrustListMasks.None;
                        if ((masks & (int)TrustListMasks.IssuerCertificates) != 0 &&
                            await UpdateStoreCertificatesAsync(m_issuerStore, issuerCertificates!, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            updateMasks |= (int)TrustListMasks.IssuerCertificates;
                        }
                        if ((masks & (int)TrustListMasks.IssuerCrls) != 0 &&
                            await UpdateStoreCrlsAsync(m_issuerStore, issuerCrls!, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            updateMasks |= (int)TrustListMasks.IssuerCrls;
                        }
                        if ((masks & (int)TrustListMasks.TrustedCertificates) != 0 &&
                            await UpdateStoreCertificatesAsync(m_trustedStore, trustedCertificates!, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            updateMasks |= (int)TrustListMasks.TrustedCertificates;
                        }
                        if ((masks & (int)TrustListMasks.TrustedCrls) != 0 &&
                            await UpdateStoreCrlsAsync(m_trustedStore, trustedCrls!, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            updateMasks |= (int)TrustListMasks.TrustedCrls;
                        }

                        if (masks != updateMasks)
                        {
                            result = StatusCodes.BadCertificateInvalid;
                        }
                    }
                }
                catch
                {
                    result = StatusCodes.BadCertificateInvalid;
                }
                finally
                {
                    issuerCertificates?.Dispose();
                    trustedCertificates?.Dispose();

                    lock (m_lock)
                    {
                        m_sessionId = default;
                        m_strm?.Dispose();
                        m_strm = null;
                        m_node.LastUpdateTime!.Value = DateTime.UtcNow;
                        m_node.OpenCount!.Value = 0;
                    }
                    m_coordinator?.SetTrustListWriteOpen(m_node.NodeId, false);
                }

                m_node.ReportTrustListUpdatedAuditEvent(
                    context,
                    objectId,
                    "Method/CloseAndUpdate",
                    method.NodeId,
                    inputParameters,
                    result.StatusCode,
                    m_logger);

                return new CloseAndUpdateMethodStateResult
                {
                    ServiceResult = result,
                    ApplyChangesRequired = false
                };
            }

            // Transactional: snapshot the pre-transaction store contents
            // for rollback, then stage the decoded payload instead of
            // applying it now.
            CertificateCollection? originalIssuerCertificates = null;
            CertificateCollection? originalTrustedCertificates = null;
            X509CRLCollection? originalIssuerCrls = null;
            X509CRLCollection? originalTrustedCrls = null;
            try
            {
                if (issuerCertificates != null)
                {
                    using ICertificateStore store = m_issuerStore.OpenStore(m_telemetry);
                    originalIssuerCertificates = await store.EnumerateAsync(cancellationToken).ConfigureAwait(false);
                }
                if (issuerCrls != null)
                {
                    using ICertificateStore store = m_issuerStore.OpenStore(m_telemetry);
                    originalIssuerCrls = await store.EnumerateCRLsAsync(cancellationToken).ConfigureAwait(false);
                }
                if (trustedCertificates != null)
                {
                    using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
                    originalTrustedCertificates = await store.EnumerateAsync(cancellationToken).ConfigureAwait(false);
                }
                if (trustedCrls != null)
                {
                    using ICertificateStore store = m_trustedStore.OpenStore(m_telemetry);
                    originalTrustedCrls = await store.EnumerateCRLsAsync(cancellationToken).ConfigureAwait(false);
                }

                NodeId trustListId = m_node.NodeId;
                CertificateCollection? stagedIssuerCertificates = issuerCertificates;
                CertificateCollection? stagedTrustedCertificates = trustedCertificates;
                X509CRLCollection? stagedIssuerCrls = issuerCrls;
                X509CRLCollection? stagedTrustedCrls = trustedCrls;
                CertificateCollection? stagedOriginalIssuerCertificates = originalIssuerCertificates;
                CertificateCollection? stagedOriginalTrustedCertificates = originalTrustedCertificates;
                X509CRLCollection? stagedOriginalIssuerCrls = originalIssuerCrls;
                X509CRLCollection? stagedOriginalTrustedCrls = originalTrustedCrls;

                // Shared by RollbackAsync AND, self-compensating, by a
                // partially applied CommitAsync below: the coordinator only
                // compensates operations that commit in full (see
                // PushConfigurationTransactionCoordinator.ApplyChangesAsync),
                // so an operation that fails after already updating some of
                // the (up to four) issuer/trusted certificate-or-CRL stores
                // must restore the pre-transaction snapshot itself.
                async Task RestoreOriginalTrustListAsync(CancellationToken ct)
                {
                    if (stagedOriginalIssuerCertificates != null)
                    {
                        await UpdateStoreCertificatesAsync(m_issuerStore, stagedOriginalIssuerCertificates, ct)
                            .ConfigureAwait(false);
                    }
                    if (stagedOriginalIssuerCrls != null)
                    {
                        await UpdateStoreCrlsAsync(m_issuerStore, stagedOriginalIssuerCrls, ct)
                            .ConfigureAwait(false);
                    }
                    if (stagedOriginalTrustedCertificates != null)
                    {
                        await UpdateStoreCertificatesAsync(m_trustedStore, stagedOriginalTrustedCertificates, ct)
                            .ConfigureAwait(false);
                    }
                    if (stagedOriginalTrustedCrls != null)
                    {
                        await UpdateStoreCrlsAsync(m_trustedStore, stagedOriginalTrustedCrls, ct)
                            .ConfigureAwait(false);
                    }
                }

                m_coordinator.Stage(sessionId, new PushConfigurationOperation
                {
                    AffectedTrustList = trustListId,
                    CommitAsync = async ct =>
                    {
                        bool ok = true;
                        try
                        {
                            if (stagedIssuerCertificates != null)
                            {
                                ok &= await UpdateStoreCertificatesAsync(m_issuerStore, stagedIssuerCertificates, ct)
                                    .ConfigureAwait(false);
                            }
                            if (stagedIssuerCrls != null)
                            {
                                ok &= await UpdateStoreCrlsAsync(m_issuerStore, stagedIssuerCrls, ct)
                                    .ConfigureAwait(false);
                            }
                            if (stagedTrustedCertificates != null)
                            {
                                ok &= await UpdateStoreCertificatesAsync(m_trustedStore, stagedTrustedCertificates, ct)
                                    .ConfigureAwait(false);
                            }
                            if (stagedTrustedCrls != null)
                            {
                                ok &= await UpdateStoreCrlsAsync(m_trustedStore, stagedTrustedCrls, ct)
                                    .ConfigureAwait(false);
                            }

                            if (!ok)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadCertificateInvalid,
                                    "Failed to apply the updated TrustList to one or more stores.");
                            }
                        }
                        catch
                        {
                            await RestoreOriginalTrustListAsync(ct).ConfigureAwait(false);
                            throw;
                        }

                        lock (m_lock)
                        {
                            m_node.LastUpdateTime!.Value = DateTime.UtcNow;
                        }

                        m_node.ReportTrustListUpdatedAuditEvent(
                            context, objectId, "Method/CloseAndUpdate", method.NodeId, inputParameters,
                            StatusCodes.Good, m_logger);
                    },
                    RollbackAsync = RestoreOriginalTrustListAsync,
                    DisposeStaged = () =>
                    {
                        stagedIssuerCertificates?.Dispose();
                        stagedTrustedCertificates?.Dispose();
                        stagedOriginalIssuerCertificates?.Dispose();
                        stagedOriginalTrustedCertificates?.Dispose();
                    }
                });

                issuerCertificates = null;
                trustedCertificates = null;
                originalIssuerCertificates = null;
                originalTrustedCertificates = null;
            }
            finally
            {
                issuerCertificates?.Dispose();
                trustedCertificates?.Dispose();
                originalIssuerCertificates?.Dispose();
                originalTrustedCertificates?.Dispose();

                lock (m_lock)
                {
                    m_sessionId = default;
                    m_strm?.Dispose();
                    m_strm = null;
                    m_node.OpenCount!.Value = 0;
                }
                m_coordinator.SetTrustListWriteOpen(m_node.NodeId, false);
            }

            return new CloseAndUpdateMethodStateResult
            {
                ServiceResult = StatusCodes.Good,
                ApplyChangesRequired = true
            };
        }

        private ServiceResult AddCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ByteString certificate,
            bool isTrustedCertificate)
        {
            AddCertificateMethodStateResult result = AddCertificateAsync(
                context,
                method,
                objectId,
                certificate,
                isTrustedCertificate,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private async ValueTask<AddCertificateMethodStateResult> AddCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ByteString certificate,
            bool isTrustedCertificate,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> inputParameters = [certificate, isTrustedCertificate];
            m_node.ReportTrustListUpdateRequestedAuditEvent(
                context,
                objectId,
                "Method/AddCertificate",
                method.NodeId,
                inputParameters,
                m_logger);
            HasSecureWriteAccess(context);

            NodeId sessionId = GetSessionId(context);
            ServiceResult result = StatusCodes.Good;

            bool isSessionOpen;
            lock (m_lock)
            {
                isSessionOpen = !m_sessionId.IsNull;
            }

            if (isSessionOpen)
            {
                result = StatusCodes.BadInvalidState;
            }
            else if (certificate.IsEmpty)
            {
                result = StatusCodes.BadInvalidArgument;
            }
            else if (certificate.Length > m_effectiveMaxTrustListSize)
            {
                // Reject an oversized certificate before it is parsed/staged
                // (direct Add path): a single certificate cannot exceed the
                // effective TrustList size the server advertises and enforces.
                result = ServiceResult.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "Certificate size exceeds the maximum allowed TrustList size of {0} bytes",
                    m_effectiveMaxTrustListSize);
            }
            else if (m_coordinator != null)
            {
                try
                {
                    m_coordinator.ValidateSessionCanParticipate(sessionId);
                }
                catch (ServiceResultException ex)
                {
                    result = ex.StatusCode;
                }
            }

            bool deferredToCommit = false;
            if (ServiceResult.IsGood(result))
            {
                Certificate? cert = null;
                try
                {
                    cert = Certificate.FromRawData(certificate);
                }
                catch
                {
                    // note: a previous version of the sample code accepted also CRL,
                    // but the behaviour was not as specified and removed
                    // https://mantis.opcfoundation.org/view.php?id=6342
                    result = StatusCodes.BadCertificateInvalid;
                }

                if (cert != null)
                {
                    if (m_coordinator == null)
                    {
                        // Legacy non-transactional behavior: apply immediately.
                        try
                        {
                            CertificateStoreIdentifier storeIdentifier = isTrustedCertificate
                                ? m_trustedStore
                                : m_issuerStore;
                            using ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                            if (store != null)
                            {
                                await store.AddAsync(cert, null, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            cert.Dispose();
                        }

                        lock (m_lock)
                        {
                            m_node.LastUpdateTime!.Value = DateTime.UtcNow;
                        }
                    }
                    else
                    {
                        NodeId trustListId = m_node.NodeId;
                        Certificate stagedCert = cert;
                        string stagedThumbprint = cert.Thumbprint;
                        m_coordinator.Stage(sessionId, new PushConfigurationOperation
                        {
                            AffectedTrustList = trustListId,
                            CommitAsync = async ct =>
                            {
                                CertificateStoreIdentifier storeIdentifier = isTrustedCertificate
                                    ? m_trustedStore
                                    : m_issuerStore;
                                using ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                                if (store != null)
                                {
                                    await store.AddAsync(stagedCert, null, ct).ConfigureAwait(false);
                                }

                                lock (m_lock)
                                {
                                    m_node.LastUpdateTime!.Value = DateTime.UtcNow;
                                }

                                m_node.ReportTrustListUpdatedAuditEvent(
                                    context, objectId, "Method/AddCertificate", method.NodeId, inputParameters,
                                    StatusCodes.Good, m_logger);
                            },
                            RollbackAsync = async ct =>
                            {
                                CertificateStoreIdentifier storeIdentifier = isTrustedCertificate
                                    ? m_trustedStore
                                    : m_issuerStore;
                                using ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                                if (store != null)
                                {
                                    await store.DeleteAsync(stagedThumbprint, ct).ConfigureAwait(false);
                                }
                            },
                            DisposeStaged = () => stagedCert.Dispose()
                        });
                        deferredToCommit = true;
                    }
                }
            }

            if (!deferredToCommit)
            {
                // Failure paths, and the legacy immediate-apply success
                // path, report their own "updated" audit event
                // synchronously; the transactional success path reports
                // it from the deferred commit instead.
                m_node.ReportTrustListUpdatedAuditEvent(
                    context,
                    objectId,
                    "Method/AddCertificate",
                    method.NodeId,
                    inputParameters,
                    result.StatusCode,
                    m_logger);
            }

            return new AddCertificateMethodStateResult
            {
                ServiceResult = result
            };
        }

        private ServiceResult RemoveCertificate(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string thumbprint,
            bool isTrustedCertificate)
        {
            RemoveCertificateMethodStateResult result = RemoveCertificateAsync(
                context,
                method,
                objectId,
                thumbprint,
                isTrustedCertificate,
                CancellationToken.None).AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            return result.ServiceResult;
        }

        private async ValueTask<RemoveCertificateMethodStateResult> RemoveCertificateAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            string thumbprint,
            bool isTrustedCertificate,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> inputParameters = [thumbprint, isTrustedCertificate];
            m_node.ReportTrustListUpdateRequestedAuditEvent(
                context,
                objectId,
                "Method/RemoveCertificate",
                method.NodeId,
                inputParameters,
                m_logger);

            HasSecureWriteAccess(context);
            NodeId sessionId = GetSessionId(context);
            ServiceResult result = StatusCodes.Good;

            bool isSessionOpen;
            lock (m_lock)
            {
                isSessionOpen = !m_sessionId.IsNull;
            }

            if (isSessionOpen)
            {
                result = StatusCodes.BadInvalidState;
            }
            else if (string.IsNullOrEmpty(thumbprint))
            {
                result = StatusCodes.BadInvalidArgument;
            }
            else if (m_coordinator != null)
            {
                try
                {
                    m_coordinator.ValidateSessionCanParticipate(sessionId);
                }
                catch (ServiceResultException ex)
                {
                    result = ex.StatusCode;
                }
            }

            bool deferredToCommit = false;
            if (ServiceResult.IsGood(result))
            {
                CertificateStoreIdentifier storeIdentifier = isTrustedCertificate
                    ? m_trustedStore
                    : m_issuerStore;

                if (m_coordinator == null)
                {
                    // Legacy non-transactional behavior: apply immediately.
                    using (ICertificateStore store = storeIdentifier.OpenStore(m_telemetry))
                    {
                        if (store == null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadConfigurationError,
                                "Failed to open certificate store.");
                        }

                        using CertificateCollection certCollection = await store
                            .FindByThumbprintAsync(thumbprint, cancellationToken)
                            .ConfigureAwait(false);

                        if (certCollection.Count == 0)
                        {
                            result = StatusCodes.BadInvalidArgument;
                        }
                        else
                        {
                            // delete all CRLs signed by cert
                            var crlsToDelete = new X509CRLCollection();
                            X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                                .ConfigureAwait(false);
                            foreach (X509CRL crl in crls)
                            {
                                foreach (Certificate cert in certCollection)
                                {
                                    if (X509Utils.CompareDistinguishedName(
                                            cert.SubjectName,
                                            crl.IssuerName) &&
                                        crl.VerifySignature(cert, false))
                                    {
                                        crlsToDelete.Add(crl);
                                        break;
                                    }
                                }
                            }

                            if (!await store.DeleteAsync(thumbprint, cancellationToken)
                                .ConfigureAwait(false))
                            {
                                result = StatusCodes.BadInvalidArgument;
                            }
                            else
                            {
                                foreach (X509CRL crl in crlsToDelete)
                                {
                                    if (!await store.DeleteCRLAsync(crl, cancellationToken)
                                        .ConfigureAwait(false))
                                    {
                                        // intentionally ignore errors, try best effort
                                        m_logger.LogError(
                                            "RemoveCertificate: Failed to delete CRL {Crl}.",
                                            crl.ToString());
                                    }
                                }
                            }
                        }
                    }

                    lock (m_lock)
                    {
                        m_node.LastUpdateTime!.Value = DateTime.UtcNow;
                    }
                }
                else
                {
                    // Transactional: identify the certificate and its
                    // associated CRLs (read-only) and stage the removal so
                    // it can be restored on rollback.
                    CertificateCollection? certCollection = null;
                    try
                    {
                        using ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                        if (store == null)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadConfigurationError,
                                "Failed to open certificate store.");
                        }

                        certCollection = await store
                            .FindByThumbprintAsync(thumbprint, cancellationToken)
                            .ConfigureAwait(false);

                        if (certCollection.Count == 0)
                        {
                            result = StatusCodes.BadInvalidArgument;
                        }
                        else
                        {
                            var crlsToDelete = new X509CRLCollection();
                            X509CRLCollection crls = await store.EnumerateCRLsAsync(cancellationToken)
                                .ConfigureAwait(false);
                            foreach (X509CRL crl in crls)
                            {
                                foreach (Certificate cert in certCollection)
                                {
                                    if (X509Utils.CompareDistinguishedName(
                                            cert.SubjectName,
                                            crl.IssuerName) &&
                                        crl.VerifySignature(cert, false))
                                    {
                                        crlsToDelete.Add(crl);
                                        break;
                                    }
                                }
                            }

                            NodeId trustListId = m_node.NodeId;
                            CertificateCollection stagedRemovedCerts = certCollection;
                            X509CRLCollection stagedRemovedCrls = crlsToDelete;
                            m_coordinator.Stage(sessionId, new PushConfigurationOperation
                            {
                                AffectedTrustList = trustListId,
                                CommitAsync = async ct =>
                                {
                                    using ICertificateStore commitStore = storeIdentifier.OpenStore(m_telemetry);
                                    if (!await commitStore.DeleteAsync(thumbprint, ct).ConfigureAwait(false))
                                    {
                                        throw new ServiceResultException(
                                            StatusCodes.BadInvalidArgument,
                                            "Certificate is no longer present in the store.");
                                    }

                                    foreach (X509CRL crl in stagedRemovedCrls)
                                    {
                                        if (!await commitStore.DeleteCRLAsync(crl, ct).ConfigureAwait(false))
                                        {
                                            // intentionally ignore errors, try best effort
                                            m_logger.LogError(
                                                "RemoveCertificate: Failed to delete CRL {Crl}.",
                                                crl.ToString());
                                        }
                                    }

                                    lock (m_lock)
                                    {
                                        m_node.LastUpdateTime!.Value = DateTime.UtcNow;
                                    }

                                    m_node.ReportTrustListUpdatedAuditEvent(
                                        context, objectId, "Method/RemoveCertificate", method.NodeId, inputParameters,
                                        StatusCodes.Good, m_logger);
                                },
                                RollbackAsync = async ct =>
                                {
                                    using ICertificateStore rollbackStore = storeIdentifier.OpenStore(m_telemetry);
                                    foreach (Certificate cert in stagedRemovedCerts)
                                    {
                                        await rollbackStore.AddAsync(cert, null, ct).ConfigureAwait(false);
                                    }
                                    foreach (X509CRL crl in stagedRemovedCrls)
                                    {
                                        await rollbackStore.AddCRLAsync(crl, ct).ConfigureAwait(false);
                                    }
                                },
                                DisposeStaged = () => stagedRemovedCerts.Dispose()
                            });
                            deferredToCommit = true;
                            certCollection = null;
                        }
                    }
                    finally
                    {
                        certCollection?.Dispose();
                    }
                }
            }

            if (!deferredToCommit)
            {
                // Failure paths, and the legacy immediate-apply success
                // path, report their own "updated" audit event
                // synchronously; the transactional success path reports
                // it from the deferred commit instead.
                m_node.ReportTrustListUpdatedAuditEvent(
                    context,
                    objectId,
                    "Method/RemoveCertificate",
                    method.NodeId,
                    inputParameters,
                    result.StatusCode,
                    m_logger);
            }

            return new RemoveCertificateMethodStateResult
            {
                ServiceResult = result
            };
        }

        private static MemoryStream EncodeTrustListData(
            ISystemContext context,
            TrustListDataType trustList)
        {
            IServiceMessageContext messageContext =
                new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris
                };
            var strm = new MemoryStream();
            using (var encoder = new BinaryEncoder(strm, messageContext, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            strm.Position = 0;
            return strm;
        }

        private static TrustListDataType DecodeTrustListData(
            ISystemContext context,
            MemoryStream strm,
            int maxSize)
        {
            var trustList = new TrustListDataType();
            var messageContext =
                new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris
                };

            // Bound the decode by the effective TrustList size so a crafted
            // payload cannot pre-allocate beyond what the server accepts. Only
            // ever tightens the shared defaults (Math.Min), never loosens them,
            // so a large safety ceiling does not raise the per-element or
            // per-array pre-allocation limits above their protective defaults.
            messageContext.MaxMessageSize = Math.Min(messageContext.MaxMessageSize, maxSize);
            messageContext.MaxByteStringLength = Math.Min(
                messageContext.MaxByteStringLength, maxSize);
            messageContext.MaxArrayLength = Math.Min(messageContext.MaxArrayLength, maxSize);

            strm.Position = 0;
            using (var decoder = new BinaryDecoder(strm, messageContext))
            {
                trustList.Decode(decoder);
            }
            return trustList;
        }

        private async Task<bool> UpdateStoreCrlsAsync(
            CertificateStoreIdentifier storeIdentifier,
            X509CRLCollection updatedCrls,
            CancellationToken cancellationToken = default)
        {
            bool result = true;
            try
            {
                ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open certificate store.");
                    }

                    X509CRLCollection storeCrls = await store.EnumerateCRLsAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (X509CRL crl in storeCrls)
                    {
                        if (!updatedCrls.Remove(crl) &&
                            !await store.DeleteCRLAsync(crl, cancellationToken).ConfigureAwait(false))
                        {
                            result = false;
                        }
                    }
                    foreach (X509CRL crl in updatedCrls)
                    {
                        await store.AddCRLAsync(crl, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    store.Close();
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private async Task<bool> UpdateStoreCertificatesAsync(
            CertificateStoreIdentifier storeIdentifier,
            CertificateCollection updatedCerts,
            CancellationToken cancellationToken = default)
        {
            bool result = true;
            try
            {
                ICertificateStore store = storeIdentifier.OpenStore(m_telemetry);
                try
                {
                    if (store == null)
                    {
                        throw ServiceResultException.ConfigurationError(
                            "Failed to open certificate store.");
                    }

                    using CertificateCollection storeCerts = await store.EnumerateAsync(cancellationToken)
                        .ConfigureAwait(false);
                    foreach (Certificate cert in storeCerts)
                    {
                        if (!updatedCerts.Remove(cert) &&
                            !await store.DeleteAsync(cert.Thumbprint, cancellationToken).ConfigureAwait(false))
                        {
                            result = false;
                        }
                    }
                    foreach (Certificate cert in updatedCerts)
                    {
                        await store.AddAsync(cert, null, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    store.Close();
                }
            }
            catch
            {
                result = false;
            }
            return result;
        }

        private void HasSecureReadAccess(ISystemContext context)
        {
            if (m_readAccess != null)
            {
                m_readAccess.Invoke(context, m_trustedStore);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        private void HasSecureWriteAccess(ISystemContext context)
        {
            if (m_writeAccess != null)
            {
                m_writeAccess.Invoke(context, m_trustedStore);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        private readonly Lock m_lock = new();
        private readonly SecureAccess m_readAccess;
        private readonly SecureAccess m_writeAccess;
        private NodeId m_sessionId;
        private uint m_fileHandle;
        private readonly CertificateStoreIdentifier m_trustedStore;
        private readonly CertificateStoreIdentifier m_issuerStore;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TrustListState m_node;
        private readonly IPushConfigurationTransactionCoordinator? m_coordinator;
        private MemoryStream? m_strm;
        private readonly int m_effectiveMaxTrustListSize;
        private long m_totalBytesProcessed;
    }
}
