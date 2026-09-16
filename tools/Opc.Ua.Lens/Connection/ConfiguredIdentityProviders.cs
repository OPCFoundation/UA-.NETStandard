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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;

namespace UaLens.Connection;

/// <summary>
/// Re-evaluates key eligibility on each acquisition, including certificate
/// rotation/reconnect. Only the configured provider owns cached key handles.
/// </summary>
internal sealed class ConfiguredCertificateIdentityProvider : IClientIdentityProvider
{
    public ConfiguredCertificateIdentityProvider(
        ConfiguredCertificateSource source,
        CertificateIdentityReference reference,
        TimeProvider timeProvider)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        m_identifier = source.CreateIdentifier(reference);
        m_passwords = source.ResolvePasswordSource(reference.PasswordSourceId);
        m_certificates = new ReferencedCertificateProvider(source.Provider, m_identifier, timeProvider);
        m_timeProvider = timeProvider;
    }

    public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; } = [UserTokenType.Certificate];

    public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; } = [];

    public DateTime ExpiresAt => DateTime.MaxValue;

    public ValueTask<CanSatisfyResult> CanSatisfyAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        m_source.RequireCryptoProvider(CryptoPurpose.UserIdentityKey, EffectivePolicy(policy, context));
        return CreateProvider().CanSatisfyAsync(policy, context, ct);
    }

    public async ValueTask<IUserIdentity> GetIdentityAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        m_source.RequireCryptoProvider(CryptoPurpose.UserIdentityKey, EffectivePolicy(policy, context));
        using Certificate resolved = await m_certificates.GetPrivateKeyCertificateAsync(
            m_identifier, m_passwords, ct: ct).ConfigureAwait(false) ??
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Unavailable, "The selected private key is absent.");
        var pinned = new CertificateIdentifier
        {
            StoreType = m_identifier.StoreType,
            StorePath = m_identifier.StorePath,
            CertificateType = m_identifier.CertificateType,
            Thumbprint = resolved.Thumbprint,
            SubjectName = resolved.Subject
        };
        var certificates = new PreparedIdentityCertificateProvider(
            new ReferencedCertificateProvider(m_source.Provider, pinned, m_timeProvider), resolved);
        IUserIdentity identity;
        try
        {
            // The stack's identity factory uses a synchronous constructor after
            // its async policy check. Preload asynchronously, then serve only
            // AddRef cache hits during construction; hardware/PIN I/O never
            // blocks that constructor. Signing later uses the async provider.
            var provider = new X509ClientIdentityProvider(pinned, m_passwords, certificates);
            identity = await provider.GetIdentityAsync(policy, context, ct).ConfigureAwait(false);
        }
        finally
        {
            certificates.ReleasePreparedCertificate();
        }
        bool returned = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            returned = true;
            return identity;
        }
        finally
        {
            if (!returned)
            {
                await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
            }
        }
    }

    private X509ClientIdentityProvider CreateProvider()
    {
        return new X509ClientIdentityProvider(m_identifier, m_passwords, m_certificates);
    }

    private static string EffectivePolicy(UserTokenPolicy policy, IdentitySelectionContext context)
    {
        return string.IsNullOrEmpty(policy.SecurityPolicyUri)
            ? context.EndpointDescription.SecurityPolicyUri ?? SecurityPolicies.None
            : policy.SecurityPolicyUri;
    }

    private readonly ConfiguredCertificateSource m_source;
    private readonly CertificateIdentifier m_identifier;
    private readonly ICertificatePasswordProvider m_passwords;
    private readonly ReferencedCertificateProvider m_certificates;
    private readonly TimeProvider m_timeProvider;
}

internal sealed class PreparedIdentityCertificateProvider : ICertificateProvider
{
    public PreparedIdentityCertificateProvider(ICertificateProvider inner, Certificate certificate)
    {
        m_inner = inner;
        m_prepared = certificate.AddRef();
    }

    public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
    {
        lock (m_gate)
        {
            return m_prepared?.AddRef();
        }
    }

    public ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
        CertificateIdentifier identifier,
        ICertificatePasswordProvider? passwordProvider = null,
        string? applicationUri = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (m_gate)
        {
            if (m_prepared is { } prepared)
            {
                return ValueTask.FromResult<Certificate?>(prepared.AddRef());
            }
        }
        return m_inner.GetPrivateKeyCertificateAsync(identifier, passwordProvider, applicationUri, ct);
    }

    public void ReleasePreparedCertificate()
    {
        Certificate? certificate;
        lock (m_gate)
        {
            certificate = m_prepared;
            m_prepared = null;
        }
        certificate?.Dispose();
    }

    private readonly ICertificateProvider m_inner;
    private readonly Lock m_gate = new();
    private Certificate? m_prepared;
}

/// <summary>
/// Validates the actual provider result, not just certificate metadata or a
/// provider label. A public-only, expired or substituted certificate is not an
/// eligible user private key. Returned references always retain stack ownership.
/// </summary>
internal sealed class ReferencedCertificateProvider : ICertificateProvider
{
    public ReferencedCertificateProvider(
        ICertificateProvider inner,
        CertificateIdentifier identifier,
        TimeProvider timeProvider)
    {
        m_inner = inner ?? throw new ArgumentNullException(nameof(inner));
        m_identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Certificate? TryGetPrivateKeyCertificate(string thumbprint)
    {
        // Use the asynchronous path so a rotated subject reference is resolved
        // and rechecked instead of borrowing an old cached key by thumbprint.
        return null;
    }

    public async ValueTask<Certificate?> GetPrivateKeyCertificateAsync(
        CertificateIdentifier identifier,
        ICertificatePasswordProvider? passwordProvider = null,
        string? applicationUri = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Certificate? certificate;
        try
        {
            certificate = await m_inner.GetPrivateKeyCertificateAsync(
                m_identifier, passwordProvider, applicationUri: null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException("Certificate/PIN acquisition was canceled.", ct);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or CryptographicException)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Denied,
                "Private-key access was denied. Check the configured device, PIN source and key-use permissions.");
        }
        catch (ServiceResultException error)
        {
            throw new ConnectionIdentityException(
                error.StatusCode == StatusCodes.BadUserAccessDenied ||
                    error.StatusCode == StatusCodes.BadCertificateUseNotAllowed
                    ? ConnectionIdentityFailure.Denied
                    : ConnectionIdentityFailure.Unavailable,
                "The certificate provider could not acquire the selected private key. Check provider/device access.");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException
            or ArgumentException or FormatException)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Unavailable,
                "The configured certificate store/device is unavailable. No alternate key or provider was selected.");
        }

        if (certificate is null)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Unavailable,
                "The selected certificate/private key is absent. Provision it externally in the configured store.");
        }
        bool returned = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!certificate.HasPrivateKey)
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Incompatible,
                    "The selected certificate has no usable private key, including no configured detached key.");
            }
            if (!string.IsNullOrEmpty(m_identifier.Thumbprint) &&
                !string.Equals(m_identifier.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Incompatible,
                    "The certificate provider returned a different certificate than the selected thumbprint.");
            }
            if (!string.IsNullOrEmpty(m_identifier.SubjectName) &&
                !X509Utils.CompareDistinguishedName(m_identifier.SubjectName, certificate.Subject))
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Incompatible,
                    "The certificate provider returned a different certificate than the selected subject.");
            }
            DateTime now = m_timeProvider.GetUtcNow().UtcDateTime;
            if (certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Expired,
                    "The selected certificate is expired or not yet valid. Renew it through its certificate owner.");
            }
            using (var publicCopy = new Certificate(certificate.RawData))
            using (X509Certificate2 publicCertificate = publicCopy.AsX509Certificate2())
            {
                foreach (X509Extension extension in publicCertificate.Extensions)
                {
                    if (extension is X509KeyUsageExtension usage &&
                        (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0)
                    {
                        throw new ConnectionIdentityException(
                            ConnectionIdentityFailure.Incompatible,
                            "The selected certificate's key usage does not permit identity signatures.");
                    }
                }
            }
            returned = true;
            return certificate;
        }
        finally
        {
            if (!returned)
            {
                certificate.Dispose();
            }
        }
    }

    private readonly ICertificateProvider m_inner;
    private readonly CertificateIdentifier m_identifier;
    private readonly TimeProvider m_timeProvider;
}

internal sealed class ConfiguredIssuedIdentityProvider : IClientIdentityProvider
{
    public ConfiguredIssuedIdentityProvider(ConfiguredAccessTokenSource source, TimeProvider timeProvider)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        m_inner = new IssuedTokenIdentityProvider(
            new GuardedAccessTokenProvider(source, timeProvider), source.ProfileUri);
    }

    public IReadOnlyList<UserTokenType> SupportedTokenTypes => m_inner.SupportedTokenTypes;

    public IReadOnlyList<string> SupportedIssuedTokenProfileUris => m_inner.SupportedIssuedTokenProfileUris;

    public DateTime ExpiresAt => m_inner.ExpiresAt;

    public ValueTask<CanSatisfyResult> CanSatisfyAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!AuthorizationServerMetadata.TryFromPolicy(policy, out AuthorizationServerMetadata metadata) ||
            !string.Equals(metadata.AuthorityUri, m_source.AuthorityUri, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(metadata.ResourceUri))
        {
            return ValueTask.FromResult(CanSatisfyResult.No(
                "The token authority/resource metadata does not match the configured provider."));
        }
        return m_inner.CanSatisfyAsync(policy, context, ct);
    }

    public async ValueTask<IUserIdentity> GetIdentityAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        CanSatisfyResult result = await CanSatisfyAsync(policy, context, ct).ConfigureAwait(false);
        if (!result.CanSatisfy)
        {
            throw new ConnectionIdentityException(ConnectionIdentityFailure.Incompatible, result.RejectionReason!);
        }
        IUserIdentity identity = await m_inner.GetIdentityAsync(policy, context, ct).ConfigureAwait(false);
        bool returned = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            returned = true;
            return identity;
        }
        finally
        {
            if (!returned)
            {
                await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
            }
        }
    }

    private readonly ConfiguredAccessTokenSource m_source;
    private readonly IssuedTokenIdentityProvider m_inner;
}

/// <summary>
/// Constrains externally configured authority access and removes raw provider
/// exception text before it can reach a workspace snapshot or log.
/// </summary>
internal sealed class GuardedAccessTokenProvider : IAccessTokenProvider
{
    public GuardedAccessTokenProvider(ConfiguredAccessTokenSource source, TimeProvider timeProvider)
    {
        m_source = source ?? throw new ArgumentNullException(nameof(source));
        m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public string AuthorityUri => m_source.AuthorityUri;

    public async ValueTask<AccessToken> AcquireAsync(
        AuthorizationServerMetadata metadata,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(metadata.AuthorityUri, AuthorityUri, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(metadata.ResourceUri))
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Incompatible, "The server advertised a different token authority/resource.");
        }
        AccessToken token;
        try
        {
            // Server-supplied endpoint overrides are not authority configuration.
            // The registered adapter resolves its own trusted authority endpoints.
            token = await m_source.Provider.AcquireAsync(metadata with
            {
                TokenEndpoint = null,
                AuthorizationEndpoint = null,
                JwksUri = null,
                AdditionalFields = new Dictionary<string, System.Text.Json.JsonElement>()
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new OperationCanceledException("Authority/broker acquisition was canceled.", ct);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Denied,
                "The configured authority/broker denied token acquisition. " +
                "Check authorization and consent externally.");
        }
        catch (ServiceResultException error)
        {
            bool denied = error.StatusCode == StatusCodes.BadUserAccessDenied ||
                error.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                error.StatusCode == StatusCodes.BadIdentityTokenInvalid ||
                error.StatusCode == StatusCodes.BadSecurityChecksFailed;
            throw new ConnectionIdentityException(
                denied ? ConnectionIdentityFailure.Denied : ConnectionIdentityFailure.Unavailable,
                denied
                    ? "The configured authority/broker denied token acquisition. Check authorization externally."
                    : "The configured authority/broker is unavailable. No alternate authority was contacted.");
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or NotSupportedException
            or System.Net.Http.HttpRequestException or TimeoutException or CryptographicException
            or ArgumentException or FormatException or System.Security.Authentication.AuthenticationException)
        {
            throw new ConnectionIdentityException(
                ConnectionIdentityFailure.Unavailable,
                "The configured authority/broker is unavailable or needs reauthentication. No fallback was attempted.");
        }
        bool returned = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (token.TokenData.IsEmpty ||
                !string.Equals(token.ProfileUri, m_source.ProfileUri, StringComparison.Ordinal))
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Incompatible, "The authority returned an incompatible token profile.");
            }
            if (token.ExpiresAt.ToUniversalTime() <= m_timeProvider.GetUtcNow().UtcDateTime.AddSeconds(30))
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Expired,
                    "The authority returned an expired/expiring token. Reauthenticate with the configured provider.");
            }
            returned = true;
            return token;
        }
        finally
        {
            if (!returned)
            {
                token.Dispose();
            }
        }
    }

    private readonly ConfiguredAccessTokenSource m_source;
    private readonly TimeProvider m_timeProvider;
}
