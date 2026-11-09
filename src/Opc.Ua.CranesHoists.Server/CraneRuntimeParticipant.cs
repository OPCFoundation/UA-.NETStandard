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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Validates equipment data before publication and advertises only committed crane systems.
    /// </summary>
    internal sealed class CraneRuntimeParticipant : ICompanionRegistrationParticipant
    {
        /// <summary>
        /// Enlists one crane runtime in its existing owner's registration.
        /// </summary>
        internal CraneRuntimeParticipant(CranesHoistsBuildContext context, CraneSystemHandle handle)
        {
            m_context = context;
            m_handle = handle;
        }

        /// <inheritdoc/>
        public DiNodeManager Manager => m_context.Robotics.Manager;

        /// <inheritdoc/>
        public BaseObjectState Root => m_handle.State;

        /// <inheritdoc/>
        public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
        {
            return m_handle.RefreshAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask BindAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        /// <inheritdoc/>
        public void Commit()
        {
            m_committed = true;
            m_context.Commit(m_handle);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_committed ? default : m_handle.DisposeAsync();
        }

        private readonly CranesHoistsBuildContext m_context;
        private readonly CraneSystemHandle m_handle;
        private bool m_committed;
    }
}
