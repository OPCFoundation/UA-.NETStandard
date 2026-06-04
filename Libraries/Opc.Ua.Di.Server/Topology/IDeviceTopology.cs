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
