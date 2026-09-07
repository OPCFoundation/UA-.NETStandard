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
using System.Text.Json.Serialization;

namespace UaLens.Plugins.Gds;

/// <summary>
/// Mutable, parameter-less, internal JSON round-trip companion for
/// <see cref="RegisteredApplicationContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RegisteredApplicationContext"/> is an <em>internal</em>
/// immutable <c>record</c> with a primary constructor — incompatible
/// with most serialisers, which require a parameter-less constructor
/// and settable properties.
/// </para>
/// <para>
/// This sibling DTO is a flat POCO with no references to the internal
/// record or its enum, dedicated to System.Text.Json round-trip via the
/// source-generated <see cref="RegisteredApplicationContextJsonContext"/>.
/// Conversion to/from the internal record lives in
/// <c>RegisteredApplicationContextXml</c> alongside the record itself.
/// </para>
/// <para>
/// Property names on the wire are camelCase courtesy of the JSON naming
/// policy declared on <see cref="RegisteredApplicationContextJsonContext"/>;
/// null values are omitted on write. No per-property
/// <see cref="JsonPropertyNameAttribute"/> overrides are needed.
/// </para>
/// <para>
/// <see cref="EndpointDescription"/> serialisation is deliberately not
/// round-tripped: the push endpoint is reduced to URL + SecurityMode +
/// SecurityPolicyUri. UaLens always re-discovers the endpoint at
/// use-time so the persisted URL is enough to drive the picker.
/// </para>
/// </remarks>
internal sealed class RegisteredApplicationContextDto
{
    /// <summary>GDS-assigned application id as its string form
    /// (<c>NodeId.ToString()</c>); empty when not yet registered.</summary>
    public string? ApplicationId { get; set; }

    /// <summary>Canonical Application URI.</summary>
    public string ApplicationUri { get; set; } = string.Empty;

    /// <summary>Display name shown in the UI.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Product URI used during registration.</summary>
    public string ProductUri { get; set; } = string.Empty;

    /// <summary>Stringified <c>GdsRegistrationType</c>:
    /// <c>ClientPull</c>, <c>ServerPull</c>, or <c>ServerPush</c>.</summary>
    public string RegistrationType { get; set; } = "ClientPull";

    /// <summary>DiscoveryUrls advertised for the application.</summary>
    public List<string> DiscoveryUrls { get; set; } = new();

    /// <summary>Server capabilities ("LiveData", "DA", "HA", …).</summary>
    public List<string> ServerCapabilities { get; set; } = new();

    /// <summary>Comma-separated SAN list used when crafting CSRs.</summary>
    public string? Domains { get; set; }

    /// <summary>Local certificate-store path (pull mode).</summary>
    public string? CertificateStorePath { get; set; }

    /// <summary>Subject name used when generating a CSR.</summary>
    public string? CertificateSubjectName { get; set; }

    /// <summary>Public-key DER/PEM file path (pull mode).</summary>
    public string? CertificatePublicKeyPath { get; set; }

    /// <summary>Private-key PFX/PEM file path (pull mode).</summary>
    public string? CertificatePrivateKeyPath { get; set; }

    /// <summary>Trust-list directory store path (pull mode).</summary>
    public string? TrustListStorePath { get; set; }

    /// <summary>Issuer-list directory store path (pull mode).</summary>
    public string? IssuerListStorePath { get; set; }

    /// <summary>HTTPS public-key file path (pull mode).</summary>
    public string? HttpsCertificatePublicKeyPath { get; set; }

    /// <summary>HTTPS private-key file path (pull mode).</summary>
    public string? HttpsCertificatePrivateKeyPath { get; set; }

    /// <summary>HTTPS trust-list store path (pull mode).</summary>
    public string? HttpsTrustListStorePath { get; set; }

    /// <summary>HTTPS issuer-list store path (pull mode).</summary>
    public string? HttpsIssuerListStorePath { get; set; }

    /// <summary>Push-endpoint URL (only meaningful in <c>ServerPush</c> mode).</summary>
    public string? PushEndpointUrl { get; set; }

    /// <summary>Push-endpoint <c>MessageSecurityMode</c> as a string
    /// (e.g. <c>SignAndEncrypt</c>).</summary>
    public string? PushEndpointSecurityMode { get; set; }

    /// <summary>Push-endpoint <c>SecurityPolicyUri</c>.</summary>
    public string? PushEndpointSecurityPolicyUri { get; set; }
}

/// <summary>
/// System.Text.Json source-generated serialiser context for
/// <see cref="RegisteredApplicationContextDto"/>.
/// </summary>
/// <remarks>
/// Using a <see cref="JsonSerializerContext"/> makes the round-trip
/// trim-safe and NativeAOT-compatible — required because the
/// <c>net10.0</c> Lens build ships with <c>&lt;PublishAot&gt;true&lt;/PublishAot&gt;</c>
/// and full trimming.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RegisteredApplicationContextDto))]
internal sealed partial class RegisteredApplicationContextJsonContext : JsonSerializerContext
{
}
