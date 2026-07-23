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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Default <see cref="IDeviceBuilder{TDevice}"/> implementation backed
    /// by a <see cref="ComponentState"/> instance.
    /// </summary>
    /// <typeparam name="TDevice">Concrete device state type.</typeparam>
    internal sealed class DeviceBuilder<TDevice> :
        IDeviceBuilder<TDevice>,
        ITopologyElementBuilder<TDevice>
        where TDevice : ComponentState
    {
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
        public TDevice Element => Device;
        public DiNodeManager Manager { get; }
        public ISystemContext Context => Manager.SystemContext;
        public INodeBuilder<TDevice> Node => m_builder.Node<TDevice>(Device.NodeId);
        private ITopologyElementBuilder<TDevice> Topology => this;

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
            Topology.WithIdentificationGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithConfigurationGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithConfigurationGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithMaintenanceGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithMaintenanceGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithDiagnosticsGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithDiagnosticsGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithStatusGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithStatusGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithOperationalGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithOperationalGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithStatisticsGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithStatisticsGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithOperationCountersGroup(
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithOperationCountersGroup(configure);
            return this;
        }

        public IDeviceBuilder<TDevice> WithFunctionalGroup(
            QualifiedName name,
            Action<IFunctionalGroupBuilder> configure)
        {
            Topology.WithFunctionalGroup(name, configure);
            return this;
        }

        public IDeviceBuilder<TDevice> ConnectsTo(NodeId other)
        {
            Topology.ConnectsTo(other);
            return this;
        }

        public IDeviceBuilder<TDevice> ConnectsToParent(NodeId parent)
        {
            Topology.ConnectsToParent(parent);
            return this;
        }

        public IDeviceBuilder<TDevice> Configure(
            Action<TDevice, ISystemContext> configure)
        {
            Topology.Configure(configure);
            return this;
        }

        private void ApplyIdentification(DeviceIdentificationData data)
        {
            if (!data.Manufacturer.IsNull)
            {
                WriteLocalizedText(
                    Device.Manufacturer,
                    "Manufacturer",
                    data.Manufacturer,
                    property => Device.Manufacturer = property);
            }
            if (data.ManufacturerUri != null)
            {
                WriteString(
                    Device.ManufacturerUri,
                    "ManufacturerUri",
                    data.ManufacturerUri,
                    property => Device.ManufacturerUri = property);
            }
            if (!data.Model.IsNull)
            {
                WriteLocalizedText(
                    Device.Model,
                    "Model",
                    data.Model,
                    property => Device.Model = property);
            }
            if (data.HardwareRevision != null)
            {
                WriteString(
                    Device.HardwareRevision,
                    "HardwareRevision",
                    data.HardwareRevision,
                    property => Device.HardwareRevision = property);
            }
            if (data.SoftwareRevision != null)
            {
                WriteString(
                    Device.SoftwareRevision,
                    "SoftwareRevision",
                    data.SoftwareRevision,
                    property => Device.SoftwareRevision = property);
            }
            if (data.DeviceRevision != null)
            {
                WriteString(
                    Device.DeviceRevision,
                    "DeviceRevision",
                    data.DeviceRevision,
                    property => Device.DeviceRevision = property);
            }
            if (data.ProductCode != null)
            {
                WriteString(
                    Device.ProductCode,
                    "ProductCode",
                    data.ProductCode,
                    property => Device.ProductCode = property);
            }
            if (data.DeviceManual != null)
            {
                WriteString(
                    Device.DeviceManual,
                    "DeviceManual",
                    data.DeviceManual,
                    property => Device.DeviceManual = property);
            }
            if (data.DeviceClass != null)
            {
                WriteString(
                    Device.DeviceClass,
                    "DeviceClass",
                    data.DeviceClass,
                    property => Device.DeviceClass = property);
            }
            if (data.SerialNumber != null)
            {
                WriteString(
                    Device.SerialNumber,
                    "SerialNumber",
                    data.SerialNumber,
                    property => Device.SerialNumber = property);
            }
            if (data.ProductInstanceUri != null)
            {
                WriteString(
                    Device.ProductInstanceUri,
                    "ProductInstanceUri",
                    data.ProductInstanceUri,
                    property => Device.ProductInstanceUri = property);
            }
            if (data.RevisionCounter.HasValue)
            {
                WriteInt32(
                    Device.RevisionCounter,
                    "RevisionCounter",
                    data.RevisionCounter.Value,
                    property => Device.RevisionCounter = property);
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

        private void FinalizeAndRegister<TValue>(
            PropertyState created,
            PropertyState<TValue> typed,
            TValue value)
        {
            created.BrowseName = new QualifiedName(
                created.BrowseName.Name,
                Manager.DiNamespaceIndex);
            created.NodeId = Context.NodeIdFactory.New(Context, created);
            typed.Value = value;

            Manager.AddPredefinedNodeAsync(
                created,
                System.Threading.CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
        }

        private readonly NodeManagerBuilder m_builder;
    }
}
