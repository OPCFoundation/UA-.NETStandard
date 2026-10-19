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
                if (m_valueReadAuthorizations.TryGetValue(node, out ValueReadAuthorizationRegistration? previous))
                {
                    if (previous.Authorize != null)
                    {
                        throw new InvalidOperationException("The variable already has a Value read authorizer.");
                    }
                    m_valueReadAuthorizations.Remove(node);
                }
                var registration = new ValueReadAuthorizationRegistration(this, authorize);
                m_valueReadAuthorizations.Add(node, registration);
                m_activeValueReadAuthorizations.Add(registration);
                return registration;
            }
        }

        /// <summary>
        /// Indicates whether the variable has an active or retired read-authorization registration.
        /// </summary>
        internal bool HasValueReadAuthorization(BaseVariableState node)
        {
            lock (m_operationLifetimeLock)
            {
                return m_valueReadAuthorizations.TryGetValue(node, out _);
            }
        }

        /// <summary>
        /// Rechecks current channel restrictions and uncached binding policy after the caller's role-permission check.
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
                validateRolePermissions: false,
                cancellationToken).ConfigureAwait(false);
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

            ValueReadAuthorizationRegistration? registration;
            lock (m_operationLifetimeLock)
            {
                m_valueReadAuthorizations.TryGetValue(variable, out registration);
            }
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
                return new OperationContext(session, diagnostics);
            }
            var channel = new SecureChannelContext(
                session.SecureChannelId ?? string.Empty, endpoint, RequestEncoding.Binary);
            return new OperationContext(
                new RequestHeader { ReturnDiagnostics = (uint)diagnostics },
                channel, RequestType.Read, RequestLifetime.None, session);
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
