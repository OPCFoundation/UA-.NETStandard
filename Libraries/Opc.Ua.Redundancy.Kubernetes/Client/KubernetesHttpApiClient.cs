/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Kubernetes
{
    internal sealed class KubernetesHttpApiClient : IKubernetesApiClient, IDisposable
    {
        public KubernetesHttpApiClient(string host, int port, string namespaceName, string token, string caPath)
            : this(CreateHttpClient(host, port, token, caPath), namespaceName, ownsClient: true)
        {
        }

        internal KubernetesHttpApiClient(HttpClient httpClient, string namespaceName, bool ownsClient = false)
        {
            m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            DefaultNamespace = namespaceName ?? throw new ArgumentNullException(nameof(namespaceName));
            m_ownsClient = ownsClient;
        }

        public bool IsInCluster => true;

        public string DefaultNamespace { get; }

        public async ValueTask<KubernetesLease?> GetLeaseAsync(string namespaceName, string name, CancellationToken ct)
        {
            using HttpResponseMessage response = await m_httpClient
                .GetAsync(new Uri(LeasePath(namespaceName, name), UriKind.Relative), ct)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            return await ReadAsync(response, KubernetesJsonContext.Default.KubernetesLease, ct).ConfigureAwait(false);
        }

        public async ValueTask<KubernetesLease> CreateLeaseAsync(
            string namespaceName,
            KubernetesLease lease,
            CancellationToken ct)
        {
            using StringContent content = JsonContent(lease, KubernetesJsonContext.Default.KubernetesLease);
            using HttpResponseMessage response = await m_httpClient
                .PostAsync(
                    new Uri($"apis/coordination.k8s.io/v1/namespaces/{Escape(namespaceName)}/leases", UriKind.Relative),
                    content,
                    ct)
                .ConfigureAwait(false);
            return await ReadAsync(response, KubernetesJsonContext.Default.KubernetesLease, ct).ConfigureAwait(false);
        }

        public async ValueTask<KubernetesLease> ReplaceLeaseAsync(
            string namespaceName,
            string name,
            KubernetesLease lease,
            CancellationToken ct)
        {
            using StringContent content = JsonContent(lease, KubernetesJsonContext.Default.KubernetesLease);
            using HttpResponseMessage response = await m_httpClient
                .PutAsync(new Uri(LeasePath(namespaceName, name), UriKind.Relative), content, ct)
                .ConfigureAwait(false);
            return await ReadAsync(response, KubernetesJsonContext.Default.KubernetesLease, ct).ConfigureAwait(false);
        }

        public async ValueTask DeleteLeaseAsync(string namespaceName, string name, CancellationToken ct)
        {
            using HttpResponseMessage response = await m_httpClient
                .DeleteAsync(new Uri(LeasePath(namespaceName, name), UriKind.Relative), ct)
                .ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            }
        }

        public async ValueTask<KubernetesEndpointSliceList> ListEndpointSlicesAsync(
            string namespaceName,
            string serviceName,
            CancellationToken ct)
        {
            string selector = Uri.EscapeDataString("kubernetes.io/service-name=" + serviceName);
            string path = $"apis/discovery.k8s.io/v1/namespaces/{Escape(namespaceName)}/endpointslices" +
                $"?labelSelector={selector}";
            using HttpResponseMessage response = await m_httpClient
                .GetAsync(new Uri(path, UriKind.Relative), ct)
                .ConfigureAwait(false);
            return await ReadAsync(response, KubernetesJsonContext.Default.KubernetesEndpointSliceList, ct)
                .ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (m_ownsClient)
            {
                m_httpClient.Dispose();
            }
        }

        private static HttpClient CreateHttpClient(string host, int port, string token, string caPath)
        {
            // Ownership transfers to HttpClient via disposeHandler; TODO remove when analyzer recognizes it.
            // CA5400: Kubernetes in-cluster CA bundles usually do not publish revocation information.
            // TODO: add an option to enforce revocation when the cluster CA supports it.
#pragma warning disable CA2000, CA5400
            var handler = new HttpClientHandler();
#pragma warning restore CA2000, CA5400
            if (File.Exists(caPath))
            {
                X509Certificate2 root = X509CertificateLoader.LoadCertificateFromFile(caPath);
                handler.ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    ValidateServerCertificate(
                        root,
                        request.RequestUri?.Host ?? host,
                        certificate,
                        chain,
                        errors);
            }

#pragma warning disable CA2000, CA5400
            var client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri($"https://{host}:{port}/")
            };
#pragma warning restore CA2000, CA5400
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        internal static bool ValidateServerCertificate(
            X509Certificate2 root,
            string expectedHost,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors errors)
        {
            if (string.IsNullOrWhiteSpace(expectedHost))
            {
                return false;
            }

            if (certificate == null)
            {
                return false;
            }
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            using var customChain = new X509Chain();
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.CustomTrustStore.Add(root);
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            using var serverCertificate = new X509Certificate2(certificate);
            return customChain.Build(serverCertificate) &&
                serverCertificate.MatchesHostname(expectedHost);
        }

        private static StringContent JsonContent<T>(
            T value,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        {
            string json = JsonSerializer.Serialize(value, typeInfo);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private static async ValueTask<T> ReadAsync<T>(
            HttpResponseMessage response,
            System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
            CancellationToken ct)
        {
            await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
            using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            T? value = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct).ConfigureAwait(false);
            return value ??
                throw new HttpRequestException(
                    "Kubernetes API returned an empty body.",
                    null,
                    response.StatusCode);
        }

        private static async ValueTask EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string message = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new HttpRequestException(message, null, HttpStatusCode.Conflict);
            }
            throw new HttpRequestException(message, null, response.StatusCode);
        }

        private static string LeasePath(string namespaceName, string name)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"apis/coordination.k8s.io/v1/namespaces/{Escape(namespaceName)}/leases/{Escape(name)}");
        }

        private static string Escape(string value)
        {
            return Uri.EscapeDataString(value);
        }

        private readonly HttpClient m_httpClient;
        private readonly bool m_ownsClient;
    }
}
