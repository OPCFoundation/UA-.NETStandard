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

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Default <see cref="IPubSubActivationCoordinator"/> that reports every
    /// component <see cref="PubSubComponentRole.Active"/>.
    /// </summary>
    /// <remarks>
    /// Registered by default so non-redundant deployments behave exactly as
    /// before: no component is ever placed on standby and
    /// <see cref="RoleChanged"/> never fires. Redundant deployments replace
    /// this with a shared-store-backed coordinator.
    /// </remarks>
    public sealed class AlwaysActiveCoordinator : IPubSubActivationCoordinator
    {
        /// <summary>
        /// A shared stateless instance.
        /// </summary>
        public static AlwaysActiveCoordinator Instance { get; } = new();

        /// <inheritdoc/>
        public event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubComponentRole> GetRoleAsync(
            string componentId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<PubSubComponentRole>(PubSubComponentRole.Active);
        }
    }
}
