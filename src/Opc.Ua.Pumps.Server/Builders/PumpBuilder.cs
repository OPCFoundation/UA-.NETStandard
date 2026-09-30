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
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Pumps.Server.Builders
{
    /// <summary>
    /// The default <see cref="IPumpBuilder"/>.
    /// </summary>
    internal sealed class PumpBuilder : IPumpBuilder
    {
        private readonly ISystemContext m_context;
        private readonly PumpNamespaceIndices m_namespaces;
        private readonly Action<NodeState>? m_register;
        private readonly Func<NodeId, NodeState?>? m_findNode;
        private readonly PumpGroupBuilder m_root;

        internal PumpBuilder(
            ISystemContext context,
            PumpState pump,
            PumpNamespaceIndices namespaces,
            Action<NodeState>? register,
            Func<NodeId, NodeState?>? findNode)
        {
            m_context = context;
            Pump = pump;
            m_namespaces = namespaces;
            m_register = register;
            m_findNode = findNode;
            m_root = new PumpGroupBuilder(context, pump, namespaces, register, findNode);
        }

        /// <inheritdoc/>
        public PumpState Pump { get; }

        /// <inheritdoc/>
        public NodeId NodeId => Pump.NodeId;

        /// <inheritdoc/>
        public IPumpGroupBuilder Identification =>
            m_root.Nested(PumpsModel.DiGroups.Identification);

        /// <inheritdoc/>
        public IPumpGroupBuilder Configuration =>
            m_root.Nested(PumpsModel.DiGroups.Configuration);

        /// <inheritdoc/>
        public IPumpGroupBuilder Operational =>
            m_root.Nested(PumpsModel.DiGroups.Operational);

        /// <inheritdoc/>
        public IPumpGroupBuilder Events => m_root.Nested(BrowseNames.Events);

        /// <inheritdoc/>
        public IPumpGroupBuilder Maintenance =>
            m_root.Nested(PumpsModel.DiGroups.Maintenance);

        /// <inheritdoc/>
        public IPumpGroupBuilder Documentation =>
            m_root.Nested(BrowseNames.Documentation);

        /// <inheritdoc/>
        public IPumpGroupBuilder Design => Configuration.Nested(BrowseNames.Design);

        /// <inheritdoc/>
        public IPumpGroupBuilder Implementation =>
            Configuration.Nested(BrowseNames.Implementation);

        /// <inheritdoc/>
        public IPumpGroupBuilder SystemRequirements =>
            Configuration.Nested(BrowseNames.SystemRequirements);

        /// <inheritdoc/>
        public IPumpGroupBuilder Measurements =>
            Operational.Nested(BrowseNames.Measurements);

        /// <inheritdoc/>
        public IPumpGroupBuilder Signals => Operational.Nested(BrowseNames.Signals);

        /// <inheritdoc/>
        public IPumpGroupBuilder Control => Operational.Nested(BrowseNames.Control);

        /// <inheritdoc/>
        public IPumpGroupBuilder PumpActuation =>
            Operational.Nested(BrowseNames.PumpActuation);

        /// <inheritdoc/>
        public IPumpGroupBuilder MultiPump => Operational.Nested(BrowseNames.MultiPump);

        /// <inheritdoc/>
        public IPumpGroupBuilder Supervision(string browseName)
        {
            return Events.Nested(browseName);
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder MaintenanceCategory(string browseName)
        {
            return Maintenance.Nested(browseName);
        }

        /// <inheritdoc/>
        public IPumpBuilder With(Action<PumpState> configure)
        {
            if (configure is null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            configure(Pump);
            return this;
        }

        /// <inheritdoc/>
        public IPumpGroupBuilder AddPort(PumpPortKind kind, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A port name is required.", nameof(name));
            }

            NodeState ports = m_root.Nested(BrowseNames.Ports).Node;
            var browseName = new QualifiedName(name, m_namespaces.Pumps);

            // PortsGroupType declares its three ports as placeholders, so
            // CreateChild cannot materialise one - the instance has to be
            // created from the port type and attached explicitly.
            BaseInstanceState port = kind switch
            {
                PumpPortKind.Drive =>
                    m_context.CreateInstanceOfDrivePortType(ports, browseName),
                PumpPortKind.InletConnection =>
                    m_context.CreateInstanceOfInletConnectionPortType(ports, browseName),
                PumpPortKind.OutletConnection =>
                    m_context.CreateInstanceOfOutletConnectionPortType(ports, browseName),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(kind),
                    kind,
                    "A port must be a drive, inlet or outlet connection port.")
            };

            port.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            ports.AddChild(port);
            m_register?.Invoke(port);
            return new PumpGroupBuilder(
                m_context,
                port,
                m_namespaces,
                m_register,
                m_findNode);
        }

        /// <inheritdoc/>
        public IPumpBuilder WithNameplate(PumpNameplate nameplate)
        {
            if (nameplate is null)
            {
                throw new ArgumentNullException(nameof(nameplate));
            }

            IPumpGroupBuilder identification = Identification;

            SetText(identification, DiBrowseNames.ManufacturerUri, nameplate.ManufacturerUri);
            SetText(identification, DiBrowseNames.ProductCode, nameplate.ProductCode);
            SetText(identification, DiBrowseNames.HardwareRevision, nameplate.HardwareRevision);
            SetText(identification, DiBrowseNames.SoftwareRevision, nameplate.SoftwareRevision);
            SetText(identification, DiBrowseNames.DeviceClass, nameplate.DeviceClass);
            SetText(identification, DiBrowseNames.SerialNumber, nameplate.SerialNumber);
            SetText(
                identification,
                DiBrowseNames.ProductInstanceUri,
                nameplate.ProductInstanceUri);
            SetText(identification, DiBrowseNames.AssetId, nameplate.AssetId);
            SetLocalized(identification, DiBrowseNames.Manufacturer, nameplate.Manufacturer);
            SetLocalized(identification, DiBrowseNames.Model, nameplate.Model);
            SetLocalized(identification, DiBrowseNames.ComponentName, nameplate.ComponentName);

            SetText(identification, MachineryBrowseNames.Location, nameplate.Location);
            SetValue(
                identification,
                MachineryBrowseNames.InitialOperationDate,
                nameplate.InitialOperationDate.HasValue
                    ? Variant.From((DateTimeUtc)nameplate.InitialOperationDate.Value)
                    : Variant.Null);
            SetValue(
                identification,
                MachineryBrowseNames.YearOfConstruction,
                nameplate.YearOfConstruction.HasValue
                    ? Variant.From(nameplate.YearOfConstruction.Value)
                    : Variant.Null);
            SetValue(
                identification,
                MachineryBrowseNames.MonthOfConstruction,
                nameplate.MonthOfConstruction.HasValue
                    ? Variant.From(nameplate.MonthOfConstruction.Value)
                    : Variant.Null);

            SetValue(
                identification,
                BrowseNames.DayOfConstruction,
                nameplate.DayOfConstruction.HasValue
                    ? Variant.From(nameplate.DayOfConstruction.Value)
                    : Variant.Null);
            SetText(identification, BrowseNames.ArticleNumber, nameplate.ArticleNumber);
            SetText(identification, BrowseNames.OrderProductCode, nameplate.OrderProductCode);
            SetText(identification, BrowseNames.TypeOfProduct, nameplate.TypeOfProduct);
            SetText(identification, BrowseNames.Supplier, nameplate.Supplier);
            SetText(identification, BrowseNames.CountryOfOrigin, nameplate.CountryOfOrigin);
            SetText(identification, BrowseNames.FabricationNumber, nameplate.FabricationNumber);
            SetText(identification, BrowseNames.GTINCode, nameplate.GTINCode);
            SetText(
                identification,
                BrowseNames.NationalStockNumber,
                nameplate.NationalStockNumber);
            SetValue(
                identification,
                BrowseNames.PhysicalAddress,
                nameplate.PhysicalAddress != null
                    ? Variant.From(new ExtensionObject(nameplate.PhysicalAddress))
                    : Variant.Null);

            return this;
        }

        /// <summary>
        /// Publishes a nameplate field, or leaves it unmaterialised when the
        /// record does not carry it. A nameplate with a missing field and one
        /// that publishes an empty string are different statements, and only
        /// the first is honest about not knowing.
        /// </summary>
        private static void SetText(IPumpGroupBuilder group, string browseName, string? value)
        {
            if (value != null)
            {
                group.Set(browseName, Variant.From(value));
            }
        }

        private static void SetLocalized(
            IPumpGroupBuilder group,
            string browseName,
            LocalizedText value)
        {
            if (!value.IsNullOrEmpty)
            {
                group.Set(browseName, Variant.From(value));
            }
        }

        private static void SetValue(
            IPumpGroupBuilder group,
            string browseName,
            Variant value)
        {
            if (!value.IsNull)
            {
                group.Set(browseName, value);
            }
        }
    }
}
