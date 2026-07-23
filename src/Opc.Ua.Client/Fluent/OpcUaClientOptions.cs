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

using Opc.Ua.Configuration;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Top-level options for
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaClientBuilderExtensions.AddClient(IOpcUaBuilder,System.Action{OpcUaClientOptions})"/>.
    /// </summary>
    public sealed class OpcUaClientOptions
    {
        /// <summary>
        /// The application configuration. When omitted,
        /// <c>ConfigureApplication(...)</c> must be registered on the root
        /// OPC UA builder, or the application identity properties below
        /// must be set instead.
        /// </summary>
        public ApplicationConfiguration? Configuration { get; set; }

        /// <summary>
        /// The application name. When set (and <see cref="Configuration"/>
        /// is omitted), the root <c>ConfigureApplication(...)</c>
        /// infrastructure is used internally to build and validate the
        /// application configuration for this client, mirroring
        /// <c>OpcUaServerOptions.ApplicationName</c>. Composes with a root
        /// <c>ConfigureApplication(...)</c> call made before or after
        /// <c>AddClient(...)</c>: fields explicitly set via
        /// <c>ConfigureApplication(...)</c> win, and this property only
        /// fills the value when it would otherwise be unset (<c>??=</c>
        /// semantics), mirroring
        /// <c>OpcUaServerApplicationConfigurationFeature.ApplyDefaults</c>.
        /// Must not be combined with an explicit <see cref="Configuration"/>.
        /// </summary>
        public string? ApplicationName { get; set; }

        /// <summary>
        /// The application URI (e.g. <c>urn:localhost:Org:Product</c>).
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public string? ApplicationUri { get; set; }

        /// <summary>
        /// The product URI (e.g. <c>uri:org:product</c>).
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public string? ProductUri { get; set; }

        /// <summary>
        /// The application certificate subject. When omitted, a subject is
        /// generated from <see cref="ApplicationName"/>.
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public string? SubjectName { get; set; }

        /// <summary>
        /// The PKI root. When omitted, a per-application directory below
        /// the temporary directory is used.
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public string? PkiRoot { get; set; }

        /// <summary>
        /// Whether unknown peer certificates are automatically accepted.
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public bool? AutoAcceptUntrustedCertificates { get; set; }

        /// <summary>
        /// Whether SHA-1-signed certificates are rejected.
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public bool? RejectSHA1SignedCertificates { get; set; }

        /// <summary>
        /// The minimum accepted RSA certificate key size.
        /// See <see cref="ApplicationName"/> for combination rules.
        /// </summary>
        public ushort? MinimumCertificateKeySize { get; set; }

        /// <summary>
        /// Default <see cref="ManagedSessionOptions"/> used by the
        /// session factory delegate registered with DI.
        /// </summary>
        public ManagedSessionOptions Session { get; set; } = new();

        /// <summary>
        /// Client identity-provider configuration bound from
        /// <c>OpcUa:Client:Identity</c>.
        /// </summary>
        public OpcUaClientIdentityOptions Identity { get; set; } = new();

        /// <summary>
        /// Client-side reverse-connect configuration. When non-null the
        /// DI container registers a singleton
        /// <see cref="ReverseConnectManager"/> together with an internal
        /// hosted service that opens the configured listener endpoints
        /// asynchronously on host start (eager), while
        /// <see cref="ReverseConnectManager.WaitForConnectionAsync"/>
        /// starts it lazily on first use when no host is present. A missing
        /// <see cref="Configuration"/> is surfaced during the async start
        /// rather than at resolution. The values are also written into
        /// <see cref="ClientConfiguration.ReverseConnect"/> on
        /// <see cref="Configuration"/> so the same data is observable
        /// through the application-configuration surface.
        /// </summary>
        public ClientReverseConnectOptions? ReverseConnect { get; set; }

        internal IOpcUaApplicationConfigurationProvider? ConfigurationProvider { get; set; }

        /// <summary>
        /// <c>true</c> when any application identity or security property
        /// (<see cref="ApplicationName"/>, <see cref="ApplicationUri"/>,
        /// <see cref="ProductUri"/>, <see cref="SubjectName"/>,
        /// <see cref="PkiRoot"/>,
        /// <see cref="AutoAcceptUntrustedCertificates"/>,
        /// <see cref="RejectSHA1SignedCertificates"/>, or
        /// <see cref="MinimumCertificateKeySize"/>) was set.
        /// </summary>
        internal bool HasApplicationOptions =>
            ApplicationName != null
            || ApplicationUri != null
            || ProductUri != null
            || SubjectName != null
            || PkiRoot != null
            || AutoAcceptUntrustedCertificates != null
            || RejectSHA1SignedCertificates != null
            || MinimumCertificateKeySize != null;
    }
}
