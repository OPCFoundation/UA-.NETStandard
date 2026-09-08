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
using Opc.Ua;

namespace UaLens.Connection;

/// <summary>
/// A persistence-safe description of the explicitly selected connection.
/// Contains neither a live endpoint/session/identity nor credential material.
/// Restoring this profile requires the same endpoint and user-token policy.
/// </summary>
internal sealed record ConnectionProfile
{
    public required string EndpointUrl { get; init; }

    public required MessageSecurityMode SecurityMode { get; init; }

    public required string SecurityPolicyUri { get; init; }

    public string? TransportProfileUri { get; init; }

    public string? ServerApplicationUri { get; init; }

    public required UserTokenType IdentityType { get; init; }

    public required string UserTokenPolicyId { get; init; }

    public string? UserTokenSecurityPolicyUri { get; init; }

    public string? IssuedTokenType { get; init; }

    public string? IdentityName { get; init; }

    /// <summary>
    /// An optional reference resolved by the injected secret registry.
    /// Interactive passwords are not persisted, even as in-memory references.
    /// </summary>
    public SecretIdentifier? CredentialReference { get; init; }

    public SubscriptionEngineKind Engine { get; init; } = SubscriptionEngineKind.ChannelV2;

    public static ConnectionProfile Create(
        EndpointDescription endpoint,
        UserTokenPolicy policy,
        SubscriptionEngineKind engine,
        string? identityName = null,
        SecretIdentifier? credentialReference = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(policy);

        var profile = new ConnectionProfile
        {
            EndpointUrl = endpoint.EndpointUrl ?? string.Empty,
            SecurityMode = endpoint.SecurityMode,
            SecurityPolicyUri = endpoint.SecurityPolicyUri ?? string.Empty,
            TransportProfileUri = endpoint.TransportProfileUri,
            ServerApplicationUri = endpoint.Server?.ApplicationUri,
            IdentityType = policy.TokenType,
            UserTokenPolicyId = policy.PolicyId ?? string.Empty,
            UserTokenSecurityPolicyUri = policy.SecurityPolicyUri,
            IssuedTokenType = policy.IssuedTokenType,
            IdentityName = identityName,
            CredentialReference = credentialReference,
            Engine = engine
        };
        profile.Validate();
        profile.RequireMatch(endpoint);
        return profile;
    }

    public void Validate()
    {
        if (!Uri.TryCreate(EndpointUrl, UriKind.Absolute, out Uri? uri) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("The endpoint must be an absolute URL without embedded credentials.");
        }
        if (SecurityMode is not (MessageSecurityMode.None or
            MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt) ||
            string.IsNullOrWhiteSpace(SecurityPolicyUri) ||
            (SecurityMode == MessageSecurityMode.None) != (SecurityPolicyUri == SecurityPolicies.None))
        {
            throw new ArgumentException("The connection profile has an inconsistent security mode and policy.");
        }
        if (IdentityType is not (UserTokenType.Anonymous or UserTokenType.UserName or
            UserTokenType.Certificate or UserTokenType.IssuedToken) ||
            string.IsNullOrWhiteSpace(UserTokenPolicyId))
        {
            throw new ArgumentException("An explicit, supported user-token policy is required.");
        }
        if (Engine is not (SubscriptionEngineKind.Classic or SubscriptionEngineKind.ChannelV2))
        {
            throw new ArgumentException("The subscription engine is not supported.");
        }
        if (IdentityType == UserTokenType.UserName && string.IsNullOrWhiteSpace(IdentityName))
        {
            throw new ArgumentException("A username is required for a username connection profile.");
        }
    }

    public bool MatchesEndpoint(EndpointDescription endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return EndpointUrlsMatch(EndpointUrl, endpoint.EndpointUrl) &&
            endpoint.SecurityMode == SecurityMode &&
            string.Equals(endpoint.SecurityPolicyUri, SecurityPolicyUri, StringComparison.Ordinal) &&
            string.Equals(
                endpoint.TransportProfileUri ?? string.Empty,
                TransportProfileUri ?? string.Empty,
                StringComparison.Ordinal) &&
            string.Equals(
                endpoint.Server?.ApplicationUri ?? string.Empty,
                ServerApplicationUri ?? string.Empty,
                StringComparison.Ordinal);
    }

    public bool MatchesPolicy(UserTokenPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.TokenType == IdentityType &&
            string.Equals(policy.PolicyId, UserTokenPolicyId, StringComparison.Ordinal) &&
            string.Equals(
                policy.SecurityPolicyUri ?? string.Empty,
                UserTokenSecurityPolicyUri ?? string.Empty,
                StringComparison.Ordinal) &&
            string.Equals(
                policy.IssuedTokenType ?? string.Empty,
                IssuedTokenType ?? string.Empty,
                StringComparison.Ordinal);
    }

    public UserTokenPolicy RequireMatch(EndpointDescription endpoint)
    {
        if (MatchesEndpoint(endpoint))
        {
            foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
            {
                if (MatchesPolicy(policy))
                {
                    return policy;
                }
            }
        }
        throw new ServiceResultException(
            StatusCodes.BadSecurityPolicyRejected,
            "The server no longer offers the selected endpoint and identity policy. Select a profile explicitly.");
    }

    public static bool EndpointUrlsMatch(string? first, string? second)
    {
        if (!Uri.TryCreate(first?.Trim(), UriKind.Absolute, out Uri? firstUri) ||
            !Uri.TryCreate(second?.Trim(), UriKind.Absolute, out Uri? secondUri))
        {
            return false;
        }
        return string.Equals(firstUri.Scheme, secondUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(firstUri.IdnHost, secondUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            firstUri.Port == secondUri.Port &&
            string.Equals(firstUri.PathAndQuery, secondUri.PathAndQuery, StringComparison.Ordinal) &&
            string.Equals(firstUri.UserInfo, secondUri.UserInfo, StringComparison.Ordinal);
    }
}
