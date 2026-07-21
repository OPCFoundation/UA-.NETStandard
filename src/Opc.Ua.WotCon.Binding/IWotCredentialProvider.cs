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
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>The WoT security scheme kind referenced by a form (no secrets).</summary>
    public enum WotSecurityScheme
    {
        /// <summary>No security (<c>nosec</c>).</summary>
        NoSecurity,

        /// <summary>HTTP Basic authentication.</summary>
        Basic,

        /// <summary>HTTP Digest authentication.</summary>
        Digest,

        /// <summary>Bearer token authentication.</summary>
        Bearer,

        /// <summary>API-key authentication.</summary>
        ApiKey,

        /// <summary>Pre-shared-key authentication.</summary>
        Psk,

        /// <summary>OAuth 2.0 authentication.</summary>
        OAuth2,

        /// <summary>Automatic security provisioned out-of-band.</summary>
        Auto,

        /// <summary>A combination of other schemes.</summary>
        Combo,

        /// <summary>A scheme not otherwise enumerated.</summary>
        Other
    }

    /// <summary>
    /// An immutable, secret-free security definition parsed from a Thing
    /// Description <c>securityDefinitions</c> entry. It records only the scheme
    /// kind and where the credential is carried (for example an HTTP header
    /// name); the actual secret is never present in the document or on registry
    /// nodes and is resolved at runtime by an <see cref="IWotCredentialProvider"/>.
    /// </summary>
    public sealed class WotSecurityDefinition
    {
        /// <summary>The well-known no-security definition.</summary>
        public static WotSecurityDefinition NoSecurity { get; } =
            new WotSecurityDefinition("nosec_sc", WotSecurityScheme.NoSecurity, null, null);

        /// <summary>Initializes a new immutable security definition.</summary>
        public WotSecurityDefinition(string name, WotSecurityScheme scheme, string? @in, string? parameterName)
        {
            Name = name ?? string.Empty;
            Scheme = scheme;
            In = @in;
            ParameterName = parameterName;
        }

        /// <summary>Gets the definition name (the scheme reference used by forms).</summary>
        public string Name { get; }

        /// <summary>Gets the security scheme kind.</summary>
        public WotSecurityScheme Scheme { get; }

        /// <summary>Gets where the credential is carried (<c>header</c>, <c>query</c>, ...).</summary>
        public string? In { get; }

        /// <summary>Gets the parameter name (for example the header name), if any.</summary>
        public string? ParameterName { get; }

        /// <summary>Parses a <c>securityDefinitions</c> entry into a definition.</summary>
        public static WotSecurityDefinition Parse(string name, JsonElement definition)
        {
            WotSecurityScheme scheme = WotSecurityScheme.Other;
            string? @in = null;
            string? parameterName = null;
            if (definition.ValueKind == JsonValueKind.Object)
            {
                if (definition.TryGetProperty("scheme", out JsonElement schemeElement) &&
                    schemeElement.ValueKind == JsonValueKind.String)
                {
                    scheme = MapScheme(schemeElement.GetString());
                }
                if (definition.TryGetProperty("in", out JsonElement inElement) &&
                    inElement.ValueKind == JsonValueKind.String)
                {
                    @in = inElement.GetString();
                }
                if (definition.TryGetProperty("name", out JsonElement nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    parameterName = nameElement.GetString();
                }
            }
            return new WotSecurityDefinition(name, scheme, @in, parameterName);
        }

        private static WotSecurityScheme MapScheme(string? scheme)
        {
            return scheme switch
            {
                "nosec" => WotSecurityScheme.NoSecurity,
                "basic" => WotSecurityScheme.Basic,
                "digest" => WotSecurityScheme.Digest,
                "bearer" => WotSecurityScheme.Bearer,
                "apikey" => WotSecurityScheme.ApiKey,
                "psk" => WotSecurityScheme.Psk,
                "oauth2" => WotSecurityScheme.OAuth2,
                "auto" => WotSecurityScheme.Auto,
                "combo" => WotSecurityScheme.Combo,
                _ => WotSecurityScheme.Other
            };
        }
    }

    /// <summary>
    /// A secret-free reference the runtime resolves against an out-of-band secret
    /// store. It carries the scheme name and endpoint context so a provider can
    /// select the correct credential without any secret ever appearing in the
    /// Thing Description or on registry nodes.
    /// </summary>
    public sealed class WotCredentialReference
    {
        /// <summary>Initializes a new immutable credential reference.</summary>
        public WotCredentialReference(
            string schemeName,
            WotSecurityScheme scheme,
            string bindingUri,
            string? endpoint,
            string? @in = null,
            string? parameterName = null)
        {
            SchemeName = schemeName ?? string.Empty;
            Scheme = scheme;
            BindingUri = bindingUri ?? string.Empty;
            Endpoint = endpoint;
            In = @in;
            ParameterName = parameterName;
        }

        /// <summary>Gets the referenced security scheme name.</summary>
        public string SchemeName { get; }

        /// <summary>Gets the security scheme kind.</summary>
        public WotSecurityScheme Scheme { get; }

        /// <summary>Gets the binding vocabulary URI requesting the credential.</summary>
        public string BindingUri { get; }

        /// <summary>Gets the endpoint the credential is scoped to, if any.</summary>
        public string? Endpoint { get; }

        /// <summary>Gets where the credential is carried, if known.</summary>
        public string? In { get; }

        /// <summary>Gets the parameter / header name, if known.</summary>
        public string? ParameterName { get; }

        /// <summary>Creates a reference from a security definition and endpoint.</summary>
        public static WotCredentialReference FromDefinition(
            WotSecurityDefinition definition, string bindingUri, string? endpoint)
            => new WotCredentialReference(
                definition.Name, definition.Scheme, bindingUri, endpoint, definition.In, definition.ParameterName);
    }

    /// <summary>
    /// Resolved credential material produced only at runtime by an
    /// <see cref="IWotCredentialProvider"/>. Instances are short-lived and never
    /// serialized to the Thing Description or registry nodes. The material is
    /// expressed as headers, query parameters and opaque properties so it can
    /// drive HTTP, MQTT and other transports uniformly.
    /// </summary>
    public sealed class WotCredential
    {
        /// <summary>Initializes a new resolved credential.</summary>
        public WotCredential(
            WotSecurityScheme scheme,
            ImmutableDictionary<string, string>? headers = null,
            ImmutableDictionary<string, string>? queryParameters = null,
            ImmutableDictionary<string, string>? properties = null,
            X509Certificate2? clientCertificate = null,
            IReadOnlyList<X509Certificate2>? trustedCertificates = null)
        {
            Scheme = scheme;
            Headers = headers ?? ImmutableDictionary<string, string>.Empty;
            QueryParameters = queryParameters ?? ImmutableDictionary<string, string>.Empty;
            Properties = properties ?? ImmutableDictionary<string, string>.Empty;
            ClientCertificate = clientCertificate;
            TrustedCertificates = trustedCertificates is null
                ? ImmutableArray<X509Certificate2>.Empty
                : trustedCertificates.ToImmutableArray();
        }

        /// <summary>Gets the scheme this credential satisfies.</summary>
        public WotSecurityScheme Scheme { get; }

        /// <summary>Gets transport headers to apply (for example <c>Authorization</c>).</summary>
        public ImmutableDictionary<string, string> Headers { get; }

        /// <summary>Gets query parameters to apply (for example an API key).</summary>
        public ImmutableDictionary<string, string> QueryParameters { get; }

        /// <summary>
        /// Gets opaque credential properties (for example <c>username</c> /
        /// <c>password</c> for MQTT, or a token reference id).
        /// </summary>
        public ImmutableDictionary<string, string> Properties { get; }

        /// <summary>
        /// Gets the resolved client certificate used for mutual TLS (for example
        /// an <c>mqtts</c> connection), or <c>null</c> when none is configured.
        /// Runtime-only material that is never serialized to the Thing Description
        /// or registry nodes.
        /// </summary>
        public X509Certificate2? ClientCertificate { get; }

        /// <summary>
        /// Gets the resolved trust anchors used to validate the peer's TLS
        /// certificate (for example an <c>mqtts</c> broker). Empty when the
        /// transport should rely on the platform default trust store. Runtime-only
        /// material that is never serialized to the Thing Description or registry
        /// nodes.
        /// </summary>
        public ImmutableArray<X509Certificate2> TrustedCertificates { get; }
    }

    /// <summary>
    /// Resolves secret-free credential references into runtime credential
    /// material. Implementations look the secret up in a trust / secret store
    /// out-of-band; the Thing Description and registry nodes only ever carry the
    /// scheme reference.
    /// </summary>
    public interface IWotCredentialProvider
    {
        /// <summary>
        /// Resolves a credential for the supplied reference, or <c>null</c> when
        /// no credential is required (for example <c>nosec</c>) or none is
        /// configured.
        /// </summary>
        ValueTask<WotCredential?> ResolveAsync(
            WotCredentialReference reference, CancellationToken cancellationToken = default);
    }

    /// <summary>A credential provider that resolves nothing (no security).</summary>
    public sealed class NullWotCredentialProvider : IWotCredentialProvider
    {
        /// <summary>Gets the shared instance.</summary>
        public static NullWotCredentialProvider Instance { get; } = new NullWotCredentialProvider();

        /// <inheritdoc/>
        public ValueTask<WotCredential?> ResolveAsync(
            WotCredentialReference reference, CancellationToken cancellationToken = default)
            => new ValueTask<WotCredential?>((WotCredential?)null);
    }
}
