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

namespace FlatTagServer
{
    /// <summary>
    /// Creates flat-tag node managers from the configured source options.
    /// </summary>
    public sealed class FlatTagNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Initializes the factory.
        /// </summary>
        public FlatTagNodeManagerFactory(FlatTagServerOptions options)
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
            IAsyncNodeManager nodeManager = new FlatTagNodeManager(server, m_options);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }

        private readonly FlatTagServerOptions m_options;
    }

    /// <summary>
    /// Minimal async address space exposing one half of the aggregate Pump tags.
    /// </summary>
    public sealed class FlatTagNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public FlatTagNodeManager(
            IServerInternal server,
            FlatTagServerOptions options)
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
            await AddPumpAsync(
                references,
                namespaceIndex,
                "Pump1",
                m_options.Values,
                cancellationToken).ConfigureAwait(false);
            await AddPumpAsync(
                references,
                namespaceIndex,
                "Pump2",
                m_options.Pump2Values,
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask AddPumpAsync(
            IList<IReference> externalReferences,
            ushort namespaceIndex,
            string pumpNodeId,
            FlatTagValues values,
            CancellationToken cancellationToken)
        {
            BaseObjectState pump = CreateObject(
                null,
                namespaceIndex,
                pumpNodeId,
                pumpNodeId,
                ReferenceTypeIds.Organizes);
            pump.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            externalReferences.Add(new NodeStateReference(
                ReferenceTypeIds.Organizes,
                false,
                pump.NodeId));

            BaseObjectState operational = CreateObject(
                pump,
                namespaceIndex,
                pumpNodeId + ".Operational",
                "Operational");
            BaseObjectState measurements = CreateObject(
                operational,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements",
                "Measurements");
            BaseObjectState events = CreateObject(
                pump,
                namespaceIndex,
                pumpNodeId + ".Events",
                "Events");

            // The pump is the notifier an aggregating server subscribes to. Its
            // conditions live under the supervision Objects below it, and OPC
            // 10000-3 only delivers their events to a client that can reach a
            // notifier, so the pump both carries the bit and is registered as a
            // root notifier with the server.
            pump.EventNotifier = EventNotifiers.SubscribeToEvents;
            events.EventNotifier = EventNotifiers.SubscribeToEvents;
            await AddRootNotifierAsync(pump, cancellationToken).ConfigureAwait(false);

            var signals = new List<SupervisionSignal>();
            AddManagementMethods(namespaceIndex, pump, pumpNodeId, signals);

            if (m_options.SourceNamespaceUri == FlatTagServerOptions.SourceANamespaceUri)
            {
                AddSourceAVariables(
                    namespaceIndex,
                    pumpNodeId,
                    measurements,
                    events,
                    values,
                    signals);
            }
            else
            {
                AddSourceBVariables(
                    namespaceIndex,
                    pumpNodeId,
                    measurements,
                    events,
                    values,
                    signals);
            }

            await AddPredefinedNodeAsync(SystemContext, pump, cancellationToken)
                .ConfigureAwait(false);
        }

        private void AddSourceAVariables(
            ushort namespaceIndex,
            string pumpNodeId,
            BaseObjectState measurements,
            BaseObjectState events,
            FlatTagValues values,
            List<SupervisionSignal> signals)
        {
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.DifferentialPressure",
                "DifferentialPressure",
                DataTypeIds.Double,
                Variant.From(values.DifferentialPressure));
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.FluidTemperature",
                "FluidTemperature",
                DataTypeIds.Double,
                Variant.From(values.FluidTemperature));
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.MassFlow",
                "MassFlow",
                DataTypeIds.Double,
                Variant.From(values.MassFlow));
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.Level",
                "Level",
                DataTypeIds.Double,
                Variant.From(values.Level));

            BaseObjectState supervision = CreateObject(
                events,
                namespaceIndex,
                pumpNodeId + ".Events.SupervisionProcessFluid",
                "SupervisionProcessFluid");
            supervision.EventNotifier = EventNotifiers.SubscribeToEvents;
            signals.Add(new SupervisionSignal(
                SystemContext,
                Server.Telemetry,
                supervision,
                namespaceIndex,
                pumpNodeId + ".Events.SupervisionProcessFluid.Cavitation",
                "Cavitation",
                "CavitationAlarm",
                severity: 700,
                initiallyActive: values.Cavitation));
        }

        private void AddSourceBVariables(
            ushort namespaceIndex,
            string pumpNodeId,
            BaseObjectState measurements,
            BaseObjectState events,
            FlatTagValues values,
            List<SupervisionSignal> signals)
        {
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.BearingTemperature",
                "BearingTemperature",
                DataTypeIds.Double,
                Variant.From(values.BearingTemperature));
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.PumpPowerInput",
                "PumpPowerInput",
                DataTypeIds.Double,
                Variant.From(values.PumpPowerInput));
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.PumpEfficiency",
                "PumpEfficiency",
                DataTypeIds.Double,
                Variant.From(values.PumpEfficiency));
            CreateVariable(
                measurements,
                namespaceIndex,
                pumpNodeId + ".Operational.Measurements.NumberOfStarts",
                "NumberOfStarts",
                DataTypeIds.UInt32,
                Variant.From(values.NumberOfStarts));

            BaseObjectState supervision = CreateObject(
                events,
                namespaceIndex,
                pumpNodeId + ".Events.SupervisionPumpOperation",
                "SupervisionPumpOperation");
            supervision.EventNotifier = EventNotifiers.SubscribeToEvents;
            signals.Add(new SupervisionSignal(
                SystemContext,
                Server.Telemetry,
                supervision,
                namespaceIndex,
                pumpNodeId + ".Events.SupervisionPumpOperation.MotorOverheat",
                "MotorOverheat",
                "MotorOverheatAlarm",
                severity: 800,
                initiallyActive: values.MotorOverheat));
        }

        /// <summary>
        /// Adds the Methods an operator uses to manage the pump. They are the
        /// members the asset's management group projects, and <c>Reset</c> is
        /// also what returns a tripped supervision signal to normal.
        /// </summary>
        private static void AddManagementMethods(
            ushort namespaceIndex,
            BaseObjectState pump,
            string pumpNodeId,
            List<SupervisionSignal> signals)
        {
            var running = new BaseDataVariableState(pump)
            {
                SymbolicName = "Running",
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(pumpNodeId + ".Running", namespaceIndex),
                BrowseName = new QualifiedName("Running", namespaceIndex),
                DisplayName = new LocalizedText("en", "Running"),
                DataType = DataTypeIds.Boolean,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false,
                Value = Variant.From(true),
                StatusCode = StatusCodes.Good,
                Timestamp = DateTime.UtcNow
            };
            pump.AddChild(running);

            CreateMethod(
                pump,
                namespaceIndex,
                pumpNodeId + ".Start",
                "Start",
                (context, _, _, _, _, _) =>
                {
                    SetRunning(context, running, value: true);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            CreateMethod(
                pump,
                namespaceIndex,
                pumpNodeId + ".Stop",
                "Stop",
                (context, _, _, _, _, _) =>
                {
                    SetRunning(context, running, value: false);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            CreateMethod(
                pump,
                namespaceIndex,
                pumpNodeId + ".Reset",
                "Reset",
                (context, _, _, _, _, _) =>
                {
                    foreach (SupervisionSignal signal in signals)
                    {
                        if (signal.IsActive)
                        {
                            signal.SetActive(context, active: false);
                        }
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
        }

        private static void SetRunning(
            ISystemContext context,
            BaseDataVariableState running,
            bool value)
        {
            running.Value = Variant.From(value);
            running.Timestamp = DateTime.UtcNow;
            running.ClearChangeMasks(context, includeChildren: false);
        }

        private static void CreateMethod(
            NodeState parent,
            ushort namespaceIndex,
            string nodeId,
            string browseName,
            GenericMethodCalledEventHandler2Async onCall)
        {
            var method = new MethodState(parent)
            {
                SymbolicName = browseName,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(nodeId, namespaceIndex),
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText("en", browseName),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true,
                OnCallMethod2Async = onCall
            };
            parent.AddChild(method);
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

        private readonly FlatTagServerOptions m_options;
    }
}
