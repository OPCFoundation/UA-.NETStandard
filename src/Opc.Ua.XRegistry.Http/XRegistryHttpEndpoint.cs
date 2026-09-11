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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Http
{
    /// <summary>
    /// A bounded, model-driven client for the xRegistry 1.0-rc4 HTTP binding.
    /// Sends each complete mutation once; it never emulates registry transactions.
    /// </summary>
    /// <remarks>
    /// The caller owns the injected HttpClient and its credential profile. Its pipeline
    /// must disable automatic redirects, retries and challenge-driven mutation replay.
    /// HttpClient does not expose handler settings for this adapter to change.
    /// Use AddXRegistryHttpEndpoint for a no-redirect, no-cookie default pipeline.
    /// Call contexts are not serialized into headers or used as credentials.
    /// </remarks>
    public sealed class XRegistryHttpEndpoint : IXRegistryEndpoint
    {
        /// <summary>
        /// Initializes a bounded HTTP adapter for the configured registry without taking ownership of its client.
        /// </summary>
        /// <param name="client">
        /// The caller-owned client whose credentials authenticate upstream requests. Its pipeline must disable
        /// automatic redirects, retries and authentication-challenge replay of mutations.
        /// </param>
        /// <param name="registryRoot">
        /// The absolute registry root without credentials, query or fragment. HTTPS is required unless
        /// <paramref name="options"/> explicitly permits loopback HTTP.
        /// </param>
        /// <param name="options">
        /// The transport limits and backend qualification, or <see langword="null"/> to use the defaults.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="client"/> or <paramref name="registryRoot"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The registry root is invalid, or the client has default Host, xRegistry attribute or validator headers.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A transport limit is outside its supported range.
        /// </exception>
        public XRegistryHttpEndpoint(HttpClient client, Uri registryRoot, XRegistryHttpOptions? options = null)
        {
            client.ThrowIfNull(nameof(client));
            registryRoot.ThrowIfNull(nameof(registryRoot));
            m_client = client;
            m_options = options ?? new XRegistryHttpOptions();
            m_address = new XRegistryHttpAddress(registryRoot, m_options);
            m_wire = new XRegistryHttpWire(m_address, m_options);
            ValidateClientHeaders();
        }

        /// <summary>
        /// Gets the normalized absolute registry root used for outbound requests and registry navigation links.
        /// The validated root always has a trailing slash.
        /// </summary>
        public Uri RegistryRoot => m_address.Root;

        /// <summary>
        /// Retrieves the registry root, effective model and capabilities, and determines the qualified
        /// mutation guarantees. Missing or malformed inspection data is an error, not an empty registry.
        /// </summary>
        /// <param name="context">
        /// The explicit caller context for inspection. It is not serialized into authentication headers.
        /// </param>
        /// <param name="cancellationToken">A token that cancels inspection and response body reads.</param>
        /// <returns>
        /// The inspected identity, model, capabilities and qualified guarantees. HTTP operation replay
        /// is always reported as unsupported.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="XRegistryHttpException">
        /// The backend rejected an inspection request; the exception retains the complete rejection response.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// Required registry metadata is missing or invalid.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Inspection was canceled or exceeded its request timeout.
        /// </exception>
        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context,
            CancellationToken cancellationToken = default)
        {
            context.ThrowIfNull(nameof(context));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_options.RequestTimeout);
            return await InspectCoreAsync(context, timeout.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes one protocol request over HTTP without replaying or decomposing mutations.
        /// Loads effective model data as needed and verifies required guarantees before sending a mutation.
        /// </summary>
        /// <param name="request">
        /// The canonical registry request, including its caller context, ordered parameters and payload.
        /// The caller context is not used as an upstream credential.
        /// </param>
        /// <param name="cancellationToken">A token that cancels inspection, execution and response body reads.</param>
        /// <returns>
        /// The complete decoded response, or a local rejection when operation replay is requested or
        /// the backend lacks deployment qualification or the guarantees required for a mutation.
        /// </returns>
        /// <remarks>
        /// Each accepted mutation is sent once. A timeout or lost response is not retried and may leave
        /// the mutation outcome unknown.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="request"/> or its caller context is <see langword="null"/>.
        /// </exception>
        /// <exception cref="XRegistryHttpException">
        /// The backend rejected a required metadata inspection; the exception retains the rejection response.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// The operation was canceled or exceeded its request timeout.
        /// </exception>
        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request,
            CancellationToken cancellationToken = default)
        {
            request.ThrowIfNull(nameof(request));
            request.Context.ThrowIfNull(nameof(request.Context));
            if (request.OperationId is not null)
            {
                return new XRegistryResponse(405)
                {
                    Error = new XRegistryError("action_not_supported",
                        "The HTTP binding does not provide operation replay protection.")
                    { Subject = request.Path },
                    AllowedActions = [XRegistryAction.Read, XRegistryAction.Describe]
                };
            }
            if (request.IsMutation && !m_options.IsQualifiedBinding)
            {
                return XRegistryHttpWire.MutationNotSupported(request.Path);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(m_options.RequestTimeout);
            JsonElement model = default;
            if (request.IsMutation)
            {
                XRegistryEndpointDescription description = await InspectCoreAsync(
                    request.Context, timeout.Token).ConfigureAwait(false);
                if (!XRegistryHttpWire.AllowsMutation(description, request.Action) ||
                    !AvailableMutable(description.Capabilities, request.Path switch
                    {
                        "/capabilities" => "capabilities",
                        "/modelsource" => "modelsource",
                        "/model" or "/export" or "/capabilitiesoffered" => string.Empty,
                        _ => "entities"
                    }))
                {
                    return XRegistryHttpWire.MutationNotSupported(request.Path);
                }
                model = description.Model;
            }
            else if (!XRegistryHttpShape.IsWellKnown(request.Path))
            {
                model = await ReadMetadataAsync("/model", request.Context, timeout.Token).ConfigureAwait(false);
            }
            var shape = XRegistryHttpShape.Resolve(model, request.Path);
            XRegistryResponse response = await SendAsync(request, shape, model, timeout.Token).ConfigureAwait(false);
            if (!request.IsMutation &&
                request.Path is "/" or "/export" &&
                response.IsSuccess &&
                response.Metadata.ValueKind == JsonValueKind.Object)
            {
                model = await ReadMetadataAsync("/model", request.Context, timeout.Token).ConfigureAwait(false);
                response = response with
                {
                    Metadata = XRegistryHttpLinks.Translate(response.Metadata, request, model, m_address, m_wire.Body,
                        m_address.GetUri(request.Path, XRegistryView.Default, request.Parameters))
                };
            }
            return response;
        }

        private async ValueTask<XRegistryEndpointDescription> InspectCoreAsync(
            XRegistryCallContext context,
            CancellationToken cancellationToken)
        {
            JsonElement root = await ReadMetadataAsync("/", context, cancellationToken).ConfigureAwait(false);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("registryid", out JsonElement identity) ||
                identity.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(identity.GetString()) ||
                !root.TryGetProperty("specversion",
                    out JsonElement version) ||
                version.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The registry root is missing its identity or specification version.");
            }
            JsonElement model = await ReadMetadataAsync("/model", context, cancellationToken).ConfigureAwait(false);
            JsonElement capabilities = await ReadMetadataAsync(
                "/capabilities", context, cancellationToken).ConfigureAwait(false);
            if (model.ValueKind != JsonValueKind.Object || capabilities.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Registry model and capabilities must be JSON objects.");
            }
            bool pinned = version.GetString() == "1.0-rc4" &&
                Contains(capabilities, "specversions", "1.0-rc4");
            bool mutable = AvailableMutable(capabilities, "entities") ||
                AvailableMutable(capabilities, "modelsource") ||
                AvailableMutable(capabilities, "capabilities");
            bool qualified = m_options.IsQualifiedBinding && pinned && mutable;
            return new XRegistryEndpointDescription(identity.GetString()!)
            {
                Profile = qualified ? "http-1.0-rc4-qualified" : "http-unqualified",
                PublicRoot = RegistryRoot,
                Model = model,
                Capabilities = capabilities,
                SupportsAtomicMutations = qualified,
                SupportsConditionalMutations = qualified,
                SupportsWriteTouch = qualified,
                SupportsOperationReplay = false
            };
        }

        private static bool AvailableMutable(JsonElement capabilities, string aspect)
        {
            if (!capabilities.TryGetProperty("available", out JsonElement available) ||
                available.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            foreach (JsonProperty property in available.EnumerateObject())
            {
                if (property.Name.Equals(aspect, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.TryGetProperty("mutable", out JsonElement mutable))
                {
                    return mutable.ValueKind == JsonValueKind.True;
                }
            }
            return false;
        }

        private async ValueTask<JsonElement> ReadMetadataAsync(
            string path,
            XRegistryCallContext context,
            CancellationToken cancellationToken)
        {
            var request = new XRegistryRequest(XRegistryAction.Read, path) { Context = context };
            XRegistryResponse response = await SendAsync(
                request, new XRegistryHttpShape(), default, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                throw new XRegistryHttpException("The backend rejected xRegistry inspection at " + path + ".",
                    response);
            }
            if (response.Metadata.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidDataException("An inspection response is missing its metadata document.");
            }
            return response.Metadata;
        }

        private async ValueTask<XRegistryResponse> SendAsync(
            XRegistryRequest request,
            XRegistryHttpShape shape,
            JsonElement model,
            CancellationToken cancellationToken)
        {
            ValidateClientHeaders();
            using HttpRequestMessage message = m_wire.EncodeRequest(request, shape);
            Uri sentUri = message.RequestUri!;
            using HttpResponseMessage response = await m_client.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return await m_wire.DecodeResponseAsync(
                response, request, shape, model, sentUri, cancellationToken).ConfigureAwait(false);
        }

        private void ValidateClientHeaders()
        {
            if (m_client.DefaultRequestHeaders.Host is not null)
            {
                throw new ArgumentException("An xRegistry HTTP client cannot override the configured Host header.");
            }
            foreach (System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.IEnumerable<string>>
                header in m_client.DefaultRequestHeaders)
            {
                if (header.Key.StartsWith("xRegistry-", StringComparison.OrdinalIgnoreCase) ||
                    XRegistryHttpWire.IsUnsupportedValidator(header.Key))
                {
                    throw new ArgumentException(
                        "Default registry attributes or validator headers change request semantics.");
                }
            }
        }

        private static bool Contains(JsonElement capabilities, string name, string expected)
        {
            if (capabilities.TryGetProperty(name, out JsonElement values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement value in values.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String && value.GetString() == expected)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private readonly HttpClient m_client;
        private readonly XRegistryHttpOptions m_options;
        private readonly XRegistryHttpAddress m_address;
        private readonly XRegistryHttpWire m_wire;
    }
}
