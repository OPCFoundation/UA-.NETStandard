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
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// <c>HttpClient</c>-based implementation of
    /// <see cref="IWebApiClient"/>. Round-trips envelope-less
    /// <c>&lt;Service&gt;Request</c> / <c>&lt;Service&gt;Response</c>
    /// bodies via <see cref="WebApiBodyCodec"/> against an OPC UA
    /// server that exposes the REST binding (Part 6 §G.3
    /// "OpenAPI Mapping").
    /// </summary>
    public sealed class WebApiClient : IWebApiClient, IDisposable
    {
        private readonly HttpClient m_httpClient;
        private readonly bool m_ownsHttpClient;
        private readonly WebApiClientOptions m_options;
        private readonly IServiceMessageContext m_messageContext;
        private readonly string m_contentType;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new REST client with an explicit
        /// <c>HttpClient</c>. The client does not dispose
        /// <paramref name="httpClient"/>; the caller owns it.
        /// </summary>
        /// <param name="httpClient">The HTTP client to use.</param>
        /// <param name="options">Configuration options.</param>
        public WebApiClient(HttpClient httpClient, WebApiClientOptions? options = null)
            : this(httpClient, ownsHttpClient: false, options)
        {
        }

        private WebApiClient(
            HttpClient httpClient,
            bool ownsHttpClient,
            WebApiClientOptions? options)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            m_httpClient = httpClient;
            m_ownsHttpClient = ownsHttpClient;
            m_options = options ?? new WebApiClientOptions();
            m_messageContext = m_options.MessageContext
                ?? ServiceMessageContext.CreateEmpty(new ClientTelemetryContext());
            m_contentType = WebApiMediaType.FormatContentType(m_options.Encoding);

            if (m_options.BearerToken != null && m_options.BasicCredentials.HasValue)
            {
                throw new InvalidOperationException(
                    "Set either WebApiClientOptions.BearerToken or BasicCredentials, not both.");
            }

            if (m_options.BearerToken is not null)
            {
                m_httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", m_options.BearerToken);
            }
            else if (m_options.BasicCredentials is var basic && basic.HasValue)
            {
                string parameter = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(basic.Value.Username + ":" + basic.Value.Password));
                m_httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", parameter);
            }

            m_httpClient.DefaultRequestHeaders.Accept.Clear();
            m_httpClient.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(m_contentType));

            if (m_options.RequestTimeout.HasValue)
            {
                m_httpClient.Timeout = m_options.RequestTimeout.Value;
            }
        }

        /// <summary>
        /// Constructs a new REST client targeting
        /// <paramref name="baseAddress"/>. A new <c>HttpClient</c> is
        /// created and disposed by the returned client.
        /// </summary>
        /// <param name="baseAddress">
        /// The server's base URI (e.g. <c>https://server:4843/</c>).
        /// </param>
        /// <param name="options">Configuration options.</param>
        /// <returns>The new client.</returns>
        public static WebApiClient Create(Uri baseAddress, WebApiClientOptions? options = null)
        {
            if (baseAddress == null)
            {
                throw new ArgumentNullException(nameof(baseAddress));
            }

            // OPC UA-style schemes (opc.https://, opc.https+webapi://)
            // must be translated to plain https:// before the URL is
            // handed to HttpClient — HttpClient only understands the
            // registered transport schemes.
            Uri normalizedAddress = NormalizeOpcUaUrl(baseAddress);
            HttpClient httpClient = options?.HttpMessageHandler != null
                ? new HttpClient(options.HttpMessageHandler, disposeHandler: options.DisposeHandler)
                : new HttpClient();
            httpClient.BaseAddress = normalizedAddress;
            return new WebApiClient(httpClient, ownsHttpClient: true, options);
        }

        private static Uri NormalizeOpcUaUrl(Uri url)
        {
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcHttpsWebApi, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url.Scheme, Utils.UriSchemeOpcHttps, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeHttps };
                return builder.Uri;
            }
            return url;
        }

        /// <inheritdoc/>
        public Uri BaseAddress
            => m_httpClient.BaseAddress ?? throw new InvalidOperationException(
                "WebApiClient requires HttpClient.BaseAddress to be set.");

        /// <inheritdoc/>
        public WebApiEncoding Encoding => m_options.Encoding;

        /// <inheritdoc/>
        public async ValueTask<TResponse> InvokeAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken ct = default)
            where TRequest : IServiceRequest, new()
            where TResponse : IServiceResponse, new()
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ThrowIfDisposed();

            if (!WebApiServiceRoutes.TryGetByRequestType(typeof(TRequest), out WebApiServiceRoute route))
            {
                throw new InvalidOperationException(
                    $"No REST route is registered for request type '{typeof(TRequest).FullName}'.");
            }

            object response = await InvokeRouteUnsafeAsync(route, request, ct).ConfigureAwait(false);
            return (TResponse)response;
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
            "Routes to WebApiBodyCodec.DecodeBodyAsync(Type, ...) which constructs an " +
            "instance of route.ResponseType via Activator.CreateInstance. Callers that need " +
            "AOT should use the generic InvokeAsync<TRequest, TResponse> overload instead.")]
        public ValueTask<IServiceResponse> InvokeRouteAsync(
            WebApiServiceRoute route,
            IServiceRequest request,
            CancellationToken ct = default)
        {
            return InvokeRouteUnsafeAsync(route, request, ct);
        }

        // Trim-unsafe core used by both InvokeAsync<,> and InvokeRouteAsync.
        // Suppressed: the generic InvokeAsync<,> is trim-safe because TResponse
        // is statically rooted by the caller. The InvokeRouteAsync entry point
        // forwards the RequiresUnreferencedCode warning to its own callers.
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "Generic InvokeAsync<,> roots TResponse statically; non-generic " +
                "InvokeRouteAsync propagates the RequiresUnreferencedCode attribute to its callers.")]
        private async ValueTask<IServiceResponse> InvokeRouteUnsafeAsync(
            WebApiServiceRoute route,
            IServiceRequest request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!route.RequestType.IsInstanceOfType(request))
            {
                throw new ArgumentException(
                    $"Request of type '{request.GetType().FullName}' does not match the " +
                    $"route's RequestType '{route.RequestType.FullName}'.",
                    nameof(request));
            }
            ThrowIfDisposed();

            byte[] body = WebApiBodyCodec.EncodeBody(
                request,
                m_messageContext,
                WebApiMediaType.ToEncoderOptions(m_options.Encoding));

            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(m_contentType);

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, route.Path)
            {
                Content = content
            };

            using HttpResponseMessage response = await m_httpClient
                .SendAsync(requestMessage, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

#if NET5_0_OR_GREATER
            using Stream stream = await response.Content
                .ReadAsStreamAsync(ct)
                .ConfigureAwait(false);
#else
            using Stream stream = await response.Content
                .ReadAsStreamAsync()
                .ConfigureAwait(false);
#endif

            IEncodeable decoded = await WebApiBodyCodec
                .DecodeBodyAsync(route.ResponseType, stream, m_messageContext, s_clientDecoderOptions, ct)
                .ConfigureAwait(false);
            return (IServiceResponse)decoded;
        }

        // Decoder options applied to every inbound response. Clients
        // typically don't know all server namespace URIs up front, so
        // UpdateNamespaceTable=true lets the codec append unknown URIs
        // to the message context's NamespaceTable on the fly (otherwise
        // NodeIds whose namespace URI isn't already registered would
        // decode as NodeId.Null).
        private static readonly JsonDecoderOptions s_clientDecoderOptions = new()
        {
            UpdateNamespaceTable = true
        };

        // Strongly-typed delegates =====================================

        /// <inheritdoc/>
        public ValueTask<ReadResponse> ReadAsync(
            ReadRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<ReadRequest, ReadResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<WriteResponse> WriteAsync(
            WriteRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<WriteRequest, WriteResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistoryReadResponse> HistoryReadAsync(
            HistoryReadRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<HistoryReadRequest, HistoryReadResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<HistoryUpdateResponse> HistoryUpdateAsync(
            HistoryUpdateRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<HistoryUpdateRequest, HistoryUpdateResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<CallResponse> CallAsync(
            CallRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<CallRequest, CallResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<BrowseResponse> BrowseAsync(
            BrowseRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<BrowseRequest, BrowseResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<BrowseNextResponse> BrowseNextAsync(
            BrowseNextRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<BrowseNextRequest, BrowseNextResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            TranslateBrowsePathsToNodeIdsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<TranslateBrowsePathsToNodeIdsRequest, TranslateBrowsePathsToNodeIdsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<RegisterNodesResponse> RegisterNodesAsync(
            RegisterNodesRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<RegisterNodesRequest, RegisterNodesResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<UnregisterNodesResponse> UnregisterNodesAsync(
            UnregisterNodesRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<UnregisterNodesRequest, UnregisterNodesResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<FindServersResponse> FindServersAsync(
            FindServersRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<FindServersRequest, FindServersResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<GetEndpointsResponse> GetEndpointsAsync(
            GetEndpointsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<GetEndpointsRequest, GetEndpointsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<CreateSessionResponse> CreateSessionAsync(
            CreateSessionRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<CreateSessionRequest, CreateSessionResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ActivateSessionResponse> ActivateSessionAsync(
            ActivateSessionRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<ActivateSessionRequest, ActivateSessionResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<CloseSessionResponse> CloseSessionAsync(
            CloseSessionRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<CloseSessionRequest, CloseSessionResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<CancelResponse> CancelAsync(
            CancelRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<CancelRequest, CancelResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            CreateMonitoredItemsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<CreateMonitoredItemsRequest, CreateMonitoredItemsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            ModifyMonitoredItemsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<ModifyMonitoredItemsRequest, ModifyMonitoredItemsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<SetMonitoringModeResponse> SetMonitoringModeAsync(
            SetMonitoringModeRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<SetMonitoringModeRequest, SetMonitoringModeResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<SetTriggeringResponse> SetTriggeringAsync(
            SetTriggeringRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<SetTriggeringRequest, SetTriggeringResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            DeleteMonitoredItemsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<DeleteMonitoredItemsRequest, DeleteMonitoredItemsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<CreateSubscriptionResponse> CreateSubscriptionAsync(
            CreateSubscriptionRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<CreateSubscriptionRequest, CreateSubscriptionResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ModifySubscriptionResponse> ModifySubscriptionAsync(
            ModifySubscriptionRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<ModifySubscriptionRequest, ModifySubscriptionResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<SetPublishingModeResponse> SetPublishingModeAsync(
            SetPublishingModeRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<SetPublishingModeRequest, SetPublishingModeResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<PublishResponse> PublishAsync(
            PublishRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<PublishRequest, PublishResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<RepublishResponse> RepublishAsync(
            RepublishRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<RepublishRequest, RepublishResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            TransferSubscriptionsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<TransferSubscriptionsRequest, TransferSubscriptionsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public ValueTask<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            DeleteSubscriptionsRequest request,
            CancellationToken ct = default)
        {
            return InvokeAsync<DeleteSubscriptionsRequest, DeleteSubscriptionsResponse>(request, ct);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            if (m_ownsHttpClient)
            {
                m_httpClient.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(WebApiClient));
            }
        }

        /// <summary>
        /// Minimal <see cref="ITelemetryContext"/> for the client-side
        /// codec when callers don't supply one. Avoids forcing a
        /// dependency on the host's telemetry infrastructure.
        /// </summary>
        private sealed class ClientTelemetryContext : TelemetryContextBase
        {
            public ClientTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }
        }
    }
}
