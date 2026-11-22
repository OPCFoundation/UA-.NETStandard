// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            return deviceSet.FindChild(m_manager.SystemContext, browseName) as
                ComponentState;
        }

        private BaseObjectState? ResolveWellKnown(uint id)
        {
            var nodeId = NodeId.Create(
                id,
                DiNodeManager.DiNamespaceUri,
                m_manager.Server.NamespaceUris);

            return m_manager.FindPredefinedNode(nodeId) as BaseObjectState;
        }
    }
}
