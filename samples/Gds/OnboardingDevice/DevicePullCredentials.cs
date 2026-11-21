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
using OnboardingClientSample;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;

namespace OnboardingDevice
{
    /// <summary>
    /// Restores a locally retained key for an existing pull signing request.
    /// </summary>
    internal interface IDevicePullSigningRecovery
    {
        /// <summary>
        /// Validates the retained request and reopens its private key.
        /// </summary>
        ValueTask RestoreSigningRequestAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Reuses the sample's local identity and CSR provider but installs only through the device security owner.
    /// The active pull attempt owns its session; reset and recovery use the same device checkpoint.
    /// </summary>
    internal sealed class DevicePullCredentials :
        IPullDcaIdentityProvider,
        IPullDcaCredentialProvider,
        IPullDcaBoundCredentialProvider,
        IPullDcaSigningKeyEvidenceProvider
    {
        /// <summary>
        /// Borrows identity selection, local signing, device installation and the registrar session accessor.
        /// </summary>
        public DevicePullCredentials(
            IPullDcaIdentityProvider identity,
            IPullDcaCredentialProvider signing,
            IProvisionableDeviceSecurity security,
            Func<CancellationToken, Task<ManagedSession>> connect,
            string expectedRegistrarApplicationUri)
        {
            m_identity = identity ?? throw new ArgumentNullException(nameof(identity));
            m_signing = signing ?? throw new ArgumentNullException(nameof(signing));
            m_security = security ?? throw new ArgumentNullException(nameof(security));
            m_connect = connect ?? throw new ArgumentNullException(nameof(connect));
            if (!Uri.TryCreate(expectedRegistrarApplicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute registrar application URI is required.",
                    nameof(expectedRegistrarApplicationUri));
            }
            m_expectedRegistrarApplicationUri = expectedRegistrarApplicationUri;
        }

        /// <inheritdoc/>
        public ValueTask ActivateAsync(ByteString selectedIdentity, CancellationToken cancellationToken = default)
        {
            return m_identity.ActivateAsync(selectedIdentity, cancellationToken);
        }

        /// <inheritdoc/>
        public async ValueTask<ByteString> CreateSigningRequestAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            CancellationToken cancellationToken = default)
        {
            ByteString request = await m_signing.CreateSigningRequestAsync(applicationId, profile, cancellationToken)
                .ConfigureAwait(false);
            m_applicationId = applicationId;
            return request;
        }

        /// <inheritdoc/>
        public ValueTask<ByteString> GetSigningKeyCertificateAsync(
            NodeId applicationId,
            PullDcaCertificateProfile profile,
            ByteString signingRequest,
            CancellationToken cancellationToken = default)
        {
            if (m_signing is not IPullDcaSigningKeyEvidenceProvider evidence)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The signing provider cannot retain key evidence.");
            }
            return evidence.GetSigningKeyCertificateAsync(applicationId, profile, signingRequest, cancellationToken);
        }

        /// <summary>
        /// Restores the retained application and signing key before resuming an existing request.
        /// </summary>
        public async ValueTask RestoreSigningRequestAsync(
            PullDcaSigningBinding binding,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (m_signing is IDevicePullSigningRecovery recovery)
            {
                await recovery.RestoreSigningRequestAsync(binding, cancellationToken).ConfigureAwait(false);
            }
            else if (m_signing is OnboardingDeviceCredentials credentials)
            {
                await credentials.RestoreSigningRequestAsync(
                    binding.ApplicationId, binding.Profile, binding.SigningKeyCertificate, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The signing provider cannot restore the retained request.");
            }
            m_applicationId = binding.ApplicationId;
        }

        /// <inheritdoc/>
        public async ValueTask InstallAsync(
            NodeId applicationId,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            if (applicationId.IsNull || m_applicationId.IsNull || applicationId != m_applicationId)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            ManagedSession session = await m_connect(cancellationToken).ConfigureAwait(false);
            await m_security.InstallFromPullAsync(
                session, m_expectedRegistrarApplicationUri,
                new ProvisionableDeviceCredentials(
                    credentials.Certificate, credentials.IssuerCertificates, credentials.TrustList),
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask InstallAsync(
            PullDcaSigningBinding binding,
            PullDcaCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            if (binding.ApplicationId.IsNull || m_applicationId.IsNull ||
                binding.ApplicationId != m_applicationId || binding.AttemptId == Guid.Empty)
            {
                throw new ServiceResultException(StatusCodes.BadRequestNotAllowed);
            }
            if (m_security is not IProvisionableDevicePullInstallation installer)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The device security owner cannot install a bound pull attempt.");
            }
            ManagedSession session = await m_connect(cancellationToken).ConfigureAwait(false);
            await installer.InstallFromPullAsync(
                session, binding.RegistrarApplicationUri,
                new ProvisionableDeviceCredentials(
                    credentials.Certificate, credentials.IssuerCertificates, credentials.TrustList),
                binding.AttemptId, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The host's existing local identity provider.</summary>
        private readonly IPullDcaIdentityProvider m_identity;

        /// <summary>The host's existing local CSR provider, never used to install credentials.</summary>
        private readonly IPullDcaCredentialProvider m_signing;

        /// <summary>The device's serialized installation, recovery and reset owner.</summary>
        private readonly IProvisionableDeviceSecurity m_security;

        /// <summary>The accessor for the active attempt's borrowed session.</summary>
        private readonly Func<CancellationToken, Task<ManagedSession>> m_connect;

        /// <summary>The operator-selected registrar identity, independent of discovery responses.</summary>
        private readonly string m_expectedRegistrarApplicationUri;

        /// <summary>The one application whose CSR was created successfully.</summary>
        private NodeId m_applicationId;
    }
}
