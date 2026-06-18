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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Default <see cref="IDeviceBuilder{TDevice}"/> implementation backed
    /// by a <see cref="ComponentState"/> instance attached to a parent
    /// node within a <see cref="DiNodeManager"/>'s address space.
    /// </summary>
    /// <typeparam name="TDevice">Concrete device state type.</typeparam>
    internal sealed class DeviceBuilder<TDevice> : IDeviceBuilder<TDevice>
        where TDevice : ComponentState
    {
        private readonly NodeManagerBuilder m_builder;

        internal DeviceBuilder(
            DiNodeManager manager,
            TDevice device,
            NodeManagerBuilder builder)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Device = device ?? throw new ArgumentNullException(nameof(device));
            m_builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public TDevice Device { get; }
        public DiNodeManager Manager { get; }
        public ISystemContext Context => Manager.SystemContext;
        public INodeBuilder<TDevice> Node => m_builder.Node<TDevice>(Device.NodeId);

        public IDeviceBuilder<TDevice> WithIdentification(
            Action<DeviceIdentificationData> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var data = new DeviceIdentificationData();
            configure(data);

            ApplyIdentification(data);
            return this;
        }

        public IDeviceBuilder<TDevice> WithIdentificationGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Identification, Manager.DiNamespaceIndex),
                useIdentificationSlot: true,
                configure);
        }

        public IDeviceBuilder<TDevice> WithConfigurationGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Configuration, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithMaintenanceGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Maintenance, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithDiagnosticsGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Diagnostics, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithStatusGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Status, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithOperationalGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Operational, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithStatisticsGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.Statistics, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithOperationCountersGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(
                new QualifiedName(WellKnownFunctionalGroups.OperationCounters, Manager.DiNamespaceIndex),
                useIdentificationSlot: false,
                configure);
        }

        public IDeviceBuilder<TDevice> WithFunctionalGroup(
            QualifiedName name,
            Action<IFunctionalGroupBuilder> configure)
        {
            return ConfigureFunctionalGroup(name, useIdentificationSlot: false, configure);
        }

        public IDeviceBuilder<TDevice> ConnectsTo(NodeId other)
        {
            if (other.IsNull)
            {
                throw new ArgumentNullException(nameof(other));
            }
            // OPC 10000-100: ConnectsTo is forward from this device to the
            // adjacent device in the topology graph.
            Device.AddReference(ResolveConnectsToRefType(), false, other);
            return this;
        }

        public IDeviceBuilder<TDevice> ConnectsToParent(NodeId parent)
        {
            if (parent.IsNull)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            // Inverse of ConnectsTo: identify the parent topology element
            // (the device that contains or aggregates this one).
            Device.AddReference(ResolveConnectsToRefType(), true, parent);
            return this;
        }

        private NodeId ResolveConnectsToRefType()
        {
            return NodeId.Create(
                Opc.Ua.Di.ReferenceTypes.ConnectsTo,
                DiNodeManager.DiNamespaceUri,
                Manager.Server.NamespaceUris);
        }

        public IDeviceBuilder<TDevice> Configure(
            Action<TDevice, ISystemContext> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(Device, Context);
            return this;
        }

        private DeviceBuilder<TDevice> ConfigureFunctionalGroup(
            QualifiedName browseName,
            bool useIdentificationSlot,
            Action<IFunctionalGroupBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            FunctionalGroupState group = GetOrCreateFunctionalGroup(
                browseName,
                useIdentificationSlot);

            INodeBuilder groupNode = m_builder.Node(group.NodeId);
            var fgBuilder = new FunctionalGroupBuilder(group, groupNode, Context);
            configure(fgBuilder);
            return this;
        }

        private FunctionalGroupState GetOrCreateFunctionalGroup(
            QualifiedName browseName,
            bool useIdentificationSlot)
        {
            // Reuse an existing child with the same browse name. This makes
            // the builder idempotent and lets callers configure
            // NodeSet-loaded groups through the same surface.
            NodeState? existing = Device.FindChild(Context, browseName);
            if (existing is FunctionalGroupState reusable)
            {
                return reusable;
            }
            FunctionalGroupState group;
            if (Device is TopologyElementState topology)
            {
                if (useIdentificationSlot && topology.Identification == null)
                {
                    topology.AddIdentification(Context);
                    group = topology.Identification!;
                }
                else
                {
                    group = topology.AddGroupIdentifier(Context, browseName);
                }
            }
            else
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Device '{0}' is not a TopologyElementState; functional groups can only be added to topology elements.",
                    Device.BrowseName);
            }

            // Normalize order: BrowseName/SymbolicName → NodeIdFactory →
            // TypeDefinitionId → children/references (added by the caller).
            group.SymbolicName = browseName.Name ?? string.Empty;
            group.BrowseName = browseName;
            group.DisplayName = new LocalizedText(browseName.Name);
            group.NodeId = Context.NodeIdFactory.New(Context, group);
            group.TypeDefinitionId = NodeId.Create(
                Opc.Ua.Di.ObjectTypes.FunctionalGroupType,
                DiNodeManager.DiNamespaceUri,
                Manager.Server.NamespaceUris);
            group.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            group.ModellingRuleId = NodeId.Null;

            // Recursively register the new group + its children with the
            // manager so direct NodeId lookup and event delivery work.
            Manager.AddPredefinedNodeAsync(
                group,
                System.Threading.CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            return group;
        }

        private void ApplyIdentification(DeviceIdentificationData data)
        {
            // Nameplate properties live on ComponentState and its
            // subtypes; if Device isn't a ComponentState the call is a
            // no-op (DI shouldn't see those, but the constraint allows
            // it). Writes only the properties that were assigned by the
            // caller, leaving the rest untouched.
            if (Device is not ComponentState component)
            {
                return;
            }
            if (!data.Manufacturer.IsNull)
            {
                WriteLocalizedText(
                    component.Manufacturer,
                    "Manufacturer",
                    data.Manufacturer,
                    p => component.Manufacturer = p);
            }
            if (data.ManufacturerUri != null)
            {
                WriteString(
                    component.ManufacturerUri,
                    "ManufacturerUri",
                    data.ManufacturerUri,
                    p => component.ManufacturerUri = p);
            }
            if (!data.Model.IsNull)
            {
                WriteLocalizedText(
                    component.Model,
                    "Model",
                    data.Model,
                    p => component.Model = p);
            }
            if (data.HardwareRevision != null)
            {
                WriteString(
                    component.HardwareRevision,
                    "HardwareRevision",
                    data.HardwareRevision,
                    p => component.HardwareRevision = p);
            }
            if (data.SoftwareRevision != null)
            {
                WriteString(
                    component.SoftwareRevision,
                    "SoftwareRevision",
                    data.SoftwareRevision,
                    p => component.SoftwareRevision = p);
            }
            if (data.DeviceRevision != null)
            {
                WriteString(
                    component.DeviceRevision,
                    "DeviceRevision",
                    data.DeviceRevision,
                    p => component.DeviceRevision = p);
            }
            if (data.ProductCode != null)
            {
                WriteString(
                    component.ProductCode,
                    "ProductCode",
                    data.ProductCode,
                    p => component.ProductCode = p);
            }
            if (data.DeviceManual != null)
            {
                WriteString(
                    component.DeviceManual,
                    "DeviceManual",
                    data.DeviceManual,
                    p => component.DeviceManual = p);
            }
            if (data.DeviceClass != null)
            {
                WriteString(
                    component.DeviceClass,
                    "DeviceClass",
                    data.DeviceClass,
                    p => component.DeviceClass = p);
            }
            if (data.SerialNumber != null)
            {
                WriteString(
                    component.SerialNumber,
                    "SerialNumber",
                    data.SerialNumber,
                    p => component.SerialNumber = p);
            }
            if (data.ProductInstanceUri != null)
            {
                WriteString(
                    component.ProductInstanceUri,
                    "ProductInstanceUri",
                    data.ProductInstanceUri,
                    p => component.ProductInstanceUri = p);
            }
            if (data.RevisionCounter.HasValue)
            {
                WriteInt32(
                    component.RevisionCounter,
                    "RevisionCounter",
                    data.RevisionCounter.Value,
                    p => component.RevisionCounter = p);
            }
        }

        private void WriteString(
            PropertyState<string>? existing,
            string browseName,
            string value,
            Action<PropertyState<string>> setBackingField)
        {
            if (existing != null)
            {
                existing.Value = value;
                existing.ClearChangeMasks(Context, false);
                return;
            }

            PropertyState created = Device.AddProperty<string, VariantBuilder>(
                browseName,
                Opc.Ua.Types.DataTypeIds.String,
                ValueRanks.Scalar);
            var typed = (PropertyState<string>)created;
            FinalizeAndRegister(created, typed, value);
            setBackingField(typed);
        }

        private void WriteLocalizedText(
            PropertyState<LocalizedText>? existing,
            string browseName,
            LocalizedText value,
            Action<PropertyState<LocalizedText>> setBackingField)
        {
            if (existing != null)
            {
                existing.Value = value;
                existing.ClearChangeMasks(Context, false);
                return;
            }

            PropertyState created = Device.AddProperty<LocalizedText, VariantBuilder>(
                browseName,
                Opc.Ua.Types.DataTypeIds.LocalizedText,
                ValueRanks.Scalar);
            var typed = (PropertyState<LocalizedText>)created;
            FinalizeAndRegister(created, typed, value);
            setBackingField(typed);
        }

        private void WriteInt32(
            PropertyState<int>? existing,
            string browseName,
            int value,
            Action<PropertyState<int>> setBackingField)
        {
            if (existing != null)
            {
                existing.Value = value;
                existing.ClearChangeMasks(Context, false);
                return;
            }

            PropertyState created = Device.AddProperty<int, VariantBuilder>(
                browseName,
                Opc.Ua.Types.DataTypeIds.Int32,
                ValueRanks.Scalar);
            var typed = (PropertyState<int>)created;
            FinalizeAndRegister(created, typed, value);
            setBackingField(typed);
        }

        /// <summary>
        /// Stamps the manager-assigned NodeId on a freshly created
        /// nameplate property, writes its value, and registers it with
        /// the manager so direct NodeId lookup and event delivery work.
        /// </summary>
        /// <typeparam name="TValue">Property value type.</typeparam>
        private void FinalizeAndRegister<TValue>(
            PropertyState created,
            PropertyState<TValue> typed,
            TValue value)
        {
            created.NodeId = Context.NodeIdFactory.New(Context, created);
            typed.Value = value;

            Manager.AddPredefinedNodeAsync(
                created,
                System.Threading.CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }
    }
}
