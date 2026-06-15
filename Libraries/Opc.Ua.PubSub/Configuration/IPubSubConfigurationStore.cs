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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Async store for the PubSub configuration document. Backed by
    /// a file, an address-space resource, an in-memory snapshot or
    /// a remote configuration source. Notifies subscribers when the
    /// configuration changes so the runtime can apply the delta.
    /// </summary>
    /// <remarks>
    /// Implements the configuration-storage contract derived from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6">
    /// Part 14 §9.1.6 PubSub configuration model</see>. The default
    /// file-backed implementation ships in Phase 4; Phase 1 only
    /// commits the contract.
    /// </remarks>
    public interface IPubSubConfigurationStore
    {
        /// <summary>
        /// Raised whenever the persisted configuration changes.
        /// </summary>
        event EventHandler<PubSubConfigurationChangedEventArgs>? Changed;

        /// <summary>
        /// Loads the current configuration.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<PubSubConfigurationDataType> LoadAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists <paramref name="configuration"/>. Raises
        /// <see cref="Changed"/> on success.
        /// </summary>
        /// <param name="configuration">Configuration to save.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask SaveAsync(
            PubSubConfigurationDataType configuration,
            CancellationToken cancellationToken = default);
    }
}
