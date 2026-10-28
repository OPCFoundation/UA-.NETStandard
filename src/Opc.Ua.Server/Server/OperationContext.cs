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
using System.Threading;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores information used while a thread is completing an operation on behalf of a client.
    /// <para>
    /// A context created for a Client request owns that request's tracking scope, so disposing the
    /// context reports the request as completed. Internal contexts own no request tracking scope.
    /// Service calls borrowing an unpinned context share one routing view until their last scope exits.
    /// </para>
    /// </summary>
    public class OperationContext : ISessionOperationContext, IDisposable, IRequestTableEntry
    {
        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="requestLifetime">The request lifetime.</param>
        /// <param name="identity">The identity used in the request.</param>
        public OperationContext(
            RequestHeader requestHeader,
            SecureChannelContext? secureChannelContext,
            RequestType requestType,
            RequestLifetime requestLifetime,
            IUserIdentity? identity = null)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            ChannelContext = secureChannelContext;
            Session = null!;
            UserIdentity = identity!;
            PreferredLocales = Array.Empty<string>();
            DiagnosticsMask = (DiagnosticsMasks)requestHeader.ReturnDiagnostics;
            AuditEntryId = requestHeader.AuditEntryId!;
            ClientTimestamp = requestHeader.Timestamp.IsNull
                ? DateTime.MinValue
                : (DateTime)requestHeader.Timestamp;
            RequestId = Utils.IncrementIdentifier(ref s_lastRequestId);
            RequestType = requestType;
            ClientHandle = requestHeader.RequestHandle;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = requestLifetime ?? RequestLifetime.None;

            if (requestHeader.TimeoutHint > 0)
            {
                OperationDeadline = DateTime.UtcNow.AddMilliseconds(requestHeader.TimeoutHint);
            }
        }

        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="requestHeader">The request header.</param>
        /// <param name="secureChannelContext">The secure channel context.</param>
        /// <param name="requestType">Type of the request.</param>
        /// <param name="requestLifetime">The request lifetime.</param>
        /// <param name="session">The session.</param>
        public OperationContext(
            RequestHeader requestHeader,
            SecureChannelContext secureChannelContext,
            RequestType requestType,
            RequestLifetime requestLifetime,
            ISession session)
        {
            if (requestHeader == null)
            {
                throw new ArgumentNullException(nameof(requestHeader));
            }

            ChannelContext = secureChannelContext;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            (UserIdentity, NotificationAuthorization) = CaptureNotificationContext(session);
            PreferredLocales = session.PreferredLocales;
            DiagnosticsMask = (DiagnosticsMasks)requestHeader.ReturnDiagnostics;
            AuditEntryId = requestHeader.AuditEntryId!;
            ClientTimestamp = requestHeader.Timestamp.IsNull
                ? DateTime.MinValue
                : (DateTime)requestHeader.Timestamp;
            RequestId = Utils.IncrementIdentifier(ref s_lastRequestId);
            RequestType = requestType;
            ClientHandle = requestHeader.RequestHandle;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = requestLifetime ?? RequestLifetime.None;

            if (requestHeader.TimeoutHint > 0)
            {
                OperationDeadline = DateTime.UtcNow.AddMilliseconds(requestHeader.TimeoutHint);
            }
        }

        /// <summary>
        /// Initializes the context with a session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        public OperationContext(ISession session, DiagnosticsMasks diagnosticsMasks)
        {
            ChannelContext = null!;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            (UserIdentity, NotificationAuthorization) = CaptureNotificationContext(session);
            PreferredLocales = session.PreferredLocales;
            DiagnosticsMask = diagnosticsMasks;
            AuditEntryId = null!;
            RequestId = 0;
            RequestType = RequestType.Unknown;
            ClientHandle = 0;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = RequestLifetime.None;
        }

        /// <summary>
        /// Initializes the context with a monitored item.
        /// </summary>
        /// <remarks>
        /// The identity is the Session's <see cref="ISession.EffectiveIdentity"/>, the
        /// one the Roles granted by <see cref="IRoleManager.ResolveGrantedRoles"/> were
        /// layered onto, and not <see cref="ISession.Identity"/>, which is the token as
        /// it arrived. Permission checks made from a monitored item - most visibly the
        /// Part 3 8.55 ReceiveEvents check on an event's EventType and SourceNode - have
        /// to see the Roles the Session actually holds.
        /// </remarks>
        /// <param name="monitoredItem">The monitored item.</param>
        public OperationContext(IMonitoredItem monitoredItem)
        {
            if (monitoredItem == null)
            {
                throw new ArgumentNullException(nameof(monitoredItem));
            }

            ChannelContext = null!;
            UserIdentity = monitoredItem.EffectiveIdentity;
            Session = monitoredItem.Session;

            if (Session != null)
            {
                (UserIdentity, NotificationAuthorization) = CaptureNotificationContext(Session);
                PreferredLocales = Session.PreferredLocales;
            }

            DiagnosticsMask = DiagnosticsMasks.SymbolicId;
            AuditEntryId = null!;
            RequestId = 0;
            RequestType = RequestType.Unknown;
            ClientHandle = 0;
            OperationDeadline = DateTime.MaxValue;
            RequestLifetime = RequestLifetime.None;
        }

        /// <summary>
        /// The context for the secure channel used to send the request.
        /// </summary>
        /// <value>The channel context.</value>
        public SecureChannelContext? ChannelContext { get; }

        /// <summary>
        /// The session associated with the context.
        /// </summary>
        /// <value>The session.</value>
        public ISession Session { get; } = null!;

        /// <summary>
        /// Gets the authorization epoch captured with the request identity.
        /// </summary>
        internal NotificationAuthorization? NotificationAuthorization { get; }

        /// <summary>
        /// Gets the routing view explicitly owned by this operation, if it is dispatching a service.
        /// Internal subscriber contexts without a service scope use live routing.
        /// </summary>
        internal NodeManagerRoutingTable.RoutingSnapshot? RoutingSnapshot { get; private set; }

        /// <summary>
        /// Gets whether this operation currently owns a routing-view pin.
        /// </summary>
        internal bool HasRoutingPin =>
            Volatile.Read(ref m_routingPinState) is
                kRoutingPinActive or kRoutingPinTemporary or kRoutingPinDisposedActive or kRoutingPinCohortOwned;

        /// <summary>
        /// Gets whether this operation's pin protects the currently selected routing view.
        /// </summary>
        internal bool RoutingPinMatchesSnapshot =>
            RoutingSnapshot is { } snapshot &&
            HasRoutingPin &&
            ReferenceEquals(Volatile.Read(ref m_routingEpoch), snapshot.Epoch);

        /// <summary>
        /// The lifetime of the request.
        /// This object is used to track the lifetime of the request and to trigger cancellation if the client aborts the request or if the request times out.
        /// </summary>
        public RequestLifetime RequestLifetime { get; }

        /// <summary>
        /// The security policy used for the secure channel.
        /// </summary>
        /// <value>The security policy URI.</value>
        public string SecurityPolicyUri
        {
            get
            {
                if (ChannelContext != null && ChannelContext.EndpointDescription != null)
                {
                    return ChannelContext.EndpointDescription.SecurityPolicyUri!;
                }

                return null!;
            }
        }

        /// <summary>
        /// The type of request.
        /// </summary>
        /// <value>The type of the request.</value>
        public RequestType RequestType { get; }

        /// <summary>
        /// A unique identifier assigned to the request by the server.
        /// </summary>
        /// <value>The request id.</value>
        public uint RequestId { get; }

        /// <summary>
        /// The handle assigned by the client to the request.
        /// </summary>
        /// <value>The client handle.</value>
        public uint ClientHandle { get; }

        /// <summary>
        /// The time the client sent the request (RequestHeader.Timestamp), or
        /// <see cref="DateTime.MinValue"/> when unknown.
        /// </summary>
        internal DateTime ClientTimestamp { get; }

        /// <summary>
        /// The id of the Session an ActivateSession request is activating, or null.
        /// </summary>
        /// <remarks>
        /// An ActivateSession request runs without a Session context (its Session is not
        /// activated yet, so <see cref="Session"/> and <see cref="SessionId"/> stay null), but a
        /// close of the Session it targets still has to abort it like any other outstanding
        /// request of that Session (OPC 10000-4 5.7.2.1). It is assigned before the request is
        /// registered with the request manager and never changes afterwards.
        /// </remarks>
        internal NodeId ActivationTargetSessionId { get; set; }

        /// <summary>
        /// Whether a CreateSession request's client certificate is kept although an
        /// <see cref="StandardServer.OnApplicationCertificateError"/> override accepted a
        /// validation error for it.
        /// </summary>
        /// <remarks>
        /// Assigned before the session manager creates the Session, so the Session is
        /// marked as not validated (<see cref="ClientCertificateProvenance"/>) before it is
        /// published to other requests, to role evaluation or to a session mirror.
        /// </remarks>
        internal bool ClientCertificateErrorAccepted { get; set; }

        /// <summary>
        /// Updates the status code (thread safe).
        /// </summary>
        /// <param name="statusCode">The status code.</param>
        [Obsolete("Use RequestLifetime.TryCancel instead to update the status code and trigger cancellation if needed.")]
        public void SetStatusCode(StatusCode statusCode)
        {
            RequestLifetime.TryCancel(statusCode);
        }

        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session id.</value>
        public NodeId SessionId
        {
            get
            {
                if (Session != null)
                {
                    return Session.Id;
                }

                return default;
            }
        }

        /// <summary>
        /// The identity context to use when processing the request.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity UserIdentity { get; }

        /// <summary>
        /// The locales to use for the operation.
        /// </summary>
        /// <value>The preferred locales.</value>
        public ArrayOf<string> PreferredLocales { get; }

        /// <summary>
        /// The diagnostics mask specified with the request.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask { get; }

        /// <summary>
        /// A table of diagnostics strings to return in the response.
        /// </summary>
        /// <value>The string table.</value>
        /// <remarks>
        /// This object is thread safe.
        /// </remarks>
        public StringTable StringTable
        {
            get
            {
                StringTable? table = Volatile.Read(ref m_stringTable);
                if (table != null)
                {
                    return table;
                }

                // created on first use: most requests write no diagnostic strings.
                table = new StringTable();
                return Interlocked.CompareExchange(ref m_stringTable, table, null) ?? table;
            }
        }

        /// <summary>
        /// The string table if anything asked for it, else <c>null</c>.
        /// </summary>
        internal StringTable? CreatedStringTable => Volatile.Read(ref m_stringTable);

        /// <summary>
        /// When the request times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline { get; }

        /// <summary>
        /// Gets the token that will be cancelled when the request is aborted.
        /// </summary>
        public System.Threading.CancellationToken CancellationToken => RequestLifetime?.CancellationToken ?? System.Threading.CancellationToken.None;

        /// <summary>
        /// The current status of the request (used to check for timeouts/client cancel requests).
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus => RequestLifetime.StatusCode;

        /// <summary>
        /// The audit log entry id provided by the client which must be included in an audit events generated by the server.
        /// </summary>
        /// <value>The audit entry id.</value>
        public string AuditEntryId { get; } = null!;

        /// <summary>
        /// Reports the request as completed and releases context-owned routing.
        /// Active borrowed service scopes retain their admission until the last scope exits.
        /// Disposing the same context twice is safe; a disposed context cannot capture another view.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable method to dispose of resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    System.Threading.Interlocked.Exchange(ref m_requestScope, null)?.Dispose();
                    System.Threading.Interlocked.Exchange(ref m_requestCompletion, null)?
                        .CompleteAttachedRequest(this);
                }
                finally
                {
                    try
                    {
                        ReleaseRoutingPin();
                    }
                    finally
                    {
                        if (Volatile.Read(ref m_routingCohort) is null)
                        {
                            RoutingSnapshot = null;
                        }
                        System.Threading.Interlocked.Exchange(ref m_sessionlessLease, null)?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Pins the current routing view once for this request before admission or service callbacks run.
        /// Subsequent calls are no-ops and do not replace the view.
        /// </summary>
        /// <param name="routingTable">The routing table providing the current published view.</param>
        internal void PinRouting(NodeManagerRoutingTable routingTable)
        {
            if (routingTable is null)
            {
                throw new ArgumentNullException(nameof(routingTable));
            }
            var spinWait = new SpinWait();
            while (true)
            {
                RoutingCohort? cohort = Volatile.Read(ref m_routingCohort);
                if (cohort is not null)
                {
                    cohort.PinRouting(routingTable);
                    return;
                }
                int state = Volatile.Read(ref m_routingPinState);
                if (state == kRoutingPinReleased)
                {
                    throw new ObjectDisposedException(nameof(OperationContext));
                }
                if (state is kRoutingPinAcquiring or kRoutingPinTemporary or kRoutingPinDisposedActive or
                    kRoutingPinCohortIdle or kRoutingPinCohortOwned)
                {
                    spinWait.SpinOnce();
                    continue;
                }
                if (state == kRoutingPinActive)
                {
                    return;
                }
                if (Interlocked.CompareExchange(
                    ref m_routingPinState,
                    kRoutingPinAcquiring,
                    kRoutingPinUnclaimed) == kRoutingPinUnclaimed)
                {
                    break;
                }
            }

            AcquireRoutingPin(routingTable, kRoutingPinActive);
        }

        /// <summary>
        /// Enters a service view without transferring a borrowed context's pin to its caller.
        /// Overlapping and nested calls share the view until quiescence; continuous overlap can extend it.
        /// </summary>
        /// <param name="routingTable">The routing table providing the published view.</param>
        /// <returns>The scope releasing this service's temporary ownership.</returns>
        internal RoutingScope CaptureRouting(NodeManagerRoutingTable routingTable)
        {
            if (routingTable is null)
            {
                throw new ArgumentNullException(nameof(routingTable));
            }
            var spinWait = new SpinWait();
            while (true)
            {
                RoutingCohort? cohort = Volatile.Read(ref m_routingCohort);
                if (cohort is not null)
                {
                    return cohort.Enter(routingTable);
                }
                int state = Volatile.Read(ref m_routingPinState);
                if (state == kRoutingPinReleased)
                {
                    throw new ObjectDisposedException(nameof(OperationContext));
                }
                if (state is kRoutingPinAcquiring or kRoutingPinTemporary or kRoutingPinDisposedActive or
                    kRoutingPinCohortIdle or kRoutingPinCohortOwned)
                {
                    spinWait.SpinOnce();
                    continue;
                }
                if (state == kRoutingPinActive)
                {
                    return default;
                }
                if (Interlocked.CompareExchange(
                    ref m_routingPinState, kRoutingPinAcquiring, kRoutingPinUnclaimed) != kRoutingPinUnclaimed)
                {
                    continue;
                }
                try
                {
                    return new RoutingCohort(this).Enter(routingTable);
                }
                catch
                {
                    _ = Interlocked.CompareExchange(
                        ref m_routingPinState, kRoutingPinUnclaimed, kRoutingPinAcquiring);
                    throw;
                }
            }
        }

        /// <summary>
        /// Acquires an epoch slot after the caller has reserved pin initialization.
        /// </summary>
        private void AcquireRoutingPin(NodeManagerRoutingTable routingTable, int state)
        {
            AddressSpaceEpoch? epoch = null;
            int slot = -1;
            try
            {
                (NodeManagerRoutingTable.RoutingSnapshot snapshot, AddressSpaceEpoch pinnedEpoch, int pinSlot) =
                    routingTable.PinCurrent();
                epoch = pinnedEpoch;
                slot = pinSlot;
                m_routingEpoch = pinnedEpoch;
                m_routingPinSlot = pinSlot;
                RoutingSnapshot = snapshot;
                Volatile.Write(ref m_routingPinState, state);
            }
            catch
            {
                if (epoch is not null && slot >= 0)
                {
                    epoch.Exit(slot);
                }
                m_routingEpoch = null;
                m_routingPinSlot = -1;
                RoutingSnapshot = null;
                _ = Interlocked.CompareExchange(
                    ref m_routingPinState,
                    kRoutingPinUnclaimed,
                    kRoutingPinAcquiring);
                throw;
            }
        }

        private void ReleaseRoutingPin()
        {
            var spinWait = new SpinWait();
            while (true)
            {
                RoutingCohort? cohort = Volatile.Read(ref m_routingCohort);
                if (cohort is not null)
                {
                    cohort.DisposeContext();
                    return;
                }
                int state = Volatile.Read(ref m_routingPinState);
                if (state == kRoutingPinReleased)
                {
                    return;
                }
                if (state is kRoutingPinAcquiring or kRoutingPinTemporary or kRoutingPinDisposedActive or
                    kRoutingPinCohortIdle or kRoutingPinCohortOwned)
                {
                    spinWait.SpinOnce();
                    continue;
                }
                if (Interlocked.CompareExchange(
                    ref m_routingPinState,
                    kRoutingPinReleased,
                    state) != state)
                {
                    continue;
                }
                if (state == kRoutingPinActive)
                {
                    AddressSpaceEpoch? epoch = m_routingEpoch;
                    int slot = m_routingPinSlot;
                    m_routingEpoch = null;
                    m_routingPinSlot = -1;
                    if (epoch is null || slot < 0)
                    {
                        throw new InvalidOperationException("An active routing pin has no epoch slot.");
                    }
                    epoch.Exit(slot);
                }
                return;
            }
        }

        /// <summary>
        /// Attaches the scope that tracks this request while it executes, so that disposing the
        /// context completes the request.
        /// </summary>
        /// <param name="requestScope">The scope that tracks the request.</param>
        internal void AttachRequestScope(IDisposable requestScope)
        {
            m_requestScope = requestScope;
        }

        /// <summary>
        /// Attaches the request manager that tracks this request while it executes, for a
        /// request that needs no scope object (see
        /// <see cref="RequestManager.AttachRequestScope(OperationContext)"/>), so that disposing
        /// the context completes the request exactly once.
        /// </summary>
        /// <param name="requestManager">The request manager the request was registered with.</param>
        internal void AttachRequestCompletion(RequestManager requestManager)
        {
            m_requestCompletion = requestManager;
        }

        /// <summary>
        /// Attaches the capacity a Session-less request holds in the Session-less request budget, so
        /// that disposing the context returns it however the request ends.
        /// </summary>
        /// <param name="lease">The lease to dispose with the context.</param>
        internal void AttachSessionlessLease(IDisposable lease)
        {
            m_sessionlessLease = lease;
        }

        /// <summary>
        /// Returns a copy of a node manager's system context bound to this operation,
        /// reusing the copy made for the previous call with the same template.
        /// </summary>
        /// <remarks>
        /// Node manager service calls and their per-node callbacks (permission and node
        /// metadata, reference descriptions) otherwise clone the system context once for
        /// every call or node of a request. The copies are equal for one request and
        /// node manager, so one copy is kept per template. A request that touches several
        /// node managers keeps one copy for each of them, so they do not evict each
        /// other.
        /// </remarks>
        /// <param name="template">The node manager's system context.</param>
        internal ServerSystemContext GetSystemContext(ServerSystemContext template)
        {
            // The copy of the first node manager is kept on its own; it knows its template,
            // so a request served by one node manager allocates no cache entry.
            ServerSystemContext? first = m_firstSystemContext;
            if (first != null && ReferenceEquals(first.CopiedFrom, template))
            {
                return first;
            }

            if (first == null)
            {
                ServerSystemContext firstCopy = template.Copy(this);
                firstCopy.CopiedFrom = template;

                // a concurrent caller may have published a copy for another template.
                first = System.Threading.Interlocked.CompareExchange(ref m_firstSystemContext, firstCopy, null);
                if (first == null || ReferenceEquals(first.CopiedFrom, template))
                {
                    return first ?? firstCopy;
                }
            }

            SystemContextCacheEntry? head = m_systemContextCache;
            int count = 0;
            for (SystemContextCacheEntry? entry = head; entry != null; entry = entry.Next)
            {
                if (ReferenceEquals(entry.Template, template))
                {
                    return entry.Copy;
                }

                count++;
            }

            ServerSystemContext copy = template.Copy(this);

            // Entries are immutable and published as one reference. A concurrent caller
            // that loses the race only costs one more copy later. A request touches few
            // node managers; past the bound the list restarts instead of growing.
            m_systemContextCache = new SystemContextCacheEntry(
                template,
                copy,
                count < kMaxSystemContextCacheEntries ? head : null);
            return copy;
        }

        /// <inheritdoc/>
        uint IRequestTableEntry.TableKey => RequestId;

        /// <inheritdoc/>
        ref int IRequestTableEntry.TableSlot => ref m_tableSlot;

        /// <summary>
        /// Captures built-in Session identity and epoch atomically, retaining support for custom sessions.
        /// </summary>
        private static (IUserIdentity Identity, NotificationAuthorization? Authorization) CaptureNotificationContext(
            ISession session)
        {
            return session is Session concreteSession
                ? concreteSession.CaptureNotificationContext()
                : (session.EffectiveIdentity, Opc.Ua.Server.NotificationAuthorization.Capture(session));
        }

        private int m_tableSlot = RequestTable<OperationContext>.kNoSlot;

        /// <summary>
        /// The most node managers after the first whose system context copies a request keeps.
        /// </summary>
        private const int kMaxSystemContextCacheEntries = 8;

        /// <summary>
        /// The system context copy of the first node manager the request called.
        /// </summary>
        private ServerSystemContext? m_firstSystemContext;

        /// <summary>
        /// A system context copy and the template it was made from, published as one
        /// reference so concurrent readers never pair a copy with the wrong template.
        /// </summary>
        private sealed class SystemContextCacheEntry
        {
            public SystemContextCacheEntry(
                ServerSystemContext template,
                ServerSystemContext copy,
                SystemContextCacheEntry? next)
            {
                Template = template;
                Copy = copy;
                Next = next;
            }

            public ServerSystemContext Template { get; }
            public ServerSystemContext Copy { get; }
            public SystemContextCacheEntry? Next { get; }
        }

        /// <summary>
        /// Returns the namespace default permissions remembered for this service
        /// request by <see cref="SetDefaultPermissions"/>.
        /// </summary>
        /// <remarks>
        /// Permission checks read the DefaultAccessRestrictions, DefaultRolePermissions
        /// and DefaultUserRolePermissions of the node's namespace for every node of a
        /// request, which took two server-wide locks and deep-copied the role permission
        /// array per node. One read per namespace and request is enough. Contexts that
        /// are not a service request (monitored item and session contexts, which can be
        /// kept for a long time) remember nothing, so a changed default applies to them
        /// at once.
        /// </remarks>
        internal bool TryGetDefaultPermissions(
            ushort namespaceIndex,
            out NamespaceDefaultPermissions permissions)
        {
            NamespaceDefaultPermissions[]? entries = m_defaultPermissions;
            if (entries != null)
            {
                long generation = NamespaceDefaultPermissionReader.Generation;
                foreach (NamespaceDefaultPermissions entry in entries)
                {
                    if (entry.NamespaceIndex == namespaceIndex && entry.Generation == generation)
                    {
                        permissions = entry;
                        return true;
                    }
                }
            }

            permissions = default;
            return false;
        }

        /// <summary>
        /// Remembers the namespace default permissions for the rest of this service request.
        /// </summary>
        internal void SetDefaultPermissions(NamespaceDefaultPermissions permissions)
        {
            if (RequestType == RequestType.Unknown ||
                m_defaultPermissionCacheDisabled ||
                !StatusCode.IsGood(permissions.Status) ||
                permissions.Generation != NamespaceDefaultPermissionReader.Generation)
            {
                return;
            }

            // copy on write: node managers can validate nodes of one request concurrently,
            // and a lost update only costs a second read.
            NamespaceDefaultPermissions[]? entries = m_defaultPermissions;
            if (entries == null)
            {
                m_defaultPermissions = [permissions];
                return;
            }

            var updated = new NamespaceDefaultPermissions[entries.Length + 1];
            Array.Copy(entries, updated, entries.Length);
            updated[entries.Length] = permissions;
            m_defaultPermissions = updated;
        }

        /// <summary>
        /// Stops this context from remembering namespace defaults. Contexts that outlive a
        /// single service request, such as subscriber contexts created with a request type,
        /// call it so dynamic namespace defaults are read again on every check.
        /// </summary>
        internal void DisableDefaultPermissionCache()
        {
            m_defaultPermissionCacheDisabled = true;
            m_defaultPermissions = null;
        }

        /// <summary>
        /// Releases temporary service ownership without changing the execution context.
        /// </summary>
        internal readonly struct RoutingScope : IDisposable
        {
            /// <summary>
            /// Creates a scope owning one admission to a borrowed context's routing cohort.
            /// </summary>
            internal RoutingScope(IDisposable cohortScope)
            {
                m_cohortScope = cohortScope;
            }

            /// <summary>
            /// Releases temporary ownership exactly once.
            /// </summary>
            public void Dispose()
            {
                m_cohortScope?.Dispose();
            }

            /// <summary>The idempotent admission owned by a borrowed service scope.</summary>
            private readonly IDisposable? m_cohortScope;
        }

        /// <summary>
        /// Serializes borrowed-context ownership without locking ordinary request-owned service dispatch.
        /// </summary>
        private sealed class RoutingCohort
        {
            /// <summary>
            /// Creates the ownership state for a context's first borrowed service call.
            /// </summary>
            public RoutingCohort(OperationContext context)
            {
                m_context = context;
            }

            /// <summary>
            /// Joins the current view or pins a fresh view after the previous cohort drained.
            /// </summary>
            public RoutingScope Enter(NodeManagerRoutingTable routingTable)
            {
                lock (m_lock)
                {
                    ThrowIfDisposed();
                    var scope = new CohortScope(this);
                    if (m_activeScopes == 0 && !m_contextOwned)
                    {
                        m_context.AcquireRoutingPin(routingTable, kRoutingPinTemporary);
                    }
                    m_activeScopes++;
                    Volatile.Write(ref m_context.m_routingCohort, this);
                    return new RoutingScope(scope);
                }
            }

            /// <summary>
            /// Transfers the current view to explicit context ownership without replacing it.
            /// </summary>
            public void PinRouting(NodeManagerRoutingTable routingTable)
            {
                lock (m_lock)
                {
                    ThrowIfDisposed();
                    if (!m_contextOwned)
                    {
                        if (m_activeScopes == 0)
                        {
                            m_context.AcquireRoutingPin(routingTable, kRoutingPinCohortOwned);
                        }
                        m_contextOwned = true;
                        Volatile.Write(ref m_context.m_routingPinState, kRoutingPinCohortOwned);
                    }
                }
            }

            /// <summary>
            /// Rejects new scopes and releases the epoch after any active scopes finish.
            /// </summary>
            public void DisposeContext()
            {
                lock (m_lock)
                {
                    m_disposed = true;
                    m_contextOwned = false;
                    if (m_activeScopes == 0)
                    {
                        ReleasePin();
                    }
                    else
                    {
                        Volatile.Write(ref m_context.m_routingPinState, kRoutingPinDisposedActive);
                    }
                }
            }

            /// <summary>
            /// Releases the last temporary owner without making the reusable context terminal.
            /// </summary>
            private void Exit()
            {
                lock (m_lock)
                {
                    m_activeScopes--;
                    if (m_activeScopes == 0 && !m_contextOwned)
                    {
                        ReleasePin();
                    }
                }
            }

            /// <summary>
            /// Clears the selected view and exits its epoch slot exactly once.
            /// </summary>
            private void ReleasePin()
            {
                AddressSpaceEpoch? epoch = m_context.m_routingEpoch;
                int slot = m_context.m_routingPinSlot;
                m_context.m_routingEpoch = null;
                m_context.m_routingPinSlot = -1;
                m_context.RoutingSnapshot = null;
                Volatile.Write(
                    ref m_context.m_routingPinState, m_disposed ? kRoutingPinReleased : kRoutingPinCohortIdle);
                if (epoch is not null)
                {
                    epoch.Exit(slot);
                }
            }

            /// <summary>
            /// Rejects pin acquisition after context disposal, even while old scopes still own a view.
            /// </summary>
            private void ThrowIfDisposed()
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(OperationContext));
                }
            }

            /// <summary>
            /// Releases one cohort admission once even if a routing scope is copied or disposed twice.
            /// </summary>
            private sealed class CohortScope : IDisposable
            {
                /// <summary>
                /// Creates one service admission.
                /// </summary>
                public CohortScope(RoutingCohort cohort)
                {
                    m_cohort = cohort;
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    Interlocked.Exchange(ref m_cohort, null)?.Exit();
                }

                /// <summary>The cohort awaiting this admission's release.</summary>
                private RoutingCohort? m_cohort;
            }

            /// <summary>The context whose identity and selected view remain unchanged.</summary>
            private readonly OperationContext m_context;

            /// <summary>Serializes acquisition, explicit ownership, and scope release.</summary>
            private readonly System.Threading.Lock m_lock = new();

            /// <summary>The number of service scopes still using the selected view.</summary>
            private int m_activeScopes;

            /// <summary>Whether an explicit pin keeps the view after the last service returns.</summary>
            private bool m_contextOwned;

            /// <summary>Whether disposal forbids new admissions.</summary>
            private bool m_disposed;
        }

        private StringTable? m_stringTable;
        private IDisposable? m_requestScope;
        private RequestManager? m_requestCompletion;
        private IDisposable? m_sessionlessLease;
        private AddressSpaceEpoch? m_routingEpoch;
        private int m_routingPinSlot = -1;
        private int m_routingPinState;
        private RoutingCohort? m_routingCohort;
        private SystemContextCacheEntry? m_systemContextCache;
        private NamespaceDefaultPermissions[]? m_defaultPermissions;
        private bool m_defaultPermissionCacheDisabled;
        private static uint s_lastRequestId;
        private const int kRoutingPinUnclaimed = 0;
        private const int kRoutingPinAcquiring = 1;
        private const int kRoutingPinActive = 2;
        private const int kRoutingPinReleased = 3;
        private const int kRoutingPinTemporary = 4;
        private const int kRoutingPinDisposedActive = 5;
        // Once installed, a cohort never reuses states accepted by stale null-cohort bootstrap paths.
        private const int kRoutingPinCohortIdle = 6;
        private const int kRoutingPinCohortOwned = 7;
    }

    /// <summary>
    /// The default permissions of a namespace, read from its NamespaceMetadata by
    /// <see cref="NamespaceDefaultPermissionReader"/>.
    /// </summary>
    internal readonly struct NamespaceDefaultPermissions
    {
        /// <summary>
        /// Creates a good result of the current generation; a non-default value counts as present.
        /// </summary>
        public NamespaceDefaultPermissions(
            ushort namespaceIndex,
            AccessRestrictionType defaultAccessRestrictions,
            ArrayOf<RolePermissionType> defaultRolePermissions,
            ArrayOf<RolePermissionType> defaultUserRolePermissions)
            : this(
                namespaceIndex,
                defaultAccessRestrictions,
                defaultRolePermissions,
                defaultUserRolePermissions,
                NamespaceDefaultPermissionReader.Generation)
        {
        }

        /// <summary>
        /// Creates a good result of the given generation; a non-default value counts as present.
        /// </summary>
        public NamespaceDefaultPermissions(
            ushort namespaceIndex,
            AccessRestrictionType defaultAccessRestrictions,
            ArrayOf<RolePermissionType> defaultRolePermissions,
            ArrayOf<RolePermissionType> defaultUserRolePermissions,
            long generation)
            : this(
                namespaceIndex,
                defaultAccessRestrictions,
                defaultRolePermissions,
                defaultUserRolePermissions,
                generation,
                StatusCodes.Good,
                defaultAccessRestrictions != AccessRestrictionType.None,
                !defaultRolePermissions.IsNull,
                !defaultUserRolePermissions.IsNull)
        {
        }

        /// <summary>
        /// Creates a result with explicit status and presence.
        /// </summary>
        public NamespaceDefaultPermissions(
            ushort namespaceIndex,
            AccessRestrictionType defaultAccessRestrictions,
            ArrayOf<RolePermissionType> defaultRolePermissions,
            ArrayOf<RolePermissionType> defaultUserRolePermissions,
            long generation,
            StatusCode status,
            bool hasDefaultAccessRestrictions,
            bool hasDefaultRolePermissions,
            bool hasDefaultUserRolePermissions)
        {
            NamespaceIndex = namespaceIndex;
            DefaultAccessRestrictions = defaultAccessRestrictions;
            DefaultRolePermissions = defaultRolePermissions;
            DefaultUserRolePermissions = defaultUserRolePermissions;
            Generation = generation;
            Status = status;
            HasDefaultAccessRestrictions = hasDefaultAccessRestrictions;
            HasDefaultRolePermissions = hasDefaultRolePermissions;
            HasDefaultUserRolePermissions = hasDefaultUserRolePermissions;
        }

        /// <summary>
        /// The namespace index.
        /// </summary>
        public ushort NamespaceIndex { get; }

        /// <summary>
        /// The decoded DefaultAccessRestrictions, None when absent or failed.
        /// </summary>
        public AccessRestrictionType DefaultAccessRestrictions { get; }

        /// <summary>
        /// The DefaultRolePermissions, default when absent or failed.
        /// </summary>
        public ArrayOf<RolePermissionType> DefaultRolePermissions { get; }

        /// <summary>
        /// The DefaultUserRolePermissions, default when absent or failed.
        /// </summary>
        public ArrayOf<RolePermissionType> DefaultUserRolePermissions { get; }

        /// <summary>
        /// The namespace default generation the values were read in.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Good, or the Bad code of a failed read/decode. A Bad status must deny the operation.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// Whether DefaultAccessRestrictions is configured (an explicit zero counts as configured).
        /// </summary>
        public bool HasDefaultAccessRestrictions { get; }

        /// <summary>
        /// Whether DefaultRolePermissions is configured (an explicit empty array counts as configured).
        /// </summary>
        public bool HasDefaultRolePermissions { get; }

        /// <summary>
        /// Whether DefaultUserRolePermissions is configured (an explicit empty array counts as configured).
        /// </summary>
        public bool HasDefaultUserRolePermissions { get; }
    }
}
