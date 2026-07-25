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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Server;

namespace WotFlatTagServer
{
    /// <summary>
    /// Creates flat-tag node managers from the configured source options.
    /// </summary>
    public sealed class WotFlatTagNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes the factory.
        /// </summary>
        public WotFlatTagNodeManagerFactory(WotFlatTagServerOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            NamespacesUris = [options.SourceNamespaceUri];
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris { get; }

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            _ = configuration;
            _ = cancellationToken;
            // CA2000 cannot model ownership transfer through ValueTask<IAsyncNodeManager>.
            // TODO: Remove this suppression when CA2000 recognizes factory ownership transfer.
#pragma warning disable CA2000
            IAsyncNodeManager nodeManager = new WotFlatTagNodeManager(server, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly WotFlatTagServerOptions m_options;
    }

    /// <summary>
    /// Minimal async address space exposing one half of the aggregate Pump tags.
    /// </summary>
    public sealed class WotFlatTagNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public WotFlatTagNodeManager(
            IServerInternal server,
            WotFlatTagServerOptions options)
            : base(server, options.SourceNamespaceUri)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (externalReferences is null)
            {
                throw new ArgumentNullException(nameof(externalReferences));
            }

            if (!externalReferences.TryGetValue(
                    ObjectIds.ObjectsFolder,
                    out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = [];
            }

            ushort namespaceIndex = NamespaceIndexes[0];
            BaseObjectState pump = CreateObject(
                null,
                namespaceIndex,
                "Pump1",
                "Pump1",
                ReferenceTypeIds.Organizes);
            pump.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(
                ReferenceTypeIds.Organizes,
                false,
                pump.NodeId));

            BaseObjectState operational = CreateObject(
                pump,
                namespaceIndex,
                "Pump1.Operational",
                "Operational");
            BaseObjectState measurements = CreateObject(
                operational,
                namespaceIndex,
                "Pump1.Operational.Measurements",
                "Measurements");
            BaseObjectState events = CreateObject(
                pump,
                namespaceIndex,
                "Pump1.Events",
                "Events");

            if (m_options.SourceNamespaceUri == WotFlatTagServerOptions.SourceANamespaceUri)
            {
                AddSourceAVariables(namespaceIndex, measurements, events);
            }
            else
            {
                AddSourceBVariables(namespaceIndex, measurements, events);
            }

            await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken)
                .ConfigureAwait(false);
        }

        private void AddSourceAVariables(
            ushort namespaceIndex,
            BaseObjectState measurements,
            BaseObjectState events)
        {
            WotFlatTagValues values = m_options.Values;
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.DifferentialPressure",
                "DifferentialPressure",
                DataTypeIds.Double,
                Variant.From(values.DifferentialPressure));
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.FluidTemperature",
                "FluidTemperature",
                DataTypeIds.Double,
                Variant.From(values.FluidTemperature));
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.MassFlow",
                "MassFlow",
                DataTypeIds.Double,
                Variant.From(values.MassFlow));
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.Level",
                "Level",
                DataTypeIds.Double,
                Variant.From(values.Level));

            BaseObjectState supervision = CreateObject(
                events,
                namespaceIndex,
                "Pump1.Events.SupervisionProcessFluid",
                "SupervisionProcessFluid");
            CreateVariable(
                supervision,
                namespaceIndex,
                "Pump1.Events.SupervisionProcessFluid.Cavitation",
                "Cavitation",
                DataTypeIds.Boolean,
                Variant.From(values.Cavitation));
        }

        private void AddSourceBVariables(
            ushort namespaceIndex,
            BaseObjectState measurements,
            BaseObjectState events)
        {
            WotFlatTagValues values = m_options.Values;
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.BearingTemperature",
                "BearingTemperature",
                DataTypeIds.Double,
                Variant.From(values.BearingTemperature));
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.PumpPowerInput",
                "PumpPowerInput",
                DataTypeIds.Double,
                Variant.From(values.PumpPowerInput));
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.PumpEfficiency",
                "PumpEfficiency",
                DataTypeIds.Double,
                Variant.From(values.PumpEfficiency));
            CreateVariable(
                measurements,
                namespaceIndex,
                "Pump1.Operational.Measurements.NumberOfStarts",
                "NumberOfStarts",
                DataTypeIds.UInt32,
                Variant.From(values.NumberOfStarts));

            BaseObjectState supervision = CreateObject(
                events,
                namespaceIndex,
                "Pump1.Events.SupervisionPumpOperation",
                "SupervisionPumpOperation");
            CreateVariable(
                supervision,
                namespaceIndex,
                "Pump1.Events.SupervisionPumpOperation.MotorOverheat",
                "MotorOverheat",
                DataTypeIds.Boolean,
                Variant.From(values.MotorOverheat));
        }

        private static BaseObjectState CreateObject(
            NodeState? parent,
            ushort namespaceIndex,
            string nodeId,
            string browseName,
            NodeId referenceTypeId = default)
        {
            var node = new BaseObjectState(parent)
            {
                SymbolicName = browseName,
                ReferenceTypeId = referenceTypeId.IsNull
                    ? ReferenceTypeIds.HasComponent
                    : referenceTypeId,
                TypeDefinitionId = ObjectTypeIds.BaseObjectType,
                NodeId = new NodeId(nodeId, namespaceIndex),
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText("en", browseName),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };
            parent?.AddChild(node);
            return node;
        }

        private static void CreateVariable(
            NodeState parent,
            ushort namespaceIndex,
            string nodeId,
            string browseName,
            NodeId dataType,
            Variant value)
        {
            var variable = new BaseDataVariableState(parent)
            {
                SymbolicName = browseName,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(nodeId, namespaceIndex),
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText("en", browseName),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                DataType = dataType,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false,
                Value = value,
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow,
                OnSimpleReadValueAsync = (_, _, ct) => ReadValueAsync(value, ct)
            };
            parent.AddChild(variable);
        }

        private static async ValueTask<AttributeSimpleReadResult> ReadValueAsync(
            Variant value,
            CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return new AttributeSimpleReadResult(ServiceResult.Good, value);
        }

        private readonly WotFlatTagServerOptions m_options;
    }
}
