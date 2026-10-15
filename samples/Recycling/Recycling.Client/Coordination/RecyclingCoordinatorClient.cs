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
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Recycling.Model;
using MonitoredItemOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Recycling
{
    /// <summary>
    /// Managed-session coordination client using only generated typed methods and real UA subscriptions.
    /// </summary>
    public sealed class RecyclingCoordinatorClient : IRecyclingMembershipClient
    {
        /// <summary>
        /// Wraps an authenticated managed session. The wrapper owns and asynchronously disposes it.
        /// </summary>
        public RecyclingCoordinatorClient(ManagedSession session, ITelemetryContext telemetry)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            ArgumentNullException.ThrowIfNull(telemetry);
            RegisterTypes(session.MessageContext.Factory);
            if (!ReferenceEquals(session.Factory, session.MessageContext.Factory))
            {
                RegisterTypes(session.Factory);
            }
            ushort index = NamespaceIndex(session);
            m_proxy = new RecyclingCoordinatorTypeClient(
                session, new NodeId(Model.Objects.Coordinator, index), telemetry);
            m_stateId = new NodeId(Model.Variables.Coordinator_State, index);
        }

        /// <summary>
        /// Actual authenticated user certificate fingerprint, used when preserving end-user forwarding identity.
        /// </summary>
        public string CertificateSha256
        {
            get
            {
                IUserIdentity identity = m_session.Identity;
                if (identity.TokenType != UserTokenType.Certificate ||
                    identity.TokenHandler.Token is not X509IdentityToken token || token.CertificateData.IsEmpty)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUserAccessDenied, "A trusted X.509 user identity is required.");
                }
                return Convert.ToHexString(SHA256.HashData(token.CertificateData.Span));
            }
        }

        /// <inheritdoc/>
        public string ConnectionId => m_session.SessionId.ToString();

        /// <inheritdoc/>
        public bool IsConnected => m_session.Connected;

        /// <summary>
        /// Underlying managed session, useful to compose generated companion clients and inspect connection health.
        /// </summary>
        public ManagedSession Session => m_session;

        /// <summary>
        /// Reads the latest committed image with the generated typed method.
        /// </summary>
        public async ValueTask<CommittedSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotDataType snapshot = await m_proxy.ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return RecyclingModelMapping.FromWire(snapshot);
        }

        /// <summary>
        /// Sends a caller-owned request ID and preserves it across caller-controlled retries.
        /// </summary>
        public async ValueTask<RecyclingCommandOutcome> ExecuteAsync(
            RecyclingCommand command,
            CancellationToken cancellationToken = default)
        {
            CommandOutcomeDataType outcome = await m_proxy.ExecuteAsync(
                RecyclingModelMapping.ToWire(command), cancellationToken).ConfigureAwait(false);
            return RecyclingModelMapping.FromWire(outcome);
        }

        /// <summary>
        /// Reads a bounded typed genealogy page.
        /// </summary>
        public async ValueTask<ArrayOf<MaterialExecution>> ReadHistoryAsync(
            long afterExecutionId,
            int maximumResults,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<ExecutionDataType> history = await m_proxy.ReadGenealogyAsync(
                afterExecutionId, maximumResults, cancellationToken).ConfigureAwait(false);
            return RecyclingModelMapping.FromWire(history);
        }

        /// <summary>
        /// Observes committed typed UA structures through a real monitored item.
        /// Non-Good source status is surfaced as unavailable with its original status and timestamps.
        /// The last Good snapshot is retained without promoting Uncertain data to usable production state.
        /// </summary>
        public async IAsyncEnumerable<CoordinatorNotification> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CommittedSnapshot? last = null;
            var options = new MonitoredItemOptions
            {
                SamplingInterval = TimeSpan.FromMilliseconds(100),
                QueueSize = 1,
                DiscardOldest = true
            };
            await foreach (DataValueChange change in m_session.DefaultStreaming.SubscribeDataChangesAsync(
                m_stateId, options, cancellationToken).ConfigureAwait(false))
            {
                CoordinatorNotification? notification = CreateNotification(
                    change.Value, m_session.MessageContext, ref last);
                if (notification != null)
                {
                    yield return notification;
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await m_session.DefaultStreaming.DisposeAsync().ConfigureAwait(false);
            await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await m_session.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Detaches a monitored value while preserving quality and rejecting stale same-run revisions.
        /// </summary>
        internal static CoordinatorNotification? CreateNotification(
            in DataValue value,
            IServiceMessageContext messageContext,
            ref CommittedSnapshot? last)
        {
            if (!StatusCode.IsGood(value.StatusCode))
            {
                return new CoordinatorNotification(
                    last ?? new CommittedSnapshot(), false, value.StatusCode.ToString())
                {
                    StatusCode = value.StatusCode,
                    SourceTimestampUtc = value.SourceTimestamp.ToDateTime(),
                    ServerTimestampUtc = value.ServerTimestamp.ToDateTime()
                };
            }
            if (!value.WrappedValue.TryGetValue<SnapshotDataType>(
                out SnapshotDataType? snapshotValue, messageContext) || snapshotValue == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "Expected a typed recycling snapshot.");
            }
            CommittedSnapshot snapshot = RecyclingModelMapping.FromWire(snapshotValue);
            if (last != null && last.RunId == snapshot.RunId && last.Revision > snapshot.Revision)
            {
                return null;
            }
            last = snapshot;
            return new CoordinatorNotification(snapshot, true, string.Empty)
            {
                StatusCode = value.StatusCode,
                SourceTimestampUtc = value.SourceTimestamp.ToDateTime(),
                ServerTimestampUtc = value.ServerTimestamp.ToDateTime()
            };
        }

        private static ushort NamespaceIndex(ManagedSession session)
        {
            int index = session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri);
            return index > 0
                ? checked((ushort)index)
                : throw new ServiceResultException(StatusCodes.BadNotFound, "Coordinator sample namespace is absent.");
        }

        private static void RegisterTypes(IEncodeableFactory factory)
        {
            if (!factory.ContainsEncodeableType(Model.DataTypeIds.SnapshotDataType))
            {
                factory.Builder.AddOpcUaRecyclingModel().Commit();
            }
        }

        private readonly ManagedSession m_session;
        private readonly RecyclingCoordinatorTypeClient m_proxy;
        private readonly NodeId m_stateId;
        private int m_disposed;
    }
}
