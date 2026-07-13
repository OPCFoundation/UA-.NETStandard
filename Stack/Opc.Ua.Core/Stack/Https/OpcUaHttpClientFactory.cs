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
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Well-known HTTP client configuration values used by OPC UA HTTPS bindings.
    /// </summary>
    public static class OpcUaHttpClientDefaults
    {
        /// <summary>
        /// The default named <see cref="HttpClient"/> used by OPC UA clients.
        /// </summary>
        public const string ClientName = "Opc.Ua.Client";
    }

    /// <summary>
    /// Creates <see cref="HttpClient"/> instances for OPC UA HTTPS transport channels.
    /// </summary>
    public interface IOpcUaHttpClientFactory
    {
        /// <summary>
        /// Creates a client for the supplied name.
        /// </summary>
        /// <param name="name">The named client or fallback cache key.</param>
        /// <returns>A configured <see cref="HttpClient"/>.</returns>
        HttpClient CreateClient(string name);
    }

    /// <summary>
    /// Default <see cref="IOpcUaHttpClientFactory"/> implementation.
    /// </summary>
    /// <remarks>
    /// When constructed by dependency injection this type delegates to the host's
    /// <see cref="IHttpClientFactory"/>. When constructed directly it provides a
    /// process-wide fallback cache keyed by the requested name.
    /// </remarks>
    public sealed class DefaultOpcUaHttpClientFactory : IOpcUaHttpClientFactory
    {
        /// <summary>
        /// Creates a standalone factory with process-wide fallback clients.
        /// </summary>
        public DefaultOpcUaHttpClientFactory()
        {
        }

        /// <summary>
        /// Creates a factory that delegates to <paramref name="httpClientFactory"/>.
        /// </summary>
        /// <param name="httpClientFactory">The host HTTP client factory.</param>
        /// <exception cref="ArgumentNullException"><paramref name="httpClientFactory"/> is <c>null</c>.</exception>
        public DefaultOpcUaHttpClientFactory(IHttpClientFactory httpClientFactory)
        {
            m_httpClientFactory = httpClientFactory
                ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        /// <summary>
        /// Shared standalone instance used when no dependency-injection container is available.
        /// </summary>
        public static DefaultOpcUaHttpClientFactory Shared { get; } = new();

        /// <inheritdoc/>
        public HttpClient CreateClient(string name)
        {
            return CreateClient(name, CreateDefaultFallbackClient);
        }

        internal HttpClient CreateClient(string name, Func<HttpClient> fallbackFactory)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Value required", nameof(name));
            }
            if (fallbackFactory == null)
            {
                throw new ArgumentNullException(nameof(fallbackFactory));
            }

            if (m_httpClientFactory != null)
            {
                return m_httpClientFactory.CreateClient(name);
            }

            return s_fallbackClients.GetOrAdd(
                name,
                _ => new Lazy<HttpClient>(
                    fallbackFactory,
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        private static HttpClient CreateDefaultFallbackClient()
        {
            return new HttpClient();
        }

        private readonly IHttpClientFactory? m_httpClientFactory;

        private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> s_fallbackClients = new(
            StringComparer.Ordinal);
    }
}
