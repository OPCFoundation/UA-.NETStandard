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
using Opc.Ua.Di.Server;

namespace Opc.Ua.Machinery.Server
{
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
            lock (m_lock)
            {
                m_resources.Add(lifetime);
            }
            try
            {
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

            /// <inheritdoc/>
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
