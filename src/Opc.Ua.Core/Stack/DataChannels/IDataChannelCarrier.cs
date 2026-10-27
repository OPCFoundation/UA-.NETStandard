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

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Creates and optionally registers an unstarted context using application-scoped providers.
    /// The caller attaches ingress before starting HELLO and owns the returned context lifetime.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelContextFactory
    {
        /// <summary>
        /// Creates a context over authenticated carrier evidence, without starting network traffic.
        /// </summary>
        DataChannelContext Create(IDataChannelCarrier carrier);
    }

    /// <summary>
    /// Identifies an explicitly selected version-one carrier.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public enum DataChannelCarrierKind
    {
        /// <summary>
        /// A dedicated authenticated UASC association.
        /// </summary>
        DedicatedUasc,

        /// <summary>
        /// FLO on an authenticated UASC association shared with Services.
        /// </summary>
        SharedUasc,

        /// <summary>
        /// Mutually authenticated native QUIC with opcua-flow/1.
        /// </summary>
        Quic,

        /// <summary>
        /// Mutually authenticated WebTransport over HTTP/3.
        /// </summary>
        WebTransport,

        /// <summary>
        /// Part 31 QUIC carrying protected FLO/FLC on the reliable UASC baseline.
        /// </summary>
        QuicUasc,

        /// <summary>
        /// Part 32 WebTransport carrying protected FLO/FLC on the reliable UASC baseline.
        /// </summary>
        WebTransportUasc
    }

    /// <summary>
    /// Identifies the real carrier path used for an encoded record.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public enum DataChannelPath
    {
        /// <summary>
        /// Reliable context management.
        /// </summary>
        Management,

        /// <summary>
        /// The flow's ordered path, including directional revision barriers.
        /// </summary>
        Flow,

        /// <summary>
        /// A genuinely unreliable native application datagram.
        /// </summary>
        Datagram
    }

    /// <summary>
    /// Adapts an already authenticated, explicitly selected carrier. Implementations must not
    /// report unsupported datagrams, stream associations or authentication as successful.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public interface IDataChannelCarrier
    {
        /// <summary>
        /// Gets the selected profile, after its real handshake has completed.
        /// </summary>
        DataChannelCarrierKind Kind { get; }

        /// <summary>
        /// Gets certificate/key evidence validated by the actual carrier.
        /// </summary>
        DataChannelPeerIdentity Identity { get; }

        /// <summary>
        /// Gets the complete native record capacity or the UASC FLO/FLC application-body capacity,
        /// after carrier headers and security overhead.
        /// </summary>
        int MaxRecordSize { get; }

        /// <summary>
        /// Gets the final native datagram capacity including ChannelId, or zero when unsupported.
        /// </summary>
        int MaxDatagramSize { get; }

        /// <summary>
        /// Gets the native initiator-created bidirectional management stream, or zero for UASC.
        /// </summary>
        ulong ManagementStreamId { get; }

        /// <summary>
        /// Opens a native bidirectional data stream in this authenticated association.
        /// </summary>
        ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies and binds a distinct bidirectional stream owned by the indicated opener.
        /// A false result refuses the association; it must never create a substitute stream.
        /// </summary>
        ValueTask<bool> BindStreamAsync(
            ulong streamId,
            uint channelId,
            bool locallyOpened,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Borrows a complete encoded record until completion. Call TryCommit immediately before
        /// security/transmission commitment. After commitment, cancellation must not retract
        /// bytes or release writer-owned storage. UASC uses its existing single ordered writer.
        /// </summary>
        ValueTask<DataChannelResult> SendAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a native flow association without closing unrelated flows or Services.
        /// </summary>
        ValueTask CloseFlowAsync(
            ulong streamId,
            DataChannelResult result,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes an invalid or explicitly disposed authenticated carrier association.
        /// </summary>
        ValueTask CloseAsync(
            DataChannelResult result,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Carries verified application/key evidence, not untrusted HELLO claims.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelPeerIdentity
    {
        /// <summary>
        /// Creates evidence after certificate validation, profile selection and rejection of early data.
        /// Thumbprints identify validated public keys using SHA-256.
        /// </summary>
        public DataChannelPeerIdentity(
            string localApplicationUri,
            string peerApplicationUri,
            ByteString localKeyThumbprint,
            ByteString peerKeyThumbprint,
            DataChannelProtectionMode protection,
            bool isTransportInitiator)
            : this(
                localApplicationUri,
                peerApplicationUri,
                localKeyThumbprint,
                peerKeyThumbprint,
                protection,
                isTransportInitiator,
                isTransportInitiator)
        {
        }

        /// <summary>
        /// Creates evidence after certificate validation, profile selection and rejection of early data.
        /// Thumbprints identify validated public keys using SHA-256.
        /// </summary>
        public DataChannelPeerIdentity(
            string localApplicationUri,
            string peerApplicationUri,
            ByteString localKeyThumbprint,
            ByteString peerKeyThumbprint,
            DataChannelProtectionMode protection,
            bool isTransportInitiator,
            bool isContextInitiator)
        {
            FlowModel.ValidateUri(localApplicationUri, nameof(localApplicationUri));
            FlowModel.ValidateUri(peerApplicationUri, nameof(peerApplicationUri));
            if (localKeyThumbprint.Length != 32 || peerKeyThumbprint.Length != 32)
            {
                throw new ArgumentException("Validated SHA-256 public-key identities are required.");
            }
            if (protection is not (DataChannelProtectionMode.Confidential or DataChannelProtectionMode.Integrity))
            {
                throw new ArgumentOutOfRangeException(nameof(protection));
            }
            LocalApplicationUri = localApplicationUri;
            PeerApplicationUri = peerApplicationUri;
            LocalKeyThumbprint = localKeyThumbprint.Copy();
            PeerKeyThumbprint = peerKeyThumbprint.Copy();
            Protection = protection;
            IsTransportInitiator = isTransportInitiator;
            IsContextInitiator = isContextInitiator;
        }

        /// <summary>
        /// Gets the authenticated local application URI.
        /// </summary>
        public string LocalApplicationUri { get; }

        /// <summary>
        /// Gets the expected authenticated remote application URI.
        /// </summary>
        public string PeerApplicationUri { get; }

        /// <summary>
        /// Gets the validated local key identity.
        /// </summary>
        public ByteString LocalKeyThumbprint { get; }

        /// <summary>
        /// Gets the validated peer key identity.
        /// </summary>
        public ByteString PeerKeyThumbprint { get; }

        /// <summary>
        /// Gets actual carrier protection.
        /// </summary>
        public DataChannelProtectionMode Protection { get; }

        /// <summary>
        /// Gets the transport role, independently of flow and payload roles.
        /// </summary>
        public bool IsTransportInitiator { get; }

        /// <summary>
        /// Gets whether this peer initiates the data context independently of the transport role.
        /// </summary>
        public bool IsContextInitiator { get; }
    }

    /// <summary>
    /// An encoded write with an atomic pre-security admission cut-off.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelWrite
    {
        /// <summary>
        /// Creates a write owned by the common runtime.
        /// </summary>
        internal DataChannelWrite(
            DataChannelPath path,
            uint channelId,
            ulong streamId,
            uint requestId,
            ByteString payload,
            bool isData,
            byte priority,
            Func<DataChannelResult> commit)
        {
            Path = path;
            ChannelId = channelId;
            StreamId = streamId;
            RequestId = requestId;
            Payload = payload;
            IsData = isData;
            Priority = priority;
            m_commit = commit;
        }

        /// <summary>
        /// Gets the real ordered or datagram path.
        /// </summary>
        public DataChannelPath Path { get; }

        /// <summary>
        /// Gets the logical flow identifier.
        /// </summary>
        public uint ChannelId { get; }

        /// <summary>
        /// Gets the bound native stream identifier.
        /// </summary>
        public ulong StreamId { get; }

        /// <summary>
        /// Gets the UASC RequestId (zero for both FLO profiles).
        /// </summary>
        public uint RequestId { get; }

        /// <summary>
        /// Gets borrowed immutable encoded bytes, including carrier-specific flow prefixes.
        /// </summary>
        public ByteString Payload { get; }

        /// <summary>
        /// Gets whether this is application DATA.
        /// </summary>
        public bool IsData { get; }

        /// <summary>
        /// Gets the admitted local scheduling priority.
        /// </summary>
        public byte Priority { get; }

        /// <summary>
        /// Gets whether the adapter has committed this write.
        /// </summary>
        public bool IsCommitted => Volatile.Read(ref m_state) == 1;

        /// <summary>
        /// Gets the outcome when the commitment cut-off refused the write.
        /// </summary>
        public DataChannelResult Result { get; private set; } = DataChannelResult.Good;

        /// <summary>
        /// Commits once, immediately before security/native submission. A refused write must
        /// not emit bytes. Repeated calls do not grant a second transmission.
        /// </summary>
        public bool TryCommit()
        {
            if (Interlocked.CompareExchange(ref m_state, -1, 0) != 0)
            {
                return false;
            }
            Result = m_commit();
            Volatile.Write(ref m_state, Result == DataChannelResult.Good ? 1 : 2);
            return IsCommitted;
        }

        private readonly Func<DataChannelResult> m_commit;
        private int m_state;
    }
}
