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
    public GdsManagementClientAdapter(GlobalDiscoveryServerClient client)
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

    private readonly GlobalDiscoveryServerClient m_client;
}
