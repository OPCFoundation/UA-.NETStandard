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

using System.Collections.Generic;
using Opc.Ua;

namespace UaLens.Plugins.Gds;

/// <summary>
/// Which side of the GDS lifecycle a particular registered application
/// is managed from. Mirrors <c>Opc.Ua.Gds.Client.RegistrationType</c> in
/// the reference sample without taking a hard dependency on the
/// sample-only type.
/// </summary>
internal enum GdsRegistrationType
{
    /// <summary>Client application that pulls its own cert + trust list from the GDS.</summary>
    ClientPull,

    /// <summary>Server application that pulls its own cert + trust list from the GDS.</summary>
    ServerPull,

    /// <summary>
    /// Server application whose configuration is pushed remotely via the
    /// <c>ServerConfiguration</c> object (push-management).
    /// </summary>
    ServerPush
}

/// <summary>
/// Immutable view of "the registered application UaLens is currently
/// working with". Set by <see cref="UaLens.Plugins.GdsManagement.GdsManagementPlugin"/>
/// (or any other plug-in that resolves a record), consumed by the
/// GdsPush / GdsDiscovery tabs to drive their delivery flows without
/// re-querying the GDS.
/// </summary>
/// <remarks>
/// <para>
/// The record mirrors the relevant fields of the sample's
/// <c>RegisteredApplication</c> POCO
/// (<c>UA-.NETStandard-Samples/Samples/GDS/Client</c>): identity, cert /
/// trust-list local paths for pull mode, and the push endpoint for
/// push mode. The two file-path sets co-exist because a server can be
/// registered with the GDS in pull mode and pushed-to from UaLens at
/// the same time (the sample's MainForm handles the same overlap).
/// </para>
/// <para>
/// All paths are stored verbatim — callers are responsible for
/// resolving special-folder placeholders (e.g.
/// <c>%CommonApplicationData%</c>) via <see cref="Utils.GetAbsoluteFilePath"/>
/// or <see cref="Utils.GetAbsoluteDirectoryPath"/> before use.
/// </para>
/// </remarks>
/// <param name="ApplicationId">GDS-assigned NodeId; <see cref="Opc.Ua.NodeId.Null"/> when not yet registered.</param>
/// <param name="ApplicationUri">Canonical Application URI; never empty for a valid record.</param>
/// <param name="ApplicationName">Display name shown in the UI.</param>
/// <param name="ProductUri">Product URI used during registration.</param>
/// <param name="RegistrationType">Pull vs. Push lifecycle.</param>
/// <param name="DiscoveryUrls">DiscoveryUrls advertised for the application.</param>
/// <param name="ServerCapabilities">Server capabilities ("LiveData", "DA", "HA", …).</param>
/// <param name="Domains">Comma-separated SAN list used when crafting CSRs.</param>
/// <param name="CertificateStorePath">Local certificate-store path (pull mode).</param>
/// <param name="CertificateSubjectName">Subject name used when generating a CSR.</param>
/// <param name="CertificatePublicKeyPath">Public-key DER/PEM file path (pull mode).</param>
/// <param name="CertificatePrivateKeyPath">Private-key PFX/PEM file path (pull mode).</param>
/// <param name="TrustListStorePath">Trust-list directory store path (pull mode).</param>
/// <param name="IssuerListStorePath">Issuer-list directory store path (pull mode).</param>
/// <param name="HttpsCertificatePublicKeyPath">HTTPS public-key file path (pull mode).</param>
/// <param name="HttpsCertificatePrivateKeyPath">HTTPS private-key file path (pull mode).</param>
/// <param name="HttpsTrustListStorePath">HTTPS trust-list store path (pull mode).</param>
/// <param name="HttpsIssuerListStorePath">HTTPS issuer-list store path (pull mode).</param>
/// <param name="PushEndpoint">Endpoint that exposes <c>ServerConfiguration</c> (push mode); <c>null</c> for pull.</param>
internal sealed record RegisteredApplicationContext(
    NodeId ApplicationId,
    string ApplicationUri,
    string ApplicationName,
    string ProductUri,
    GdsRegistrationType RegistrationType,
    IReadOnlyList<string> DiscoveryUrls,
    IReadOnlyList<string> ServerCapabilities,
    string? Domains = null,
    string? CertificateStorePath = null,
    string? CertificateSubjectName = null,
    string? CertificatePublicKeyPath = null,
    string? CertificatePrivateKeyPath = null,
    string? TrustListStorePath = null,
    string? IssuerListStorePath = null,
    string? HttpsCertificatePublicKeyPath = null,
    string? HttpsCertificatePrivateKeyPath = null,
    string? HttpsTrustListStorePath = null,
    string? HttpsIssuerListStorePath = null,
    EndpointDescription? PushEndpoint = null)
{
    /// <summary>True when this record has been assigned a GDS application id.</summary>
    public bool IsRegistered => !ApplicationId.IsNull;

    /// <summary>True when the record points at a push-enabled endpoint.</summary>
    public bool HasPushEndpoint => PushEndpoint is not null;
}
