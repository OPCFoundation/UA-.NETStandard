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
using System.Threading;

namespace Opc.Ua.Server
{
    public partial class MasterNodeManager
    {
        /// <summary>
        /// Determines whether a source generation may currently emit notifications.
        /// </summary>
        /// <param name="nodeManager">The source node manager.</param>
        /// <param name="includeCaptured">Whether an already admitted source emission is allowed.</param>
        /// <returns><c>true</c> when the source generation is live or already admitted.</returns>
        internal bool IsSourceEmissionAllowed(IAsyncNodeManager nodeManager, bool includeCaptured = true)
        {
            if (includeCaptured && HasCapturedSourceEmission(nodeManager))
            {
                return true;
            }
            return m_nodeManagers.Revision.TryGetGeneration(
                    nodeManager,
                    out NodeManagerGeneration? generation) &&
                generation!.Gate.State == NodeManagerGenerationState.Live;
        }

        /// <summary>
        /// Determines whether this execution context holds an emission for the given generation.
        /// </summary>
        /// <param name="nodeManager">The source node manager.</param>
        /// <returns><c>true</c> when the source emission was admitted before retirement.</returns>
        internal bool HasCapturedSourceEmission(IAsyncNodeManager nodeManager)
        {
            return m_currentSourceEmission.Value is { } captured &&
                ReferenceEquals(captured.NodeManager, nodeManager);
        }

        /// <summary>
        /// Enters the source generation gate for one notification emission.
        /// </summary>
        /// <param name="server">The server internal.</param>
        /// <param name="nodeManager">The source node manager.</param>
        /// <param name="emission">The generation gate admission, when admitted.</param>
        /// <param name="generationReference">The exact bound generation for an item emission, if known.</param>
        /// <param name="isGlobalServerEventSink">
        /// Whether this is the Server object's event callback, which receives events from every node manager.
        /// </param>
        /// <returns><c>true</c> when the generation accepted the emission.</returns>
        internal static bool TryCaptureSourceEmission(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            out SourceEmissionAdmission? emission,
            NodeManagerGenerationReference? generationReference = null,
            bool isGlobalServerEventSink = false)
        {
            emission = null;
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }

            NodeManagerGeneration generation;
            if ((isGlobalServerEventSink ||
                ReferenceEquals(nodeManager, master.CoreNodeManager)) &&
                master.m_currentSourceEmission.Value is { } captured)
            {
                generation = captured;
            }
            else if (generationReference is not null)
            {
                if (master.m_currentSourceEmission.Value is { } capturedSourceEmission &&
                    !generationReference.Identifies(capturedSourceEmission))
                {
                    return false;
                }
                if (!generationReference.TryGetGeneration(out NodeManagerGeneration? boundGeneration) ||
                    boundGeneration is null ||
                    !ReferenceEquals(boundGeneration.NodeManager, nodeManager))
                {
                    return false;
                }
                generation = boundGeneration;
            }
            else
            {
                if (!master.m_nodeManagers.Revision.TryGetGeneration(
                        nodeManager,
                        out NodeManagerGeneration? currentGeneration))
                {
                    return false;
                }
                generation = currentGeneration!;
            }
            if (!generation.Gate.TryEnter(out int slot))
            {
                return false;
            }

            emission = new SourceEmissionAdmission(master, generation, slot);
            return true;
        }

        /// <summary>
        /// Admits creation of a custom source while its generation remains live.
        /// </summary>
        /// <param name="server">The server internal.</param>
        /// <param name="nodeManager">The source node manager.</param>
        /// <param name="admission">The generation lease, or null when no master gate applies.</param>
        /// <returns><c>true</c> when creation was admitted.</returns>
        internal static bool TryBeginCustomSourceCreation(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            out SourceEmissionAdmission? admission)
        {
            admission = null;
            if (server.NodeManager is not MasterNodeManager master)
            {
                return true;
            }

            if (!master.m_nodeManagers.Revision.TryGetGeneration(
                    nodeManager,
                    out NodeManagerGeneration? generation))
            {
                return false;
            }
            NodeManagerGeneration currentGeneration = generation!;
            if (!currentGeneration.Gate.TryEnter(out int slot))
            {
                return false;
            }
            admission = new SourceEmissionAdmission(master, currentGeneration, slot);
            return true;
        }

        private SourceEmissionScope EnterSourceEmission(NodeManagerGeneration generation)
        {
            NodeManagerGeneration? previous = m_currentSourceEmission.Value;
            m_currentSourceEmission.Value = generation;
            return new SourceEmissionScope(this, previous);
        }

        private readonly AsyncLocal<NodeManagerGeneration?> m_currentSourceEmission = new();

        /// <summary>
        /// Owns one source-emission generation gate admission without allocating a dispatch lease.
        /// This value has single-owner semantics and must be disposed exactly once.
        /// </summary>
        internal readonly struct SourceEmissionAdmission : IDisposable
        {
            /// <summary>
            /// Initializes an admitted source emission.
            /// </summary>
            internal SourceEmissionAdmission(
                MasterNodeManager owner,
                NodeManagerGeneration generation,
                int slot)
            {
                m_owner = owner;
                Generation = generation;
                m_slot = slot;
            }

            /// <summary>
            /// Gets the exact generation admitted by the source emission.
            /// </summary>
            internal NodeManagerGeneration Generation { get; }

            /// <summary>
            /// Enters the source-emission scope for this generation.
            /// </summary>
            internal SourceEmissionScope EnterSourceEmission()
            {
                return m_owner.EnterSourceEmission(Generation);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Generation.Gate.Exit(m_slot);
            }

            private readonly MasterNodeManager m_owner;
            private readonly int m_slot;
        }

        /// <summary>
        /// Restores the previous admitted source emission when disposed.
        /// </summary>
        /// <param name="owner">The master node manager.</param>
        /// <param name="previous">The previous source emission.</param>
        internal readonly struct SourceEmissionScope(
            MasterNodeManager owner,
            NodeManagerGeneration? previous) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                owner.m_currentSourceEmission.Value = previous;
            }
        }
    }
}
