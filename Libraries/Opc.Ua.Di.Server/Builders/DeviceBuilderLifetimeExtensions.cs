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
        public static LifetimeVariableState AddLifetimeIndication<TDevice>(
            this IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            LifetimeIndicationKind kind,
            double startValue,
            Action<LifetimeVariableState>? configure = null)
            where TDevice : ComponentState
        {
            if (device == null) { throw new ArgumentNullException(nameof(device)); }
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
                DataType = Opc.Ua.Types.DataTypeIds.Double,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = startValue,
                Historizing = false,
                ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent
            };
            variable.NodeId = device.Context.NodeIdFactory.New(device.Context, variable);

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
        public static NodeId ResolveIndicationTypeId(
            LifetimeIndicationKind kind,
            NamespaceTable namespaceUris)
        {
            if (namespaceUris == null) { throw new ArgumentNullException(nameof(namespaceUris)); }
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
