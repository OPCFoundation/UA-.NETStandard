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

using System;
using System.Collections.Generic;
using Opc.Ua.Identity;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for creating a <see cref="ManagedSession"/>. Provided as a
    /// record so it can be used with the .NET options pattern
    /// (<see cref="Microsoft.Extensions.Options.IOptions{T}"/>) and bound
    /// from configuration.
    /// </summary>
    public sealed record ManagedSessionOptions
    {
        /// <summary>
        /// The configured endpoint to connect to. Required.
        /// </summary>
        public ConfiguredEndpoint? Endpoint { get; init; }

        /// <summary>
        /// Optional lazy identity provider. When both <see cref="IdentityProvider"/>
        /// and <see cref="Identity"/> are set, the provider takes precedence
        /// and can refresh identities after the session is connected.
        /// </summary>
        public IClientIdentityProvider? IdentityProvider { get; init; }

        /// <summary>
        /// Optional eager user identity. If null, the session uses anonymous.
        /// When both <see cref="IdentityProvider"/> and <see cref="Identity"/>
        /// are set, <see cref="IdentityProvider"/> takes precedence.
        /// </summary>
        [Obsolete(
            "Use IdentityProvider for lazy/refresh-capable identities; the eager Identity setter " +
            "cannot refresh on token expiry. See Docs/IdentityProviders.md.")]
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// Optional time provider for proactive identity refresh scheduling.
        /// </summary>
        public TimeProvider? TimeProvider { get; init; }

        /// <summary>
        /// Session display name.
        /// </summary>
        public string SessionName { get; init; } = "ManagedSession";

        /// <summary>
        /// Requested session timeout. Defaults to 60 seconds.
        /// </summary>
        public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Preferred locale identifiers (e.g. "en-US"). May be null.
        /// </summary>
        public IReadOnlyList<string>? PreferredLocales { get; init; }

        /// <summary>
        /// Whether to validate the domain in the server certificate.
        /// </summary>
        public bool CheckDomain { get; init; }

        /// <summary>
        /// Reconnect policy configuration.
        /// </summary>
        public ReconnectPolicyOptions ReconnectPolicy { get; init; } = new();

        /// <summary>
        /// Optional shared gate that asynchronously admits initial
        /// <see cref="ManagedSession"/> connect attempts. Default:
        /// <c>null</c> (no client-side connect admission limit).
        /// </summary>
        public IClientConnectGate? ConnectGate { get; init; }

        /// <summary>
        /// Optional max concurrency used by DI/configuration to create a
        /// shared <see cref="RateLimiterClientConnectGate"/> when
        /// <see cref="ConnectGate"/> is not supplied. Default:
        /// <c>null</c> (no client-side connect admission limit).
        /// </summary>
        public int? ConnectRateLimiterMaxConcurrency { get; init; }

        /// <summary>
        /// When set, server-side redundancy failover is enabled using a
        /// default <see cref="DefaultServerRedundancyHandler"/>. To use a
        /// custom handler, pass it explicitly to
        /// <see cref="ManagedSession"/>.<c>CreateAsync</c>.
        /// </summary>
        public bool EnableServerRedundancy { get; init; }

        /// <summary>
        /// Optional subscription engine factory. When null, defaults to the
        /// V2 engine (<see cref="DefaultSubscriptionEngineFactory"/>) so the
        /// session's V2 subscription manager (see
        /// <see cref="ISession.TryGetSubscriptionManager"/>) is available.
        /// </summary>
        public ISubscriptionEngineFactory? SubscriptionEngineFactory { get; init; }

        /// <summary>
        /// When <c>true</c>, opt the V2 subscription engine into
        /// transfer-on-recreate. After a session re-create (e.g. a
        /// <c>ManagedSession</c> failover via
        /// <c>Session.RecreateInPlaceAsync</c>) the V2
        /// <see cref="Subscriptions.ISubscriptionManager"/> first
        /// attempts to transfer existing server-side subscriptions
        /// from the previous session to the new one before falling
        /// back to per-subscription recreate. Default: <c>false</c>
        /// (recreate is the universal, server-agnostic fallback;
        /// transfer requires server support and is opt-in).
        /// </summary>
        /// <remarks>
        /// Has no effect when the classic subscription engine is in
        /// use, because the classic engine drives recreate through
        /// the <see cref="Session"/>'s template-based path, not the
        /// V2 manager.
        /// </remarks>
        public bool TransferSubscriptionsOnRecreate { get; init; }

        /// <summary>
        /// <para>
        /// When <c>true</c>, opt the V2 subscription engine into
        /// activator-level pooling of notification payload instances.
        /// After each publish dispatch the subscription calls
        /// <see cref="IPooledEncodeable.Reuse"/> on notification objects
        /// (such as <c>MonitoredItemNotification</c>), releasing them
        /// back to their activator's pool for reuse on the next decode.
        /// </para>
        /// <para>
        /// Handlers that retain references to notification values past
        /// the dispatch call must copy the retained values before
        /// returning from the handler — the pool may re-rent those
        /// instances to other consumers immediately after the handler
        /// returns. Default: <c>false</c> (opt-in).
        /// </para>
        /// <para>
        /// Has no effect when the classic subscription engine is in
        /// use; this option only applies to the V2
        /// <see cref="Subscriptions.ISubscriptionManager"/>.
        /// </para>
        /// </summary>
        public bool PoolNotifications { get; init; }

        /// <summary>
        /// <para>
        /// When <c>true</c>, the <see cref="ManagedSession"/> automatically
        /// enables address-space model change tracking once connected.
        /// It subscribes to <c>GeneralModelChangeEventType</c> on the
        /// server's notifier (and to <c>SemanticChangeEventType</c>),
        /// invalidates the session's <see cref="INodeCache"/> when changes
        /// are reported, and exposes the changes via
        /// <see cref="ManagedSession.ModelChange"/>.
        /// </para>
        /// <para>
        /// Default: <c>false</c>. Enable for applications that cache
        /// browse results long-term and need to react to dynamic
        /// address-space changes (devices joining/leaving, type updates).
        /// </para>
        /// </summary>
        public bool ModelChangeTracking { get; init; }
    }
}
