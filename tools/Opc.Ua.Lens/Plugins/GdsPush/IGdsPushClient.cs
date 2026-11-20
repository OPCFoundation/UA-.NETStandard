/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;

namespace UaLens.Plugins.GdsPush;

/// <summary>
/// Server trust-list operations used by the push tool's mutation and refresh flows.
/// </summary>
internal interface IGdsPushClient
{
    ValueTask<TrustListDataType> ReadTrustListAsync(TrustListMasks masks, CancellationToken cancellationToken);

    ValueTask<CertificateCollection> GetRejectedListAsync(CancellationToken cancellationToken);

    ValueTask AddCertificateAsync(Certificate certificate, bool trusted, CancellationToken cancellationToken);

    ValueTask RemoveCertificateAsync(string thumbprint, bool trusted, CancellationToken cancellationToken);
}

internal sealed class GdsPushClientAdapter : IGdsPushClient
{
    public GdsPushClientAdapter(IServerPushConfigurationClient client)
    {
        m_client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public ValueTask<TrustListDataType> ReadTrustListAsync(
        TrustListMasks masks, CancellationToken cancellationToken)
        => m_client.ReadTrustListAsync(masks, 0, cancellationToken);

    public ValueTask<CertificateCollection> GetRejectedListAsync(CancellationToken cancellationToken)
        => m_client.GetRejectedListAsync(cancellationToken);

    public ValueTask AddCertificateAsync(Certificate certificate, bool trusted, CancellationToken cancellationToken)
        => m_client.AddCertificateAsync(certificate, trusted, cancellationToken);

    public ValueTask RemoveCertificateAsync(string thumbprint, bool trusted, CancellationToken cancellationToken)
        => m_client.RemoveCertificateAsync(thumbprint, trusted, cancellationToken);

    private readonly IServerPushConfigurationClient m_client;
}
