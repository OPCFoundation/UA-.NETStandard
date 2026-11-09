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

using Opc.Ua.Client;
using Opc.Ua.Client.Discovery;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Shared strict application identity and explicit endpoint discovery; all sessions use the one configured user.
/// </summary>
internal sealed class ConsoleConnections(IServiceProvider services, ConsoleOptions options) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose()
    {
        m_initialization.Dispose();
    }

    /// <summary>
    /// Connects to an explicitly configured endpoint with hostname checking and no insecure fallback.
    /// </summary>
    internal async Task<ManagedSession> ConnectAsync(string url, CancellationToken cancellationToken)
    {
        await m_initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!m_initialized)
            {
                ApplicationConfiguration configuration = await services
                    .GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                    .GetAsync(cancellationToken).ConfigureAwait(false);
                TransportQuotas quotas = configuration.TransportQuotas ??
                    throw new InvalidOperationException("Application transport quotas are required.");
                quotas.MaxMessageSize = 16 * 1024 * 1024;
                quotas.MaxByteStringLength = 16 * 1024 * 1024;
                quotas.MaxArrayLength = 100_000;
                configuration.SecurityConfiguration.TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.GetFullPath(options.TrustedPeerStore)
                };
                configuration.SecurityConfiguration.TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.GetFullPath(options.TrustedIssuerStore)
                };
                configuration.SecurityConfiguration.RejectedCertificateStore = new CertificateStoreIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.GetFullPath(options.RejectedCertificateStore)
                };
                await configuration.CertificateManager.UpdateAsync(configuration.SecurityConfiguration,
                    ct: cancellationToken).ConfigureAwait(false);
                m_initialized = true;
            }
        }
        finally
        {
            m_initialization.Release();
        }
        ArrayOf<EndpointDescription> endpoints = await services.GetRequiredService<IOpcUaDiscoveryService>()
            .GetEndpointsAsync(url, ct: cancellationToken).ConfigureAwait(false);
        EndpointDescription selected = endpoints.Span.ToArray().FirstOrDefault(static endpoint =>
            endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt &&
            endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256 &&
            endpoint.UserIdentityTokens.Contains(static policy => policy.TokenType == UserTokenType.Certificate))
            ?? throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected,
                "No encrypted X.509 endpoint is offered.");
        Uri requested = new(url);
        Uri advertised = new(selected.EndpointUrl ??
            throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected, "Endpoint URL is absent."));
        if (advertised.Scheme != requested.Scheme)
        {
            throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected);
        }
        selected = CoreUtils.Clone(selected)!;
        selected.EndpointUrl = new UriBuilder(advertised)
        {
            Host = requested.Host,
            Port = requested.Port
        }.Uri.ToString();
        ApplicationConfiguration application = await services
            .GetRequiredService<IOpcUaApplicationConfigurationProvider>()
            .GetAsync(cancellationToken).ConfigureAwait(false);
        return await services.GetRequiredService<IManagedSessionFactory>()
            .ConnectAsync(new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(application)),
                cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Single-flight certificate/trust initialization.</summary>
    private readonly SemaphoreSlim m_initialization = new(1, 1);
    /// <summary>Whether explicit trust stores have been applied.</summary>
    private bool m_initialized;
}
