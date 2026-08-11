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

            // The pump is the notifier an aggregating server subscribes to. Its
            // conditions live under the supervision Objects below it, and OPC
            // 10000-3 only delivers their events to a client that can reach a
            // notifier, so the pump both carries the bit and is registered as a
            // root notifier with the server.
            pump.EventNotifier = EventNotifiers.SubscribeToEvents;
            events.EventNotifier = EventNotifiers.SubscribeToEvents;
            await AddRootNotifierAsync(pump, cancellationToken).ConfigureAwait(false);

            AddManagementMethods(namespaceIndex, pump);

            if (m_options.SourceNamespaceUri == FlatTagServerOptions.SourceANamespaceUri)
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
            FlatTagValues values = m_options.Values;
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
            supervision.EventNotifier = EventNotifiers.SubscribeToEvents;
            m_signals.Add(new SupervisionSignal(
                SystemContext,
                Server.Telemetry,
                supervision,
                namespaceIndex,
                "Pump1.Events.SupervisionProcessFluid.Cavitation",
                "Cavitation",
                "CavitationAlarm",
                severity: 700,
                initiallyActive: values.Cavitation));
        }

        private void AddSourceBVariables(
            ushort namespaceIndex,
            BaseObjectState measurements,
            BaseObjectState events)
        {
            FlatTagValues values = m_options.Values;
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
            supervision.EventNotifier = EventNotifiers.SubscribeToEvents;
            m_signals.Add(new SupervisionSignal(
                SystemContext,
                Server.Telemetry,
                supervision,
                namespaceIndex,
                "Pump1.Events.SupervisionPumpOperation.MotorOverheat",
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
        private void AddManagementMethods(ushort namespaceIndex, BaseObjectState pump)
        {
            m_running = new BaseDataVariableState(pump)
            {
                SymbolicName = "Running",
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId("Pump1.Running", namespaceIndex),
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
            pump.AddChild(m_running);

            CreateMethod(
                pump,
                namespaceIndex,
                "Pump1.Start",
                "Start",
                (context, _, _, _, _, _) =>
                {
                    SetRunning(context, running: true);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            CreateMethod(
                pump,
                namespaceIndex,
                "Pump1.Stop",
                "Stop",
                (context, _, _, _, _, _) =>
                {
                    SetRunning(context, running: false);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            CreateMethod(
                pump,
                namespaceIndex,
                "Pump1.Reset",
                "Reset",
                (context, _, _, _, _, _) =>
                {
                    foreach (SupervisionSignal signal in m_signals)
                    {
                        if (signal.IsActive)
                        {
                            signal.SetActive(context, active: false);
                        }
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
        }

        private void SetRunning(ISystemContext context, bool running)
        {
            if (m_running is null)
            {
                return;
            }
            m_running.Value = Variant.From(running);
            m_running.Timestamp = DateTime.UtcNow;
            m_running.ClearChangeMasks(context, includeChildren: false);
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
        private readonly List<SupervisionSignal> m_signals = [];
        private BaseDataVariableState? m_running;
    }
}
