// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;

namespace UaLens.Connection;

internal enum ConnectionPhase
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

/// <summary>
/// Immutable shell state. Generation changes only when a new session is installed,
/// not during ManagedSession's automatic reconnect; documents can retain their
/// subscriptions while temporarily unavailable.
/// </summary>
internal sealed record ConnectionSnapshot(
    ConnectionPhase Phase,
    ConnectionProfile? Profile,
    string? Error,
    long Generation)
{
    public bool IsConnected => Phase == ConnectionPhase.Connected;
}

/// <summary>
/// Owns the primary connection, trust decisions, credential reacquisition and
/// cancellation. Events may arrive on any thread; UI subscribers must dispatch.
/// A profile can be saved; an identity provider must never be serialized.
/// </summary>
internal interface IConnectionWorkspace : IAsyncDisposable
{
    ConnectionSnapshot Snapshot { get; }

    ISession? CurrentSession { get; }

    bool IsConnected { get; }

    event Action? StateChanged;

    /// <summary>
    /// Awaited when the primary session reference changes, not on transport-only
    /// reconnect. On disconnect CurrentSession is null, but the old session is
    /// not disposed until observers finish. On connect observers can attach to
    /// the new session before ConnectAsync returns. Do not re-enter primary
    /// connection transitions from an observer; local-tools lookups are allowed.
    /// </summary>
    event Func<CancellationToken, Task>? ConnectionChangedAsync;

    Task ConnectAsync(
        ConnectionProfile profile,
        IClientIdentityProvider? identityProvider = null,
        CertificateTrustPrompt? certificatePrompt = null,
        CancellationToken ct = default);

    Task ReconnectAsync(SubscriptionEngineKind engine, CancellationToken ct = default);

    /// <summary>
    /// Replaces the connection using the current endpoint and new identity.
    /// Takes ownership of the identity and updates the retained profile and credentials together.
    /// </summary>
    Task ChangeIdentityAsync(IUserIdentity identity, CancellationToken ct = default);

    Task CancelAsync();

    Task DisconnectAsync();
}

internal enum TrustChoice
{
    Reject,
    AcceptOnce,
    TrustPermanently
}

/// <summary>
/// Public certificate bytes are copied, not borrowed from a validator handle.
/// The prompt may outlive cancellation without accessing disposed certificates.
/// </summary>
internal sealed record CertificateTrustRequest(
    ConnectionProfile Profile,
    ByteString CertificateData,
    ServiceResult Error);

/// <summary>
/// Runs outside certificate validation. A desktop implementation should close its
/// dialog when cancellation is requested. Late answers are never applied.
/// </summary>
internal delegate Task<TrustChoice> CertificateTrustPrompt(
    CertificateTrustRequest request,
    CancellationToken ct);
