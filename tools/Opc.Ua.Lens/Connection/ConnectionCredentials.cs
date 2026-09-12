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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Identity;

namespace UaLens.Connection;

/// <summary>
/// Resolves a non-secret profile to a source of fresh identities. Desktop hosts
/// may inject an asynchronous credential prompt instead of a secret registry.
/// Returned providers remain caller-owned and must return a fresh identity.
/// </summary>
internal interface IConnectionCredentialProvider
{
    ValueTask<IClientIdentityProvider> GetAsync(ConnectionProfile profile, CancellationToken ct);
}

internal sealed class ProfileCredentialProvider : IConnectionCredentialProvider
{
    public ProfileCredentialProvider(
        ISecretRegistry? secrets = null,
        ConnectionIdentityConfiguration? configuration = null)
    {
        m_secrets = secrets;
        Configuration = configuration ?? new ConnectionIdentityConfiguration();
    }

    public ConnectionIdentityConfiguration Configuration { get; }

    public ValueTask<IClientIdentityProvider> GetAsync(ConnectionProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ct.ThrowIfCancellationRequested();
        profile.Validate();

        IClientIdentityProvider provider = profile.IdentityType switch
        {
            UserTokenType.Anonymous => new AnonymousIdentityProvider(),
            UserTokenType.UserName when m_secrets is not null && profile.CredentialReference is not null =>
                new UserNamePasswordIdentityProvider(
                    profile.IdentityName!,
                    m_secrets,
                    profile.CredentialReference),
            UserTokenType.Certificate or UserTokenType.IssuedToken => Configuration.Resolve(profile),
            _ => throw new CredentialsRequiredException(profile)
        };
        return ValueTask.FromResult(provider);
    }

    private readonly ISecretRegistry? m_secrets;
}

/// <summary>
/// A profile was retained, but its credentials were not. The shell must request
/// credentials for this profile rather than substituting Anonymous or None.
/// </summary>
internal sealed class CredentialsRequiredException : InvalidOperationException
{
    public CredentialsRequiredException()
        : base(k_defaultMessage)
    {
    }

    public CredentialsRequiredException(string? message)
        : base(message)
    {
    }

    public CredentialsRequiredException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public CredentialsRequiredException(ConnectionProfile profile)
        : base(k_defaultMessage)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public ConnectionProfile? Profile { get; }

    private const string k_defaultMessage =
        "Credentials must be acquired again for the selected connection profile.";
}

/// <summary>
/// Pins a stack identity provider to the chosen endpoint/user-token policy and
/// detects an accidentally reused session-owned identity without retaining it.
/// </summary>
internal sealed class ProfileIdentityProvider : IClientIdentityProvider
{
    public ProfileIdentityProvider(
        ConnectionProfile profile,
        IClientIdentityProvider inner,
        ConnectionIdentityTracker? identities = null)
    {
        m_profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ArgumentNullException.ThrowIfNull(inner);
        m_inner = inner is ProfileIdentityProvider pinned ? pinned.m_inner : inner;
        m_identities = identities ??
            (inner as ProfileIdentityProvider)?.m_identities ??
            new ConnectionIdentityTracker();
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
        if (!m_profile.MatchesEndpoint(context.EndpointDescription) || !m_profile.MatchesPolicy(policy))
        {
            return ValueTask.FromResult(CanSatisfyResult.No("The policy differs from the selected profile."));
        }
        return m_inner.CanSatisfyAsync(policy, context, ct);
    }

    public async ValueTask<IUserIdentity> GetIdentityAsync(
        UserTokenPolicy policy,
        IdentitySelectionContext context,
        CancellationToken ct = default)
    {
        CanSatisfyResult match = await CanSatisfyAsync(policy, context, ct).ConfigureAwait(false);
        if (!match.CanSatisfy)
        {
            throw new ServiceResultException(
                StatusCodes.BadIdentityTokenRejected,
                match.RejectionReason ?? "The identity provider rejected the selected user-token policy.");
        }

        IUserIdentity identity = await m_inner.GetIdentityAsync(policy, context, ct).ConfigureAwait(false);
        if (!m_identities.TryClaim(identity))
        {
            throw new CredentialsRequiredException(m_profile);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            IUserIdentityTokenHandler handler = identity.TokenHandler;
            if (identity.TokenType != m_profile.IdentityType ||
                handler.TokenType != m_profile.IdentityType ||
                (m_profile.IdentityType == UserTokenType.IssuedToken &&
                    (handler is not IssuedIdentityTokenHandler issued ||
                        !string.Equals(issued.IssuedTokenTypeProfileUri, m_profile.IssuedTokenType,
                            StringComparison.Ordinal))) ||
                (m_profile.IdentityType == UserTokenType.UserName &&
                    (handler.Token is not UserNameIdentityToken userName ||
                        !string.Equals(userName.UserName, m_profile.IdentityName, StringComparison.Ordinal))))
            {
                throw new ServiceResultException(
                    StatusCodes.BadIdentityTokenRejected,
                    "The credential provider returned an identity different from the selected profile.");
            }
            handler.UpdatePolicy(policy);
            return identity;
        }
        catch
        {
            await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
            throw;
        }
    }

    private readonly ConnectionProfile m_profile;
    private readonly IClientIdentityProvider m_inner;
    private readonly ConnectionIdentityTracker m_identities;
}

/// <summary>
/// Tracks identity ownership across disconnects without retaining credentials.
/// A caller-provided provider cannot hand an old session's identity back later.
/// </summary>
internal sealed class ConnectionIdentityTracker
{
    public bool TryClaim(IUserIdentity identity)
    {
        lock (m_gate)
        {
            if (m_identities.TryGetValue(identity, out _))
            {
                return false;
            }
            m_identities.Add(identity, new IdentityMarker());
            return true;
        }
    }

    private sealed class IdentityMarker
    {
    }

    private readonly Lock m_gate = new();
    private readonly ConditionalWeakTable<IUserIdentity, IdentityMarker> m_identities = new();
}

/// <summary>
/// Owns only connection-lifetime material. Username passwords supplied by the
/// legacy picker are copied into the stack's private in-memory secret store.
/// Disconnect removes that entry; engine changes retain it until replacement.
/// </summary>
internal sealed class ConnectionCredentials : IAsyncDisposable
{
    public ConnectionCredentials(
        ConnectionProfile profile,
        IClientIdentityProvider provider,
        ConnectionIdentityTracker? identities = null)
    {
        Provider = new ProfileIdentityProvider(profile, provider, identities);
    }

    public IClientIdentityProvider Provider { get; private set; }

    public void Pin(ConnectionProfile profile, ConnectionIdentityTracker identities)
    {
        Provider = new ProfileIdentityProvider(profile, Provider, identities);
    }

    public static async Task<ConnectionCredentials> FromIdentityAsync(
        ConnectionProfile profile,
        IUserIdentity identity,
        CancellationToken ct,
        ConnectionIdentityTracker? identities = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.TokenType == UserTokenType.Anonymous)
        {
            await ReleaseIdentityAsync(identity).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new ConnectionCredentials(profile, new AnonymousIdentityProvider(), identities);
        }
        if (identity.TokenHandler is UserNameIdentityTokenHandler { DecryptedPassword: not null } handler)
        {
            var store = new InMemorySecretStore();
            var id = new SecretIdentifier(Guid.NewGuid().ToString("N"), store.StoreType);
            try
            {
                ct.ThrowIfCancellationRequested();
                await store.SetAsync(id, handler.DecryptedPassword, ct).ConfigureAwait(false);
                var provider = new UserNamePasswordIdentityProvider(handler.UserName, new SecretRegistry(store), id);
                return new ConnectionCredentials(profile, provider, identities)
                {
                    m_store = store,
                    m_secretId = id
                };
            }
            catch
            {
                await store.RemoveAsync(id, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await ReleaseIdentityAsync(identity).ConfigureAwait(false);
            }
        }

        var singleUse = new SingleUseIdentityProvider(profile, identity);
        return new ConnectionCredentials(profile, singleUse, identities)
        {
            m_singleUse = singleUse
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (m_store is { } store && m_secretId is { } id)
        {
            m_store = null;
            m_secretId = null;
            await store.RemoveAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
        if (m_singleUse is not null)
        {
            try
            {
                await m_singleUse.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                m_singleUse = null;
            }
        }
    }

    public static async ValueTask ReleaseIdentityAsync(IUserIdentity identity)
    {
        if (identity is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (identity is IDisposable disposable)
        {
            disposable.Dispose();
        }
        else if (identity.TokenHandler is UserNameIdentityTokenHandler { DecryptedPassword: { } password } userName)
        {
            Array.Clear(password);
            userName.DecryptedPassword = null;
        }
        else if (identity.TokenHandler is IssuedIdentityTokenHandler issued)
        {
            issued.DecryptedTokenData = null;
        }
    }

    private sealed class SingleUseIdentityProvider : IClientIdentityProvider, IAsyncDisposable
    {
        public SingleUseIdentityProvider(ConnectionProfile profile, IUserIdentity identity)
        {
            m_profile = profile;
            m_identity = identity;
            SupportedTokenTypes = [profile.IdentityType];
            SupportedIssuedTokenProfileUris = profile.IssuedTokenType is { } issuedType ? [issuedType] : [];
        }

        public IReadOnlyList<UserTokenType> SupportedTokenTypes { get; }

        public IReadOnlyList<string> SupportedIssuedTokenProfileUris { get; }

        public DateTime ExpiresAt => DateTime.MaxValue;

        public ValueTask<CanSatisfyResult> CanSatisfyAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(m_profile.MatchesPolicy(policy)
                ? CanSatisfyResult.Yes
                : CanSatisfyResult.No("The identity does not support this policy."));
        }

        public ValueTask<IUserIdentity> GetIdentityAsync(
            UserTokenPolicy policy,
            IdentitySelectionContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IUserIdentity identity = Interlocked.Exchange(ref m_identity, null)
                ?? throw new CredentialsRequiredException(m_profile);
            return ValueTask.FromResult(identity);
        }

        public async ValueTask DisposeAsync()
        {
            IUserIdentity? identity = Interlocked.Exchange(ref m_identity, null);
            if (identity is not null)
            {
                await ReleaseIdentityAsync(identity).ConfigureAwait(false);
            }
        }

        private readonly ConnectionProfile m_profile;
        private IUserIdentity? m_identity;
    }

    private InMemorySecretStore? m_store;
    private SecretIdentifier? m_secretId;
    private SingleUseIdentityProvider? m_singleUse;
}

/// <summary>
/// An explicitly selected endpoint plus a reconnectable credential owner.
/// Dialog cancellation/abandonment disposes it; a connection consumes it once.
/// Unlike the compatibility picker Result, this never exposes token material.
/// </summary>
internal sealed class ConnectionSelection : IAsyncDisposable
{
    public ConnectionSelection(
        EndpointDescription endpoint,
        ConnectionProfile profile,
        ConnectionCredentials credentials)
        : this(endpoint, profile)
    {
        m_credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    }

    public ConnectionSelection(
        EndpointDescription endpoint,
        ConnectionProfile profile,
        IClientIdentityProvider provider)
        : this(endpoint, profile)
    {
        ArgumentNullException.ThrowIfNull(provider);
        m_credentials = new ConnectionCredentials(profile, provider);
    }

    private ConnectionSelection(EndpointDescription endpoint, ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        profile.RequireMatch(endpoint);
        Endpoint = (EndpointDescription)endpoint.Clone();
        Profile = profile;
    }

    public EndpointDescription Endpoint { get; }

    public ConnectionProfile Profile { get; private set; }

    public IClientIdentityProvider Provider
    {
        get
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_disposal is not null, this);
                return m_credentials?.Provider ??
                    throw new InvalidOperationException(
                        "The connection selection has already transferred its credential ownership.");
            }
        }
    }

    public void ApplySetup(ConnectionSetupSelection setup)
    {
        ConnectionProfile profile = Profile with
        {
            ReverseConnection = setup.ReverseConnection,
            ApplicationIdentityId = setup.ApplicationIdentityId
        };
        profile.Validate();
        Profile = profile;
    }

    public ConnectionCredentials TakeCredentials()
    {
        lock (m_gate)
        {
            ObjectDisposedException.ThrowIf(m_disposal is not null, this);
            ConnectionCredentials credentials = m_credentials ??
                throw new InvalidOperationException("The connection selection has already been consumed.");
            m_credentials = null;
            return credentials;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (m_gate)
        {
            m_disposal ??= DisposeCoreAsync();
            return new ValueTask(m_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            if (m_credentials is not null)
            {
                await m_credentials.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            m_credentials = null;
        }
    }

    private readonly Lock m_gate = new();
    private ConnectionCredentials? m_credentials;
    private Task? m_disposal;
}
