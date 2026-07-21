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
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding.Http
{
    /// <summary>
    /// A live HTTP binding channel. It executes read (GET), write (PUT/method),
    /// action (POST/method), observe and event operations with bounded timeouts
    /// and payload sizes, cooperative cancellation, HTTP-to-<see cref="StatusCode"/>
    /// mapping and credential-provider-driven authentication.
    /// </summary>
    internal sealed class HttpWotBindingChannel : IWotBindingChannel
    {
        public HttpWotBindingChannel(
            HttpClient client,
            bool ownsClient,
            bool manualRedirects,
            WotCompiledForm form,
            WotExecutorContext context,
            HttpWotBindingOptions options)
        {
            m_client = client;
            m_ownsClient = ownsClient;
            m_manualRedirects = manualRedirects;
            m_form = form;
            m_context = context;
            m_options = options;
            context.Codecs.TrySelect(form.Payload.ContentType, out m_codec);
            m_baseTarget = form.Addressing.Target;
        }

        public WotCompiledForm Form => m_form;

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            (StatusCode status, byte[] body, string? error) =
                await SendAsync(HttpMethod.Get, null, cancellationToken).ConfigureAwait(false);
            if (!StatusCode.IsGood(status))
            {
                return new WotReadResult(status, DataValue.FromStatusCode(status), error);
            }
            WotDecodeResult decoded = m_codec.Decode(body, m_form.Payload);
            if (!decoded.Success)
            {
                return new WotReadResult(
                    StatusCodes.BadDecodingError, DataValue.FromStatusCode(StatusCodes.BadDecodingError), decoded.Error);
            }
            return new WotReadResult(
                StatusCodes.Good,
                new DataValue(decoded.Value, StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now));
        }

        public async ValueTask<WotWriteResult> WriteAsync(
            DataValue value, CancellationToken cancellationToken = default)
        {
            WotEncodeResult encoded = m_codec.Encode(value.WrappedValue, m_form.Payload);
            if (!encoded.Success)
            {
                return new WotWriteResult(StatusCodes.BadEncodingError, encoded.Error);
            }
            HttpMethod method = ResolveMethod("PUT");
            (StatusCode status, _, string? error) =
                await SendAsync(method, encoded.Data, cancellationToken).ConfigureAwait(false);
            return new WotWriteResult(status, error);
        }

        public async ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            ReadOnlyMemory<byte>? content = null;
            if (inputs is { Count: > 0 })
            {
                WotEncodeResult encoded = m_codec.Encode(inputs[0], m_form.Payload);
                if (!encoded.Success)
                {
                    return new WotInvokeResult(StatusCodes.BadEncodingError, null, encoded.Error);
                }
                content = encoded.Data;
            }
            HttpMethod method = ResolveMethod("POST");
            (StatusCode status, byte[] body, string? error) =
                await SendAsync(method, content, cancellationToken).ConfigureAwait(false);
            if (!StatusCode.IsGood(status))
            {
                return new WotInvokeResult(status, null, error);
            }
            if (body.Length == 0)
            {
                return new WotInvokeResult(StatusCodes.Good, Array.Empty<DataValue>());
            }
            WotDecodeResult decoded = m_codec.Decode(body, m_form.Payload);
            var output = new DataValue(
                decoded.Success ? decoded.Value : Variant.Null,
                decoded.Success ? StatusCodes.Good : StatusCodes.BadDecodingError,
                DateTimeUtc.Now, DateTimeUtc.Now);
            return new WotInvokeResult(StatusCodes.Good, new[] { output });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Ownership of the subscription is transferred to the caller, who disposes it.")]
        public ValueTask<IWotSubscription> ObserveAsync(
            Action<WotNotification> onNotification, CancellationToken cancellationToken = default)
        {
            if (onNotification is null)
            {
                throw new ArgumentNullException(nameof(onNotification));
            }
            var subscription = new PollingWotSubscription(
                m_form,
                async token =>
                {
                    WotReadResult result = await ReadAsync(token).ConfigureAwait(false);
                    if (result.Success)
                    {
                        onNotification(new WotNotification(result.Value));
                    }
                },
                m_options.ObserveInterval,
                // A transient poll fault is reported as a Bad-status notification
                // so consumers observe the fault without the poll loop faulting.
                onError: _ => onNotification(new WotNotification(
                    DataValue.FromStatusCode(StatusCodes.BadCommunicationError))));
            return new ValueTask<IWotSubscription>(subscription);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
            => ObserveAsync(onEvent, cancellationToken);

        public ValueTask DisposeAsync()
        {
            if (m_ownsClient)
            {
                m_client.Dispose();
            }
            return default;
        }

        private HttpMethod ResolveMethod(string fallback)
        {
            string method = string.IsNullOrEmpty(m_form.OperationInfo.Method)
                ? fallback : m_form.OperationInfo.Method;
            return new HttpMethod(method.ToUpperInvariant());
        }

        private async ValueTask<(StatusCode Status, byte[] Body, string? Error)> SendAsync(
            HttpMethod method, ReadOnlyMemory<byte>? content, CancellationToken cancellationToken)
        {
            await EnsureCredentialAsync(cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_context.Bounds.DefaultTimeout);
            try
            {
                if (!Uri.TryCreate(m_baseTarget, UriKind.Absolute, out Uri? current) || current is null)
                {
                    return (StatusCodes.BadInvalidArgument, Array.Empty<byte>(),
                        "The HTTP target is not a valid absolute URI.");
                }
                Uri origin = current;
                HttpMethod currentMethod = method;
                ReadOnlyMemory<byte>? currentContent = content;
                int redirectsRemaining = m_manualRedirects ? Math.Max(0, m_options.MaxAutomaticRedirects) : 0;
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    visited.Add(current.AbsoluteUri);
                    // Custom header / query credentials are only applied while the
                    // request stays on the original origin; a cross-origin redirect
                    // drops them so they never leak to a different host.
                    bool sameOrigin = IsSameOrigin(origin, current);
                    Uri requestUri = sameOrigin ? AppendCredentialQuery(current) : current;
                    HopResult hop = await SendOnceAsync(
                        currentMethod, requestUri, sameOrigin, currentContent, timeout.Token).ConfigureAwait(false);

                    if (hop.Redirect is null)
                    {
                        return (hop.Status, hop.Body, hop.Error);
                    }

                    if (redirectsRemaining <= 0)
                    {
                        return (StatusCodes.BadCommunicationError, Array.Empty<byte>(),
                            "The HTTP redirect limit was exceeded.");
                    }
                    Uri? next = ResolveRedirectTarget(current, hop.Location, out string? redirectError);
                    if (next is null)
                    {
                        return (StatusCodes.BadSecurityChecksFailed, Array.Empty<byte>(), redirectError);
                    }
                    if (visited.Contains(next.AbsoluteUri))
                    {
                        return (StatusCodes.BadCommunicationError, Array.Empty<byte>(),
                            "The HTTP redirect chain contains a loop.");
                    }
                    redirectsRemaining--;
                    // 303 (and, per browser convention, 301/302) turn the follow-up
                    // request into a bodyless GET; 307/308 preserve method and body.
                    if (hop.Redirect is System.Net.HttpStatusCode.MovedPermanently or
                        System.Net.HttpStatusCode.Found or System.Net.HttpStatusCode.SeeOther)
                    {
                        currentMethod = HttpMethod.Get;
                        currentContent = null;
                    }
                    current = next;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (StatusCodes.BadTimeout, Array.Empty<byte>(), "The HTTP request timed out.");
            }
            catch (HttpRequestException ex)
            {
                return (StatusCodes.BadCommunicationError, Array.Empty<byte>(), ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return (StatusCodes.BadEncodingLimitsExceeded, Array.Empty<byte>(), ex.Message);
            }
        }

        /// <summary>The outcome of a single request hop: either a terminal result or a redirect.</summary>
        private readonly struct HopResult
        {
            private HopResult(
                System.Net.HttpStatusCode? redirect, Uri? location,
                StatusCode status, byte[] body, string? error)
            {
                Redirect = redirect;
                Location = location;
                Status = status;
                Body = body;
                Error = error;
            }

            public System.Net.HttpStatusCode? Redirect { get; }

            public Uri? Location { get; }

            public StatusCode Status { get; }

            public byte[] Body { get; }

            public string? Error { get; }

            public static HopResult Terminal(StatusCode status, byte[] body, string? error)
                => new HopResult(null, null, status, body, error);

            public static HopResult RedirectTo(System.Net.HttpStatusCode redirect, Uri? location)
                => new HopResult(redirect, location, StatusCodes.Good, Array.Empty<byte>(), null);
        }

        private async Task<HopResult> SendOnceAsync(
            HttpMethod method, Uri requestUri, bool sameOrigin,
            ReadOnlyMemory<byte>? content, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, requestUri);
            ApplyHeaders(request, sameOrigin);
            if (content is { } body && method != HttpMethod.Get && method != HttpMethod.Head)
            {
                var byteContent = new ByteArrayContent(body.ToArray());
                if (!string.IsNullOrEmpty(m_form.Payload.ContentType))
                {
                    byteContent.Headers.TryAddWithoutValidation("Content-Type", m_form.Payload.ContentType);
                }
                request.Content = byteContent;
            }

            using HttpResponseMessage response = await m_client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (m_manualRedirects && IsRedirect(response.StatusCode))
            {
                return HopResult.RedirectTo(response.StatusCode, response.Headers.Location);
            }

            StatusCode status = HttpStatusMapper.Map(response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                return HopResult.Terminal(status, Array.Empty<byte>(),
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            byte[] payload = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
            return HopResult.Terminal(StatusCodes.Good, payload, null);
        }

        private static bool IsRedirect(System.Net.HttpStatusCode status)
            => status is System.Net.HttpStatusCode.MovedPermanently or
                         System.Net.HttpStatusCode.Found or
                         System.Net.HttpStatusCode.SeeOther or
                         System.Net.HttpStatusCode.TemporaryRedirect or
                         System.Net.HttpStatusCode.PermanentRedirect;

        private Uri? ResolveRedirectTarget(Uri current, Uri? location, out string? error)
        {
            error = null;
            if (location is null)
            {
                error = "The HTTP redirect response carried no Location header.";
                return null;
            }
            if (!location.IsAbsoluteUri)
            {
                location = new Uri(current, location);
            }
            if (!string.Equals(location.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(location.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = $"The HTTP redirect targets a disallowed scheme '{location.Scheme}'.";
                return null;
            }
            if (string.Equals(current.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(location.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !m_options.AllowInsecureRedirectDowngrade)
            {
                error = "The HTTP redirect downgrades https to http, which is refused.";
                return null;
            }
            return location;
        }

        private static bool IsSameOrigin(Uri a, Uri b)
            => string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
               a.Port == b.Port;

        private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int max = m_context.Bounds.MaxPayloadBytes;
            int total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > max)
                {
                    throw new InvalidOperationException(
                        $"The HTTP response exceeds the maximum payload size of {max} bytes.");
                }
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }

        private async ValueTask EnsureCredentialAsync(CancellationToken cancellationToken)
        {
            Task task;
            lock (m_credentialLock)
            {
                // Start (or reuse) a single shared resolution. Concurrent callers
                // all await the same task, so the resolved credential and the
                // effective target are published exactly once and no request is
                // ever sent before that state is ready.
                task = m_credentialTask ??= ResolveCredentialAsync(cancellationToken);
            }
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Failure retry policy: a failed (or cancelled) resolution is not
                // cached, so the next request re-attempts resolution instead of
                // being permanently wedged on the fault.
                lock (m_credentialLock)
                {
                    if (ReferenceEquals(m_credentialTask, task))
                    {
                        m_credentialTask = null;
                    }
                }
                throw;
            }
        }

        private async Task ResolveCredentialAsync(CancellationToken cancellationToken)
        {
            WotCredential? credential = null;
            if (!m_form.Security.IsEmpty)
            {
                credential = await m_context.Credentials
                    .ResolveAsync(m_form.Security[0], cancellationToken).ConfigureAwait(false);
            }
            // Publish the resolved credential only after resolution has completed. A
            // caller reads m_credential in SendAsync only after awaiting the shared
            // task, so it can never observe a half-initialized state or send a
            // request without the resolved credential applied.
            m_credential = credential;
        }

        private void ApplyHeaders(HttpRequestMessage request, bool includeCredentials)
        {
            // A cross-origin redirect must not carry any custom (potentially
            // credential-bearing) header, so both the caller's default headers and
            // the resolved credential headers are only applied on the original
            // origin.
            if (!includeCredentials)
            {
                return;
            }
            if (m_options.DefaultHeaders is { Count: > 0 })
            {
                foreach (KeyValuePair<string, string> header in m_options.DefaultHeaders)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            if (m_credential is { } credential)
            {
                foreach (KeyValuePair<string, string> header in credential.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        private Uri AppendCredentialQuery(Uri target)
        {
            WotCredential? credential = m_credential;
            if (credential is null || credential.QueryParameters.Count == 0)
            {
                return target;
            }
            var query = new StringBuilder();
            foreach (KeyValuePair<string, string> parameter in credential.QueryParameters)
            {
                if (query.Length > 0)
                {
                    query.Append('&');
                }
                query.Append(Uri.EscapeDataString(parameter.Key)).Append('=')
                    .Append(Uri.EscapeDataString(parameter.Value));
            }
            var builder = new UriBuilder(target);
            builder.Query = string.IsNullOrEmpty(builder.Query)
                ? query.ToString()
                : builder.Query.TrimStart('?') + "&" + query;
            return builder.Uri;
        }

        private readonly HttpClient m_client;
        private readonly bool m_ownsClient;
        private readonly bool m_manualRedirects;
        private readonly WotCompiledForm m_form;
        private readonly WotExecutorContext m_context;
        private readonly HttpWotBindingOptions m_options;
        private readonly IWotPayloadCodec m_codec;
        private readonly string m_baseTarget;
        private WotCredential? m_credential;
        private readonly object m_credentialLock = new object();
        private Task? m_credentialTask;
    }
}
