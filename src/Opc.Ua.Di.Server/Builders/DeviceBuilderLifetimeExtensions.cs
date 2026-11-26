// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Typed materialisation helpers for OPC 10000-100 §10.6
    /// <c>LifetimeVariableType</c> + indication classifier subtypes.
    /// </summary>
    public static class DeviceBuilderLifetimeExtensions
    {
        /// <summary>
        /// Adds a <see cref="LifetimeVariableState"/> under the device
        /// with the supplied start value and the indication
        /// classifier kind.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        public static LifetimeVariableState AddLifetimeIndication<TDevice>(
            this IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            LifetimeIndicationKind kind,
            double startValue,
            Action<LifetimeVariableState>? configure = null)
            where TDevice : ComponentState
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException(
                    "browseName must be non-null.", nameof(browseName));
            }
            _ = kind;

            var variable = new LifetimeVariableState(device.Device)
            {
                SymbolicName = browseName.Name ?? string.Empty,
                BrowseName = browseName,
                DisplayName = new LocalizedText(browseName.Name ?? string.Empty),
                DataType = Types.DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = startValue,
                Historizing = false,
                ReferenceTypeId = Types.ReferenceTypeIds.HasComponent
            };
            variable.NodeId = device.Context.RequireNodeIdFactory().New(device.Context, variable);

            device.Device.AddChild(variable);
            device.Manager.AddPredefinedNodeAsync(variable, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            configure?.Invoke(variable);
            return variable;
        }

        /// <summary>
        /// Resolves the <c>BaseLifetimeIndicationType</c> subtype
        /// NodeId for the given <paramref name="kind"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="namespaceUris"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static NodeId ResolveIndicationTypeId(
            this LifetimeIndicationKind kind,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }
            uint id = kind switch
            {
                LifetimeIndicationKind.Time => ObjectTypes.TimeIndicationType,
                LifetimeIndicationKind.NumberOfParts =>
                    ObjectTypes.NumberOfPartsIndicationType,
                LifetimeIndicationKind.NumberOfUsages =>
                    ObjectTypes.NumberOfUsagesIndicationType,
                LifetimeIndicationKind.Length => ObjectTypes.LengthIndicationType,
                LifetimeIndicationKind.Diameter => ObjectTypes.DiameterIndicationType,
                LifetimeIndicationKind.SubstanceVolume =>
                    ObjectTypes.SubstanceVolumeIndicationType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
            return NodeId.Create(id, Namespaces.OpcUaDi, namespaceUris);
        }
    }
}
