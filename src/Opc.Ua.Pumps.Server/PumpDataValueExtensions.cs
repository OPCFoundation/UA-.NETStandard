// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Pumps.Server.Builders;

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Publishes acquired pump values without replacing their source quality or acquisition time.
    /// </summary>
    public static class PumpDataValueExtensions
    {
        /// <summary>
        /// Publishes a variable in a group owned by this runtime, preserving its source timestamp and status.
        /// </summary>
        /// <remarks>
        /// Unlike the convenience Set/SetAnalog methods, an unset source timestamp remains unset.
        /// Signals represented by objects must address their DiscreteInputValue/DiscreteOutputValue group.
        /// </remarks>
        public static ValueTask<IPumpGroupBuilder> PublishValueAsync(
            this PumpsRuntime runtime,
            IPumpGroupBuilder group,
            string browseName,
            in DataValue value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (runtime is null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }
            if (group is null)
            {
                throw new ArgumentNullException(nameof(group));
            }
            if (!ReferenceEquals(runtime.Manager.FindPredefinedNode(group.Node.NodeId), group.Node))
            {
                throw new ArgumentException("The group must belong to this address-space owner.", nameof(group));
            }
            NodeState? ancestor = group.Node;
            while (ancestor is BaseInstanceState instance && ancestor is not PumpState)
            {
                ancestor = instance.Parent;
            }
            if (ancestor is not PumpState pump || !ReferenceEquals(runtime.Pump(pump.NodeId)?.Pump, pump))
            {
                throw new ArgumentException("The group must belong to a registered pump.", nameof(group));
            }
            if (string.IsNullOrEmpty(browseName))
            {
                throw new ArgumentException("A browse name is required.", nameof(browseName));
            }
            BaseInstanceState? child = null;
            foreach (ushort namespaceIndex in runtime.NamespaceIndices.SearchOrder)
            {
                child = group.Node.FindChild(runtime.SystemContext, new QualifiedName(browseName, namespaceIndex));
                if (child is not null)
                {
                    break;
                }
            }
            if (child is not BaseVariableState variable ||
                !ReferenceEquals(runtime.Manager.FindPredefinedNode(variable.NodeId), variable))
            {
                throw new ArgumentException("The group member must be a registered variable.", nameof(browseName));
            }
            variable.WrappedValue = value.WrappedValue;
            variable.StatusCode = value.StatusCode;
            variable.Timestamp = value.SourceTimestamp;
            return NotifyAsync(runtime.SystemContext, variable, group);
        }

        /// <summary>
        /// Awaits committed-value notifications without canceling after the value has been assigned.
        /// </summary>
        private static async ValueTask<IPumpGroupBuilder> NotifyAsync(
            ISystemContext context,
            BaseVariableState variable,
            IPumpGroupBuilder group)
        {
            await variable.ClearChangeMasksAsync(context, includeChildren: false, CancellationToken.None)
                .ConfigureAwait(false);
            return group;
        }
    }
}
