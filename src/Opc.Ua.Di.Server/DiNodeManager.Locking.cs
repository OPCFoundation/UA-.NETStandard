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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Server
{
    public partial class DiNodeManager
    {
        /// <summary>
        /// Binds an element's DI Lock and admits UA writes and calls throughout its owned subtree
        /// through the supplied activity service. Reads and browsing remain available. Direct
        /// application handles are trusted and do not pass through UA request admission.
        /// </summary>
        /// <param name="element">The element with a materialized Lock child.</param>
        /// <param name="service">The existing lock service with request-activity support.</param>
        /// <param name="cancellationToken">The registration cancellation token.</param>
        /// <returns>A registration owned until the element's cleanup has finished.</returns>
        public async ValueTask<IDisposable> RegisterLockableElementAsync(
            TopologyElementState element,
            ILockActivityService service,
            CancellationToken cancellationToken = default)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }
            if (element.NodeId.IsNull || element.Lock == null ||
                service.LockDuration <= TimeSpan.Zero)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            await m_lockConfiguration.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (m_lockRegistrationsGate)
                {
                    if (m_lockRegistrations.ContainsKey(element.NodeId))
                    {
                        throw new InvalidOperationException("The DI element already has lock admission.");
                    }
                }
                await EnsureLockTimeoutAsync(service.LockDuration, cancellationToken).ConfigureAwait(false);
                element.Lock.BindToLockService(element.NodeId, service);
                if (element.Lock.BreakLock is { } breakLock && breakLock.RolePermissions.Count == 0)
                {
                    breakLock.RolePermissions =
                    [
                        new RolePermissionType
                        {
                            RoleId = global::Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                        },
                        new RolePermissionType
                        {
                            RoleId = global::Opc.Ua.ObjectIds.WellKnownRole_ConfigureAdmin,
                            Permissions = (uint)(PermissionType.Browse | PermissionType.Call)
                        }
                    ];
                }
                var registration = new LockRegistration(this, element, service);
                lock (m_lockRegistrationsGate)
                {
                    m_lockRegistrations.Add(element.NodeId, registration);
                }
                return registration;
            }
            finally
            {
                m_lockConfiguration.Release();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask ReadAsync(
            OperationContext context,
            double maxAge,
            ArrayOf<ReadValueId> nodesToRead,
            IList<DataValue> values,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            var activities = new List<IDisposable>();
            try
            {
                ServerSystemContext systemContext = SystemContext.Copy(context);
                foreach (ReadValueId request in nodesToRead)
                {
                    if (!request.Processed && FindPredefinedNode<NodeState>(request.NodeId) is { } node)
                    {
                        EnterLockActivities(node, systemContext, activities,
                            exemptLockMethods: false, modifying: false);
                    }
                }
                await base.ReadAsync(context, maxAge, nodesToRead, values, errors, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ReleaseLockActivities(activities);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask WriteAsync(
            OperationContext context,
            ArrayOf<WriteValue> nodesToWrite,
            IList<ServiceResult> errors,
            CancellationToken cancellationToken = default)
        {
            var activities = new List<IDisposable>();
            try
            {
                ServerSystemContext systemContext = SystemContext.Copy(context);
                for (int ii = 0; ii < nodesToWrite.Count; ii++)
                {
                    WriteValue request = nodesToWrite[ii];
                    if (request.Processed)
                    {
                        continue;
                    }
                    NodeState? node = FindPredefinedNode<NodeState>(request.NodeId);
                    if (node == null)
                    {
                        continue;
                    }
                    try
                    {
                        EnterLockActivities(node, systemContext, activities, exemptLockMethods: false);
                    }
                    catch (ServiceResultException failure)
                    {
                        request.Processed = true;
                        errors[ii] = failure.Result;
                    }
                }
                await base.WriteAsync(context, nodesToWrite, errors, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ReleaseLockActivities(activities);
            }
        }

        /// <inheritdoc/>
        protected override IDisposable? BeginBrowseActivity(ISystemContext context, NodeState source)
        {
            var activities = new List<IDisposable>();
            try
            {
                EnterLockActivities(source, context, activities, exemptLockMethods: false, modifying: false);
                return activities.Count == 0 ? null : new BrowseActivity(activities);
            }
            catch
            {
                ReleaseLockActivities(activities);
                throw;
            }
        }

        /// <inheritdoc/>
        protected override async ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            CallMethodRequest methodToCall,
            MethodState method,
            CallMethodResult result,
            CancellationToken cancellationToken = default)
        {
            var activities = new List<IDisposable>();
            try
            {
                EnterLockActivities(method, context, activities, exemptLockMethods: true);
                return await base.CallAsync(context, methodToCall, method, result, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException failure)
            {
                result.StatusCode = failure.StatusCode;
                return failure.Result;
            }
            finally
            {
                ReleaseLockActivities(activities);
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    List<IDisposable> registrations;
                    lock (m_lockRegistrationsGate)
                    {
                        registrations = [.. m_lockRegistrations.Values];
                    }
                    try
                    {
                        ReleaseLockActivities(registrations);
                    }
                    finally
                    {
                        m_lockConfiguration.Dispose();
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Creates the server-wide DI inactivity property, rejecting conflicting per-element timeouts.
        /// </summary>
        private async ValueTask EnsureLockTimeoutAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            ServerCapabilitiesState capabilities = Server.DiagnosticsNodeManager
                .FindPredefinedNode<ServerCapabilitiesState>(global::Opc.Ua.ObjectIds.Server_ServerCapabilities) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            var name = new QualifiedName("MaxInactiveLockTime", Server.NamespaceUris.GetIndexOrAppend(DiNamespaceUri));
            NodeState? existing = FindPredefinedNode<NodeState>(NodeId.Create(
                Opc.Ua.Di.Variables.MaxInactiveLockTime, DiNamespaceUri, Server.NamespaceUris)) ??
                capabilities.FindChild(SystemContext, name);
            if (existing != null)
            {
                if (existing is not BaseVariableState property ||
                    (!property.Value.IsNull &&
                        (!property.Value.TryGetValue(out double configuredDuration) ||
                            (configuredDuration > 0 && configuredDuration != duration.TotalMilliseconds))))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "All DI locks must use the advertised inactivity timeout.");
                }
                property.Value = Variant.From(duration.TotalMilliseconds);
                return;
            }
            var timeout = new PropertyState(capabilities)
            {
                NodeId = new NodeId("DI.MaxInactiveLockTime", InstanceNamespaceIndex),
                BrowseName = name,
                DisplayName = new LocalizedText(name.Name),
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasProperty,
                TypeDefinitionId = Opc.Ua.Types.VariableTypeIds.PropertyType,
                DataType = Opc.Ua.Types.DataTypeIds.Duration,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.From(duration.TotalMilliseconds),
                StatusCode = StatusCodes.Good
            };
            await AddPredefinedNodeAsync(SystemContext, timeout, cancellationToken).ConfigureAwait(false);
            capabilities.AddChild(timeout);
        }

        /// <summary>
        /// Checks every registered ancestor, including nodes added after device publication.
        /// </summary>
        private void EnterLockActivities(
            NodeState node,
            ISystemContext context,
            List<IDisposable> activities,
            bool exemptLockMethods,
            bool modifying = true)
        {
            if (ReferenceEquals(context, SystemContext))
            {
                return;
            }
            NodeState? current = node;
            LockingServicesState? lockMethods = null;
            while (current != null)
            {
                if (exemptLockMethods && current is LockingServicesState methods)
                {
                    lockMethods = methods;
                }
                LockRegistration? registration;
                lock (m_lockRegistrationsGate)
                {
                    m_lockRegistrations.TryGetValue(current.NodeId, out registration);
                }
                if (registration != null)
                {
                    registration.AttachSessionManager();
                    if (!ReferenceEquals(registration.Element.Lock, lockMethods))
                    {
                        IDisposable? activity = registration.Service.EnterActivity(
                            context, registration.Element.NodeId, modifying);
                        if (activity != null)
                        {
                            activities.Add(activity);
                        }
                    }
                }
                current = (current as BaseInstanceState)?.Parent;
            }
        }

        /// <summary>
        /// Completes request activity in reverse admission order.
        /// </summary>
        private static void ReleaseLockActivities(List<IDisposable> activities)
        {
            List<Exception>? errors = null;
            for (int ii = activities.Count - 1; ii >= 0; ii--)
            {
                try
                {
                    activities[ii].Dispose();
                }
                catch (Exception error)
                {
                    (errors ??= []).Add(error);
                }
            }
            if (errors != null)
            {
                throw new AggregateException(errors);
            }
        }

        /// <summary>Serializes configuration, including the shared inactivity property.</summary>
        private readonly SemaphoreSlim m_lockConfiguration = new(1, 1);

        /// <summary>Protects registration lookup without invoking provider callbacks.</summary>
        private readonly Lock m_lockRegistrationsGate = new();

        /// <summary>The registered topology roots, independent of subtree publication time.</summary>
        private readonly Dictionary<NodeId, LockRegistration> m_lockRegistrations = [];

        /// <summary>
        /// Retains owner browse activity until reference traversal and browser cleanup finish.
        /// </summary>
        private sealed class BrowseActivity(List<IDisposable> activities) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                ReleaseLockActivities(activities);
            }
        }

        /// <summary>
        /// Owns the root's session-close subscription and lock-admission registration.
        /// </summary>
        private sealed class LockRegistration : IDisposable
        {
            /// <summary>
            /// Subscribes to session closure for this element.
            /// </summary>
            public LockRegistration(DiNodeManager owner, TopologyElementState element, ILockActivityService service)
            {
                m_owner = owner;
                Element = element;
                Service = service;
                AttachSessionManager();
            }

            /// <summary>
            /// Gets the protected element.
            /// </summary>
            public TopologyElementState Element { get; }

            /// <summary>
            /// Gets the shared lock and activity authority.
            /// </summary>
            public ILockActivityService Service { get; }

            /// <summary>
            /// Attaches once the server has created its session manager, before the first lock request.
            /// </summary>
            public void AttachSessionManager()
            {
                DiNodeManager? owner = Volatile.Read(ref m_owner);
                if (owner == null)
                {
                    return;
                }
                lock (owner.m_lockRegistrationsGate)
                {
                    if (m_owner != null && m_sessions == null && owner.Server.SessionManager is { } sessions)
                    {
                        m_sessions = sessions;
                        sessions.SessionClosing += OnSessionClosing;
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                DiNodeManager? owner = Volatile.Read(ref m_owner);
                if (owner == null)
                {
                    return;
                }
                Service.BreakLock(owner.SystemContext, Element.NodeId);
                if (ReferenceEquals(Interlocked.CompareExchange(ref m_owner, null, owner), owner))
                {
                    lock (owner.m_lockRegistrationsGate)
                    {
                        if (m_sessions != null)
                        {
                            m_sessions.SessionClosing -= OnSessionClosing;
                            m_sessions = null;
                        }
                        owner.m_lockRegistrations.Remove(Element.NodeId);
                    }
                }
            }

            /// <summary>
            /// Releases the closing session after any admitted activity drains.
            /// </summary>
            private void OnSessionClosing(ISession session, SessionEventReason reason)
            {
                Service.ReleaseSession(Element.NodeId, session.Id);
            }

            /// <summary>The owner, cleared on withdrawal.</summary>
            private DiNodeManager? m_owner;

            /// <summary>The session lifetime source retained by this registration.</summary>
            private ISessionManager? m_sessions;
        }
    }
}
