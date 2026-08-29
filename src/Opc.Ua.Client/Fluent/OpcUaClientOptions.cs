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
using System.IO;
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
        /// must be set instead, or the configuration must be supplied as an
        /// existing XML document via <see cref="ConfigurationFile"/> /
        /// <see cref="ConfigurationStream"/>.
        /// </summary>
        public ApplicationConfiguration? Configuration { get; set; }

        /// <summary>
        /// Optional path to a classic OPC UA application configuration XML
        /// file (e.g. <c>MyClient.Config.xml</c>). When set, the client
        /// loads the <see cref="ApplicationConfiguration"/> from this file
        /// instead of building one, so existing applications can adopt
        /// dependency injection and keep every setting from their
        /// configuration file (security configuration, certificate stores,
        /// transport quotas, client configuration, ...).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A relative path is resolved against the current working
        /// directory. The file must validate as a client (or
        /// client-and-server) configuration. The document is loaded and the
        /// application-instance certificate is ensured on first use (first
        /// session connect, reverse-connect startup, or an explicit
        /// <c>GetAsync</c> on the resolved configuration provider),
        /// mirroring how a shared <c>ConfigureApplication(...)</c>
        /// application completes asynchronously.
        /// </para>
        /// <para>
        /// Because the file is authoritative, it must not be combined with
        /// an explicit <see cref="Configuration"/> or with the application
        /// identity properties (<see cref="ApplicationName"/>, ...); use
        /// <see cref="ConfigureLoadedConfiguration"/> for programmatic
        /// overrides of the loaded file. This path also takes precedence
        /// over a shared application registered with
        /// <c>ConfigureApplication(...)</c>. Mutually exclusive with
        /// <see cref="ConfigurationStream"/>.
        /// </para>
        /// </remarks>
        public string? ConfigurationFile { get; set; }

        /// <summary>
        /// Optional stream containing a classic OPC UA application
        /// configuration XML document, e.g. an embedded resource. When set,
        /// the client loads the <see cref="ApplicationConfiguration"/> from
        /// this stream with the same semantics as
        /// <see cref="ConfigurationFile"/>.
        /// </summary>
        /// <remarks>
        /// The stream must remain open until the configuration is first
        /// used; it is read once and disposed after loading. Mutually
        /// exclusive with <see cref="ConfigurationFile"/>.
        /// </remarks>
        public Stream? ConfigurationStream { get; set; }

        /// <summary>
        /// Optional callback invoked with the <see cref="ApplicationConfiguration"/>
        /// loaded from <see cref="ConfigurationFile"/> or
        /// <see cref="ConfigurationStream"/>, after the document has been
        /// read and validated but before the application-instance
        /// certificate is checked. Use it to override individual settings
        /// from code without editing the document. Ignored when neither
        /// <see cref="ConfigurationFile"/> nor
        /// <see cref="ConfigurationStream"/> is set.
        /// </summary>
        public Action<ApplicationConfiguration>? ConfigureLoadedConfiguration { get; set; }

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

        /// <summary>
        /// <c>true</c> when an existing configuration XML document was
        /// supplied via <see cref="ConfigurationFile"/> or
        /// <see cref="ConfigurationStream"/>.
        /// </summary>
        internal bool HasSuppliedConfigurationDocument =>
            !string.IsNullOrEmpty(ConfigurationFile) || ConfigurationStream != null;
    }
}
