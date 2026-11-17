/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Gds.Client;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Fakes the internal pull-connection contracts without relying on dynamic proxy access.
    /// </summary>
    internal sealed class FakePullDcaBoundSigningConnection :
        IPullDcaConnection,
        IPullDcaBoundSigningConnection
    {
        private readonly Func<NodeId, NodeId, CancellationToken, ValueTask<PullDcaSigningBinding>> m_getBinding;

        /// <summary>
        /// Creates a fake connection with a request-binding resolver.
        /// </summary>
        public FakePullDcaBoundSigningConnection(
            Func<NodeId, NodeId, CancellationToken, ValueTask<PullDcaSigningBinding>> getBinding)
        {
            m_getBinding = getBinding ?? throw new ArgumentNullException(nameof(getBinding));
        }

        /// <summary>
        /// Gets or sets the callback for finishing a signing request.
        /// </summary>
        public Func<NodeId, NodeId, CancellationToken, ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)>>
            Finish { get; set; } = (_, _, _) => throw new NotSupportedException();

        /// <summary>
        /// Gets or sets the callback for reading assigned certificates.
        /// </summary>
        public Func<NodeId, NodeId, CancellationToken,
            ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)>> GetCertificates { get; set; } =
            (_, _, _) => throw new NotSupportedException();

        /// <summary>
        /// Gets or sets the callback for reading a trust list.
        /// </summary>
        public Func<NodeId, NodeId, CancellationToken, ValueTask<TrustListDataType>> ReadTrustList { get; set; } =
            (_, _, _) => throw new NotSupportedException();

        /// <summary>
        /// Gets or sets the callback for checking managers on the installed channel.
        /// </summary>
        public Func<PullDcaSigningBinding?, ByteString, CancellationToken, ValueTask<ArrayOf<ManagerDescription>>>
            GetManagersOnInstalledChannel { get; set; } =
                (_, _, _) => throw new NotSupportedException();

        /// <summary>
        /// Gets the number of finish calls.
        /// </summary>
        public int FinishCalls { get; private set; }

        /// <summary>
        /// Gets the number of assigned-certificate reads.
        /// </summary>
        public int GetCertificatesCalls { get; private set; }

        /// <summary>
        /// Gets the number of trust-list reads.
        /// </summary>
        public int ReadTrustListCalls { get; private set; }

        /// <summary>
        /// Gets the number of installed-channel manager checks.
        /// </summary>
        public int GetManagersOnInstalledChannelCalls { get; private set; }

        /// <inheritdoc/>
        public ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
            PullDcaIdentityOffer offer,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> GetManagersOnInstalledChannelAsync(
            PullDcaSigningBinding? binding,
            ByteString installedCertificate,
            CancellationToken ct)
        {
            GetManagersOnInstalledChannelCalls++;
            return GetManagersOnInstalledChannel(binding, installedCertificate, ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString csr,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartNewKeyPairAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken ct)
        {
            FinishCalls++;
            return Finish(applicationId, requestId, ct);
        }

        /// <inheritdoc/>
        public ValueTask<(ByteString Certificate, ByteString PrivateKeyPfx, ArrayOf<ByteString> Issuers)>
            FinishNewKeyPairAsync(NodeId applicationId, NodeId requestId, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct)
        {
            GetCertificatesCalls++;
            return GetCertificates(applicationId, certificateGroupId, ct);
        }

        /// <inheritdoc/>
        public ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct)
        {
            ReadTrustListCalls++;
            return ReadTrustList(applicationId, certificateGroupId, ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartWithEvidenceAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString csr,
            ByteString signingKeyCertificate,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public ValueTask<PullDcaSigningBinding> GetSigningBindingAsync(
            NodeId applicationId,
            NodeId requestId,
            CancellationToken ct)
        {
            return m_getBinding(applicationId, requestId, ct);
        }
    }
}
