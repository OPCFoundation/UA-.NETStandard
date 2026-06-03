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
using System.Collections.Generic;

namespace Opc.Ua.Di.Server.Topology
{
    /// <summary>
    /// Default <see cref="IDeviceTopology"/> implementation backed by a
    /// <see cref="DiNodeManager"/>'s predefined-node dictionary. Cheap to
    /// construct (lookups happen on demand) and safe to use both during
    /// address-space configuration and at runtime.
    /// </summary>
    public sealed class DiDeviceTopology : IDeviceTopology
    {
        private readonly DiNodeManager m_manager;

        /// <summary>
        /// Creates a new topology accessor over
        /// <paramref name="manager"/>.
        /// </summary>
        public DiDeviceTopology(DiNodeManager manager)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <inheritdoc/>
        public BaseObjectState? DeviceSet
            => ResolveWellKnown(Opc.Ua.Di.Objects.DeviceSet);

        /// <inheritdoc/>
        public BaseObjectState? NetworkSet
            => ResolveWellKnown(Opc.Ua.Di.Objects.NetworkSet);

        /// <inheritdoc/>
        public BaseObjectState? DeviceTopology
            => ResolveWellKnown(Opc.Ua.Di.Objects.DeviceTopology);

        /// <inheritdoc/>
        public IEnumerable<ComponentState> Devices
        {
            get
            {
                BaseObjectState? deviceSet = DeviceSet;
                if (deviceSet == null)
                {
                    yield break;
                }
                var children = new List<BaseInstanceState>();
                deviceSet.GetChildren(m_manager.SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    if (child is ComponentState device)
                    {
                        yield return device;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public ComponentState? FindDevice(QualifiedName browseName)
        {
            if (browseName.IsNull)
            {
                return null;
            }
            BaseObjectState? deviceSet = DeviceSet;
            if (deviceSet == null)
            {
                return null;
            }
            return deviceSet.FindChild(m_manager.SystemContext, browseName)
                as ComponentState;
        }

        private BaseObjectState? ResolveWellKnown(uint id)
        {
            NodeId nodeId = NodeId.Create(
                id,
                DiNodeManager.DiNamespaceUri,
                m_manager.Server.NamespaceUris);

            return m_manager.FindPredefinedNode(nodeId) as BaseObjectState;
        }
    }
}
