// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Migration shim &#8212; restores the <c>SecurityPolicies</c> lookup and
    /// cryptography statics that 2.0 moved onto
    /// <see cref="ISecurityPolicyRegistry"/>.
    /// </summary>
    /// <remarks>
    /// These operate on the set of registered security policies rather than on
    /// constants, so in 2.0 they are members of the registry that owns that set.
    /// Each shim forwards to <see cref="SecurityPolicies.Default"/>, which
    /// carries the built-in policies &#8212; so a 1.05.378 application that never
    /// registered a policy of its own behaves as it did.
    /// <para>
    /// The policy URI constants (<c>SecurityPolicies.None</c> and friends) are
    /// unaffected and remain on <c>SecurityPolicies</c>.
    /// </para>
    /// </remarks>
    public static class SecurityPoliciesShim
    {
        extension(SecurityPolicies)
        {
            /// <summary>
            /// Returns the uri associated with the display name.
            /// </summary>
            [Obsolete("SecurityPolicies.GetUri was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an " +
                "ISecurityPolicyRegistry, or use SecurityPolicies.Default.GetUri(displayName). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static string? GetUri(string displayName)
                => SecurityPolicies.Default.GetUri(displayName);

            /// <summary>
            /// Returns a display name for a security policy uri.
            /// </summary>
            [Obsolete("SecurityPolicies.GetDisplayName was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an " +
                "ISecurityPolicyRegistry, or use SecurityPolicies.Default.GetDisplayName(policyUri). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static string? GetDisplayName(string policyUri)
                => SecurityPolicies.Default.GetDisplayName(policyUri);

            /// <summary>
            /// If a security policy is known and spelled according to the spec.
            /// </summary>
            [Obsolete("SecurityPolicies.IsValidSecurityPolicyUri was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an ISecurityPolicyRegistry, or use " +
                "SecurityPolicies.Default.IsValidSecurityPolicyUri(policyUri). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static bool IsValidSecurityPolicyUri(string policyUri)
                => SecurityPolicies.Default.IsValidSecurityPolicyUri(policyUri);

            /// <summary>
            /// Returns the display names for all security policy uris including https.
            /// </summary>
            [Obsolete("SecurityPolicies.GetDisplayNames was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an ISecurityPolicyRegistry, or use " +
                "SecurityPolicies.Default.GetDisplayNames(). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static string[] GetDisplayNames()
                => SecurityPolicies.Default.GetDisplayNames();

            /// <summary>
            /// Returns the deprecated RSA security policy uri.
            /// </summary>
            [Obsolete("SecurityPolicies.GetDefaultDeprecatedUris was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an ISecurityPolicyRegistry, or use " +
                "SecurityPolicies.Default.GetDefaultDeprecatedUris(). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static string[] GetDefaultDeprecatedUris()
                => SecurityPolicies.Default.GetDefaultDeprecatedUris();

            /// <summary>
            /// Returns the default RSA security policy uri.
            /// </summary>
            [Obsolete("SecurityPolicies.GetDefaultUris was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an ISecurityPolicyRegistry, or use " +
                "SecurityPolicies.Default.GetDefaultUris(). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static string[] GetDefaultUris()
                => SecurityPolicies.Default.GetDefaultUris();

            /// <summary>
            /// Returns the default ECC security policy uri.
            /// </summary>
            [Obsolete("SecurityPolicies.GetDefaultEccUris was moved in 2.0 to ISecurityPolicyRegistry, " +
                "because it reads the set of registered policies. Resolve an ISecurityPolicyRegistry, or use " +
                "SecurityPolicies.Default.GetDefaultEccUris(). " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static string[] GetDefaultEccUris()
                => SecurityPolicies.Default.GetDefaultEccUris();

            /// <summary>
            /// Encrypts the text using the SecurityPolicyUri and returns the result.
            /// </summary>
            /// <remarks>
            /// <paramref name="logger"/> is accepted for source compatibility and
            /// ignored: the registry reports through the logger it was created with.
            /// </remarks>
            [Obsolete("SecurityPolicies.Encrypt was moved in 2.0 to ISecurityPolicyRegistry, which resolves " +
                "the policy from the set it owns and logs through its own logger. Resolve an " +
                "ISecurityPolicyRegistry, or use SecurityPolicies.Default.Encrypt(certificate, " +
                "securityPolicyUri, plainText) - note the logger argument is gone. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static EncryptedData Encrypt(
                Certificate certificate,
                string securityPolicyUri,
                ReadOnlySpan<byte> plainText,
                ILogger logger)
                => SecurityPolicies.Default.Encrypt(certificate, securityPolicyUri, plainText);

            /// <summary>
            /// Decrypts the CipherText using the SecurityPolicyUri and returns the PlainText.
            /// </summary>
            /// <remarks>
            /// <paramref name="logger"/> is accepted for source compatibility and
            /// ignored: the registry reports through the logger it was created with.
            /// </remarks>
            [Obsolete("SecurityPolicies.Decrypt was moved in 2.0 to ISecurityPolicyRegistry, which resolves " +
                "the policy from the set it owns and logs through its own logger. Resolve an " +
                "ISecurityPolicyRegistry, or use SecurityPolicies.Default.Decrypt(certificate, " +
                "securityPolicyUri, dataToDecrypt) - note the logger argument is gone. " +
                "See https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md#ua0029")]
            [OpcUaShim("UA0029")]
            public static byte[]? Decrypt(
                Certificate certificate,
                string securityPolicyUri,
                EncryptedData? dataToDecrypt,
                ILogger logger)
                => SecurityPolicies.Default.Decrypt(certificate, securityPolicyUri, dataToDecrypt);
        }
    }
}
