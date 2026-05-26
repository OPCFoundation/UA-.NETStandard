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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Fluent configurator for a DI device (any subclass of
    /// <see cref="ComponentState"/>).
    /// </summary>
    /// <typeparam name="TDevice">
    /// Concrete device state class. The base constraint is
    /// <see cref="ComponentState"/> because the DI nameplate properties
    /// (used by <see cref="WithIdentification"/>) live on
    /// <see cref="ComponentState"/>. <see cref="DeviceState"/>-specific
    /// operations (e.g. <c>WithDeviceHealth</c>) are exposed via
    /// extension methods constrained to <c>TDevice : DeviceState</c>.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// Devices created through this builder are fully registered with the
    /// owning <see cref="DiNodeManager"/> (i.e. recursively passed through
    /// <c>AsyncCustomNodeManager.AddPredefinedNodeAsync</c>), so direct
    /// NodeId lookup, subscription wiring, and the standard event
    /// pipeline all work out of the box.
    /// </para>
    /// <para>
    /// All <c>WithXxx</c> methods return <see langword="this"/> so the
    /// builder composes into a single chain.
    /// </para>
    /// </remarks>
    public interface IDeviceBuilder<TDevice>
        where TDevice : ComponentState
    {
        /// <summary>The state instance being configured.</summary>
        TDevice Device { get; }

        /// <summary>Manager that owns the device's predefined-node registration.</summary>
        DiNodeManager Manager { get; }

        /// <summary>The system context for property/reference resolution.</summary>
        ISystemContext Context { get; }

        /// <summary>
        /// A typed <see cref="INodeBuilder{TState}"/> view of the device
        /// for composition with the rest of the fluent surface
        /// (e.g. <c>WithProperty</c>, <c>CreateLimitAlarm</c>,
        /// <c>Variable&lt;double&gt;(...)</c>).
        /// </summary>
        INodeBuilder<TDevice> Node { get; }

        /// <summary>
        /// Populates the standard DI nameplate properties
        /// (Manufacturer, Model, SerialNumber, ...) on the underlying
        /// device. Only members set inside the action are written, so
        /// pre-existing values are preserved.
        /// </summary>
        IDeviceBuilder<TDevice> WithIdentification(
            Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Configures the device's <c>Identification</c> functional
        /// group. The group itself is created on first call; subsequent
        /// calls re-configure the same group.
        /// </summary>
        IDeviceBuilder<TDevice> WithIdentificationGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>Configuration</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithConfigurationGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>Maintenance</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithMaintenanceGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>Diagnostics</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithDiagnosticsGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>Status</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithStatusGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>Operational</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithOperationalGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>Statistics</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithStatisticsGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>Configures the <c>OperationCounters</c> functional group.</summary>
        IDeviceBuilder<TDevice> WithOperationCountersGroup(
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>
        /// Adds (or reconfigures) a functional group with an
        /// application-defined <paramref name="name"/>. Use this for
        /// groups that fall outside the eight well-known DI names.
        /// </summary>
        IDeviceBuilder<TDevice> WithFunctionalGroup(
            QualifiedName name,
            Action<IFunctionalGroupBuilder> configure);

        /// <summary>
        /// Adds a <c>ConnectsTo</c> reference from the device to another
        /// topology element (e.g. an upstream/downstream device).
        /// </summary>
        IDeviceBuilder<TDevice> ConnectsTo(NodeId other);

        /// <summary>
        /// Adds a <c>ConnectsTo</c> reference from
        /// <paramref name="parent"/> to the device (inverse semantics
        /// — for declaring the parent that contains this device).
        /// </summary>
        IDeviceBuilder<TDevice> ConnectsToParent(NodeId parent);

        /// <summary>
        /// Escape hatch — invokes <paramref name="configure"/> with the
        /// raw <typeparamref name="TDevice"/> state and the system
        /// context for low-level operations not exposed via the fluent
        /// surface.
        /// </summary>
        IDeviceBuilder<TDevice> Configure(Action<TDevice, ISystemContext> configure);
    }
}
