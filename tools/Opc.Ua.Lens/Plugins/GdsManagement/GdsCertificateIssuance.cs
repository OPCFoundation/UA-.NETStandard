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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Plugins.Gds;

namespace UaLens.Plugins.GdsManagement;

/// <summary>
/// An application, issuing GDS and delivery configuration captured together.
/// Mutable endpoint and collection values never escape the private snapshot.
/// </summary>
internal sealed class GdsIssuanceTarget
{
    private GdsIssuanceTarget(RegisteredApplicationContext context, NodeId certificateTypeId, bool https)
    {
        m_context = Snapshot(context);
        CertificateTypeId = certificateTypeId;
        Https = https;
    }

    public NodeId ApplicationId => m_context.ApplicationId;

    public string ApplicationName => m_context.ApplicationName;

    public string GdsEndpointUrl => m_context.GdsEndpointUrl;

    public NodeId CertificateTypeId { get; }

    public bool Https { get; }

    public string Subject => string.IsNullOrWhiteSpace(m_context.CertificateSubjectName)
        ? "CN=" + ApplicationName
        : m_context.CertificateSubjectName;

    public ArrayOf<string> Domains => string.IsNullOrWhiteSpace(m_context.Domains)
        ? ArrayOf<string>.Empty
        : m_context.Domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public RegisteredApplicationContext DeliveryContext => Snapshot(m_context);

    public static GdsIssuanceTarget Create(
        RegisteredApp application,
        RegisteredApplicationContext? context,
        string currentGdsEndpointUrl,
        NodeId certificateTypeId,
        bool https)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (context is null)
        {
            throw new InvalidOperationException(
                "Register the selected application with this GDS and configure its delivery destinations first.");
        }
        if (application.ApplicationId.IsNull
            || application.ApplicationId != context.ApplicationId
            || !string.Equals(application.ApplicationUri, context.ApplicationUri, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The delivery configuration belongs to another application. Select that application or register "
                + "the selected application with its own delivery destinations.");
        }
        if (!SameEndpoint(application.GdsEndpointUrl, context.GdsEndpointUrl)
            || !SameEndpoint(currentGdsEndpointUrl, context.GdsEndpointUrl))
        {
            throw new InvalidOperationException(
                "The application and delivery configuration do not belong to this GDS endpoint. "
                + "Reconnect to the registering GDS, refresh, or register the application with this GDS.");
        }
        return new GdsIssuanceTarget(context, certificateTypeId, https);
    }

    public void ValidateEndpoint(string endpointUrl)
    {
        if (!SameEndpoint(GdsEndpointUrl, endpointUrl))
        {
            throw new InvalidOperationException(
                "The GDS endpoint changed. Refresh and select the application registered with this GDS "
                + "before issuing.");
        }
    }

    private static bool SameEndpoint(string left, string right)
    {
        return !string.IsNullOrWhiteSpace(left)
            && !string.IsNullOrWhiteSpace(right)
            && Uri.TryCreate(left, UriKind.Absolute, out Uri? leftUri)
            && Uri.TryCreate(right, UriKind.Absolute, out Uri? rightUri)
            && leftUri.Equals(rightUri);
    }

    private static RegisteredApplicationContext Snapshot(RegisteredApplicationContext context)
    {
        return context with
        {
            DiscoveryUrls = Array.AsReadOnly(context.DiscoveryUrls.ToArray()),
            ServerCapabilities = Array.AsReadOnly(context.ServerCapabilities.ToArray()),
            PushEndpoint = context.PushEndpoint is null ? null : (EndpointDescription)context.PushEndpoint.Clone()
        };
    }

    private readonly RegisteredApplicationContext m_context;
}

internal sealed record GdsIssuedCertificate(
    NodeId RequestId,
    ByteString PublicKey,
    ByteString PrivateKey,
    ArrayOf<ByteString> Issuers)
{
    public string PrivateKeyFormat => "PFX";
}

internal interface IGdsCertificateDelivery
{
    Task<string> DeliverAsync(
        GdsIssuanceTarget target,
        GdsIssuedCertificate certificate,
        CancellationToken cancellationToken);
}

internal interface IGdsCertificateIssuance
{
    Task<(GdsIssuedCertificate Certificate, string Delivery)> IssueAndDeliverAsync(
        GdsIssuanceTarget target,
        IGdsManagementClient client,
        IGdsCertificateDelivery delivery,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Issues and delivers using only one validated target, never live workspace selection.
/// </summary>
internal sealed class GdsCertificateIssuance : IGdsCertificateIssuance
{
    public async Task<(GdsIssuedCertificate Certificate, string Delivery)> IssueAndDeliverAsync(
        GdsIssuanceTarget target,
        IGdsManagementClient client,
        IGdsCertificateDelivery delivery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(delivery);
        cancellationToken.ThrowIfCancellationRequested();
        target.ValidateEndpoint(client.EndpointUrl);
        ArrayOf<NodeId> groups = await client.GetCertificateGroupsAsync(target.ApplicationId, cancellationToken)
            .ConfigureAwait(true);
        NodeId groupId = groups.Count > 0 ? groups[0] : NodeId.Null;
        target.ValidateEndpoint(client.EndpointUrl);
        NodeId requestId = await client.StartNewKeyPairRequestAsync(target, groupId, cancellationToken)
            .ConfigureAwait(true);
        for (int attempt = 0; attempt < 30; attempt++)
        {
            target.ValidateEndpoint(client.EndpointUrl);
            GdsIssuedCertificate certificate = await client.FinishRequestAsync(
                target.ApplicationId, requestId, cancellationToken).ConfigureAwait(true);
            if (!certificate.PublicKey.Memory.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                target.ValidateEndpoint(client.EndpointUrl);
                string detail = await delivery.DeliverAsync(target, certificate, cancellationToken)
                    .ConfigureAwait(true);
                return (certificate, detail);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        }
        throw new TimeoutException("FinishRequest did not produce a certificate within 30 seconds.");
    }
}
