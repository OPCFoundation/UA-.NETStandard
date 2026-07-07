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
    /// Coordinates which PubSub components are active on this instance in a
    /// redundant (high-availability) deployment, per OPC UA Part 14 §9.1.6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A coordinator answers, per component, whether this instance should be
    /// <see cref="PubSubComponentRole.Active"/> (drive the transport) or
    /// <see cref="PubSubComponentRole.Standby"/> (wait to take over), and
    /// raises <see cref="RoleChanged"/> when that decision changes so the
    /// runtime can pause or resume the component.
    /// </para>
    /// <para>
    /// The default registration is <see cref="AlwaysActiveCoordinator"/>,
    /// which reports every component active so non-redundant deployments are
    /// unaffected. Redundant deployments register a shared-store-backed
    /// coordinator (for example a lease-based one) via dependency injection.
    /// Implementations are a provider extension point and must be injectable.
    /// </para>
    /// </remarks>
    public interface IPubSubActivationCoordinator
    {
        /// <summary>
        /// Starts coordination (for example begins acquiring and renewing
        /// leadership leases). Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops coordination and relinquishes any acquired leadership.
        /// Idempotent.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the current role of the identified component on this
        /// instance.
        /// </summary>
        /// <param name="componentId">
        /// Deterministic component identifier (for example
        /// <c>pubsub:writergroup:WriterGroup1</c>).
        /// </param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The component's current role.</returns>
        ValueTask<PubSubComponentRole> GetRoleAsync(
            string componentId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Raised whenever the role of a component changes on this instance.
        /// </summary>
        event EventHandler<PubSubRoleChangedEventArgs>? RoleChanged;
    }
}
