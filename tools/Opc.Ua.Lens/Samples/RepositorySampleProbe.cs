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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace UaLens.Samples
{
    internal interface IRepositorySampleProbe
    {
        Task<ArrayOf<EndpointDescription>> DiscoverAsync(Uri endpoint, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Certificate-free, bounded GetEndpoints. Deliberately does not use Lens AppConfig:
    /// readiness must not initialize, modify or trust anything in the host PKI.
    /// </summary>
    internal sealed class RepositorySampleDiscoveryProbe : IRepositorySampleProbe
    {
        public RepositorySampleDiscoveryProbe(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        public async Task<ArrayOf<EndpointDescription>> DiscoverAsync(
            Uri endpoint,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri ||
                endpoint.Scheme != "opc.tcp" ||
                !endpoint.IsLoopback ||
                !string.IsNullOrEmpty(endpoint.UserInfo) ||
                endpoint.Query.Length != 0 ||
                endpoint.Fragment.Length != 0)
            {
                throw new ArgumentException("A sample probe requires a local OPC TCP endpoint.", nameof(endpoint));
            }
            var configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 2000;
            configuration.MaxMessageSize = 256 * 1024;
            configuration.MaxStringLength = 4096;
            configuration.MaxByteStringLength = 65536;
            configuration.MaxArrayLength = 256;
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                endpoint, configuration, m_telemetry, ct: cancellationToken).ConfigureAwait(false);
            return await client.GetEndpointsAsync(default, cancellationToken).ConfigureAwait(false);
        }

        private readonly ITelemetryContext m_telemetry;
    }

    internal static class RepositorySampleEvidenceVerifier
    {
        public static async Task<RepositorySampleEvidence?> VerifyAsync(
            RepositorySampleLaunch launch,
            ArrayOf<EndpointDescription> endpoints,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(launch);
            cancellationToken.ThrowIfCancellationRequested();
            if (endpoints.Count == 0)
            {
                return null;
            }
            if (endpoints.Count > 64)
            {
                throw new RepositorySampleException(
                    RepositorySampleFailure.Readiness, "The sample advertised too many endpoints.");
            }
            bool expectedApplication = false;
            for (int index = 0; index < endpoints.Count; index++)
            {
                EndpointDescription candidate = endpoints[index];
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate is null)
                {
                    throw new RepositorySampleException(
                        RepositorySampleFailure.Readiness, "Discovery returned an empty endpoint entry.");
                }
                if (!Uri.TryCreate(candidate.EndpointUrl, UriKind.Absolute, out Uri? endpoint) ||
                    endpoint.Scheme != launch.Endpoint.Scheme ||
                    !endpoint.IsLoopback ||
                    endpoint.Port != launch.Endpoint.Port ||
                    endpoint.AbsolutePath != launch.Endpoint.AbsolutePath ||
                    endpoint.UserInfo.Length != 0 ||
                    endpoint.Query.Length != 0 ||
                    endpoint.Fragment.Length != 0 ||
                    candidate.Server is not ApplicationDescription application ||
                    application.ApplicationType is not (ApplicationType.Server or ApplicationType.ClientAndServer) ||
                    application.ApplicationName.Text != launch.Descriptor.ApplicationName ||
                    application.ProductUri != launch.Descriptor.ProductUri)
                {
                    continue;
                }
                bool expectedUri = false;
                for (int uriIndex = 0; uriIndex < launch.ApplicationUris.Count; uriIndex++)
                {
                    string uri = launch.ApplicationUris[uriIndex];
                    expectedUri |= string.Equals(application.ApplicationUri, uri, StringComparison.Ordinal);
                }
                if (!expectedUri)
                {
                    continue;
                }
                expectedApplication = true;
                if (candidate.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    candidate.SecurityPolicyUri is not (
                        SecurityPolicies.Basic256Sha256 or
                        SecurityPolicies.Aes128_Sha256_RsaOaep or
                        SecurityPolicies.Aes256_Sha256_RsaPss))
                {
                    continue;
                }
                if (await launch.Files.ContainsPublicCertificateAsync(
                    candidate.ServerCertificate, launch.Descriptor.Id, cancellationToken)
                    .ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new RepositorySampleEvidence(
                        endpoint,
                        application.ApplicationUri!,
                        launch.Descriptor.ApplicationName,
                        launch.Descriptor.ProductUri,
                        ByteString.From(SHA256.HashData(candidate.ServerCertificate.Span)));
                }
            }
            if (!expectedApplication)
            {
                throw new RepositorySampleException(
                    RepositorySampleFailure.Readiness,
                    "Discovery advertised a different endpoint or application; this is not the owned sample.");
            }
            return null;
        }
    }
}
