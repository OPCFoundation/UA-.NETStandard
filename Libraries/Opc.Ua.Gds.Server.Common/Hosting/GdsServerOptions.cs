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
using Opc.Ua.Configuration;

#nullable enable

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// Options for an OPC UA Global Discovery Server (GDS) hosted by
    /// the .NET Generic Host via
    /// <see cref="Microsoft.Extensions.DependencyInjection.OpcUaGdsServerBuilderExtensions.AddGdsServer(IOpcUaBuilder, Action{GdsServerOptions})"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <c>OpcUaServerOptions</c> for the standard server feature
    /// and adds GDS-specific knobs that populate the
    /// <see cref="GlobalDiscoveryServerConfiguration"/> extension attached
    /// to the underlying <see cref="ApplicationConfiguration"/>.
    /// </para>
    /// <para>
    /// Only the most common knobs are exposed directly. Use
    /// <see cref="ConfigureBuilder"/> for full control of the underlying
    /// <see cref="IApplicationConfigurationBuilder"/> chain (security
    /// policies, transport quotas, custom security stores, additional
    /// configuration extensions, etc.).
    /// </para>
    /// </remarks>
    public sealed class GdsServerOptions
    {
        /// <summary>
        /// Application name. Used as the default for the certificate
        /// subject common-name and the PKI root path when those are not
        /// set explicitly.
        /// </summary>
        public string ApplicationName { get; set; } = "GlobalDiscoveryServer";

        /// <summary>
        /// Application URI (e.g. <c>urn:localhost:OPCFoundation:GDS</c>).
        /// </summary>
        public string ApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Product URI (e.g. <c>http://opcfoundation.org/UA/GDS</c>).
        /// </summary>
        public string ProductUri { get; set; } = string.Empty;

        /// <summary>
        /// Optional explicit certificate subject. When not set, defaults
        /// to <c>CN={ApplicationName}, O=OPC Foundation, DC=localhost</c>.
        /// </summary>
        public string SubjectName { get; set; } = string.Empty;

        /// <summary>
        /// Listen URLs. Mutated in place by
        /// <c>options.EndpointUrls.Add(...)</c>.
        /// </summary>
        public IList<string> EndpointUrls { get; } = [];

        /// <summary>
        /// Filesystem root used for the certificate stores. When empty,
        /// defaults to <c>%TEMP%/OPC Foundation/{ApplicationName}/pki</c>.
        /// </summary>
        public string PkiRoot { get; set; } = string.Empty;

        /// <summary>
        /// Whether unknown peer certificates are auto-accepted.
        /// Convenient for quickstart samples; do not enable in
        /// production.
        /// </summary>
        public bool AutoAcceptUntrustedCertificates { get; set; }

        /// <summary>
        /// Toggle the OPC UA server diagnostics node manager.
        /// </summary>
        public bool DiagnosticsEnabled { get; set; } = true;

        /// <summary>
        /// Maximum byte-string length advertised on the transport.
        /// Defaults to 4 MiB.
        /// </summary>
        public uint MaxByteStringLength { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Maximum array length advertised on the transport. Defaults
        /// to 1 Mi elements.
        /// </summary>
        public uint MaxArrayLength { get; set; } = 1024 * 1024;

        /// <summary>
        /// When true (default), <c>AddSignAndEncryptPolicies()</c> is
        /// applied to the configuration builder.
        /// </summary>
        public bool IncludeSignAndEncryptPolicies { get; set; } = true;

        /// <summary>
        /// When true, <c>AddUnsecurePolicyNone()</c> is applied to the
        /// configuration builder. Off by default.
        /// </summary>
        public bool IncludeUnsecurePolicyNone { get; set; }

        /// <summary>
        /// Whether new certificate requests are auto-approved.
        /// Convenient for quickstart samples; do not enable in
        /// production.
        /// </summary>
        public bool AutoApprove { get; set; } = true;

        /// <summary>
        /// Filesystem path for the trusted CA Authorities store.
        /// When empty, defaults to <c>{PkiRoot}/CA/authorities</c>.
        /// </summary>
        public string AuthoritiesStorePath { get; set; } = string.Empty;

        /// <summary>
        /// Filesystem path for the registered Applications certificate
        /// store. When empty, defaults to <c>{PkiRoot}/applications</c>.
        /// </summary>
        public string ApplicationCertificatesStorePath { get; set; } = string.Empty;

        /// <summary>
        /// Filesystem path used as the base for the default certificate
        /// group's CA. When empty, defaults to <c>{PkiRoot}/CA</c>.
        /// </summary>
        public string BaseCertificateGroupStorePath { get; set; } = string.Empty;

        /// <summary>
        /// Default subject-name suffix appended to certificates issued
        /// by this GDS (e.g. <c>,O=OPC Foundation,DC=localhost</c>).
        /// </summary>
        public string DefaultSubjectNameContext { get; set; } = string.Empty;

        /// <summary>
        /// Optional escape hatch invoked after the standard
        /// configuration steps (transport quotas, server policies,
        /// security configuration, GDS extension) but before
        /// <c>CreateAsync</c>. Use it to add bespoke security policies,
        /// override quotas, or add custom security stores.
        /// </summary>
        public Action<IApplicationConfigurationBuilderServerSelected>? ConfigureBuilder { get; set; }
    }
}
