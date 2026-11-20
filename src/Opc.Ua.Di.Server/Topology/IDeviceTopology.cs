/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Di.Server.Topology
{
    /// <summary>
    /// Convenience accessor over the well-known DI topology containers
    /// (<c>DeviceSet</c>, <c>DeviceTopology</c>, and <c>NetworkSet</c>).
    /// </summary>
    /// <remarks>
    /// Use this to enumerate, lookup, or organise devices/networks that
    /// were loaded from a NodeSet2 XML, programmatically created via
    /// <see cref="DiNodeManager.CreateDeviceAsync(QualifiedName, NodeState?, System.Threading.CancellationToken)"/>,
    /// or otherwise registered with a DI node manager.
    /// </remarks>
    public interface IDeviceTopology
    {
        /// <summary>The DI <c>DeviceSet</c> object node, when present.</summary>
        BaseObjectState? DeviceSet { get; }

        /// <summary>The DI <c>NetworkSet</c> object node, when present.</summary>
        BaseObjectState? NetworkSet { get; }

        /// <summary>The DI <c>DeviceTopology</c> object node, when present.</summary>
        BaseObjectState? DeviceTopology { get; }

        /// <summary>
        /// Enumerates all devices currently registered under
        /// <see cref="DeviceSet"/>, regardless of source (NodeSet load
        /// versus programmatic creation).
        /// </summary>
        IEnumerable<ComponentState> Devices { get; }

        /// <summary>
        /// Looks up a device by browse name under
        /// <see cref="DeviceSet"/>. Returns <see langword="null"/> when
        /// no matching child is found.
        /// </summary>
        ComponentState? FindDevice(QualifiedName browseName);
    }
}
