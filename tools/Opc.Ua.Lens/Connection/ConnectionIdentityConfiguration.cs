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
using System.IO;
using Opc.Ua;
using Opc.Ua.Identity;

namespace UaLens.Connection;

/// <summary>
/// Portable user-certificate intent. Store paths, key handles and password/PIN
/// material belong exclusively to the named, trusted application registration.
/// </summary>
internal sealed record CertificateIdentityReference
{
    public required string SourceId { get; init; }

    public required string PasswordSourceId { get; init; }

    public string? Thumbprint { get; init; }

    public string? SubjectName { get; init; }

    public void Validate()
    {
        ConnectionReference.Validate(SourceId);
        ConnectionReference.Validate(PasswordSourceId);
        if (string.IsNullOrWhiteSpace(Thumbprint) && string.IsNullOrWhiteSpace(SubjectName))
        {
            throw new ArgumentException("Select a certificate thumbprint or subject in the configured store.");
        }
        if (Thumbprint is { } thumbprint &&
            (thumbprint.Length > 128 || !IsHex(thumbprint)))
        {
            throw new ArgumentException("A certificate thumbprint must contain hexadecimal characters only.");
        }
        if (SubjectName is { } subject &&
            (subject.Length > 512 ||
             subject.Contains('\r', StringComparison.Ordinal) ||
             subject.Contains('\n', StringComparison.Ordinal)))
        {
            throw new ArgumentException("A certificate subject must be a single bounded distinguished name.");
        }
    }

    private static bool IsHex(string value)
    {
        foreach (char character in value)
        {
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }
        return value.Length > 0;
    }
}

/// <summary>
/// Non-secret reference to a token authority adapter registered by the host.
/// The authority and profile are pinned; a workspace cannot install a broker.
/// </summary>
internal sealed record IssuedIdentityReference
{
    public required string ProviderId { get; init; }

    public required string AuthorityUri { get; init; }

    public void Validate()
    {
        ConnectionReference.Validate(ProviderId);
        ConnectionReference.ValidateUri(AuthorityUri);
    }
}

internal static class ConnectionReference
{
    public static void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new ArgumentException("A bounded, configured provider reference is required.");
        }
        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                throw new ArgumentException("Provider references are names, not file paths or credentials.");
            }
        }
    }

    public static void ValidateUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.Query))
        {
            throw new ArgumentException("A non-secret absolute authority or application URI is required.");
        }
    }
}

internal sealed record ConfiguredCertificatePasswordSource(
    string Id,
    string DisplayName,
    ICertificatePasswordProvider Provider)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

/// <summary>
/// A host-owned store/provider registration. The certificate provider may
/// resolve detached hardware keys. UaLens never loads a provider/module path.
/// </summary>
internal sealed class ConfiguredCertificateSource
{
    public ConfiguredCertificateSource(
        string id,
        string displayName,
        CertificateIdentifier store,
        ICertificateProvider provider,
        ArrayOf<ConfiguredCertificatePasswordSource> passwordSources,
        CryptoPurpose purpose = default,
        ICryptoProviderRegistry? cryptoProviders = null,
        string? cryptoProviderName = null)
    {
        ConnectionReference.Validate(id);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(provider);
        if (passwordSources.Count == 0)
        {
            throw new ArgumentException("Configure an explicit password/PIN source, including an empty source.");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConfiguredCertificatePasswordSource source in passwordSources)
        {
            ConnectionReference.Validate(source.Id);
            ArgumentNullException.ThrowIfNull(source.Provider);
            if (!names.Add(source.Id))
            {
                throw new ArgumentException("Certificate password/PIN source names must be unique.");
            }
        }
        Id = id;
        DisplayName = displayName;
        Store = new CertificateIdentifier
        {
            StoreType = store.StoreType,
            StorePath = store.StorePath,
            CertificateType = store.CertificateType,
            Thumbprint = store.Thumbprint,
            SubjectName = store.SubjectName
        };
        Provider = provider;
        PasswordSources = passwordSources;
        Purpose = purpose == default ? CryptoPurpose.UserIdentityKey : purpose;
        CryptoProviders = cryptoProviders;
        CryptoProviderName = cryptoProviderName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public CertificateIdentifier Store { get; }

    public ICertificateProvider Provider { get; }

    public ArrayOf<ConfiguredCertificatePasswordSource> PasswordSources { get; }

    public CryptoPurpose Purpose { get; }

    public ICryptoProviderRegistry? CryptoProviders { get; }

    public string? CryptoProviderName { get; }

    public CertificateIdentifier CreateIdentifier(CertificateIdentityReference reference)
    {
        reference.Validate();
        if (!string.Equals(reference.SourceId, Id, StringComparison.Ordinal))
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.RequiresConfiguration, "The selected certificate source is not configured.");
        }
        return new CertificateIdentifier
        {
            StoreType = Store.StoreType,
            StorePath = Store.StorePath,
            CertificateType = Store.CertificateType,
            Thumbprint = reference.Thumbprint,
            SubjectName = reference.SubjectName
        };
    }

    public ICertificatePasswordProvider ResolvePasswordSource(string id)
    {
        foreach (ConfiguredCertificatePasswordSource source in PasswordSources)
        {
            if (string.Equals(source.Id, id, StringComparison.Ordinal))
            {
                return source.Provider;
            }
        }
        throw new ConnectionIdentityException(
            ConnectionIdentityFailure.RequiresConfiguration,
            "The selected password/PIN source is not configured. Configure it in the host, not in a workspace.");
    }

    public void RequireCryptoProvider(CryptoPurpose purpose, string securityPolicyUri)
    {
        if (Purpose != purpose)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Incompatible,
                "Application-instance keys and user-identity keys are separate configured purposes.");
        }
        if (CryptoProviderName is null)
        {
            return;
        }
        if (CryptoProviders is null ||
            !string.Equals(
                CryptoProviders.Resolve(purpose, securityPolicyUri, Store.CertificateType).Name,
                CryptoProviderName,
                StringComparison.Ordinal))
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.RequiresConfiguration,
                "The selected crypto provider is not registered for this key purpose and security policy.");
        }
    }

    public override string ToString()
    {
        return CryptoProviderName is null
            ? DisplayName
            : $"{DisplayName} ({CryptoProviderName}; configured, device access is checked on use)";
    }
}

internal sealed class ConfiguredAccessTokenSource
{
    public ConfiguredAccessTokenSource(
        string id,
        string displayName,
        IAccessTokenProvider provider,
        string profileUri = Profiles.JwtUserToken)
    {
        ConnectionReference.Validate(id);
        ArgumentNullException.ThrowIfNull(provider);
        ConnectionReference.ValidateUri(provider.AuthorityUri);
        if (!string.Equals(profileUri, Profiles.JwtUserToken, StringComparison.Ordinal))
        {
            throw new ArgumentException("The configured stack flow supports the JWT user-token profile.");
        }
        Id = id;
        DisplayName = displayName;
        Provider = provider;
        AuthorityUri = provider.AuthorityUri;
        ProfileUri = profileUri;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public IAccessTokenProvider Provider { get; }

    public string AuthorityUri { get; }

    public string ProfileUri { get; }

    public override string ToString()
    {
        return $"{DisplayName} — {AuthorityUri}";
    }
}

/// <summary>
/// Explicit, injectable capabilities. Registrations and their devices/secret
/// providers remain host-owned. Only the direct fallback's cache is owned here.
/// </summary>
internal sealed class ConnectionIdentityConfiguration : IDisposable
{
    public ConnectionIdentityConfiguration(
        ArrayOf<ConfiguredCertificateSource> certificateSources = default,
        ArrayOf<ConfiguredAccessTokenSource> accessTokenSources = default,
        TimeProvider? timeProvider = null)
    {
        var certificateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConfiguredCertificateSource source in certificateSources)
        {
            if (!certificateIds.Add(source.Id))
            {
                throw new ArgumentException("Certificate source names must be unique.");
            }
        }
        var tokenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConfiguredAccessTokenSource source in accessTokenSources)
        {
            if (!tokenIds.Add(source.Id))
            {
                throw new ArgumentException("Access-token provider names must be unique.");
            }
        }
        CertificateSources = certificateSources;
        AccessTokenSources = accessTokenSources;
        TimeProvider = timeProvider ?? System.TimeProvider.System;
    }

    public ArrayOf<ConfiguredCertificateSource> CertificateSources { get; }

    public ArrayOf<ConfiguredAccessTokenSource> AccessTokenSources { get; }

    public TimeProvider TimeProvider { get; }

    public static ConnectionIdentityConfiguration CreateDefault(ITelemetryContext telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        var certificates = new CertificateManager(telemetry);
        var source = new ConfiguredCertificateSource(
            "local-user",
            "Local application data → OPC Foundation → pki → user (existing certificates only)",
            new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OPC Foundation", "pki", "user")
            },
            certificates.CertificateProvider,
            [new ConfiguredCertificatePasswordSource("unprotected", "Unprotected key / OS-managed access",
                new CertificatePasswordProvider())]);
        return new ConnectionIdentityConfiguration([source])
        {
            m_ownedCertificates = certificates
        };
    }

    public IClientIdentityProvider Resolve(ConnectionProfile profile)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        profile.Validate();
        if (profile.IdentityType == UserTokenType.Certificate && profile.CertificateIdentity is { } reference)
        {
            foreach (ConfiguredCertificateSource source in CertificateSources)
            {
                if (string.Equals(source.Id, reference.SourceId, StringComparison.Ordinal))
                {
                    return new ConfiguredCertificateIdentityProvider(source, reference, TimeProvider);
                }
            }
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.RequiresConfiguration,
                "The selected certificate provider/store is absent. Register it in trusted host configuration.");
        }
        if (profile.IdentityType == UserTokenType.IssuedToken && profile.IssuedIdentity is { } issued)
        {
            foreach (ConfiguredAccessTokenSource source in AccessTokenSources)
            {
                if (string.Equals(source.Id, issued.ProviderId, StringComparison.Ordinal))
                {
                    if (!string.Equals(issued.AuthorityUri, source.AuthorityUri, StringComparison.Ordinal) ||
                        !string.Equals(profile.IssuedTokenType, source.ProfileUri, StringComparison.Ordinal))
                    {
                        throw new ConnectionIdentityException(
                            ConnectionIdentityFailure.Incompatible,
                            "The configured token provider does not match the selected authority and token profile.");
                    }
                    return new ConfiguredIssuedIdentityProvider(source, TimeProvider);
                }
            }
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.RequiresConfiguration,
                "The access-token provider is absent. Configure the authority/broker and its permissions externally.");
        }
        throw new CredentialsRequiredException(profile);
    }

    public void Dispose()
    {
        if (m_disposed)
        {
            return;
        }
        m_disposed = true;
        m_ownedCertificates?.Dispose();
        m_ownedCertificates = null;
    }

    private CertificateManager? m_ownedCertificates;
    private bool m_disposed;
}

internal enum ConnectionIdentityFailure
{
    RequiresConfiguration,
    Incompatible,
    Unavailable,
    Denied,
    Expired
}

/// <summary>
/// Classifies identity failures. Provider adapters supply sanitized reasons and
/// never attach external broker exception text or token/PIN material.
/// </summary>
internal sealed class ConnectionIdentityException : InvalidOperationException
{
    public ConnectionIdentityException()
        : base("The configured identity source is unavailable.")
    {
    }

    public ConnectionIdentityException(string? message)
        : base(message)
    {
    }

    public ConnectionIdentityException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public ConnectionIdentityException(ConnectionIdentityFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public ConnectionIdentityFailure Failure { get; } = ConnectionIdentityFailure.Unavailable;
}
