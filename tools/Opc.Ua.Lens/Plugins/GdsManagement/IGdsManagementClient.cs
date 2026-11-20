// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// Directory operations used by registration, refresh and certificate issuance.
/// </summary>
internal interface IGdsManagementClient
{
    string EndpointUrl { get; }

    ValueTask<ArrayOf<ApplicationDescription>> QueryApplicationsAsync(CancellationToken cancellationToken);

    ValueTask<ArrayOf<ApplicationRecordDataType>> FindApplicationAsync(
        string applicationUri, CancellationToken cancellationToken);

    ValueTask<NodeId> RegisterApplicationAsync(
        ApplicationRecordDataType application, CancellationToken cancellationToken);

    ValueTask UnregisterApplicationAsync(NodeId applicationId, CancellationToken cancellationToken);

    ValueTask<ArrayOf<NodeId>> GetCertificateGroupsAsync(NodeId applicationId, CancellationToken cancellationToken);

    ValueTask<NodeId> StartNewKeyPairRequestAsync(
        GdsIssuanceTarget target, NodeId groupId, CancellationToken cancellationToken);

    ValueTask<GdsIssuedCertificate> FinishRequestAsync(
        NodeId applicationId, NodeId requestId, CancellationToken cancellationToken);
}

internal sealed class GdsManagementClientAdapter : IGdsManagementClient
{
    public GdsManagementClientAdapter(IGlobalDiscoveryServerClient client)
    {
        m_client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string EndpointUrl => m_client.Session?.ConfiguredEndpoint?.Description.EndpointUrl ?? string.Empty;

    public async ValueTask<ArrayOf<ApplicationDescription>> QueryApplicationsAsync(CancellationToken cancellationToken)
    {
        (ArrayOf<ApplicationDescription> applications, _, _) = await m_client.QueryApplicationsAsync(
            0, 0, string.Empty, string.Empty, 0, string.Empty, ArrayOf<string>.Empty, cancellationToken)
            .ConfigureAwait(false);
        return applications;
    }

    public ValueTask<ArrayOf<ApplicationRecordDataType>> FindApplicationAsync(
        string applicationUri, CancellationToken cancellationToken)
        => m_client.FindApplicationAsync(applicationUri, cancellationToken);

    public ValueTask<NodeId> RegisterApplicationAsync(
        ApplicationRecordDataType application, CancellationToken cancellationToken)
        => m_client.RegisterApplicationAsync(application, cancellationToken);

    public ValueTask UnregisterApplicationAsync(NodeId applicationId, CancellationToken cancellationToken)
        => m_client.UnregisterApplicationAsync(applicationId, cancellationToken);

    public ValueTask<ArrayOf<NodeId>> GetCertificateGroupsAsync(
        NodeId applicationId, CancellationToken cancellationToken)
        => m_client.GetCertificateGroupsAsync(applicationId, cancellationToken);

    public ValueTask<NodeId> StartNewKeyPairRequestAsync(
        GdsIssuanceTarget target, NodeId groupId, CancellationToken cancellationToken)
        => m_client.StartNewKeyPairRequestAsync(
            target.ApplicationId, groupId, target.CertificateTypeId, target.Subject, target.Domains,
            "PFX", Array.Empty<char>(), cancellationToken);

    public async ValueTask<GdsIssuedCertificate> FinishRequestAsync(
        NodeId applicationId, NodeId requestId, CancellationToken cancellationToken)
    {
        (ByteString publicKey, ByteString privateKey, ArrayOf<ByteString> issuers) =
            await m_client.FinishRequestAsync(applicationId, requestId, cancellationToken).ConfigureAwait(false);
        return new GdsIssuedCertificate(requestId, publicKey, privateKey, issuers);
    }

    private readonly IGlobalDiscoveryServerClient m_client;
}
