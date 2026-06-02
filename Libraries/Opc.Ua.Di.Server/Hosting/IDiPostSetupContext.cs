/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Context handed to every
    /// <see cref="IDiPostSetupConfigurator"/> delegate that runs after a
    /// DI node manager's address space is initialised. Exposes the
    /// active <see cref="DiNodeManager"/> and convenience pass-throughs
    /// for the most common
    /// <see cref="IDeviceBuilder{TDevice}"/> entry points so
    /// configurators don't have to re-reach into the manager surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Configurators are <em>fail-fast</em>: any exception aborts the
    /// hosted server startup with a diagnostic that identifies the
    /// failing configurator. Do not catch and swallow exceptions inside
    /// the delegate unless you intentionally want partial setup to be
    /// considered successful.
    /// </para>
    /// <para>
    /// The context intentionally exposes a narrow
    /// <see cref="GetRequiredService{T}"/> helper rather than a raw
    /// <see cref="System.IServiceProvider"/> to discourage service-
    /// locator usage and lifetime-scoped traps.
    /// </para>
    /// </remarks>
    public interface IDiPostSetupContext
    {
        /// <summary>
        /// The fully initialised DI node manager. Configurators may
        /// create new devices, wire references, attach simulation
        /// loops, or otherwise mutate the address space — the manager's
        /// <c>PredefinedNodes</c> dictionary is already populated and
        /// the type tree resolved.
        /// </summary>
        DiNodeManager Manager { get; }

        /// <summary>
        /// Cancellation token from the hosting layer. Honour this when
        /// performing async work; the hosted service uses it to cancel
        /// startup on shutdown.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required application service. Equivalent to
        /// <c>IServiceProvider.GetRequiredService&lt;T&gt;()</c>, but
        /// scoped to the OPC UA hosting graph and intentionally narrow
        /// to prevent ad-hoc service-locator anti-patterns.
        /// </summary>
        /// <typeparam name="T">Service contract to resolve.</typeparam>
        T GetRequiredService<T>() where T : notnull;

        /// <summary>
        /// Convenience pass-through to
        /// <see cref="DiNodeManager.CreateDeviceAsync(QualifiedName, NodeState?, CancellationToken)"/>.
        /// </summary>
        ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceAsync(
            QualifiedName browseName,
            NodeState? parent = null);

        /// <summary>
        /// Convenience pass-through to the generic
        /// <see cref="DiNodeManager.CreateDeviceAsync{TDevice}(QualifiedName, NodeId, Func{NodeState, TDevice}, NodeState?, CancellationToken)"/>.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        ValueTask<IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(
            QualifiedName browseName,
            NodeId typeDefinitionId,
            Func<NodeState, TDevice> factory,
            NodeState? parent = null)
            where TDevice : ComponentState;

        /// <summary>
        /// Resolves an existing device by NodeId and returns its
        /// fluent builder for further configuration.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId)
            where TDevice : ComponentState;

        /// <summary>
        /// Resolves an existing device by browse name (under the
        /// manager's default parent) and returns its fluent builder.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TDevice : ComponentState;
    }
}
