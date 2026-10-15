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
