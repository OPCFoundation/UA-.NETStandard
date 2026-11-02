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
using Opc.Ua.Client;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Typed transport boundary for the pull workflow; never owns its underlying session.
    /// </summary>
    internal interface IPullDcaConnection
    {
        /// <summary>
        /// Calls the registrar's generated identity proxy.
        /// </summary>
        ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(PullDcaIdentityOffer offer, CancellationToken ct);

        /// <summary>
        /// Reloads the host-installed certificate and reconnects the managed session.
        /// </summary>
        ValueTask ReloadAndReconnectAsync(CancellationToken ct);

        /// <summary>
        /// Calls the generated manager proxy.
        /// </summary>
        ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct);

        /// <summary>
        /// Calls the generated application registration proxy.
        /// </summary>
        ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct);

        /// <summary>
        /// Calls the certificate directory's generated signing proxy.
        /// </summary>
        ValueTask<NodeId> StartAsync(
            NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct);

        /// <summary>
        /// Calls the certificate directory's generated completion proxy.
        /// </summary>
        ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
            NodeId applicationId, NodeId requestId, CancellationToken ct);

        /// <summary>
        /// Reads currently assigned certificates for one application and certificate group.
        /// </summary>
        ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct);

        /// <summary>
        /// Resolves and downloads a bounded trust list using generated file proxies.
        /// </summary>
        ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct);
    }

    /// <summary>
    /// Generated method proxy adapter over a factory-owned managed session.
    /// </summary>
    internal sealed class PullDcaConnection : IPullDcaConnection
    {
        /// <summary>
        /// Creates proxies for one explicitly selected endpoint.
        /// </summary>
        public PullDcaConnection(
            ManagedSession session,
            PullDcaRegistrar registrar,
            ITelemetryContext telemetry,
            long maximumTrustListSize)
            : this(session, registrar, telemetry, maximumTrustListSize, ct => ReloadAndReconnectAsync(session, ct))
        {
            if (!registrar.EndpointUrl.Equals(session.ConfiguredEndpoint.EndpointUrl))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "The borrowed session is not connected to the selected registrar.");
            }
        }

        /// <summary>
        /// Creates a typed proxy adapter with an explicit reconnect operation for transport-level tests.
        /// </summary>
        internal PullDcaConnection(
            ISession session,
            PullDcaRegistrar registrar,
            ITelemetryContext telemetry,
            long maximumTrustListSize,
            Func<CancellationToken, ValueTask> reconnect)
        {
            m_session = session ?? throw new ArgumentNullException(nameof(session));
            m_registrar = new DeviceRegistrarTypeClient(session, registrar.RegistrarId, telemetry);
            m_directory = new CertificateDirectoryTypeClient(session, registrar.CertificateDirectoryId, telemetry);
            m_telemetry = telemetry;
            m_maximumTrustListSize = maximumTrustListSize;
            m_reconnect = reconnect ?? throw new ArgumentNullException(nameof(reconnect));
        }

        /// <inheritdoc/>
        public async ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
            PullDcaIdentityOffer offer,
            CancellationToken ct)
        {
            var (identity, ticket, applicationId, manager) = await m_registrar.ProvideIdentitiesAsync(
                offer.Identities, offer.Issuers, offer.Tickets, ct).ConfigureAwait(false);
            return new PullDcaIdentitySelection(identity, ticket, applicationId, manager);
        }

        /// <inheritdoc/>
        public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
        {
            return m_reconnect(ct);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
        {
            return m_registrar.GetManagersAsync(ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
        {
            return m_registrar.RegisterManagedApplicationAsync(application.Application, application.ProtocolUri, ct);
        }

        /// <inheritdoc/>
        public ValueTask<NodeId> StartAsync(
            NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct)
        {
            return m_directory.StartSigningRequestAsync(
                applicationId, profile.CertificateGroupId, profile.CertificateTypeId, csr, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
            NodeId applicationId, NodeId requestId, CancellationToken ct)
        {
            var (certificate, privateKey, issuers) = await m_directory.FinishRequestAsync(
                applicationId, requestId, ct).ConfigureAwait(false);
            if (!privateKey.IsEmpty)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A signing request unexpectedly returned a private key.");
            }
            return (certificate, issuers);
        }

        /// <inheritdoc/>
        public ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
        {
            return m_directory.GetCertificatesAsync(applicationId, certificateGroupId, ct);
        }

        /// <inheritdoc/>
        public async ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
        {
            NodeId trustListId = await m_directory.GetTrustListAsync(
                applicationId, certificateGroupId, ct).ConfigureAwait(false);
            if (trustListId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            var file = new FileTypeClient(m_session, trustListId, m_telemetry);
            uint handle = await file.OpenAsync((byte)OpenFileMode.Read, ct).ConfigureAwait(false);
            return await TrustListFileTransferHelper.ReadAndCloseAsync(
                file, handle, m_session.MessageContext, m_maximumTrustListSize, 4096, ct).ConfigureAwait(false);
        }

        private static async ValueTask ReloadAndReconnectAsync(ManagedSession session, CancellationToken ct)
        {
            await session.ReloadInstanceCertificateAsync(ct).ConfigureAwait(false);
            await session.ReconnectAsync(null, null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Borrowed session; its factory alone owns disposal.
        /// </summary>
        private readonly ISession m_session;

        /// <summary>
        /// Generated registrar proxy.
        /// </summary>
        private readonly DeviceRegistrarTypeClient m_registrar;

        /// <summary>
        /// Generated certificate directory proxy.
        /// </summary>
        private readonly CertificateDirectoryTypeClient m_directory;

        /// <summary>
        /// Telemetry used by file proxies.
        /// </summary>
        private readonly ITelemetryContext m_telemetry;

        /// <summary>
        /// Bound on every downloaded trust list.
        /// </summary>
        private readonly long m_maximumTrustListSize;

        /// <summary>
        /// Reloads and reconnects the host-owned session.
        /// </summary>
        private readonly Func<CancellationToken, ValueTask> m_reconnect;
    }
}
