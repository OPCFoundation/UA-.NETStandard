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

namespace Opc.Ua.Server
{
    public partial class AsyncCustomNodeManager
    {
        /// <summary>
        /// Registers asynchronous Value read authorization for monitored-item admission and notification delivery.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The callback receives the subscriber's context and runs outside node-manager collection gates.
        /// Its result is never cached. Direct value-read handlers remain responsible for authorizing direct reads.
        /// There may be only one active registration for a particular variable instance.
        /// </para>
        /// <para>
        /// Disposing the lease is idempotent and nonblocking, including from the callback. It releases the
        /// callback and makes this variable fail closed with Bad_Shutdown until explicitly registered again.
        /// An already admitted callback cannot grant access after its registration is disposed. Node deletion
        /// and manager disposal release registrations; manager disposal cancels and drains admitted callbacks.
        /// Callbacks must honor cancellation and must not await disposal of their owning node manager.
        /// </para>
        /// </remarks>
        /// <param name="node">The exact variable instance protected by this registration.</param>
        /// <param name="authorize">The read authorization callback.</param>
        /// <returns>A nonblocking registration lease owned by the binding.</returns>
        /// <exception cref="ArgumentNullException">The node or callback is null.</exception>
        /// <exception cref="InvalidOperationException">The variable already has an active registration.</exception>
        /// <exception cref="ObjectDisposedException">The node manager is stopping.</exception>
        public IDisposable RegisterValueReadAuthorization(
            BaseVariableState node,
            Func<ISystemContext, CancellationToken, ValueTask<ServiceResult>> authorize)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (authorize == null)
            {
                throw new ArgumentNullException(nameof(authorize));
            }
            lock (m_operationLifetimeLock)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(AsyncCustomNodeManager));
                }
                if (m_valueReadAuthorizations.TryGetValue(node, out ValueReadAuthorizationRegistration? previous) &&
                    previous.Authorize != null)
                {
                    throw new InvalidOperationException("The variable already has a Value read authorizer.");
                }
                var registration = new ValueReadAuthorizationRegistration(this, authorize);
                // Lookups run without the lock: an odd version marks the replacement in progress.
                Interlocked.Increment(ref m_valueReadAuthorizationVersion);
                try
                {
                    if (previous != null)
                    {
                        m_valueReadAuthorizations.Remove(node);
                    }
                    m_valueReadAuthorizations.Add(node, registration);
                }
                finally
                {
                    Interlocked.Increment(ref m_valueReadAuthorizationVersion);
                }
                m_activeValueReadAuthorizations.Add(registration);
                return registration;
            }
        }

        /// <summary>
        /// Indicates whether the variable has an active or retired read-authorization registration.
        /// </summary>
        internal bool HasValueReadAuthorization(BaseVariableState node)
        {
            return GetValueReadAuthorization(node) != null;
        }

        /// <summary>
        /// Gets the variable's active or retired read-authorization registration.
        /// </summary>
        /// <remarks>
        /// Runs per monitored-item notification, so it reads the thread-safe table without the
        /// lock and only takes it when a registration was replaced during the lookup.
        /// </remarks>
        private ValueReadAuthorizationRegistration? GetValueReadAuthorization(BaseVariableState node)
        {
            int version = Volatile.Read(ref m_valueReadAuthorizationVersion);
            if ((version & 1) == 0)
            {
                m_valueReadAuthorizations.TryGetValue(node, out ValueReadAuthorizationRegistration? registration);
                Interlocked.MemoryBarrier();
                if (Volatile.Read(ref m_valueReadAuthorizationVersion) == version)
                {
                    return registration;
                }
            }
            lock (m_operationLifetimeLock)
            {
                m_valueReadAuthorizations.TryGetValue(node, out ValueReadAuthorizationRegistration? registration);
                return registration;
            }
        }

        /// <summary>
        /// Rechecks the exact source's permissions, current channel restrictions and uncached binding policy.
        /// </summary>
        internal async ValueTask<ServiceResult> ValidateMonitoredReadAsync(
            ServerSystemContext context,
            NodeState node,
            uint attributeId,
            CancellationToken cancellationToken)
        {
            using NodeManagerOperation operation = BeginNodeManagerOperation();
            OperationContext operationContext = context.OperationContext!;
            PermissionType requested = MonitoredReadAuthorization.PermissionFor(attributeId);
            (bool handled, ServiceResult? result) = await TryValidatePermissionsCoreAsync(
                operationContext,
                new NodeHandle(node.NodeId, node),
                requested,
                null,
                permissionsOnly: true,
                logger: null,
                validateAccessRestrictions: true,
                validateRolePermissions: true,
                cancellationToken,
                exactSource: true).ConfigureAwait(false);
            if (!handled)
            {
                return StatusCodes.BadNodeIdUnknown;
            }
            if (ServiceResult.IsBad(result) ||
                attributeId != Attributes.Value ||
                node is not BaseVariableState variable)
            {
                return result!;
            }

            ValueReadAuthorizationRegistration? registration = GetValueReadAuthorization(variable);
            if (registration == null)
            {
                return result!;
            }
            Func<ISystemContext, CancellationToken, ValueTask<ServiceResult>>? authorize = registration.Authorize;
            if (authorize == null)
            {
                return StatusCodes.BadShutdown;
            }

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_valuePublicationShutdown.Token);
            try
            {
                result = await authorize(context, lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                return ReferenceEquals(authorize, registration.Authorize)
                    ? result
                    : StatusCodes.BadShutdown;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (m_valuePublicationShutdown.IsCancellationRequested)
            {
                return StatusCodes.BadShutdown;
            }
            catch (OperationCanceledException)
            {
                return registration.Authorize == null
                    ? StatusCodes.BadShutdown
                    : StatusCodes.BadRequestCancelledByClient;
            }
            catch (ObjectDisposedException)
            {
                return StatusCodes.BadShutdown;
            }
            catch (ServiceResultException error)
            {
                return registration.Authorize == null ? StatusCodes.BadShutdown : error.Result;
            }
            catch (Exception error) when (
                error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                return registration.Authorize == null
                    ? StatusCodes.BadShutdown
                    : ServiceResult.Create(error, StatusCodes.BadUnexpectedError, "A Value read authorizer failed.");
            }
        }

        /// <summary>
        /// Retires a deleted variable's authorizer without making pending notifications unprotected.
        /// </summary>
        private void DisposeValueReadAuthorization(NodeState node)
        {
            if (node is not BaseVariableState variable)
            {
                return;
            }
            ValueReadAuthorizationRegistration? registration;
            lock (m_operationLifetimeLock)
            {
                m_valueReadAuthorizations.TryGetValue(variable, out registration);
            }
            registration?.Dispose();
        }

        /// <summary>
        /// Releases policy closures after admitted manager operations have drained.
        /// </summary>
        private void DisposeValueReadAuthorizations()
        {
            ValueReadAuthorizationRegistration[] registrations;
            lock (m_operationLifetimeLock)
            {
                registrations = [.. m_activeValueReadAuthorizations];
                m_activeValueReadAuthorizations.Clear();
            }
            foreach (ValueReadAuthorizationRegistration registration in registrations)
            {
                registration.Dispose();
            }
        }

        /// <summary>Weak keys keep retired variables fail closed without retaining deleted nodes or bindings.</summary>
        private readonly ConditionalWeakTable<BaseVariableState, ValueReadAuthorizationRegistration>
            m_valueReadAuthorizations =
#if NET8_0_OR_GREATER
                [];
#else
                new();
#endif

        /// <summary>
        /// Incremented before and after a registration replaces another, so lookups without the lock
        /// detect a concurrent replacement.
        /// </summary>
        private int m_valueReadAuthorizationVersion;

        /// <summary>Live policy registrations protected by the existing manager operation-admission gate.</summary>
        private readonly HashSet<ValueReadAuthorizationRegistration> m_activeValueReadAuthorizations = [];

        /// <summary>
        /// Releases a policy closure without waiting for a callback that may itself own the lease.
        /// </summary>
        private sealed class ValueReadAuthorizationRegistration : IDisposable
        {
            /// <summary>
            /// Captures the existing node-manager lifetime and policy delegate.
            /// </summary>
            public ValueReadAuthorizationRegistration(
                AsyncCustomNodeManager owner,
                Func<ISystemContext, CancellationToken, ValueTask<ServiceResult>> authorize)
            {
                m_owner = owner;
                m_authorize = authorize;
            }

            /// <summary>
            /// Gets a callback snapshot, or null once the variable's registration is retired.
            /// </summary>
            public Func<ISystemContext, CancellationToken, ValueTask<ServiceResult>>? Authorize =>
                Volatile.Read(ref m_authorize);

            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_authorize, null);
                AsyncCustomNodeManager? owner = Interlocked.Exchange(ref m_owner, null);
                if (owner != null)
                {
                    lock (owner.m_operationLifetimeLock)
                    {
                        owner.m_activeValueReadAuthorizations.Remove(this);
                    }
                }
            }

            /// <summary>The manager whose admission and drain protocol owns this callback.</summary>
            private AsyncCustomNodeManager? m_owner;

            /// <summary>The policy closure, cleared before releasing the registration.</summary>
            private Func<ISystemContext, CancellationToken, ValueTask<ServiceResult>>? m_authorize;
        }
    }

    /// <summary>
    /// Applies existing read-permission machinery with the current subscriber's identity and endpoint.
    /// </summary>
    internal static class MonitoredReadAuthorization
    {
        /// <summary>
        /// Gets accepted channel evidence rather than the native Session's original creation endpoint.
        /// </summary>
        public static EndpointDescription? GetEndpoint(ISession? session)
        {
            return session is Session native ? native.BoundEndpointDescription : session?.EndpointDescription;
        }

        /// <summary>
        /// Refreshes stale background grants using the accepted endpoint before capturing authorization.
        /// </summary>
        internal static bool TryRefreshIdentity(IServerInternal server, ISession? session)
        {
            if (session?.IsIdentityStale != true)
            {
                return true;
            }
            using OperationContext context = CreateContext(session);
            return server.SessionManager is SessionManager manager &&
                manager.TryRefreshEffectiveIdentity(session, context.ChannelContext);
        }

        /// <summary>
        /// Applies the notifier's current channel restrictions to event delivery on its exact source.
        /// </summary>
        internal static async ValueTask<ServiceResult> ValidateEventRestrictionsAsync(
            IServerInternal server,
            NodeState notifier,
            OperationContext context,
            CancellationToken cancellationToken)
        {
            NamespaceDefaultPermissions defaults = await NamespaceDefaultPermissionReader.ReadAsync(
                server, new ServerSystemContext(server, context), notifier.NodeId.NamespaceIndex, cancellationToken)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(defaults.Status))
            {
                return defaults.Status;
            }
            var metadata = new NodeMetadata(notifier, notifier.NodeId)
            {
                AccessRestrictions = notifier.AccessRestrictions ?? AccessRestrictionType.None,
                DefaultAccessRestrictions = notifier.AccessRestrictions.HasValue
                    ? AccessRestrictionType.None : defaults.DefaultAccessRestrictions
            };
            return MasterNodeManager.ValidateAccessRestrictions(context, metadata, PermissionType.ReceiveEvents);
        }

        /// <summary>
        /// Identifies encryption supplied by the accepted channel's message security or known TLS transport profile.
        /// </summary>
        public static bool IsConfidential(EndpointDescription? endpoint)
        {
            return endpoint != null &&
                (endpoint.SecurityMode == MessageSecurityMode.SignAndEncrypt ||
                    Profiles.IsHttpsBinary(endpoint.TransportProfileUri) ||
                    Profiles.IsHttpsJson(endpoint.TransportProfileUri) ||
                    Profiles.IsHttpsOpenApi(endpoint.TransportProfileUri) ||
                    Profiles.IsWssBinary(endpoint.TransportProfileUri) ||
                    Profiles.IsWssJson(endpoint.TransportProfileUri) ||
                    Profiles.IsWssOpenApi(endpoint.TransportProfileUri));
        }

        /// <summary>
        /// Gets the subscriber context of a monitored item for read checks and reads on its
        /// notification path, shared by every item of the same session.
        /// </summary>
        /// <remarks>
        /// The context of a session only depends on its effective identity, its notification
        /// authorization epoch and its accepted endpoint, so all items of the session reuse one
        /// context until one of them changes, and the context's per-node-manager system context
        /// copy is reused with it. A notification therefore allocates no context. The returned
        /// context is shared: callers must not dispose or retain it.
        /// </remarks>
        /// <param name="item">The monitored item.</param>
        /// <param name="diagnostics">
        /// The diagnostics mask of the context. A session keeps one context for the default mask
        /// and one per operation-level mask (a sampling group reads with the operation-level mask
        /// of the request that created its items); any other mask shares a single entry.
        /// </param>
        public static OperationContext GetSubscriberContext(
            IMonitoredItem item,
            DiagnosticsMasks diagnostics = DiagnosticsMasks.SymbolicId)
        {
            return GetSubscriberEntry(item, diagnostics).Context;
        }

        /// <summary>
        /// Gets the copy of a node manager's system context for the shared subscriber context of
        /// a monitored item.
        /// </summary>
        /// <remarks>
        /// The copy is kept with the shared context for every node manager, so it stays the same
        /// object across notifications however many node managers the session's items span; the
        /// item's cached verdicts are keyed by it. The returned context is shared: callers must
        /// not dispose or retain it.
        /// </remarks>
        /// <param name="item">The monitored item.</param>
        /// <param name="template">The node manager's system context.</param>
        /// <param name="diagnostics">The diagnostics mask of the shared context.</param>
        public static ServerSystemContext GetSubscriberSystemContext(
            IMonitoredItem item,
            ServerSystemContext template,
            DiagnosticsMasks diagnostics = DiagnosticsMasks.SymbolicId)
        {
            return GetSubscriberEntry(item, diagnostics).GetSystemContext(template);
        }

        private static SubscriberContext GetSubscriberEntry(IMonitoredItem item, DiagnosticsMasks diagnostics)
        {
            ISession? session = item.Session;
            if (session == null)
            {
                IUserIdentity? identity = item.EffectiveIdentity;
                SubscriberContext? itemContext = s_itemContexts.TryGetValue(item, out SubscriberContextSlot? itemSlot)
                    ? Volatile.Read(ref itemSlot.Current) : null;
                // A context created while the item had a session is bound to that session.
                if (itemContext != null &&
                    itemContext.Context.Session is null &&
                    ReferenceEquals(itemContext.Identity, identity))
                {
                    return itemContext;
                }
                // CA2000: the shared context is owned by the item's entry and never disposed by callers.
#pragma warning disable CA2000
                var created = new SubscriberContext(identity, null, 0, null, null, new OperationContext(item));
#pragma warning restore CA2000
                Volatile.Write(ref s_itemContexts.GetValue(item, static _ => new SubscriberContextSlot()).Current, created);
                return created;
            }

            // read without the session's lock; a context captured under it is reused only when
            // every input it captured is still current.
            // A custom session has no epoch object to compare; its identity refresh generation is.
            NotificationAuthorization? authorization = (session as Session)?.NotificationAuthorization;
            long generation = session is Session ? 0 : session.CaptureIdentityRefreshSnapshot().Generation;
            IUserIdentity effectiveIdentity = session.EffectiveIdentity;
            EndpointDescription? endpoint = GetEndpoint(session);
            string? secureChannelId = session.SecureChannelId;
            SubscriberContextSlot slot = s_sessionContexts.GetValue(session, static _ => new SubscriberContextSlot());
            ref SubscriberContext? entry = ref slot.GetEntry(diagnostics);
            SubscriberContext? current = Volatile.Read(ref entry);
            if (current != null &&
                current.Diagnostics == diagnostics &&
                ReferenceEquals(current.Identity, effectiveIdentity) &&
                current.Generation == generation &&
                ReferenceEquals(current.Endpoint, endpoint) &&
                string.Equals(current.SecureChannelId, secureChannelId, StringComparison.Ordinal) &&
                (session is not Session || ReferenceEquals(current.Authorization, authorization)))
            {
                return current;
            }
            OperationContext context = CreateContext(session, diagnostics);
            // the constructor captured identity and epoch together; key the entry on what it captured.
            current = new SubscriberContext(
                context.UserIdentity,
                context.NotificationAuthorization,
                generation,
                endpoint,
                secureChannelId,
                context)
            {
                Diagnostics = diagnostics
            };
            Volatile.Write(ref entry, current);
            return current;
        }

        /// <summary>
        /// A shared subscriber context, the inputs it was created from and its system context
        /// copies, one per node manager.
        /// </summary>
        private sealed class SubscriberContext
        {
            public SubscriberContext(
                IUserIdentity? identity,
                NotificationAuthorization? authorization,
                long generation,
                EndpointDescription? endpoint,
                string? secureChannelId,
                OperationContext context)
            {
                Identity = identity;
                Authorization = authorization;
                Generation = generation;
                Endpoint = endpoint;
                SecureChannelId = secureChannelId;
                Context = context;
                m_createCopy = template => template.Copy(context);
            }

            public IUserIdentity? Identity { get; }
            public NotificationAuthorization? Authorization { get; }
            public long Generation { get; }
            public EndpointDescription? Endpoint { get; }
            public string? SecureChannelId { get; }
            public OperationContext Context { get; }
            public DiagnosticsMasks Diagnostics { get; init; } = DiagnosticsMasks.SymbolicId;

            /// <summary>
            /// Gets the copy of the node manager's system context for this context. Unlike the
            /// bounded per-request cache of <see cref="OperationContext.GetSystemContext"/>, the
            /// table keeps one copy for every node manager.
            /// </summary>
            public ServerSystemContext GetSystemContext(ServerSystemContext template)
            {
                return m_copies.TryGetValue(template, out ServerSystemContext? copy)
                    ? copy
                    : m_copies.GetValue(template, m_createCopy);
            }

            private readonly ConditionalWeakTable<ServerSystemContext, ServerSystemContext> m_copies = new();
            private readonly ConditionalWeakTable<ServerSystemContext, ServerSystemContext>.CreateValueCallback m_createCopy;
        }

        /// <summary>
        /// Holds the current shared context of a session or sessionless item.
        /// </summary>
        private sealed class SubscriberContextSlot
        {
            public SubscriberContext? Current;

            /// <summary>
            /// Gets the entry of a diagnostics mask: the default mask's, one per operation-level
            /// mask (what a sampling group reads with), or a single entry for any other mask.
            /// </summary>
            public ref SubscriberContext? GetEntry(DiagnosticsMasks diagnostics)
            {
                if (diagnostics == DiagnosticsMasks.SymbolicId)
                {
                    return ref Current;
                }
                if ((diagnostics & ~DiagnosticsMasks.OperationAll) != 0)
                {
                    return ref m_other;
                }
                SubscriberContext?[] masked = Volatile.Read(ref m_masked) ??
                    Interlocked.CompareExchange(ref m_masked, new SubscriberContext?[kOperationMasks], null) ??
                    m_masked;
                return ref masked[(int)diagnostics >> kOperationMaskShift];
            }

            /// <summary>The operation-level mask bits are bits 5 to 9.</summary>
            private const int kOperationMaskShift = 5;
            private const int kOperationMasks = ((int)DiagnosticsMasks.OperationAll >> kOperationMaskShift) + 1;

            private SubscriberContext?[]? m_masked;
            private SubscriberContext? m_other;
        }

        private static readonly ConditionalWeakTable<ISession, SubscriberContextSlot> s_sessionContexts = new();
        private static readonly ConditionalWeakTable<IMonitoredItem, SubscriberContextSlot> s_itemContexts = new();

        /// <summary>
        /// Creates a read context from the current item owner, never from the change reporter.
        /// </summary>
        public static OperationContext CreateContext(
            IMonitoredItem item,
            DiagnosticsMasks diagnostics = DiagnosticsMasks.SymbolicId)
        {
            ISession? session = item.Session;
            return session == null ? new OperationContext(item) : CreateContext(session, diagnostics);
        }

        /// <summary>
        /// Creates the same subscriber context while an initial monitored item has no subscription callback yet.
        /// </summary>
        public static OperationContext CreateContext(
            ISession session,
            DiagnosticsMasks diagnostics = DiagnosticsMasks.SymbolicId)
        {
            if (GetEndpoint(session) is not EndpointDescription endpoint)
            {
                var context = new OperationContext(session, diagnostics);
                context.DisableDefaultPermissionCache();
                return context;
            }
            var channel = new SecureChannelContext(
                session.SecureChannelId ?? string.Empty, endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader { ReturnDiagnostics = (uint)diagnostics },
                channel, RequestType.Read, RequestLifetime.None, session);
            operationContext.DisableDefaultPermissionCache();
            return operationContext;
        }

        /// <summary>
        /// Returns the same permission required by the Read service for a monitored attribute.
        /// </summary>
        public static PermissionType PermissionFor(uint attributeId)
        {
            return attributeId == Attributes.Value
                ? PermissionType.Read
                : attributeId == Attributes.RolePermissions
                    ? PermissionType.ReadRolePermissions : PermissionType.Browse;
        }

        /// <summary>
        /// Revalidates restrictions and optional asynchronous policy without caching a subscriber-dependent result.
        /// </summary>
        public static async ValueTask<ServiceResult> ValidateAsync(
            IAsyncNodeManager manager,
            NodeState node,
            ServerSystemContext context,
            uint attributeId,
            CancellationToken cancellationToken)
        {
            bool recheckRoles = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ISession? session = context.OperationContext?.Session;
                IUserIdentity? identity = context.UserIdentity;
                if (session != null && !ReferenceEquals(identity, session.EffectiveIdentity))
                {
                    return StatusCodes.BadUserAccessDenied;
                }
                using OperationContext? refreshed = session != null &&
                    !ReferenceEquals(
                        context.OperationContext?.ChannelContext?.EndpointDescription, GetEndpoint(session))
                    ? CreateContext(session) : null;
                ServerSystemContext contextToUse = refreshed == null ? context : context.Copy(refreshed);
                EndpointDescription? endpoint = GetEndpoint(session) ??
                    contextToUse.OperationContext?.ChannelContext?.EndpointDescription;
                MessageSecurityMode? securityMode = endpoint?.SecurityMode;
                string? transport = endpoint?.TransportProfileUri;
                AccessRestrictionType? restrictions = node.AccessRestrictions;
                long version = NodeState.RolePermissionsVersion;
                if (recheckRoles)
                {
                    ServiceResult roles = await manager.ValidateRolePermissionsAsync(
                        contextToUse.OperationContext!, node.NodeId, PermissionFor(attributeId), cancellationToken)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(roles))
                    {
                        return roles;
                    }
                }
                ServiceResult result = await ValidateCoreAsync(
                    manager, node, contextToUse, attributeId, cancellationToken).ConfigureAwait(false);
                if (session != null && !ReferenceEquals(identity, session.EffectiveIdentity))
                {
                    return StatusCodes.BadUserAccessDenied;
                }
                EndpointDescription? currentEndpoint = GetEndpoint(session) ??
                    contextToUse.OperationContext?.ChannelContext?.EndpointDescription;
                if (ReferenceEquals(endpoint, currentEndpoint) &&
                    securityMode == currentEndpoint?.SecurityMode &&
                    transport == currentEndpoint?.TransportProfileUri &&
                    restrictions == node.AccessRestrictions &&
                    version == NodeState.RolePermissionsVersion)
                {
                    return result;
                }
                recheckRoles = true;
            }
        }

        /// <summary>
        /// Applies current restrictions and the optional policy for one stable subscriber context.
        /// </summary>
        private static async ValueTask<ServiceResult> ValidateCoreAsync(
            IAsyncNodeManager manager,
            NodeState node,
            ServerSystemContext context,
            uint attributeId,
            CancellationToken cancellationToken)
        {
            if (manager is AsyncCustomNodeManager custom)
            {
                return await custom.ValidateMonitoredReadAsync(context, node, attributeId, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (manager is AsyncNodeManagerAdapter { SyncNodeManager: CustomNodeManager2 synchronous })
            {
                return synchronous.ValidateMonitoredRead(context, node, attributeId);
            }

            var metadata = new NodeMetadata(node, node.NodeId)
            {
                IsPartOfTypeHierarchy = node.IsPartOfTypeHierarchy,
                AccessRestrictions = node.AccessRestrictions ?? AccessRestrictionType.None
            };
            object? handle = await manager.GetManagerHandleAsync(node.NodeId, cancellationToken).ConfigureAwait(false);
            if (handle != null)
            {
                metadata = await manager.GetNodeMetadataAsync(
                    context.OperationContext!, handle, BrowseResultMask.All, cancellationToken).ConfigureAwait(false)
                    ?? metadata;
            }
            return MasterNodeManager.ValidateAccessRestrictions(
                context.OperationContext!, metadata, PermissionFor(attributeId));
        }
    }
}
