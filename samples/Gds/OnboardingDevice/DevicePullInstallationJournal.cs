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
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;

namespace OnboardingDevice
{
    /// <summary>
    /// Connects the device's serialized security owner to the shared pull journal.
    /// </summary>
    internal sealed class DevicePullInstallationJournal : IProvisionableDevicePullInstallationJournal
    {
        /// <summary>
        /// Borrows the signing journal owned by the host.
        /// </summary>
        public DevicePullInstallationJournal(DevicePullJournal journal)
            : this(journal, null)
        {
        }

        /// <summary>
        /// Also retires keys from the dedicated signing store after installation or reset.
        /// </summary>
        public DevicePullInstallationJournal(DevicePullJournal journal, DevicePullSigningKeyStore? signingKeys)
        {
            m_journal = journal ?? throw new ArgumentNullException(nameof(journal));
            m_signingKeys = signingKeys;
        }

        /// <inheritdoc/>
        public ValueTask RequireReceivedAsync(
            Guid attemptId,
            ByteString registrarCertificate,
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            return m_journal.RequireReceivedAsync(
                attemptId, registrarCertificate, ToPullCredentials(credentials), cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask RecordInstalledAsync(
            Guid attemptId,
            ByteString registrarCertificate,
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            await m_journal.RecordInstalledAsync(
                attemptId, registrarCertificate, ToPullCredentials(credentials), cancellationToken)
                .ConfigureAwait(false);
            if (m_signingKeys != null)
            {
                await m_signingKeys.RetireInstalledAsync(attemptId, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask BeginResetAsync(CancellationToken cancellationToken = default)
        {
            return m_journal.BeginResetAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask CompleteResetAsync(CancellationToken cancellationToken = default)
        {
            await m_journal.CompleteResetAsync(cancellationToken).ConfigureAwait(false);
            if (m_signingKeys != null)
            {
                await m_signingKeys.RetireResetAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adapts the security owner's credential shape to the shared pull journal.
        /// </summary>
        private static PullDcaCredentials ToPullCredentials(ProvisionableDeviceCredentials credentials)
        {
            ArgumentNullException.ThrowIfNull(credentials);
            return new PullDcaCredentials(
                credentials.Certificate, credentials.IssuerCertificates, credentials.TrustList);
        }

        /// <summary>The host's shared signing journal.</summary>
        private readonly DevicePullJournal m_journal;

        /// <summary>The dedicated pull signing-key store, when configured.</summary>
        private readonly DevicePullSigningKeyStore? m_signingKeys;
    }
}
