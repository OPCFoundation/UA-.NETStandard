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
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings.Http
{
    /// <summary>
    /// A live HTTP binding channel. It executes read (GET/method), write (PUT/method),
    /// action (POST/method), observe and event operations with bounded timeouts
    /// and payload sizes, cooperative cancellation, HTTP-to-<see cref="StatusCode"/>
    /// mapping and credential-provider-driven authentication.
    /// </summary>
    internal sealed class HttpWotBindingChannel : IWotContextualBindingChannel
    {
        public HttpWotBindingChannel(
            HttpClient client,
            bool ownsClient,
            bool manualRedirects,
            ImmutableArray<KeyValuePair<string, string>> defaultHeaders,
            WotCompiledForm form,
            WotExecutorContext context,
            HttpWotBindingOptions options,
            IWotPayloadCodec codec)
        {
            m_client = client;
            m_ownsClient = ownsClient;
            m_manualRedirects = manualRedirects;
            m_defaultHeaders = defaultHeaders;
            Form = form;
            m_context = context;
            m_options = options;
            m_codec = codec;
            m_baseTarget = form.Addressing.Target;
        }

        public WotCompiledForm Form { get; }

        public async ValueTask<WotReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            (StatusCode status, byte[] body, string? error) =
                await SendAsync(ResolveMethod("GET"), null, cancellationToken).ConfigureAwait(false);
            if (!StatusCode.IsGood(status))
            {
                return new WotReadResult(status, DataValue.FromStatusCode(status), error);
            }
            WotDecodeResult decoded = m_codec.Decode(body, Form.Payload);
            if (!decoded.Success)
            {
                return new WotReadResult(
                    StatusCodes.BadDecodingError,
                    DataValue.FromStatusCode(StatusCodes.BadDecodingError),
                    decoded.Error);
            }
            return new WotReadResult(
                StatusCodes.Good,
                new DataValue(decoded.Value, StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now));
        }

        public async ValueTask<WotWriteResult> WriteAsync(
            DataValue value, CancellationToken cancellationToken = default)
        {
            WotEncodeResult encoded = m_codec.Encode(value.WrappedValue, Form.Payload);
            if (!encoded.Success)
            {
                return new WotWriteResult(StatusCodes.BadEncodingError, encoded.Error);
            }
            HttpMethod method = ResolveMethod("PUT");
            (StatusCode status, _, string? error) =
                await SendAsync(method, encoded.Data, cancellationToken).ConfigureAwait(false);
            return new WotWriteResult(status, error);
        }

        public ValueTask<WotInvokeResult> InvokeAsync(
            IReadOnlyList<Variant> inputs, CancellationToken cancellationToken = default)
        {
            if (inputs is null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }
            var values = new Variant[inputs.Count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = inputs[index];
            }
            return InvokeAsync(new WotInvokeRequest(values, m_context.MessageContext), cancellationToken);
        }

        public async ValueTask<WotInvokeResult> InvokeAsync(
            WotInvokeRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            WotEncodeResult encoded = EncodeArguments(request);
            if (!encoded.Success)
            {
                return new WotInvokeResult(encoded.Status, null, encoded.Error);
            }
            ReadOnlyMemory<byte>? content = null;
            if (!encoded.Data.IsEmpty ||
                request.Inputs.Count != 0 ||
                Form.Payload.InputLayout?.Schema.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                content = encoded.Data;
            }
            HttpMethod method = ResolveMethod("POST");
            (StatusCode status, byte[] body, string? error) =
                await SendAsync(method, content, cancellationToken).ConfigureAwait(false);
            if (!StatusCode.IsGood(status))
            {
                return new WotInvokeResult(status, null, error);
            }
            if (m_codec is IWotInteractionPayloadCodec interaction && Form.Payload.OutputLayout is not null)
            {
                return ValidateOutputs(interaction.DecodeArguments(
                    new ByteString(body), Form.Payload, request.Context, m_context.Bounds), request.Context);
            }
            return ValidateOutputs(DecodeLegacyArguments(body, request.Context), request.Context);
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
            return CreateSubscription(
                async token =>
                {
                    WotReadResult result = await ReadAsync(token).ConfigureAwait(false);
                    // A mapped failure carries its bad StatusCode on the value, so surface it
                    // rather than leaving the last good value in place, and report the poll as
                    // unhealthy so the retry policy backs off instead of hammering the asset.
                    onNotification(new WotNotification(result.Value));
                    return result.Success;
                }, onNotification, cancellationToken);
        }

        public ValueTask<IWotSubscription> SubscribeEventAsync(
            Action<WotNotification> onEvent, CancellationToken cancellationToken = default)
        {
            if (onEvent is null)
            {
                throw new ArgumentNullException(nameof(onEvent));
            }
            if (Form.AffordanceKind != WotAffordanceKind.Event || m_codec is not IWotInteractionPayloadCodec codec)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "The selected HTTP codec does not support complete event payloads.");
            }
            return CreateSubscription(async token =>
            {
                (StatusCode status, byte[] body, _) = await SendAsync(ResolveMethod("GET"), null, token)
                    .ConfigureAwait(false);
                WotNotification notification = StatusCode.IsGood(status)
                    ? ValidateEvent(codec.DecodeEvent(
                        new ByteString(body), Form.Payload, Form.EventSelection ?? WotEventSelection.Default,
                        m_context.MessageContext, m_context.Bounds))
                    : new WotNotification(DataValue.FromStatusCode(status)).WithContext(m_context.MessageContext);
                token.ThrowIfCancellationRequested();
                onEvent(notification);
                return StatusCode.IsGood(notification.Value.StatusCode);
            }, onEvent, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            PollingWotSubscription[] subscriptions;
            lock (m_subscriptionsGate)
            {
                m_disposed = true;
                subscriptions = [.. m_subscriptions];
            }
            foreach (PollingWotSubscription subscription in subscriptions)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
            if (m_ownsClient)
            {
                m_client.Dispose();
            }
        }

        private WotEncodeResult EncodeArguments(WotInvokeRequest request)
        {
            WotMethodArgumentLayout? input = Form.Payload.InputLayout;
            WotMethodArgumentLayout? output = Form.Payload.OutputLayout;
            if (input is not null && request.Inputs.Count != input.ArgumentCount)
            {
                return WotEncodeResult.Fail(
                    "The action input count does not match its layout.", StatusCodes.BadInvalidArgument);
            }
            if (m_codec is IWotInteractionPayloadCodec codec && input is not null)
            {
                return codec.EncodeArguments(request, Form.Payload, m_context.Bounds);
            }
            if (request.Inputs.Count > 1 || input?.Kind == WotMethodArgumentLayoutKind.Named ||
                output?.Kind == WotMethodArgumentLayoutKind.Named ||
                (request.Inputs.Count == 1 && WotBindingValueMapper.RequiresContext(request.Inputs[0])) ||
                IsCompoundSchema(input, "input") || IsCompoundSchema(output, "output"))
            {
                return WotEncodeResult.Fail(
                    "The selected legacy codec cannot preserve this action's complete payload and context.",
                    StatusCodes.BadNotSupported);
            }
            return request.Inputs.Count == 0
                ? WotEncodeResult.Ok(ReadOnlyMemory<byte>.Empty)
                : m_codec.Encode(request.Inputs[0], Form.Payload);
        }

        private WotInvokeResult DecodeLegacyArguments(byte[] body, IServiceMessageContext context)
        {
            WotMethodArgumentLayout? output = Form.Payload.OutputLayout;
            if (body.Length == 0 && (output is null || output.ArgumentCount == 0))
            {
                return new WotInvokeResult(StatusCodes.Good, []).WithContext(context);
            }
            if (output?.ArgumentCount == 0)
            {
                return new WotInvokeResult(
                    StatusCodes.BadDecodingError, null, "The response does not match the declared output contract.");
            }
            WotDecodeResult decoded = m_codec.Decode(body, Form.Payload);
            if (!decoded.Success)
            {
                return new WotInvokeResult(StatusCodes.BadDecodingError, null, decoded.Error);
            }
            if (WotBindingValueMapper.RequiresContext(decoded.Value))
            {
                return new WotInvokeResult(
                    StatusCodes.BadNotSupported, null, "The legacy codec returned a value without its source context.");
            }
            return new WotInvokeResult(StatusCodes.Good,
                [new DataValue(decoded.Value, StatusCodes.Good, DateTimeUtc.Now, DateTimeUtc.Now)])
                .WithContext(context);
        }

        private WotInvokeResult ValidateOutputs(WotInvokeResult result, IServiceMessageContext requestContext)
        {
            if (!result.Success)
            {
                return result;
            }
            WotMethodArgumentLayout? layout = Form.Payload.OutputLayout;
            if (layout is not null && result.Outputs.Count != layout.ArgumentCount)
            {
                return new WotInvokeResult(
                    StatusCodes.BadDecodingError, null, "The codec did not return every declared output argument.");
            }
            try
            {
                WotPayloadSchema? schema = layout is null ? null : Form.Payload.GetActionSchema();
                var validator = new WotPayloadValueValidator(result.Context ?? requestContext);
                for (int index = 0; index < result.Outputs.Count; index++)
                {
                    DataValue output = result.Outputs[index];
                    if (!StatusCode.IsGood(output.StatusCode))
                    {
                        return new WotInvokeResult(
                            output.StatusCode, null, "The codec returned an unsuccessful output value.");
                    }
                    if (result.Context is null && WotBindingValueMapper.RequiresContext(output.WrappedValue))
                    {
                        return new WotInvokeResult(
                            StatusCodes.BadNotSupported, null,
                            "The codec returned an output without its source context.");
                    }
                    if (layout is not null)
                    {
                        System.Text.Json.JsonElement argument = layout.GetArgumentSchema(index);
                        if (argument.TryGetProperty("type", out System.Text.Json.JsonElement kind) &&
                            kind.ValueKind == System.Text.Json.JsonValueKind.String &&
                            kind.ValueEquals("null"))
                        {
                            if (!output.WrappedValue.IsNull)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadTypeMismatch, "The codec did not return the declared null value.");
                            }
                            continue;
                        }
                        string pointer = layout.Kind == WotMethodArgumentLayoutKind.Named
                            ? "/output/properties/" + WotAffordanceForm.EscapePointerToken(layout.FieldOrder[index])
                            : "/output";
                        if (schema is null || !schema.TryGetTypeBinding(pointer, out WotPayloadTypeBinding? binding))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNotSupported, "The native output contract is unresolved.");
                        }
                        validator.Validate(output.WrappedValue, binding.DataTypeId, binding.TypeInfo);
                    }
                }
            }
            catch (ServiceResultException exception)
            {
                return new WotInvokeResult(exception.StatusCode, null, exception.Message);
            }
            return result;
        }

        private WotNotification ValidateEvent(WotNotification notification)
        {
            if (!StatusCode.IsGood(notification.Value.StatusCode))
            {
                return notification;
            }
            IServiceMessageContext context = notification.Context ?? m_context.MessageContext;
            try
            {
                WotEventSelection selection = Form.EventSelection ?? WotEventSelection.Default;
                ArrayOf<ArrayOf<string>> paths = WotEventSelectClauses.GetMaterializedMemberPaths(selection.Clauses);
                var validator = new WotPayloadValueValidator(context);
                for (int index = 0; index < selection.Clauses.Count; index++)
                {
                    WotResolvedEventSelectClause clause = selection.Clauses[index];
                    string key = clause.IsConditionIdSelection
                        ? WotEventSelectClauses.ConditionIdFieldName : clause.BrowsePath;
                    if (!notification.Data.TryGetValue(paths[index], out DataValue field) ||
                        !notification.EventFields.TryGetValue(key, out DataValue selected) ||
                        field.WrappedValue != selected.WrappedValue ||
                        field.StatusCode.Code != selected.StatusCode.Code ||
                        field.SourceTimestamp != selected.SourceTimestamp ||
                        field.ServerTimestamp != selected.ServerTimestamp)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError, "The codec did not return consistent selected event fields.");
                    }
                    if (!StatusCode.IsGood(field.StatusCode))
                    {
                        throw new ServiceResultException(field.StatusCode, "The codec returned an unsuccessful field.");
                    }
                    if (notification.Context is null && WotBindingValueMapper.RequiresContext(field.WrappedValue))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported,
                            "The codec returned an event field without its source context.");
                    }
                    BuiltInType standard = WotPayloadDescriptor.GetStandardEventFieldType(clause);
                    if (standard != BuiltInType.Null)
                    {
                        validator.Validate(field.WrappedValue, new ExpandedNodeId((uint)standard),
                            TypeInfo.CreateScalar(standard));
                        continue;
                    }
                    WotPayloadSchema? schema = clause.PayloadSchema ?? Form.Payload.Schema;
                    string pointer = clause.PayloadSchema is null ? "/data" : string.Empty;
                    foreach (string member in paths[index])
                    {
                        pointer += "/properties/" + WotAffordanceForm.EscapePointerToken(member);
                    }
                    if (schema is null || !schema.TryGetTypeBinding(pointer, out WotPayloadTypeBinding? binding))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported, "The native selected event contract is unresolved.");
                    }
                    validator.Validate(field.WrappedValue, binding.DataTypeId, binding.TypeInfo);
                }
                return notification;
            }
            catch (ServiceResultException exception)
            {
                return new WotNotification(DataValue.FromStatusCode(exception.StatusCode)).WithContext(context);
            }
        }

        private bool IsCompoundSchema(WotMethodArgumentLayout? layout, string member)
        {
            if (layout?.Schema.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }
            if (Form.Payload.Schema?.TryGetTypeBinding("/" + member, out WotPayloadTypeBinding? binding) == true &&
                (binding.TypeInfo.ValueRank >= 0 || binding.TypeInfo.BuiltInType == BuiltInType.ExtensionObject ||
                    (binding.TypeInfo.BuiltInType == BuiltInType.Null && !binding.DataTypeId.IsNull)))
            {
                return true;
            }
            if (!layout.Schema.TryGetProperty("type", out System.Text.Json.JsonElement type))
            {
                return false;
            }
            if (type.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return type.GetString() is "array" or "object";
            }
            if (type.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (System.Text.Json.JsonElement alternative in type.EnumerateArray())
                {
                    if (alternative.ValueKind == System.Text.Json.JsonValueKind.String &&
                        alternative.GetString() is "array" or "object")
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private ValueTask<IWotSubscription> CreateSubscription(
            Func<CancellationToken, ValueTask<bool>> poll,
            Action<WotNotification> callback,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_subscriptionsGate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HttpWotBindingChannel));
                }
                m_subscriptions.RemoveWhere(item => item.IsDisposed && item.Completion.IsCompleted);
                var subscription = new PollingWotSubscription(
                    Form, poll, m_options.ObserveInterval, cancellationToken,
                    onError: exception => callback(new WotNotification(DataValue.FromStatusCode(
                        exception is ServiceResultException service
                            ? service.StatusCode : StatusCodes.BadCommunicationError))),
                    retryPolicy: m_options.RetryPolicy, telemetry: m_context.Telemetry);
                m_subscriptions.Add(subscription);
                return new ValueTask<IWotSubscription>(subscription);
            }
        }

        private HttpMethod ResolveMethod(string fallback)
        {
            string method = string.IsNullOrEmpty(Form.OperationInfo.Method)
                ? fallback : Form.OperationInfo.Method;
            return new HttpMethod(method.ToUpperInvariant());
        }

        private async ValueTask<(StatusCode Status, byte[] Body, string? Error)> SendAsync(
            HttpMethod method, ReadOnlyMemory<byte>? content, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (content is { } payload)
            {
                if (payload.Length > m_context.Bounds.MaxPayloadBytes)
                {
                    return (StatusCodes.BadEncodingLimitsExceeded, [],
                        "The HTTP request exceeds the payload byte limit.");
                }
                if (!payload.IsEmpty && (method == HttpMethod.Get || method == HttpMethod.Head))
                {
                    return (StatusCodes.BadNotSupported, [],
                        "A body-bearing action cannot discard its input for GET or HEAD.");
                }
            }
            lock (m_subscriptionsGate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(HttpWotBindingChannel));
                }
            }
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

        /// <summary>
        /// The outcome of a single request hop: either a terminal result or a redirect.
        /// </summary>
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
            {
                return new HopResult(null, null, status, body, error);
            }

            public static HopResult RedirectTo(System.Net.HttpStatusCode redirect, Uri? location)
            {
                return new HopResult(redirect, location, StatusCodes.Good, [], null);
            }
        }

        private async Task<HopResult> SendOnceAsync(
            HttpMethod method, Uri requestUri, bool sameOrigin,
            ReadOnlyMemory<byte>? content, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(method, requestUri);
            string? headerError = ApplyHeaders(request, sameOrigin);
            if (headerError is not null)
            {
                return HopResult.Terminal(StatusCodes.BadSecurityChecksFailed, [], headerError);
            }
            if (content is { } body && method != HttpMethod.Get && method != HttpMethod.Head)
            {
                MediaTypeHeaderValue? mediaType = null;
                if (!string.IsNullOrEmpty(Form.Payload.ContentType) &&
                    !MediaTypeHeaderValue.TryParse(Form.Payload.ContentType, out mediaType))
                {
                    return HopResult.Terminal(
                        StatusCodes.BadInvalidArgument,
                        [],
                        "The form contentType is not a valid media type.");
                }
                var byteContent = new ByteArrayContent(body.ToArray());
                if (mediaType is not null)
                {
                    byteContent.Headers.ContentType = mediaType;
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
                return HopResult.Terminal(status, [],
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            byte[] payload = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
            return HopResult.Terminal(StatusCodes.Good, payload, null);
        }

        private static bool IsRedirect(System.Net.HttpStatusCode status)
        {
            return status is System.Net.HttpStatusCode.MovedPermanently or
                                 System.Net.HttpStatusCode.Found or
                                 System.Net.HttpStatusCode.SeeOther or
                                 System.Net.HttpStatusCode.TemporaryRedirect or
                                 System.Net.HttpStatusCode.PermanentRedirect;
        }

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

            // The endpoint policy is applied to the form's own target before the channel
            // opens, but a redirect chooses a new target after that check. Re-apply the
            // policy to every hop, otherwise a permitted host can bounce the request to a
            // loopback or link-local address that the initial validation would have refused.
            ServiceResult validation = WotEndpointValidator.Validate(
                location.AbsoluteUri,
                m_context.EndpointPolicy,
                out _);
            if (ServiceResult.IsBad(validation))
            {
                error = $"The HTTP redirect targets an endpoint refused by policy: {validation.StatusCode}.";
                return null;
            }
            return location;
        }

        private static bool IsSameOrigin(Uri a, Uri b)
        {
            return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) &&
                a.Port == b.Port;
        }

        private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int max = m_context.Bounds.MaxPayloadBytes;
            int total = 0;
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)
                .ConfigureAwait(false)) > 0)
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
            if (!Form.Security.IsEmpty)
            {
                credential = await m_context.Credentials
                    .ResolveAsync(Form.Security[0], cancellationToken).ConfigureAwait(false);
            }
            // Publish the resolved credential only after resolution has completed. A
            // caller reads m_credential in SendAsync only after awaiting the shared
            // task, so it can never observe a half-initialized state or send a
            // request without the resolved credential applied.
            m_credential = credential;
        }

        private static bool TryAddHeader(HttpRequestMessage request, KeyValuePair<string, string> header)
        {
            try
            {
                request.Headers.Add(header.Key, header.Value);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private string? ApplyHeaders(HttpRequestMessage request, bool includeCredentials)
        {
            // A cross-origin redirect must not carry any custom (potentially
            // credential-bearing) header, so both the caller's default headers and
            // the resolved credential headers are only applied on the original
            // origin.
            if (!includeCredentials)
            {
                return null;
            }
            foreach (KeyValuePair<string, string> header in m_defaultHeaders)
            {
                if (!TryAddHeader(request, header))
                {
                    return "A configured default HTTP header is not valid.";
                }
            }
            if (m_credential is { } credential)
            {
                foreach (KeyValuePair<string, string> header in credential.Headers)
                {
                    if (!TryAddHeader(request, header))
                    {
                        return "A resolved credential HTTP header is not valid.";
                    }
                }
            }
            return null;
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
        private readonly ImmutableArray<KeyValuePair<string, string>> m_defaultHeaders;
        private readonly WotExecutorContext m_context;
        private readonly HttpWotBindingOptions m_options;
        private readonly IWotPayloadCodec m_codec;
        private readonly string m_baseTarget;
        private WotCredential? m_credential;
        private readonly Lock m_credentialLock = new();
        private Task? m_credentialTask;
        private readonly Lock m_subscriptionsGate = new();
        private readonly HashSet<PollingWotSubscription> m_subscriptions = [];
        private bool m_disposed;
    }
}
