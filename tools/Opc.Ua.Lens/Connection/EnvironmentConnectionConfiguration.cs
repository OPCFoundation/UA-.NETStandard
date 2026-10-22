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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Identity;

namespace UaLens.Connection;

/// <summary>
/// Holds an exact, environment-provided connection intent for unattended startup.
/// </summary>
internal sealed class EnvironmentConnectionConfiguration : IDisposable
{
    private EnvironmentConnectionConfiguration(
        string endpointUrl,
        string? applicationUri,
        MessageSecurityMode securityMode,
        string securityPolicyUri,
        string? username,
        byte[]? password)
    {
        EndpointUrl = endpointUrl;
        ApplicationUri = applicationUri;
        SecurityMode = securityMode;
        SecurityPolicyUri = securityPolicyUri;
        Username = username;
        m_password = password;
    }

    /// <summary>
    /// Gets the discovery URL used to locate the configured endpoint.
    /// </summary>
    public string EndpointUrl { get; }

    /// <summary>
    /// Gets the optional expected server application URI.
    /// </summary>
    public string? ApplicationUri { get; }

    /// <summary>
    /// Gets the required message security mode.
    /// </summary>
    public MessageSecurityMode SecurityMode { get; }

    /// <summary>
    /// Gets the required security policy URI.
    /// </summary>
    public string SecurityPolicyUri { get; }

    /// <summary>
    /// Gets the configured username, or <c>null</c> for anonymous identity.
    /// </summary>
    public string? Username { get; }

    /// <summary>
    /// Reads and validates the UaLens environment-variable contract.
    /// </summary>
    public static bool TryCreateFromEnvironment(
        out EnvironmentConnectionConfiguration? configuration,
        out string? error)
    {
        return TryCreate(Environment.GetEnvironmentVariable, out configuration, out error);
    }

    /// <summary>
    /// Reads and validates the UaLens environment-variable contract from a supplied lookup.
    /// </summary>
    internal static bool TryCreate(
        Func<string, string?> getValue,
        out EnvironmentConnectionConfiguration? configuration,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(getValue);
        configuration = null;
        error = null;

        string? endpointUrl = getValue("UALENS_ENDPOINT_URL");
        string? applicationUri = getValue("UALENS_APPLICATION_URI");
        string? username = getValue("UALENS_USERNAME");
        string? passwordText = getValue("UALENS_PASSWORD");
        string? securityModeText = getValue("UALENS_SECURITY_MODE");
        string? securityPolicyText = getValue("UALENS_SECURITY_POLICY");

        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            if (applicationUri is null &&
                username is null &&
                passwordText is null &&
                securityModeText is null &&
                securityPolicyText is null)
            {
                return true;
            }
            error = "UALENS_ENDPOINT_URL is required when automatic connection settings are provided.";
            return false;
        }

        if (!Uri.TryCreate(endpointUrl.Trim(), UriKind.Absolute, out Uri? endpoint) ||
            string.IsNullOrEmpty(endpoint.Host) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            error = "UALENS_ENDPOINT_URL must be an absolute endpoint URL without credentials, query or fragment.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(applicationUri) &&
            !Uri.TryCreate(applicationUri.Trim(), UriKind.Absolute, out _))
        {
            error = "UALENS_APPLICATION_URI must be an absolute URI.";
            return false;
        }

        if (!TryParseSecurityMode(securityModeText, out MessageSecurityMode securityMode))
        {
            error = "UALENS_SECURITY_MODE must be None, Sign or SignAndEncrypt.";
            return false;
        }

        if (!TryParseSecurityPolicy(securityPolicyText, out string? securityPolicyUri))
        {
            error = "UALENS_SECURITY_POLICY must be None, a supported policy name or a full policy URI.";
            return false;
        }

        if ((securityMode == MessageSecurityMode.None) !=
            string.Equals(securityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal))
        {
            error = "UALENS_SECURITY_MODE and UALENS_SECURITY_POLICY describe an inconsistent security profile.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(username) && passwordText is not null)
        {
            error = "UALENS_USERNAME is required when UALENS_PASSWORD is provided.";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(username) && passwordText is null)
        {
            error = "UALENS_PASSWORD is required when UALENS_USERNAME is provided.";
            return false;
        }

        configuration = new EnvironmentConnectionConfiguration(
            endpoint.AbsoluteUri,
            string.IsNullOrWhiteSpace(applicationUri) ? null : applicationUri.Trim(),
            securityMode,
            securityPolicyUri!,
            string.IsNullOrWhiteSpace(username) ? null : username,
            passwordText is null ? null : Encoding.UTF8.GetBytes(passwordText));
        return true;
    }

    /// <summary>
    /// Creates a single-use connection selection for the exact configured endpoint and identity policy.
    /// </summary>
    public async Task<ConnectionSelection> CreateSelectionAsync(
        ArrayOf<EndpointDescription> endpoints,
        SubscriptionEngineKind engine,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EndpointDescription? endpoint = null;
        for (int i = 0; i < endpoints.Count; i++)
        {
            EndpointDescription candidate = endpoints[i];
            if (!ConnectionProfile.EndpointUrlsMatch(EndpointUrl, candidate.EndpointUrl) ||
                candidate.SecurityMode != SecurityMode ||
                !string.Equals(candidate.SecurityPolicyUri, SecurityPolicyUri, StringComparison.Ordinal) ||
                (ApplicationUri is not null &&
                    !string.Equals(candidate.Server?.ApplicationUri, ApplicationUri, StringComparison.Ordinal)))
            {
                continue;
            }
            if (endpoint is not null)
            {
                throw new InvalidOperationException(
                    "More than one endpoint matches the environment-provided connection profile.");
            }
            endpoint = candidate;
        }

        if (endpoint is null)
        {
            throw new ServiceResultException(
                StatusCodes.BadSecurityPolicyRejected,
                "The server does not advertise the environment-provided application, security mode and policy.");
        }

        UserTokenType tokenType = Username is null ? UserTokenType.Anonymous : UserTokenType.UserName;
        UserTokenPolicy? policy = null;
        foreach (UserTokenPolicy candidate in endpoint.UserIdentityTokens)
        {
            if (candidate.TokenType != tokenType)
            {
                continue;
            }
            if (policy is not null)
            {
                throw new InvalidOperationException(
                    $"The selected endpoint advertises more than one {tokenType} policy.");
            }
            policy = candidate;
        }

        if (policy is null)
        {
            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenRejected,
                $"The selected endpoint does not advertise a {tokenType} identity policy.");
        }
        ConnectionProfile profile = ConnectionProfile.Create(endpoint, policy, engine, Username);
        if (Username is null)
        {
            return new ConnectionSelection(endpoint, profile, new AnonymousIdentityProvider());
        }

        byte[] password = Interlocked.Exchange(ref m_password, null)
            ?? throw new InvalidOperationException("The environment-provided password has already been consumed.");
        try
        {
            var identity = new UserIdentity(Username, password.AsSpan())
            {
                PolicyId = policy.PolicyId!
            };
            ConnectionCredentials credentials = await ConnectionCredentials
                .FromIdentityAsync(profile, identity, cancellationToken)
                .ConfigureAwait(false);
            return new ConnectionSelection(endpoint, profile, credentials);
        }
        finally
        {
            Array.Clear(password);
        }
    }

    /// <summary>
    /// Clears any password material that was not consumed by a connection attempt.
    /// </summary>
    public void Dispose()
    {
        byte[]? password = Interlocked.Exchange(ref m_password, null);
        if (password is not null)
        {
            Array.Clear(password);
        }
    }

    private static bool TryParseSecurityMode(string? value, out MessageSecurityMode mode)
    {
        value = string.IsNullOrWhiteSpace(value) ? nameof(MessageSecurityMode.None) : value.Trim();
        return Enum.TryParse(value, ignoreCase: true, out mode) &&
            mode is MessageSecurityMode.None or MessageSecurityMode.Sign or MessageSecurityMode.SignAndEncrypt;
    }

    private static bool TryParseSecurityPolicy(string? value, out string? policyUri)
    {
        value = string.IsNullOrWhiteSpace(value) ? nameof(SecurityPolicies.None) : value.Trim();
        policyUri = value.ToUpperInvariant() switch
        {
            "NONE" => SecurityPolicies.None,
            "BASIC256SHA256" => SecurityPolicies.Basic256Sha256,
            "AES128SHA256RSAOAEP" => SecurityPolicies.Aes128_Sha256_RsaOaep,
            "AES256SHA256RSAPSS" => SecurityPolicies.Aes256_Sha256_RsaPss,
            "RSADHAESGCM" => SecurityPolicies.RSA_DH_AesGcm,
            "RSADHCHACHAPOLY" => SecurityPolicies.RSA_DH_ChaChaPoly,
            "ECCNISTP256" => SecurityPolicies.ECC_nistP256,
            "ECCNISTP256AESGCM" => SecurityPolicies.ECC_nistP256_AesGcm,
            "ECCNISTP256CHACHAPOLY" => SecurityPolicies.ECC_nistP256_ChaChaPoly,
            "ECCNISTP384" => SecurityPolicies.ECC_nistP384,
            "ECCNISTP384AESGCM" => SecurityPolicies.ECC_nistP384_AesGcm,
            "ECCNISTP384CHACHAPOLY" => SecurityPolicies.ECC_nistP384_ChaChaPoly,
            "ECCBRAINPOOLP256R1" => SecurityPolicies.ECC_brainpoolP256r1,
            "ECCBRAINPOOLP256R1AESGCM" => SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
            "ECCBRAINPOOLP256R1CHACHAPOLY" => SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly,
            "ECCBRAINPOOLP384R1" => SecurityPolicies.ECC_brainpoolP384r1,
            "ECCBRAINPOOLP384R1AESGCM" => SecurityPolicies.ECC_brainpoolP384r1_AesGcm,
            "ECCBRAINPOOLP384R1CHACHAPOLY" => SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly,
            "ECCCURVE25519" => SecurityPolicies.ECC_curve25519,
            "ECCCURVE25519AESGCM" => SecurityPolicies.ECC_curve25519_AesGcm,
            "ECCCURVE25519CHACHAPOLY" => SecurityPolicies.ECC_curve25519_ChaChaPoly,
            _ when IsAllowedSecurityPolicy(value) => value,
            _ => null
        };
        return policyUri is not null;
    }

    private static bool IsAllowedSecurityPolicy(string policyUri)
    {
        return policyUri is SecurityPolicies.None
            or SecurityPolicies.Basic256Sha256
            or SecurityPolicies.Aes128_Sha256_RsaOaep
            or SecurityPolicies.Aes256_Sha256_RsaPss
            or SecurityPolicies.RSA_DH_AesGcm
            or SecurityPolicies.RSA_DH_ChaChaPoly
            or SecurityPolicies.ECC_nistP256
            or SecurityPolicies.ECC_nistP256_AesGcm
            or SecurityPolicies.ECC_nistP256_ChaChaPoly
            or SecurityPolicies.ECC_nistP384
            or SecurityPolicies.ECC_nistP384_AesGcm
            or SecurityPolicies.ECC_nistP384_ChaChaPoly
            or SecurityPolicies.ECC_brainpoolP256r1
            or SecurityPolicies.ECC_brainpoolP256r1_AesGcm
            or SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly
            or SecurityPolicies.ECC_brainpoolP384r1
            or SecurityPolicies.ECC_brainpoolP384r1_AesGcm
            or SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly
            or SecurityPolicies.ECC_curve25519
            or SecurityPolicies.ECC_curve25519_AesGcm
            or SecurityPolicies.ECC_curve25519_ChaChaPoly;
    }

    private byte[]? m_password;
}
