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
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Directory query filters passed without changing the server's cursor semantics.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsQueryInput
    {
        /// <summary>
        /// Gets or sets the server's record cursor.
        /// </summary>
        public uint StartingRecordId { get; set; }

        /// <summary>
        /// Gets or sets the wire-level record limit, 1..500.
        /// </summary>
        public uint MaxRecords { get; set; } = 100;

        /// <summary>
        /// Gets or sets the application-name filter.
        /// </summary>
        public string ApplicationName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the application-URI filter.
        /// </summary>
        public string ApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Part 12 application-type filter.
        /// </summary>
        public uint ApplicationType { get; set; }

        /// <summary>
        /// Gets or sets the product-URI filter.
        /// </summary>
        public string ProductUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the required capability identifiers.
        /// </summary>
        [Description(GdsToolDescriptions.ServerCapabilities)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> ServerCapabilities { get; set; } = [];
    }

    /// <summary>
    /// One localized application name; locale is preserved rather than inferred.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsApplicationName
    {
        /// <summary>
        /// Gets or sets the name text.
        /// </summary>
        public required string Text { get; set; }

        /// <summary>
        /// Gets or sets the optional locale.
        /// </summary>
        public string? Locale { get; set; }
    }

    /// <summary>
    /// A complete typed application registration. Update is replacement, not a merge or fabricated CAS.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsApplicationInput
    {
        /// <summary>
        /// Gets or sets the existing application id for update.
        /// </summary>
        public string? ApplicationId { get; set; }

        /// <summary>
        /// Gets or sets the application URI.
        /// </summary>
        public required string ApplicationUri { get; set; }

        /// <summary>
        /// Gets or sets the Part 4 application kind.
        /// </summary>
        public ApplicationType ApplicationType { get; set; }

        /// <summary>
        /// Gets or sets explicit localized display names.
        /// </summary>
        [Description(GdsToolDescriptions.ApplicationNames)]
        [JsonConverter(typeof(GdsInputArrayJsonConverter<GdsApplicationName>))]
        public ArrayOf<GdsApplicationName> ApplicationNames { get; set; } = [];

        /// <summary>
        /// Gets or sets the product URI.
        /// </summary>
        public required string ProductUri { get; set; }

        /// <summary>
        /// Gets or sets discovery URLs; registration never connects to them.
        /// </summary>
        [Description(GdsToolDescriptions.DiscoveryUrls)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> DiscoveryUrls { get; set; } = [];

        /// <summary>
        /// Gets or sets server capability identifiers.
        /// </summary>
        [Description(GdsToolDescriptions.ServerCapabilities)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> ServerCapabilities { get; set; } = [];
    }

    /// <summary>
    /// A pull certificate request generating private material at the remote issuer.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsKeyPairInput
    {
        /// <summary>
        /// Gets or sets the registered application id.
        /// </summary>
        public required string ApplicationId { get; set; }

        /// <summary>
        /// Gets or sets the group id; omission selects the protocol default.
        /// </summary>
        public string? CertificateGroupId { get; set; }

        /// <summary>
        /// Gets or sets the advertised certificate type id.
        /// </summary>
        public required string CertificateTypeId { get; set; }

        /// <summary>
        /// Gets or sets the requested subject name.
        /// </summary>
        public required string SubjectName { get; set; }

        /// <summary>
        /// Gets or sets subject alternative names.
        /// </summary>
        [Description(GdsToolDescriptions.DomainNames)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> DomainNames { get; set; } = [];

        /// <summary>
        /// Gets or sets the server-advertised private-key format.
        /// </summary>
        public required string PrivateKeyFormat { get; set; }

        /// <summary>
        /// Gets or sets a protected UTF-8 password reference, never a plaintext password.
        /// </summary>
        public required string PasswordReference { get; set; }
    }

    /// <summary>
    /// Explicit certificate slot selection and server-generated CSR settings.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsCsrInput
    {
        /// <summary>
        /// Gets or sets the certificate group.
        /// </summary>
        public string? CertificateGroupId { get; set; }

        /// <summary>
        /// Gets or sets the certificate type.
        /// </summary>
        public required string CertificateTypeId { get; set; }

        /// <summary>
        /// Gets or sets the subject name.
        /// </summary>
        public required string SubjectName { get; set; }

        /// <summary>
        /// Gets or sets the explicit remote key-regeneration request.
        /// </summary>
        public bool RegeneratePrivateKey { get; set; }

        /// <summary>
        /// Gets or sets an authorized nonce reference, required for key regeneration.
        /// </summary>
        public string? NonceReference { get; set; }
    }

    /// <summary>
    /// Explicit self-signed certificate settings for the advertised optional server method.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsSelfSignedInput
    {
        /// <summary>
        /// Gets or sets the certificate group.
        /// </summary>
        public string? CertificateGroupId { get; set; }

        /// <summary>
        /// Gets or sets the certificate type.
        /// </summary>
        public required string CertificateTypeId { get; set; }

        /// <summary>
        /// Gets or sets the subject.
        /// </summary>
        public required string SubjectName { get; set; }

        /// <summary>
        /// Gets or sets explicit DNS names.
        /// </summary>
        [Description(GdsToolDescriptions.DnsNames)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> DnsNames { get; set; } = [];

        /// <summary>
        /// Gets or sets explicit IP addresses.
        /// </summary>
        [Description(GdsToolDescriptions.IpAddresses)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> IpAddresses { get; set; } = [];

        /// <summary>
        /// Gets or sets the requested lifetime in days; validation remains the server's decision.
        /// </summary>
        public ushort LifetimeInDays { get; set; } = 365;

        /// <summary>
        /// Gets or sets the requested key size in bits.
        /// </summary>
        public ushort KeySizeInBits { get; set; } = 2048;
    }

    /// <summary>
    /// A certificate staging operation with public files and protected private-key material.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsPushCertificateInput
    {
        /// <summary>
        /// Gets or sets the target group.
        /// </summary>
        public string? CertificateGroupId { get; set; }

        /// <summary>
        /// Gets or sets the advertised certificate type.
        /// </summary>
        public required string CertificateTypeId { get; set; }

        /// <summary>
        /// Gets or sets a public DER file beneath TransferRoot.
        /// </summary>
        public required string CertificatePath { get; set; }

        /// <summary>
        /// Gets or sets public issuer DER file paths.
        /// </summary>
        [Description(GdsToolDescriptions.IssuerCertificatePaths)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> IssuerCertificatePaths { get; set; } = [];

        /// <summary>
        /// Gets or sets the server-advertised private-key format.
        /// </summary>
        public string PrivateKeyFormat { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets an optional private-key reference; omission uses the server's pending key.
        /// </summary>
        public string? PrivateKeyReference { get; set; }
    }

    /// <summary>
    /// Input to a KeyCredential request, excluding any private key or plaintext credential.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsKeyCredentialInput
    {
        /// <summary>
        /// Gets or sets the requesting application's URI.
        /// </summary>
        public required string ApplicationUri { get; set; }

        /// <summary>
        /// Gets or sets the optional public encryption-key file.
        /// </summary>
        public string? PublicKeyPath { get; set; }

        /// <summary>
        /// Gets or sets the requested policy URI; empty lets the server choose.
        /// </summary>
        public string SecurityPolicyUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets requested role NodeIds.
        /// </summary>
        [Description(GdsToolDescriptions.KeyCredentialRoles)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> RequestedRoles { get; set; } = [];
    }

    /// <summary>
    /// Selects the wire identity-token kind without treating the channel identity as a token proof.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<GdsIdentityKind>))]
    public enum GdsIdentityKind
    {
        /// <summary>
        /// An explicitly anonymous user-token proof.
        /// </summary>
        Anonymous,

        /// <summary>
        /// A username with protected, policy-encoded password bytes.
        /// </summary>
        UserName,

        /// <summary>
        /// An issued identity token with protected token bytes.
        /// </summary>
        IssuedToken,

        /// <summary>
        /// An X.509 identity certificate with a separately protected signature.
        /// </summary>
        Certificate
    }

    /// <summary>
    /// Policy-prepared user proof. Encryption/signature algorithms remain explicit and server-validated.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsTokenProof
    {
        /// <summary>
        /// Gets or sets the explicit identity kind.
        /// </summary>
        public GdsIdentityKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the advertised user-token policy id.
        /// </summary>
        public string PolicyId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the username for a username proof.
        /// </summary>
        public string UserName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a reference to the policy-encoded password, token or certificate bytes.
        /// </summary>
        public string? CredentialReference { get; set; }

        /// <summary>
        /// Gets or sets the identity token's encryption algorithm.
        /// </summary>
        public string EncryptionAlgorithm { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a protected user-token signature reference.
        /// </summary>
        public string? SignatureReference { get; set; }

        /// <summary>
        /// Gets or sets the user-token signature algorithm.
        /// </summary>
        public string SignatureAlgorithm { get; set; } = string.Empty;
    }

    /// <summary>
    /// A bounded, concrete array of protected onboarding-ticket references.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsTicketInput
    {
        /// <summary>
        /// Gets or sets 1..100 aliases or owner-bound issued references.
        /// </summary>
        [Description(GdsToolDescriptions.TicketReferences)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public required ArrayOf<string> TicketReferences { get; set; }
    }

    /// <summary>
    /// A closed typed role array for modern token finish.
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed class GdsTokenFinishInput
    {
        /// <summary>
        /// Gets or sets requested role names, not a fabricated granted-role list.
        /// </summary>
        [Description(GdsToolDescriptions.AuthorizationRoles)]
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> RequestedRoles { get; set; } = [];

        /// <summary>
        /// Gets or sets the explicit user-token proof.
        /// </summary>
        public GdsTokenProof Proof { get; set; } = new();
    }

    /// <summary>
    /// Reflection-free binding for generated GDS request arrays.
    /// </summary>
    /// <typeparam name="T">A registered GDS request element.</typeparam>
    public sealed class GdsInputArrayJsonConverter<T> : McpInputArrayJsonConverter<T>
        where T : class
    {
        /// <inheritdoc/>
        protected override JsonTypeInfo<T> ElementType =>
            GdsInputJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T> ??
            throw new NotSupportedException("The element is not a registered GDS input type.");
    }

    /// <summary>
    /// Source-generated metadata for all concrete GDS tool inputs.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, UseStringEnumConverter = true)]
    [JsonSerializable(typeof(GdsQueryInput))]
    [JsonSerializable(typeof(GdsApplicationName))]
    [JsonSerializable(typeof(GdsApplicationInput))]
    [JsonSerializable(typeof(GdsKeyPairInput))]
    [JsonSerializable(typeof(GdsCsrInput))]
    [JsonSerializable(typeof(GdsSelfSignedInput))]
    [JsonSerializable(typeof(GdsPushCertificateInput))]
    [JsonSerializable(typeof(GdsKeyCredentialInput))]
    [JsonSerializable(typeof(GdsTokenProof))]
    [JsonSerializable(typeof(GdsTicketInput))]
    [JsonSerializable(typeof(GdsTokenFinishInput))]
    [JsonSerializable(typeof(GdsCredentialServiceKind))]
    [JsonSerializable(typeof(GdsCertificateSource))]
    [JsonSerializable(typeof(int))]
    internal sealed partial class GdsInputJsonContext : JsonSerializerContext;
}
