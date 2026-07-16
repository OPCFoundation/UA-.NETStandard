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
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Identity
{
    /// <summary>
    /// Helper APIs for materializing client user identities from endpoint policies.
    /// </summary>
    public static class ClientIdentityProviderExtensions
    {
        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// The set of client-enabled security policies defaults to the channel security policy carried
        /// on <paramref name="endpointDescription"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpointDescription"/> or <paramref name="messageContext"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            EndpointDescription endpointDescription,
            IServiceMessageContext messageContext,
            CancellationToken ct = default)
        {
            return AcquireIdentityAsync(
                provider,
                endpointDescription,
                messageContext,
                ArrayOf<string>.Null,
                ct);
        }

        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="endpointDescription"/> or <paramref name="messageContext"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            EndpointDescription endpointDescription,
            IServiceMessageContext messageContext,
            ArrayOf<string> enabledSecurityPolicyUris,
            CancellationToken ct = default)
        {
            if (endpointDescription == null)
            {
                throw new ArgumentNullException(nameof(endpointDescription));
            }
            if (messageContext == null)
            {
                throw new ArgumentNullException(nameof(messageContext));
            }

            var context = new IdentitySelectionContext(
                endpointDescription,
                endpointDescription.UserIdentityTokens,
                messageContext,
                enabledSecurityPolicyUris);
            return provider.AcquireIdentityAsync(context, ct);
        }

        /// <summary>
        /// Selects a matching user-token policy and asks <paramref name="provider"/> to create an identity.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
        public static async ValueTask<IUserIdentity> AcquireIdentityAsync(
            this IClientIdentityProvider provider,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            UserTokenPolicy identityPolicy = await provider
                .SelectUserTokenPolicyAsync(context, ct)
                .ConfigureAwait(false);
            IUserIdentity identity = await provider
                .GetIdentityAsync(identityPolicy, context, ct)
                .ConfigureAwait(false);
            identity.TokenHandler.Token.PolicyId = identityPolicy.PolicyId;
            return identity;
        }

        /// <summary>
        /// Selects the best offered user-token policy for <paramref name="provider"/>.
        /// </summary>
        /// <remarks>
        /// Each offered <see cref="UserTokenPolicy"/> is evaluated in two stages:
        /// <list type="number">
        /// <item><description>
        /// Policies with a non-empty <see cref="UserTokenPolicy.SecurityPolicyUri"/> that is
        /// not in <see cref="IdentitySelectionContext.EnabledSecurityPolicyUris"/> are skipped
        /// (rejection reason <c>NotEnabledByClient</c>). Policies with an empty
        /// <see cref="UserTokenPolicy.SecurityPolicyUri"/> inherit the channel policy and pass
        /// this filter unconditionally.
        /// </description></item>
        /// <item><description>
        /// Surviving candidates are submitted to
        /// <see cref="IClientIdentityProvider.CanSatisfyAsync"/>. Providers may reject a policy
        /// for token-type, profile-URI, or material-specific reasons (e.g. an X.509 user-cert
        /// public key whose curve does not match the policy's
        /// <see cref="UserTokenPolicy.SecurityPolicyUri"/>).
        /// </description></item>
        /// </list>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="provider"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadIdentityTokenRejected"/> when the endpoint offers no
        /// user-token policy that the provider can satisfy. The message lists each offered policy
        /// together with the reason it was rejected.
        /// </exception>
        public static async ValueTask<UserTokenPolicy> SelectUserTokenPolicyAsync(
            this IClientIdentityProvider provider,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            string tokenSecurityPolicyUri =
                context.EndpointDescription?.SecurityPolicyUri ?? SecurityPolicies.None;
            UserTokenPolicy? sameEncryptionAlgorithm = null;
            UserTokenPolicy? unspecifiedSecPolicy = null;
            UserTokenPolicy? certificateAnyFamily = null;
            var rejections = new List<(UserTokenPolicy Policy, string Reason)>();

            ArrayOf<UserTokenPolicy> offered = context.OfferedPolicies;
            WarnIfPolicyUniquenessViolated(offered, context);
            for (int i = 0; i < offered.Count; i++)
            {
                UserTokenPolicy policy = offered[i];
                if (!IsPolicyEnabledByClient(policy, context, out string? notEnabledReason))
                {
                    rejections.Add((policy, notEnabledReason));
                    continue;
                }

                if (!IsUserTokenPolicyValidForChannelMode(policy, context, out string? specReason))
                {
                    rejections.Add((policy, specReason));
                    continue;
                }

                if (!IsBoundEphemeralKeyCompatible(policy, context, out string? pinReason))
                {
                    rejections.Add((policy, pinReason));
                    continue;
                }

                if (!IsClientInstanceCertificateCompatible(policy, context, out string? instReason))
                {
                    rejections.Add((policy, instReason));
                    continue;
                }

                CanSatisfyResult satisfied = await provider
                    .CanSatisfyAsync(policy, context, ct)
                    .ConfigureAwait(false);
                if (!satisfied.CanSatisfy)
                {
                    rejections.Add((policy, satisfied.RejectionReason ?? "ProviderRejected"));
                    continue;
                }

                if (policy.TokenType == UserTokenType.Anonymous ||
                    string.Equals(
                        policy.SecurityPolicyUri,
                        tokenSecurityPolicyUri,
                        StringComparison.Ordinal))
                {
                    return policy;
                }

                if (string.IsNullOrEmpty(policy.SecurityPolicyUri))
                {
                    unspecifiedSecPolicy ??= policy;
                }
                else if (HasSameEncryptionAlgorithm(
                    policy.SecurityPolicyUri!,
                    tokenSecurityPolicyUri))
                {
                    sameEncryptionAlgorithm ??= policy;
                }
                else if (policy.TokenType == UserTokenType.Certificate)
                {
                    // Per Part 4 §7.41 last paragraph, CERTIFICATE
                    // UserTokenPolicies may use any valid SecurityPolicy
                    // regardless of the channel's PublicKey algorithm.
                    // Keep them as a low-preference fallback so they're
                    // selected when no same-family / inherits-channel
                    // candidate exists.
                    certificateAnyFamily ??= policy;
                }
            }

            UserTokenPolicy? selected = sameEncryptionAlgorithm
                ?? unspecifiedSecPolicy
                ?? certificateAnyFamily;
            if (selected != null)
            {
                return selected;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadIdentityTokenRejected,
                BuildNoPolicyDiagnostic(context, rejections));
        }

        private static bool IsPolicyEnabledByClient(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            [NotNullWhen(false)] out string? rejectionReason)
        {
            if (string.IsNullOrEmpty(policy?.SecurityPolicyUri))
            {
                rejectionReason = null;
                return true;
            }

            ArrayOf<string> enabled = context.EnabledSecurityPolicyUris;
            if (enabled.IsNull || enabled.Count == 0)
            {
                rejectionReason = null;
                return true;
            }

            ReadOnlySpan<string> span = enabled.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (string.Equals(span[i], policy.SecurityPolicyUri, StringComparison.Ordinal))
                {
                    rejectionReason = null;
                    return true;
                }
            }

            rejectionReason = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "NotEnabledByClient (client SupportedSecurityPolicies excludes '{0}').",
                policy.SecurityPolicyUri);
            return false;
        }

        private static bool IsBoundEphemeralKeyCompatible(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            [NotNullWhen(false)] out string? rejectionReason)
        {
            string? boundUri = context.CurrentEphemeralKeyPolicyUri;
            if (string.IsNullOrEmpty(boundUri))
            {
                rejectionReason = null;
                return true;
            }

            // The effective URI of an offered policy with empty
            // SecurityPolicyUri is the channel security policy.
            string effectiveUri = !string.IsNullOrEmpty(policy?.SecurityPolicyUri)
                ? policy!.SecurityPolicyUri!
                : context.EndpointDescription?.SecurityPolicyUri ?? SecurityPolicies.None;

            if (string.Equals(effectiveUri, boundUri, StringComparison.Ordinal))
            {
                rejectionReason = null;
                return true;
            }

            rejectionReason = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "EphemeralKeyPolicyMismatch (an ECC ephemeral key is already bound to '{0}'; call Session.UpdateIdentityAsync(overrideUserTokenPolicyUri: '{1}') to renew).",
                boundUri,
                effectiveUri);
            return false;
        }

        /// <summary>
        /// Enforces the <see cref="UserTokenPolicy"/> rules from
        /// <c>OPC 10000-4 §7.41</c>: when the channel SecurityPolicy
        /// is <see cref="SecurityPolicies.None"/>, USERNAME and
        /// ISSUEDTOKEN UserTokenPolicies must NOT use ECC or RSA-DH
        /// SecurityPolicies (no ephemeral-key handshake exists);
        /// otherwise an explicit user-token <see cref="UserTokenPolicy.SecurityPolicyUri"/>
        /// SHALL use the same PublicKey algorithm as the SecureChannel
        /// (RSA vs. ECC family). CERTIFICATE and Anonymous policies
        /// are exempt per the same section.
        /// </summary>
        private static bool IsUserTokenPolicyValidForChannelMode(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            [NotNullWhen(false)] out string? rejectionReason)
        {
            // Rules 1–3 in §7.41 only constrain USERNAME and
            // ISSUEDTOKEN UserTokenPolicies. CERTIFICATE may use any
            // valid SecurityPolicy; Anonymous carries no secret.
            if (policy == null ||
                (policy.TokenType != UserTokenType.UserName &&
                    policy.TokenType != UserTokenType.IssuedToken))
            {
                rejectionReason = null;
                return true;
            }

            // Empty / null SecurityPolicyUri inherits the channel policy
            // — the spec explicitly allows this and it's the recommended
            // form for endpoints that want the user-token to follow the
            // channel security.
            if (string.IsNullOrEmpty(policy.SecurityPolicyUri))
            {
                rejectionReason = null;
                return true;
            }

            SecurityPolicyInfo? policyInfo = SecurityPolicies.GetInfo(policy.SecurityPolicyUri);
            if (policyInfo == null)
            {
                // Unknown URI handled later by
                // UnknownOrUnsupportedSecurityPolicy in the X.509 path
                // (or the provider-specific CanSatisfyAsync). Don't
                // double-reject here.
                rejectionReason = null;
                return true;
            }

            string channelUri = context.EndpointDescription?.SecurityPolicyUri ?? SecurityPolicies.None;
            bool channelIsNone = string.Equals(channelUri, SecurityPolicies.None, StringComparison.Ordinal);

            if (channelIsNone)
            {
                if (policyInfo.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None)
                {
                    rejectionReason = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "InsecureChannelEccUserTokenForbidden (channel SecurityPolicy is None; per Part 4 §7.41 USERNAME/ISSUEDTOKEN UserTokenPolicies must not use ECC or RSA-DH SecurityPolicies — '{0}' requires an ephemeral-key handshake).",
                        policy.SecurityPolicyUri);
                    return false;
                }

                rejectionReason = null;
                return true;
            }

            SecurityPolicyInfo? channelInfo = SecurityPolicies.GetInfo(channelUri);
            if (channelInfo == null)
            {
                rejectionReason = null;
                return true;
            }

            bool channelIsEcc = channelInfo.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None;
            bool policyIsEcc = policyInfo.EphemeralKeyAlgorithm != CertificateKeyAlgorithm.None;
            if (channelIsEcc != policyIsEcc)
            {
                rejectionReason = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "PublicKeyAlgorithmMismatch (channel '{0}' uses {1}, user-token policy '{2}' uses {3}; per Part 4 §7.41 they must match for USERNAME/ISSUEDTOKEN).",
                    channelUri,
                    channelIsEcc ? "ECC" : "RSA",
                    policy.SecurityPolicyUri,
                    policyIsEcc ? "ECC" : "RSA");
                return false;
            }

            rejectionReason = null;
            return true;
        }

        /// <summary>
        /// Per Part 4 §7.41 an EndpointDescription <c>shall</c> have at
        /// most one USERNAME UserTokenPolicy and at most one ISSUEDTOKEN
        /// UserTokenPolicy for each unique <c>issuerEndpointUrl</c>.
        /// Servers that violate this are mis-advertising; the client
        /// continues with first-match selection but emits a structured
        /// warning so operators can detect / fix the server.
        /// </summary>
        private static void WarnIfPolicyUniquenessViolated(
            ArrayOf<UserTokenPolicy> offered,
            IdentitySelectionContext context)
        {
            int userNameCount = 0;
            Dictionary<string, int>? issuedTokenByUrl = null;

            for (int i = 0; i < offered.Count; i++)
            {
                UserTokenPolicy policy = offered[i];
                if (policy == null)
                {
                    continue;
                }

                if (policy.TokenType == UserTokenType.UserName)
                {
                    userNameCount++;
                }
                else if (policy.TokenType == UserTokenType.IssuedToken)
                {
                    string url = policy.IssuerEndpointUrl ?? string.Empty;
                    issuedTokenByUrl ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    issuedTokenByUrl[url] = issuedTokenByUrl.TryGetValue(url, out int existing)
                        ? existing + 1
                        : 1;
                }
            }

            if (userNameCount <= 1 &&
                (issuedTokenByUrl == null || !HasIssuedTokenDuplicate(issuedTokenByUrl)))
            {
                return;
            }

            ITelemetryContext? telemetry = context.MessageContext?.Telemetry;
            if (telemetry == null)
            {
                return;
            }

            ILogger logger = telemetry.CreateLogger(typeof(ClientIdentityProviderExtensions));
            if (userNameCount > 1)
            {
                logger.EndpointDescriptionAdvertisesCountUSERNAMEUserTokenPoliciesPer(userNameCount);
            }

            if (issuedTokenByUrl != null)
            {
                foreach (KeyValuePair<string, int> entry in issuedTokenByUrl)
                {
                    if (entry.Value > 1)
                    {
                        logger.EndpointDescriptionAdvertisesCountISSUEDTOKENUserTokenPoliciesIssuerEndpointUrl(
                            entry.Value,
                            string.IsNullOrEmpty(entry.Key) ? "<empty>" : entry.Key);
                    }
                }
            }
        }

        private static bool HasIssuedTokenDuplicate(Dictionary<string, int> counts)
        {
            foreach (KeyValuePair<string, int> entry in counts)
            {
                if (entry.Value > 1)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsClientInstanceCertificateCompatible(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            [NotNullWhen(false)] out string? rejectionReason)
        {
            CertificateKeyAlgorithm instanceAlg = context.ClientInstanceCertificateAlgorithm;
            if (instanceAlg == CertificateKeyAlgorithm.None)
            {
                // Caller did not supply an instance cert algorithm —
                // gate disabled for back-compat with legacy callers.
                rejectionReason = null;
                return true;
            }

            // The instance cert is only used as the ECDH sender for
            // EccEncryptedSecret-encrypted token payloads. That covers
            // UserName (encrypted password) and IssuedToken (encrypted
            // token data). X.509 user-token activation carries a DER
            // cert + RSA/ECDSA signature using the *user* cert; the
            // Anonymous token has no secret to encrypt. For those two
            // the instance cert is irrelevant — see
            // X509IdentityTokenHandler.EncryptAsync and
            // AnonymousIdentityTokenHandler.EncryptAsync (no-ops).
            if (policy == null ||
                (policy.TokenType != UserTokenType.UserName &&
                    policy.TokenType != UserTokenType.IssuedToken))
            {
                rejectionReason = null;
                return true;
            }

            string effectiveUri = !string.IsNullOrEmpty(policy.SecurityPolicyUri)
                ? policy.SecurityPolicyUri!
                : context.EndpointDescription?.SecurityPolicyUri ?? SecurityPolicies.None;

            if (string.Equals(effectiveUri, SecurityPolicies.None, StringComparison.Ordinal))
            {
                // Per Part 4 v1.05.07 §7.40.2 an effective policy of
                // None implies no encryption — no curve constraint.
                rejectionReason = null;
                return true;
            }

            SecurityPolicyInfo? info = SecurityPolicies.GetInfo(effectiveUri);
            if (info == null || info.EphemeralKeyAlgorithm == CertificateKeyAlgorithm.None)
            {
                // RSA user-token encryption uses the SERVER's certificate;
                // the client instance cert curve is irrelevant.
                rejectionReason = null;
                return true;
            }

            // ECC user-token encryption uses the client's instance
            // certificate as ECDH sender — its public-key curve must
            // match the policy's curve.
            if (info.CertificateKeyAlgorithm == instanceAlg)
            {
                rejectionReason = null;
                return true;
            }

            rejectionReason = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "ClientInstanceCertificateAlgorithmMismatch (client instance certificate is {0}, policy '{1}' requires {2} for ECC user-token encryption).",
                instanceAlg,
                effectiveUri,
                info.CertificateKeyAlgorithm);
            return false;
        }

        private const int MaxRejectionsInDiagnostic = 32;
        private const int MaxDiagnosticFieldLength = 256;

        private static string BuildNoPolicyDiagnostic(
            IdentitySelectionContext context,
            List<(UserTokenPolicy Policy, string Reason)> rejections)
        {
            var sb = new StringBuilder();
            sb.Append("Endpoint does not offer a user token policy that can be satisfied by the identity provider.");

            if (rejections == null || rejections.Count == 0)
            {
                sb.Append(" Endpoint advertises no user-token policies.");
                return sb.ToString();
            }

            int reportCount = Math.Min(rejections.Count, MaxRejectionsInDiagnostic);
            sb.Append(" Offered policies:");
            for (int i = 0; i < reportCount; i++)
            {
                (UserTokenPolicy policy, string reason) = rejections[i];
                sb.AppendLine()
                    .Append("  - PolicyId='")
                    .Append(Truncate(policy?.PolicyId ?? string.Empty))
                    .Append("', TokenType=")
                    .Append(policy?.TokenType.ToString() ?? "<null>");
                if (!string.IsNullOrEmpty(policy?.SecurityPolicyUri))
                {
                    sb.Append(", SecurityPolicyUri='")
                        .Append(Truncate(policy.SecurityPolicyUri!))
                        .Append('\'');
                }
                if (!string.IsNullOrEmpty(policy?.IssuedTokenType))
                {
                    sb.Append(", IssuedTokenType='")
                        .Append(Truncate(policy.IssuedTokenType!))
                        .Append('\'');
                }
                sb.Append(": ").Append(Truncate(reason ?? string.Empty));
            }

            if (rejections.Count > reportCount)
            {
                sb.AppendLine()
                    .Append("  +").Append(rejections.Count - reportCount)
                    .Append(" more rejected policies (truncated).");
            }

            ArrayOf<string> enabled = context.EnabledSecurityPolicyUris;
            if (!enabled.IsNull && enabled.Count > 0)
            {
                sb.AppendLine()
                    .Append("  Client-enabled SecurityPolicyUris: ");
                int enabledReportCount = Math.Min(enabled.Count, MaxRejectionsInDiagnostic);
                bool first = true;
                for (int i = 0; i < enabledReportCount; i++)
                {
                    if (!first)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(Truncate(enabled[i]));
                    first = false;
                }
                if (enabled.Count > enabledReportCount)
                {
                    sb.Append(", +").Append(enabled.Count - enabledReportCount).Append(" more");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Truncates a server-supplied or caller-supplied string to a
        /// bounded length before embedding it in a client-side
        /// diagnostic message, so that a malicious or buggy server
        /// cannot inflate the resulting exception text unboundedly
        /// (defense-in-depth against CR 7.1 / CR/SR 3.7).
        /// </summary>
        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxDiagnosticFieldLength)
            {
                return value;
            }

            return string.Concat(
                value[..MaxDiagnosticFieldLength],
                "…(truncated)");
        }

        private static bool HasSameEncryptionAlgorithm(
            string policySecurityPolicyUri,
            string tokenSecurityPolicyUri)
        {
            return CryptoUtils.IsEccPolicy(policySecurityPolicyUri) ==
                CryptoUtils.IsEccPolicy(tokenSecurityPolicyUri);
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="ClientIdentityProviderExtensions"/>.
    /// </summary>
    internal static partial class ClientIdentityProviderExtensionsLog
    {
        [LoggerMessage(EventId = ClientEventIds.ClientIdentityProviderExtensions + 0, Level = LogLevel.Warning,
            Message = "EndpointDescription advertises {Count} USERNAME UserTokenPolicies; per Part 4 §7.41 there" +
                " must be at most one.")]
        public static partial void EndpointDescriptionAdvertisesCountUSERNAMEUserTokenPoliciesPer(
            this ILogger logger,
            int count);

        [LoggerMessage(EventId = ClientEventIds.ClientIdentityProviderExtensions + 1, Level = LogLevel.Warning,
            Message = "EndpointDescription advertises {Count} ISSUEDTOKEN UserTokenPolicies for" +
                " IssuerEndpointUrl='{Url}'; per Part 4 §7.41 there must be at most one per unique" +
                " IssuerEndpointUrl.")]
        public static partial void EndpointDescriptionAdvertisesCountISSUEDTOKENUserTokenPoliciesIssuerEndpointUrl(
            this ILogger logger,
            int count,
            string url);
    }

}
