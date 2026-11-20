// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server
{
    /// <summary>
    /// Coordinates node-bound Machinery resource ownership with DI owner cleanup.
    /// </summary>
    internal sealed partial class MachineryBuildCoordinator
    {
        /// <summary>
        /// Transfers one machine's resources to both actual node removal and manager shutdown.
        /// Ownership is consumed even when adoption fails, so callers must not release the batch again.
        /// </summary>
        internal async ValueTask AdoptMachineResourcesAsync(
            DiNodeManager manager,
            NodeState root,
            ArrayOf<IAsyncDisposable> resources)
        {
            if (resources.Count == 0)
            {
                return;
            }
            var lifetime = new MachineResourceLifetime(this, resources);
            try
            {
                lock (m_lock)
                {
                    manager.RegisterResource(GetCleanup(manager));
                    m_resources.Add(lifetime);
                }
                manager.RegisterNodeResource(root, lifetime);
                if (!ReferenceEquals(manager.FindPredefinedNode(root.NodeId), root))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown,
                        "The machine was removed during resource adoption.");
                }
            }
            catch (Exception failure)
            {
                try
                {
                    await lifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Machine resource adoption and rollback both failed.", failure, cleanup);
                }
                throw;
            }
        }

        /// <summary>
        /// Shares reverse-order cleanup across concurrent node deletion and manager disposal.
        /// </summary>
        private sealed class MachineResourceLifetime : IAsyncDisposable
        {
            /// <summary>
            /// Takes ownership of the completed registration's resource batch.
            /// </summary>
            internal MachineResourceLifetime(
                MachineryBuildCoordinator coordinator,
                ArrayOf<IAsyncDisposable> resources)
            {
                m_coordinator = coordinator;
                m_resources = resources;
            }

            /// <summary>
            /// Joins one shared cleanup task across node removal and owner shutdown.
            /// </summary>
            public ValueTask DisposeAsync()
            {
                TaskCompletionSource<bool> completion;
                lock (m_gate)
                {
                    if (m_disposal != null)
                    {
                        return new ValueTask(m_disposal);
                    }
                    completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_disposal = completion.Task;
                }
                _ = CompleteDisposalAsync(completion);
                return new ValueTask(completion.Task);
            }

            /// <summary>
            /// Attempts every resource before releasing coordinator ownership and signalling completion.
            /// </summary>
            private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
            {
                var failures = new List<Exception>();
                for (int index = m_resources.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        await m_resources[index].DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        failures.Add(error);
                    }
                }
                m_resources = ArrayOf<IAsyncDisposable>.Empty;
                lock (m_coordinator.m_lock)
                {
                    m_coordinator.m_resources.Remove(this);
                }
                if (failures.Count == 0)
                {
                    completion.TrySetResult(true);
                }
                else
                {
                    completion.TrySetException(failures.Count == 1 ? failures[0] :
                        new AggregateException("Releasing machine resources failed.", failures));
                }
            }

            /// <summary>Owns this batch until all disposal attempts complete.</summary>
            private readonly MachineryBuildCoordinator m_coordinator;

            /// <summary>Protects only the shared cleanup completion, not callbacks.</summary>
            private readonly Lock m_gate = new();

            /// <summary>The batch released newest first.</summary>
            private ArrayOf<IAsyncDisposable> m_resources;

            /// <summary>The single shared asynchronous cleanup.</summary>
            private Task? m_disposal;
        }
    }
}
