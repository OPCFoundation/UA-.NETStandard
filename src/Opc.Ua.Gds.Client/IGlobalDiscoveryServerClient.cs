// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Public surface of <see cref="GlobalDiscoveryServerClient"/>. Exposes
    /// the asynchronous operations that the GDS client offers against a
    /// connected Global Discovery Server.
    /// </summary>
    public interface IGlobalDiscoveryServerClient : IAsyncDisposable
    {
        /// <summary>The application configuration in use.</summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>The message context derived from the configuration.</summary>
        IServiceMessageContext MessageContext { get; }

        /// <summary>The administrator credentials used to elevate calls.</summary>
        IUserIdentity? AdminCredentials { get; set; }

        /// <summary>The endpoint URL of the connected server, when available.</summary>
        string? EndpointUrl { get; }

        /// <summary><c>true</c> when an active session exists.</summary>
        bool IsConnected { get; }

        /// <summary>The active session, when connected.</summary>
        ISession? Session { get; }

        /// <summary>The endpoint to connect to. Write-once before connect.</summary>
        ConfiguredEndpoint? Endpoint { get; set; }

        /// <summary>Locales preferred by the client.</summary>
        ArrayOf<string> PreferredLocales { get; set; }

        /// <summary>Raised on every keep-alive callback.</summary>
        event KeepAliveEventHandler? KeepAlive;

        /// <summary>
        /// Raised when the state of the connected server changes, and once for
        /// the state observed by the first keep-alive after a connect.
        /// </summary>
        event EventHandler<ServerStatusChangedEventArgs>? ServerStatusChanged;

        /// <summary>Clears the cached <see cref="AdminCredentials"/>.</summary>
        void ResetCredentials();

        /// <summary>Connects using the previously assigned endpoint.</summary>
        ValueTask ConnectAsync(CancellationToken ct = default);

        /// <summary>Connects to the supplied endpoint URL.</summary>
        ValueTask ConnectAsync(string endpointUrl, CancellationToken ct = default);

        /// <summary>Connects to the supplied configured endpoint.</summary>
        ValueTask ConnectAsync(ConfiguredEndpoint? endpoint, CancellationToken ct = default);

        /// <summary>Disconnects the active session.</summary>
        ValueTask DisconnectAsync(CancellationToken ct = default);

        /// <summary>Returns the URLs of the servers known to the LDS, excluding GDS instances.</summary>
        ValueTask<List<string>> GetDefaultServerUrlsAsync(
            LocalDiscoveryServerClient? lds,
            CancellationToken ct = default);

        /// <summary>Returns the URLs of GDS instances known to the LDS.</summary>
        ValueTask<List<string>> GetDefaultGdsUrlsAsync(
            LocalDiscoveryServerClient? lds,
            CancellationToken ct = default);

        /// <summary>Finds the application records with the supplied URI.</summary>
        /// <remarks>Calls the <c>FindApplications</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.4).</remarks>
        ValueTask<ArrayOf<ApplicationRecordDataType>> FindApplicationAsync(
            string? applicationUri,
            CancellationToken ct = default);

        /// <summary>Queries the GDS for servers matching the supplied criteria.</summary>
        /// <remarks>Calls the <c>QueryServers</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.6).</remarks>
        ValueTask<ArrayOf<ServerOnNetwork>> QueryServersAsync(
            uint maxRecordsToReturn,
            string? applicationName,
            string? applicationUri,
            string? productUri,
            ArrayOf<string> serverCapabilities,
            CancellationToken ct = default);

        /// <summary>Queries the GDS for servers matching the supplied criteria.</summary>
        /// <remarks>Calls the <c>QueryServers</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.6).</remarks>
        ValueTask<(ArrayOf<ServerOnNetwork> servers, DateTimeUtc lastCounterResetTime)> QueryServersAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string? applicationName,
            string? applicationUri,
            string? productUri,
            ArrayOf<string> serverCapabilities,
            CancellationToken ct = default);

        /// <summary>Queries the GDS for application registrations.</summary>
        /// <remarks>Calls the <c>QueryApplications</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.7).</remarks>
        ValueTask<(
            ArrayOf<ApplicationDescription> applications,
            DateTimeUtc lastCounterResetTime,
            uint nextRecordId)> QueryApplicationsAsync(
                uint startingRecordId,
                uint maxRecordsToReturn,
                string? applicationName,
                string? applicationUri,
                uint applicationType,
                string? productUri,
                ArrayOf<string> serverCapabilities,
                CancellationToken ct = default);

        /// <summary>Gets the registered application with the specified id.</summary>
        /// <remarks>Calls the <c>GetApplication</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.5).</remarks>
        ValueTask<ApplicationRecordDataType> GetApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default);

        /// <summary>Registers the supplied application with the GDS.</summary>
        /// <remarks>Calls the <c>RegisterApplication</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.2).</remarks>
        ValueTask<NodeId> RegisterApplicationAsync(
            ApplicationRecordDataType application,
            CancellationToken ct = default);

        /// <summary>Updates an existing application registration.</summary>
        /// <remarks>Calls the <c>UpdateApplication</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.3).</remarks>
        ValueTask UpdateApplicationAsync(
            ApplicationRecordDataType application,
            CancellationToken ct = default);

        /// <summary>Unregisters the application with the supplied id.</summary>
        /// <remarks>Calls the <c>UnregisterApplication</c> method on the GDS <c>DirectoryType</c> (OPC 10000-12 §7.5.8).</remarks>
        ValueTask UnregisterApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default);

        /// <summary>Lists the certificates issued for the supplied application.</summary>
        /// <remarks>Calls the <c>GetCertificates</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.10).</remarks>
        ValueTask<(ArrayOf<NodeId> certificateTypeIds, ArrayOf<ByteString> certificates)> GetCertificatesAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct = default);

        /// <summary>Checks the revocation status of the supplied certificate.</summary>
        /// <remarks>Calls the <c>CheckRevocationStatus</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.11).</remarks>
        ValueTask<(StatusCode certificateStatus, DateTimeUtc validityTime)> CheckRevocationStatusAsync(
            ByteString certificate,
            CancellationToken ct = default);

        /// <summary>Revokes the supplied certificate for an application.</summary>
        /// <remarks>Calls the <c>RevokeCertificate</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.9).</remarks>
        ValueTask RevokeCertificateAsync(
            NodeId applicationId,
            ByteString certificate,
            CancellationToken ct = default);

        /// <summary>Starts a new key-pair certificate request.</summary>
        /// <remarks>Calls the <c>StartNewKeyPairRequest</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.4).</remarks>
        ValueTask<NodeId> StartNewKeyPairRequestAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            string subjectName,
            ArrayOf<string> domainNames,
            string privateKeyFormat,
            char[] privateKeyPassword,
            CancellationToken ct = default);

        /// <summary>Starts a signing certificate request.</summary>
        /// <remarks>Calls the <c>StartSigningRequest</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.5).</remarks>
        ValueTask<NodeId> StartSigningRequestAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            ByteString certificateRequest,
            CancellationToken ct = default);

        /// <summary>Finishes the supplied certificate request.</summary>
        /// <remarks>Calls the <c>FinishRequest</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.6).</remarks>
        ValueTask<(ByteString publicKey, ByteString privateKey, ArrayOf<ByteString> issuerCertificates)> FinishRequestAsync(
                NodeId applicationId,
                NodeId requestId,
                CancellationToken ct = default);

        /// <summary>Lists the certificate groups for the supplied application.</summary>
        /// <remarks>Calls the <c>GetCertificateGroups</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.7).</remarks>
        ValueTask<ArrayOf<NodeId>> GetCertificateGroupsAsync(
            NodeId applicationId,
            CancellationToken ct = default);

        /// <summary>Returns the trust list node for the supplied certificate group.</summary>
        /// <remarks>Calls the <c>GetTrustList</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.8).</remarks>
        ValueTask<NodeId> GetTrustListAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            CancellationToken ct = default);

        /// <summary>Returns the certificate status for the supplied group / type.</summary>
        /// <remarks>Calls the <c>GetCertificateStatus</c> method on the GDS <c>CertificateDirectoryType</c> (OPC 10000-12 §7.6.12).</remarks>
        ValueTask<bool> GetCertificateStatusAsync(
            NodeId applicationId,
            NodeId certificateGroupId,
            NodeId certificateTypeId,
            CancellationToken ct = default);

        /// <summary>Reads the trust list referenced by the supplied id.</summary>
        /// <remarks>Reads the trust list via the file transfer methods of <c>TrustListType</c> (OPC 10000-12 §7.8).</remarks>
        ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId trustListId,
            CancellationToken ct = default);

        /// <summary>Reads the trust list referenced by the supplied id.</summary>
        /// <remarks>Reads the trust list via the file transfer methods of <c>TrustListType</c> (OPC 10000-12 §7.8).</remarks>
        ValueTask<TrustListDataType> ReadTrustListAsync(
            NodeId trustListId,
            long maxTrustListSize,
            CancellationToken ct = default);
    }
}
