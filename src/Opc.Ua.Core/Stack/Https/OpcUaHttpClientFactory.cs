/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
