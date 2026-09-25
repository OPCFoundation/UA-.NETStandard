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

#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redaction;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    /// <summary>
    /// Reflection-free minimal HTTP routes over an authoritative registry endpoint.
    /// </summary>
    public static class XRegistryHttpEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps a literal registry mount, such as "/registry" or "/". The host supplies
        /// authentication middleware or a context factory, and separately authorizes writes.
        /// No MVC, runtime serializer contract generation or registry business engine is installed.
        /// </summary>
        /// <param name="routes">The route builder to which the registry route group is added.</param>
        /// <param name="pattern">
        /// A literal mount path, such as <c>/registry</c>. An empty string or <c>/</c> mounts at the root;
        /// route templates are not accepted.
        /// </param>
        /// <param name="endpoint">
        /// The authoritative endpoint responsible for inspection, model validation and atomic execution.
        /// </param>
        /// <param name="options">
        /// The configured public root, transport limits, trusted inbound context factory and authorization policy.
        /// </param>
        /// <returns>
        /// A convention builder for the route group, allowing endpoint metadata and authorization policies
        /// to be applied.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// A required argument or <see cref="XRegistryHttpRouteOptions.Transport"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The pattern is not a literal registry path, or the configured public root is invalid.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A transport limit is outside its supported range.
        /// </exception>
        public static IEndpointConventionBuilder MapXRegistry(
            this IEndpointRouteBuilder routes,
            string pattern,
            IXRegistryEndpoint endpoint,
            XRegistryHttpRouteOptions options)
        {
            endpoint.ThrowIfNull(nameof(endpoint));
            return MapXRegistry(routes, pattern, XRegistryEndpointResolver.Borrow(endpoint), options);
        }

        /// <summary>
        /// Maps a caller-specific upstream resolver. One lease spans inspection, response preflight,
        /// preparation and publication; a different caller can never reuse that lease.
        /// </summary>
        /// <exception cref="ArgumentException">The mount is not a valid literal registry path.</exception>
        public static IEndpointConventionBuilder MapXRegistry(
            this IEndpointRouteBuilder routes,
            string pattern,
            IXRegistryEndpointResolver resolver,
            XRegistryHttpRouteOptions options)
        {
            routes.ThrowIfNull(nameof(routes));
            pattern.ThrowIfNull(nameof(pattern));
            resolver.ThrowIfNull(nameof(resolver));
            options.ThrowIfNull(nameof(options));
            options.Transport.ThrowIfNull(nameof(options.Transport));
            string mount = XRegistryPath.Normalize(pattern.Length == 0 ? "/" : pattern);
            string routePrefix = Uri.UnescapeDataString(mount);
            if (routePrefix.IndexOfAny(['{', '}', '?', '#']) >= 0)
            {
                throw new ArgumentException("A registry mount must be a literal path, not a route template.",
                    nameof(pattern));
            }
            var dispatcher = new XRegistryHttpDispatcher(mount, resolver, options);
            RouteGroupBuilder group = routes.MapGroup(mount == "/" ? string.Empty : routePrefix);
            group.Map("/{**xregistryPath}", new RequestDelegate(dispatcher.HandleAsync));
            return group;
        }
    }

    internal sealed class XRegistryHttpDispatcher
    {
        public XRegistryHttpDispatcher(
            string mount, IXRegistryEndpointResolver resolver, XRegistryHttpRouteOptions options)
        {
            m_mount = mount == "/" ? string.Empty : mount;
            m_resolver = resolver;
            m_options = options;
            m_wire = new XRegistryHttpWire(new XRegistryHttpAddress(options.PublicRoot, options.Transport),
                options.Transport);
            m_logger = options.Transport.Telemetry.CreateLogger<XRegistryHttpDispatcher>();
        }

        public async Task HandleAsync(HttpContext context)
        {
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(m_options.Transport.RequestTimeout);
            var outcome = new DispatchOutcome();
            Task<XRegistryHttpPreparedResponse> pending = PrepareResponseAsync(context, timeout, outcome);
            XRegistryHttpPreparedResponse prepared;
            try
            {
                prepared = await pending.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                CancellationTokenSource retained = timeout;
                timeout = null;
                // The pending operation retains its deadline and caller lease until it stops using them.
                // TODO: Remove when CA2025 models asynchronous ownership transfer to late observers.
#pragma warning disable CA2025
                _ = ObserveLateRequestAsync(pending, retained);
#pragma warning restore CA2025
                context.RequestAborted.ThrowIfCancellationRequested();
                prepared = outcome.Committed ??
                    PrepareError(new XRegistryResponse(504)
                    {
                        Error = new XRegistryError("about:blank",
                                "The registry deadline elapsed. A dispatched mutation " +
                                "outcome may be unknown; do not retry blindly.")
                    }, new XRegistryRequest(XRegistryAction.Read, "/"));
                if (outcome.Committed is null)
                {
                    m_logger.BackendTimedOut();
                }
            }
            finally
            {
                timeout?.Dispose();
            }
            await SendResponseAsync(context, prepared).ConfigureAwait(false);
        }

        private async Task<XRegistryHttpPreparedResponse> PrepareResponseAsync(
            HttpContext context, CancellationTokenSource timeout, DispatchOutcome outcome)
        {
            CancellationToken aborted = context.RequestAborted;
            XRegistryRequest request = new(XRegistryAction.Read, "/");
            XRegistryEndpointDescription? description = null;
            var shape = new XRegistryHttpShape();
            bool inputPhase = true;
            XRegistryHttpPreparedResponse prepared;
            try
            {
                request = ReadTarget(context);
                XRegistryCallContext caller = m_options.CreateContextAsync is null
                    ? CreateContext(context.User)
                    : await m_options.CreateContextAsync(context, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                caller.ThrowIfNull(nameof(caller));
                request = request with { Context = caller };
                if (m_options.RequireAuthenticatedUser && !caller.IsAuthenticated)
                {
                    throw new XRegistryHttpWireException(401, "about:blank", "Inbound authentication is required.");
                }
                if (!await AuthorizeAsync(context, request, timeout.Token).ConfigureAwait(false))
                {
                    throw new XRegistryHttpWireException(403, "about:blank", "The caller is not authorized.");
                }
                timeout.Token.ThrowIfCancellationRequested();
                ArrayOf<KeyValuePair<string, string>> headers = ReadHeaders(context.Request.Headers);
                XRegistryHttpHeaders.ValidateBudget(headers, m_options.Transport);
                inputPhase = false;
                IXRegistryEndpointLease lease =
                    await m_resolver.AcquireAsync(caller, timeout.Token).ConfigureAwait(false);
                var leaseOwner = new XRegistryHttpLeaseLifetime(lease, m_options.Transport.CleanupTimeout, m_logger);
                await using ConfiguredAsyncDisposable leaseLifetime = leaseOwner.ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (!XRegistryEndpointLease.SameScope(caller, lease.Context))
                {
                    throw new UnauthorizedAccessException(
                        "The resolved endpoint lease belongs to another caller scope.");
                }
                await lease.EnsureCurrentAsync(timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                using CancellationTokenRegistration revocation = lease.Revoked.Register(
                    static state => ((CancellationTokenSource)state!).Cancel(), timeout);
                IXRegistryEndpoint endpoint = lease.Endpoint;
                description = await endpoint.InspectAsync(caller, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                if (description.Model.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("The endpoint did not provide an effective registry model.");
                }
                if (endpoint is IXRegistryAddressResolver addresses)
                {
                    XRegistryAddressResolution resolution = await addresses.ResolveAddressAsync(request, timeout.Token)
                        .ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested();
                    if (resolution.Rejection is { } rejection)
                    {
                        inputPhase = true;
                        throw new XRegistryHttpWireException(rejection.StatusCode,
                            rejection.Error?.Code ?? "not_found",
                            rejection.Error?.Detail ?? "The address is unavailable.");
                    }
                    if (resolution.Request.Path != request.Path)
                    {
                        request = request.AtResolvedPath(resolution.Request.Path);
                        await lease.EnsureCurrentAsync(timeout.Token).ConfigureAwait(false);
                        if (!await AuthorizeAsync(context, request, timeout.Token).ConfigureAwait(false))
                        {
                            inputPhase = true;
                            throw new XRegistryHttpWireException(403, "about:blank",
                                "The caller is not authorized for the canonical entity.");
                        }
                    }
                }
                inputPhase = true;
                shape = XRegistryHttpShape.Resolve(description.Model, request.Path);
                if (request.View == XRegistryView.Metadata && !shape.IsResource)
                {
                    throw new XRegistryHttpWireException(400, "bad_details",
                        "$details is only defined for Resource and Version entities.");
                }
                if (request.IsMutation && !AllowsPreparedMutation(endpoint, description, request.Action))
                {
                    prepared = m_wire.PrepareResponse(XRegistryHttpWire.MutationNotSupported(request.Path),
                        request, shape, description.Model);
                }
                else
                {
                    ByteString bytes = await m_wire.Body.ReadAsync(context.Request.Body, context.Request.ContentLength,
                        XRegistryHttpWire.GetEncodings(headers), timeout.Token).ConfigureAwait(false);
                    timeout.Token.ThrowIfCancellationRequested();
                    request = m_wire.DecodeRequest(request, shape, headers,
                        XRegistryHttpWire.GetSingle(headers, "Content-Type"), bytes);
                    inputPhase = false;
                    if (request.IsMutation)
                    {
                        prepared =
                            await PrepareAndCommitAsync(
                                context, lease, leaseOwner, request, shape, description, outcome, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await lease.EnsureCurrentAsync(timeout.Token).ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        XRegistryResponse response = await endpoint.ExecuteAsync(request,
                            timeout.Token).ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        prepared =
                            await PrepareEndpointResponseAsync(context, endpoint, request, response, shape, description,
                            timeout.Token).ConfigureAwait(false);
                    }
                }
                inputPhase = false;
            }
            catch (XRegistryHttpException exception) when (exception.Response is not null)
            {
                m_logger.BackendRejected(exception.Response.StatusCode);
                prepared = PrepareError(exception.Response, request);
            }
            catch (UnauthorizedAccessException)
            {
                m_logger.RequestRejected(403, "unauthorized");
                prepared = PrepareError(new XRegistryResponse(403)
                {
                    Error = new XRegistryError("unauthorized", "The caller's upstream lease is not authorized.")
                }, request);
            }
            catch (XRegistryHttpWireException exception) when (inputPhase)
            {
                m_logger.RequestRejected(exception.StatusCode, exception.Code);
                prepared = PrepareError(exception.ToResponse(request.Path), request);
            }
            catch (Exception exception) when (inputPhase &&
                exception is JsonException or InvalidDataException or ArgumentException)
            {
                m_logger.RequestRejected(400, "about:blank");
                prepared = PrepareError(new XRegistryResponse(400)
                {
                    Error = new XRegistryError("about:blank", "The HTTP registry request is malformed.")
                }, request);
            }
            catch (OperationCanceledException) when (!aborted.IsCancellationRequested)
            {
                m_logger.BackendTimedOut();
                prepared = PrepareError(new XRegistryResponse(504)
                {
                    Error = new XRegistryError("about:blank",
                        "The registry operation timed out. A mutation outcome may be unknown; do not retry blindly.")
                }, request);
            }
            catch (Exception exception) when (!inputPhase &&
                exception is IOException or InvalidDataException or HttpRequestException or JsonException
                    or ArgumentException)
            {
                m_logger.BackendFailed(exception);
                prepared = PrepareError(new XRegistryResponse(502)
                {
                    Error = new XRegistryError("about:blank",
                        "The registry endpoint or response preparation failed. A mutation outcome may be unknown.")
                }, request);
            }

            if (m_logger.IsEnabled(LogLevel.Information))
            {
                m_logger.RegistryRequestCompleted(request.Action, Redact.Create(request.Context.Subject),
                    Redact.Create(request.Context.Authority), Redact.Create(request.Path),
                    Redact.Create(description?.RegistryId ?? string.Empty), prepared.StatusCode);
            }
            return prepared;
        }

        private async Task ObserveLateRequestAsync(
            Task<XRegistryHttpPreparedResponse> pending, CancellationTokenSource timeout)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException
                or OperationCanceledException or UnauthorizedAccessException or ServiceResultException
                    or HttpRequestException)
            {
                m_logger.BackendFailed(exception);
            }
            finally
            {
                timeout.Dispose();
            }
        }

        private async Task SendResponseAsync(HttpContext context, XRegistryHttpPreparedResponse prepared)
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            sendTimeout.CancelAfter(m_options.Transport.RequestTimeout);
            context.Response.StatusCode = prepared.StatusCode;
            foreach (KeyValuePair<string, string> header in prepared.Headers)
            {
                context.Response.Headers.Append(header.Key, header.Value);
            }
            if (prepared.StatusCode is not (204 or 304))
            {
                context.Response.ContentLength = prepared.Body.Length;
            }
            if (!HttpMethods.IsHead(context.Request.Method) && !prepared.Body.IsNull)
            {
                await context.Response.Body.WriteAsync(prepared.Body.Memory, sendTimeout.Token).ConfigureAwait(false);
            }
        }

        private XRegistryRequest ReadTarget(HttpContext context)
        {
            if (!XRegistryHttpWire.TryAction(context.Request.Method, out XRegistryAction action))
            {
                throw new XRegistryHttpWireException(405, "action_not_supported", "The HTTP method is not supported.");
            }
            string? rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
            string target = string.IsNullOrEmpty(rawTarget)
                ? context.Request.PathBase.ToUriComponent() +
                    context.Request.Path.ToUriComponent() +
                    context.Request.QueryString.Value
                : rawTarget;
            if (target.Length > m_options.Transport.MaximumUriLength || !target.StartsWith('/'))
            {
                throw new XRegistryHttpWireException(414, "about:blank", "The HTTP target is invalid or too long.");
            }
            int offset = target.IndexOf('?', StringComparison.Ordinal);
            string query = offset < 0 ? string.Empty : target[offset..];
            string path = offset < 0 ? target : target[..offset];
            (path, bool details) = XRegistryHttpAddress.SplitDetails(path);
            path = XRegistryPath.Normalize(path);
            string prefix = context.Request.PathBase.ToUriComponent().TrimEnd('/') + m_mount;
            prefix = prefix.Length == 0 ? string.Empty : XRegistryPath.Normalize(prefix);
            if (prefix.Length != 0)
            {
                if (path == prefix)
                {
                    path = "/";
                }
                else if (path.StartsWith(prefix + "/", StringComparison.Ordinal))
                {
                    path = path[prefix.Length..];
                }
                else
                {
                    throw new XRegistryHttpWireException(404, "api_not_found", "The target is outside the registry.");
                }
            }
            return new XRegistryRequest(action, path)
            {
                View = details ? XRegistryView.Metadata : XRegistryView.Default,
                Parameters = m_wire.Address.ParseQuery(query)
            };
        }

        private ValueTask<bool> AuthorizeAsync(
            HttpContext context,
            XRegistryRequest request,
            CancellationToken cancellationToken)
        {
            return m_options.AuthorizeAsync is null
                ? new ValueTask<bool>(!request.IsMutation)
                : m_options.AuthorizeAsync(context, request, cancellationToken);
        }

        private async ValueTask<XRegistryResponse> FilterActionsAsync(
            HttpContext context,
            IXRegistryEndpoint endpoint,
            XRegistryRequest request,
            XRegistryResponse response,
            XRegistryEndpointDescription description,
            CancellationToken cancellationToken)
        {
            if (response.AllowedActions.Count == 0)
            {
                return response;
            }
            var allowed = new List<XRegistryAction>();
            var shape = XRegistryHttpShape.Resolve(description.Model, request.Path);
            for (int index = 0; index < response.AllowedActions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                XRegistryAction action = response.AllowedActions[index];
                var candidate = new XRegistryRequest(action, request.Path)
                {
                    Context = request.Context,
                    Parameters = request.Parameters,
                    View = request.View
                };
                bool representable = !(action == XRegistryAction.Replace && shape.IsCollection) &&
                    !(action == XRegistryAction.Merge && shape.IsDocumentView(candidate));
                if (representable &&
                    (!candidate.IsMutation || AllowsPreparedMutation(endpoint, description, action)) &&
                    await AuthorizeAsync(context, candidate, cancellationToken).ConfigureAwait(false))
                {
                    allowed.Add(action);
                }
            }
            return response with { AllowedActions = [.. allowed] };
        }

        private static bool AllowsPreparedMutation(
            IXRegistryEndpoint endpoint, XRegistryEndpointDescription description, XRegistryAction action)
        {
            return XRegistryHttpWire.AllowsMutation(description, action) &&
                description.SupportsPreparedMutations &&
                endpoint is IXRegistryPreparedEndpoint;
        }

        private async ValueTask<XRegistryHttpPreparedResponse> PrepareAndCommitAsync(
            HttpContext context, IXRegistryEndpointLease lease, XRegistryHttpLeaseLifetime leaseOwner,
            XRegistryRequest request, XRegistryHttpShape shape,
            XRegistryEndpointDescription description, DispatchOutcome outcome, CancellationToken cancellationToken)
        {
            var endpoint = (IXRegistryPreparedEndpoint)lease.Endpoint;
            await lease.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            bool needsModel = request.Path == "/" &&
                request.Metadata.ValueKind == JsonValueKind.Object &&
                request.Metadata.TryGetProperty("modelsource", out _);
            bool modelRequested = request.Parameters.ToList().Any(parameter =>
                parameter.Name == "inline" &&
                (parameter.Value ?? string.Empty).Split(',').Contains("model", StringComparer.Ordinal));
            bool injectModel = needsModel && !description.SupportsPreparedSnapshots && !modelRequested;
            XRegistryRequest staged = injectModel ? request with
            {
                Parameters = [.. request.Parameters.ToList(), new XRegistryParameter("inline", "model")]
            } : request;
            IXRegistryPreparedOperation operation = await endpoint.PrepareAsync(staged, cancellationToken)
                .ConfigureAwait(false);
            leaseOwner.Retain(operation);
            cancellationToken.ThrowIfCancellationRequested();
            XRegistryResponse response = operation.Response;
            if (response.IsSuccess && needsModel)
            {
                if (operation is IXRegistryPreparedSnapshot { HasCandidate: true } snapshot)
                {
                    description = await snapshot.InspectCandidateAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                else if (response.Metadata.ValueKind == JsonValueKind.Object &&
                    response.Metadata.TryGetProperty("model", out JsonElement model) &&
                    model.ValueKind == JsonValueKind.Object)
                {
                    description = description with { Model = model };
                }
                else
                {
                    throw new InvalidDataException(
                        "The prepared model change did not provide its effective response model.");
                }
                if (injectModel)
                {
                    var metadata = (JsonObject)JsonNode.Parse(response.Metadata.GetRawText())!;
                    metadata.Remove("model");
                    response = response with { Metadata = m_wire.Body.Parse(m_wire.Body.Encode(metadata)) };
                }
            }
            XRegistryHttpPreparedResponse preview = await PrepareEndpointResponseAsync(
                context, endpoint, request, response, shape, description, cancellationToken).ConfigureAwait(false);
            await lease.EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await AuthorizeAsync(context, request with { Metadata = default, Document = default },
                cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException("Authorization changed before publication.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            XRegistryResponse committed = await operation.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (!committed.IsSuccess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await PrepareEndpointResponseAsync(context, endpoint, request, committed, shape, description,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            if (committed.StatusCode != operation.Response.StatusCode)
            {
                throw new InvalidDataException("The prepared endpoint changed its response status after committing.");
            }
            outcome.Committed = preview;
            return preview;
        }

        private async ValueTask<XRegistryHttpPreparedResponse> PrepareEndpointResponseAsync(
            HttpContext context, IXRegistryEndpoint endpoint,
            XRegistryRequest request, XRegistryResponse response, XRegistryHttpShape shape,
            XRegistryEndpointDescription description, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (response.StatusCode < 200)
            {
                throw new InvalidDataException("An endpoint must return a final HTTP status.");
            }
            if (description.PublicRoot is { } root)
            {
                bool documentRedirect = XRegistryHttpDocumentReference.IsRedirect(response, request, shape);
                var source = new XRegistryHttpAddress(root, m_options.Transport);
                var links = new List<XRegistryLink>();
                foreach (XRegistryLink link in response.Links)
                {
                    links.Add(link with { Target = RebaseLink(source, link.Target)! });
                }
                response = response with
                {
                    Metadata = XRegistryHttpLinks.Translate(response.Metadata, request, description.Model,
                        source, m_wire.Body, source.Root, protocolLinks: true),
                    Location = documentRedirect ? response.Location : RebaseLink(source, response.Location),
                    ContentLocation = RebaseLink(source, response.ContentLocation),
                    Links = [.. links]
                };
            }
            response = await FilterActionsAsync(context, endpoint, request, response, description, cancellationToken)
                .ConfigureAwait(false);
            return m_wire.PrepareResponse(response, request, shape, description.Model);
        }

        private static string? RebaseLink(XRegistryHttpAddress source, string? target)
        {
            return target is not null && Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && !uri.IsFile
                ? source.ReadLink(target, source.Root) : target;
        }

        private XRegistryHttpPreparedResponse PrepareError(XRegistryResponse response, XRegistryRequest request)
        {
            // Error reporting needs a small independent budget even if a caller selected a tiny body limit.
            XRegistryHttpOptions errorOptions = m_options.Transport with
            {
                MaximumBodyBytes = Math.Max(4096, m_options.Transport.MaximumBodyBytes),
                MaximumHeaderBytes = Math.Max(4096, m_options.Transport.MaximumHeaderBytes),
                MaximumHeaders = Math.Max(16, m_options.Transport.MaximumHeaders)
            };
            var wire = new XRegistryHttpWire(m_wire.Address, errorOptions);
            return wire.PrepareResponse(response, request, new XRegistryHttpShape(), default);
        }

        private static XRegistryCallContext CreateContext(ClaimsPrincipal principal)
        {
            var identity = principal.Identity as ClaimsIdentity;
            if (identity?.IsAuthenticated != true)
            {
                return XRegistryCallContext.Anonymous;
            }
            string? subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value ?? identity.Name;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new XRegistryHttpWireException(401, "about:blank", "The authenticated principal has no subject.");
            }
            var roles = new List<string>();
            foreach (Claim claim in principal.FindAll(identity.RoleClaimType))
            {
                roles.Add(claim.Value);
            }
            return new XRegistryCallContext(subject)
            {
                IsAuthenticated = true,
                Authority = identity.AuthenticationType ?? string.Empty,
                Roles = [.. roles],
                SessionId = principal.FindFirst("sid")?.Value
            };
        }

        private static ArrayOf<KeyValuePair<string, string>> ReadHeaders(IHeaderDictionary headers)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in headers)
            {
                foreach (string? value in header.Value)
                {
                    result.Add(new KeyValuePair<string, string>(header.Key, value ?? string.Empty));
                }
            }
            return [.. result];
        }

        private sealed class DispatchOutcome
        {
            public XRegistryHttpPreparedResponse? Committed
            {
                get => Volatile.Read(ref m_committed);
                set => Volatile.Write(ref m_committed, value);
            }

            private XRegistryHttpPreparedResponse? m_committed;
        }

        private readonly string m_mount;
        private readonly IXRegistryEndpointResolver m_resolver;
        private readonly XRegistryHttpRouteOptions m_options;
        private readonly XRegistryHttpWire m_wire;
        private readonly ILogger m_logger;
    }

    internal static partial class XRegistryHttpDispatcherLog
    {
        [LoggerMessage(EventId = XRegistryHttpEventIds.Dispatcher, Level = LogLevel.Warning,
            Message = "xRegistry HTTP request rejected with status {StatusCode}, code {Code}.")]
        public static partial void RequestRejected(this ILogger logger, int statusCode, string code);

        [LoggerMessage(EventId = XRegistryHttpEventIds.Dispatcher + 1, Level = LogLevel.Warning,
            Message = "xRegistry backend inspection rejected with status {StatusCode}.")]
        public static partial void BackendRejected(this ILogger logger, int statusCode);

        [LoggerMessage(EventId = XRegistryHttpEventIds.Dispatcher + 2, Level = LogLevel.Error,
            Message = "xRegistry endpoint or HTTP response preparation failed.")]
        public static partial void BackendFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = XRegistryHttpEventIds.Dispatcher + 3, Level = LogLevel.Warning,
            Message = "xRegistry endpoint operation timed out; mutation outcome may be unknown.")]
        public static partial void BackendTimedOut(this ILogger logger);

        [LoggerMessage(EventId = XRegistryHttpEventIds.Dispatcher + 4, Level = LogLevel.Information,
            Message = "xRegistry HTTP audit: {Action} caller={Caller} authority={Authority} target={Target} "
                + "upstream={Upstream} status={Status}. No request payload or credential is recorded.")]
        public static partial void RegistryRequestCompleted(
            this ILogger logger, XRegistryAction action, RedactionWrapper<string> caller,
            RedactionWrapper<string> authority, RedactionWrapper<string> target,
            RedactionWrapper<string> upstream, int status);
    }
}
#endif
