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
using System.Linq;
using System.Threading;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        internal bool IsSourceEmissionAllowed(IAsyncNodeManager nodeManager, bool includeCaptured = true)
        {
            lock (m_retiredGenerationNotificationsLock)
            {
                if (includeCaptured && HasCapturedSourceEmission(nodeManager))
                {
                    return true;
                }
                return m_notificationDispatchStates.All(state =>
                    !state.References(nodeManager) || state.BusinessEmissionsEnabled);
            }
        }

        internal bool HasCapturedSourceEmission(IAsyncNodeManager nodeManager)
        {
            return m_currentSourceEmission.Value is { IsActive: true } captured &&
                ReferenceEquals(captured.NodeManager, nodeManager);
        }

        internal static bool TryCaptureSourceEmission(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            out NotificationDispatchLease? emission)
        {
            emission = null;
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }
            lock (master.m_retiredGenerationNotificationsLock)
            {
                // Forwarding a source event through the Server object must retain its original owner.
                if (ReferenceEquals(nodeManager, master.CoreNodeManager) &&
                    master.m_currentSourceEmission.Value is { IsActive: true } captured)
                {
                    nodeManager = captured.NodeManager;
                }
                NotificationDispatchState state = master.GetOrCreateNotificationDispatchState(nodeManager);
                if (!state.BusinessEmissionsEnabled && !master.HasCapturedSourceEmission(nodeManager))
                {
                    return false;
                }
                emission = master.CreateNotificationDispatch(nodeManager, state, null);
                return true;
            }
        }

        internal static bool TryBeginCustomSourceCreation(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            out IDisposable? admission)
        {
            admission = null;
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }
            lock (master.m_retiredGenerationNotificationsLock)
            {
                NotificationDispatchState state = master.GetOrCreateNotificationDispatchState(nodeManager);
                if (state.EmissionCutoffReservations != 0 || !state.BusinessEmissionsEnabled)
                {
                    return false;
                }
                state.CustomSourceCreations++;
                admission = new CustomSourceCreation(
                    master, master.CreateNotificationDispatch(nodeManager, state, null));
                return true;
            }
        }

        private SourceEmissionScope EnterSourceEmission(NotificationDispatchLease emission)
        {
            NotificationDispatchLease? previous = m_currentSourceEmission.Value;
            m_currentSourceEmission.Value = emission;
            return new SourceEmissionScope(this, previous);
        }

        private PreparedSourceEmissionCutoff PrepareSourceEmissionCutoff(
            ArrayOf<IAsyncNodeManager> nodeManagers)
        {
            if (nodeManagers.Count > 0)
            {
                if (CoreNodeManager is not Opc.Ua.Server.CoreNodeManager)
                {
                    throw new NotSupportedException(
                        "The configured Core provider cannot service immediately retired monitored items.");
                }
                foreach (ISubscription subscription in Server.SubscriptionManager.GetSubscriptions())
                {
                    if (subscription.MonitoredItemCount > 0 && subscription is not ISubscriptionMonitoredItemLifecycle)
                    {
                        throw new NotSupportedException(
                            "The configured subscription cannot retain immediately retired monitored-item identities.");
                    }
                }
            }
            var states = new NotificationDispatchState[nodeManagers.Count];
            lock (m_retiredGenerationNotificationsLock)
            {
                for (int index = 0; index < nodeManagers.Count; index++)
                {
                    IAsyncNodeManager manager = nodeManagers[index];
                    if (manager is not AsyncCustomNodeManager { SupportsSourceEmissionCutoff: true })
                    {
                        throw new NotSupportedException(
                            "Immediate batch retirement requires the stock source and monitored-item emission paths.");
                    }
                    NotificationDispatchState state = GetOrCreateNotificationDispatchState(manager);
                    if (state.CustomSourceCreations != 0)
                    {
                        throw new NotSupportedException(
                            "A custom source factory is still establishing its emission ownership.");
                    }
                    states[index] = state;
                }
                foreach (NotificationDispatchState state in states)
                {
                    state.EmissionCutoffReservations++;
                }
            }
            return new PreparedSourceEmissionCutoff(this, states);
        }

        private readonly AsyncLocal<NotificationDispatchLease?> m_currentSourceEmission = new();

        internal readonly struct SourceEmissionScope(
            MasterNodeManager owner,
            NotificationDispatchLease? previous) : IDisposable
        {
            public void Dispose()
            {
                owner.m_currentSourceEmission.Value = previous;
            }
        }

        private sealed class CustomSourceCreation : IDisposable
        {
            public CustomSourceCreation(MasterNodeManager owner, NotificationDispatchLease dispatch)
            {
                m_owner = owner;
                m_dispatch = dispatch;
            }

            public void Dispose()
            {
                MasterNodeManager? current = Interlocked.Exchange(ref m_owner, null);
                if (current is not null)
                {
                    lock (current.m_retiredGenerationNotificationsLock)
                    {
                        m_dispatch.DispatchState.CustomSourceCreations--;
                    }
                    m_dispatch.Dispose();
                }
            }

            private MasterNodeManager? m_owner;
            private readonly NotificationDispatchLease m_dispatch;
        }

        private sealed class PreparedSourceEmissionCutoff(
            MasterNodeManager owner,
            NotificationDispatchState[] states) : IDisposable
        {
            public void Publish(NodeManagerRoutingTable.PreparedRoutes routes)
            {
                // Source admission observes the same switch, never a per-manager drain or callback.
                lock (owner.m_retiredGenerationNotificationsLock)
                {
                    routes.Publish();
                    foreach (NotificationDispatchState state in states)
                    {
                        state.BusinessEmissionsEnabled = false;
                    }
                }
            }

            public void Dispose()
            {
                lock (owner.m_retiredGenerationNotificationsLock)
                {
                    foreach (NotificationDispatchState state in states)
                    {
                        state.EmissionCutoffReservations--;
                    }
                }
            }
        }
    }
}
