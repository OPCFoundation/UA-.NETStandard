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
using System.Threading.Tasks;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Requests device sleep transitions without changing the server host's power state.
    /// </summary>
    public interface IMachineStatusProvider
    {
        /// <summary>
        /// Commits a request to activate or deactivate the machine's sleep mode.
        /// </summary>
        ValueTask SetSleepModeAsync(
            PlasticsRubberRequestContext context,
            bool enabled,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Binds optional sleep methods while the device remains authoritative for its actual mode.
    /// </summary>
    public sealed class MachineStatusBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds the selected generated methods to the device provider.
        /// </summary>
        public MachineStatusBinding(
            MachineStatusState node,
            ISystemContext context,
            IMachineStatusProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_binding = new PlasticsRubberMethodBinding(node, context, accessPolicy);
            if (node.ActivateSleepMode != null)
            {
                m_binding.Bind(node.ActivateSleepMode, (request, ct) =>
                    provider.SetSleepModeAsync(request, true, ct));
            }
            if (node.DeactivateSleepMode != null)
            {
                m_binding.Bind(node.DeactivateSleepMode, (request, ct) =>
                    provider.SetSleepModeAsync(request, false, ct));
            }
        }

        /// <summary>
        /// Gets the generated status instance and its observed device mode.
        /// </summary>
        public MachineStatusState Node { get; }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// The common operation lifetime.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;
    }
}
