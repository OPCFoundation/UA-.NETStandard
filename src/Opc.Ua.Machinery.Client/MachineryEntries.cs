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

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// A machine discovered below the OPC 40001-1 <c>Machines</c> folder.
    /// </summary>
    /// <param name="NodeId">The machine's NodeId.</param>
    /// <param name="BrowseName">The machine's browse name.</param>
    /// <param name="DisplayName">The machine's display name.</param>
    /// <param name="TypeDefinitionId">
    /// The machine's type definition. OPC 40001-1 defines no machine type, so
    /// this is whatever type the vendor model gave it — useful for telling a
    /// pump from a generator.
    /// </param>
    public sealed record MachineEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId);

    /// <summary>
    /// The OPC 40001-1 identification of a machinery item, as read from its
    /// <c>Identification</c> add-in.
    /// </summary>
    public sealed record MachineIdentification
    {
        /// <summary>
        /// Manufacturer. Mandatory in OPC 40001-1.
        /// </summary>
        public LocalizedText Manufacturer { get; init; }

        /// <summary>
        /// Manufacturer-assigned serial number. Mandatory.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// Globally unique product-instance URI.
        /// </summary>
        public string? ProductInstanceUri { get; init; }

        /// <summary>
        /// Model designation.
        /// </summary>
        public LocalizedText Model { get; init; }

        /// <summary>
        /// Globally unique manufacturer URI.
        /// </summary>
        public string? ManufacturerUri { get; init; }

        /// <summary>
        /// Manufacturer-defined product code.
        /// </summary>
        public string? ProductCode { get; init; }

        /// <summary>
        /// Hardware revision level.
        /// </summary>
        public string? HardwareRevision { get; init; }

        /// <summary>
        /// Software revision level.
        /// </summary>
        public string? SoftwareRevision { get; init; }

        /// <summary>
        /// Overall device revision level, for a component.
        /// </summary>
        public string? DeviceRevision { get; init; }

        /// <summary>
        /// Device class.
        /// </summary>
        public string? DeviceClass { get; init; }

        /// <summary>
        /// Operator-assigned asset identifier.
        /// </summary>
        public string? AssetId { get; init; }

        /// <summary>
        /// Operator-assigned component name.
        /// </summary>
        public LocalizedText ComponentName { get; init; }

        /// <summary>
        /// Location, for a machine.
        /// </summary>
        public string? Location { get; init; }

        /// <summary>
        /// Date the item was first put into operation.
        /// </summary>
        public DateTime? InitialOperationDate { get; init; }

        /// <summary>
        /// Year of construction.
        /// </summary>
        public ushort? YearOfConstruction { get; init; }

        /// <summary>
        /// Month of construction.
        /// </summary>
        public byte? MonthOfConstruction { get; init; }
    }

    /// <summary>
    /// One OPC 40001-2 process value, as read from its object and its
    /// <c>AnalogSignal</c>.
    /// </summary>
    /// <remarks>
    /// Every member but the two NodeIds is optional in the part, so a
    /// <see langword="null"/> means the server does not publish it.
    /// </remarks>
    public sealed record MachineryProcessValue
    {
        /// <summary>
        /// The process value object.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The <c>AnalogSignal</c> variable carrying the reading.
        /// </summary>
        public required NodeId SignalNodeId { get; init; }

        /// <summary>
        /// The current reading.
        /// </summary>
        public double? Value { get; init; }

        /// <summary>
        /// The setpoint the value is steered to.
        /// </summary>
        public double? Setpoint { get; init; }

        /// <summary>
        /// The vendor-specific status word.
        /// </summary>
        public ushort? Status { get; init; }

        /// <summary>
        /// Which alarms the server suppresses.
        /// </summary>
        public ushort? AlarmSuppression { get; init; }

        /// <summary>
        /// Low-low limit.
        /// </summary>
        public double? LowLowLimit { get; init; }

        /// <summary>
        /// Low limit.
        /// </summary>
        public double? LowLimit { get; init; }

        /// <summary>
        /// High limit.
        /// </summary>
        public double? HighLimit { get; init; }

        /// <summary>
        /// High-high limit.
        /// </summary>
        public double? HighHighLimit { get; init; }

        /// <summary>
        /// The reading as a percentage of the instrument range.
        /// </summary>
        public double? PercentageValue { get; init; }

        /// <summary>
        /// The engineering unit of the reading.
        /// </summary>
        public EUInformation? EngineeringUnits { get; init; }

        /// <summary>
        /// The instrument range.
        /// </summary>
        public Range? EuRange { get; init; }
    }

    /// <summary>
    /// One OPC 34100 measurement value below an OPC 40001-4 metering point.
    /// </summary>
    /// <param name="BrowseName">The measurement's browse name.</param>
    /// <param name="Value">The reading.</param>
    public sealed record MachineryMeasurementValue(QualifiedName BrowseName, Variant Value);

    /// <summary>
    /// One OPC 34100 <c>EnergyMeasurementType</c> metering point below an
    /// OPC 40001-4 resource folder.
    /// </summary>
    /// <param name="NodeId">The metering point's NodeId.</param>
    /// <param name="ApplicationTag">The operator-facing name of the point.</param>
    /// <param name="Measurements">Every measurement value below it.</param>
    public sealed record MachineryMeteringPoint(
        NodeId NodeId,
        string? ApplicationTag,
        ArrayOf<MachineryMeasurementValue> Measurements);

    /// <summary>
    /// A machinery item's OPC 40001-1 operation counters.
    /// </summary>
    public sealed record MachineryOperationCounters
    {
        /// <summary>
        /// The counters' functional group.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Hours the item has been powered on.
        /// </summary>
        public double? PowerOnDuration { get; init; }

        /// <summary>
        /// Hours the item has been operating.
        /// </summary>
        public double? OperationDuration { get; init; }

        /// <summary>
        /// The operation cycle counter. Its data type is left open by
        /// OPC 40001-1, so it is returned as read.
        /// </summary>
        public Variant OperationCycleCounter { get; init; }
    }

    /// <summary>
    /// One Device Integration <c>LifetimeVariableType</c> — an OPC 40001-1
    /// lifetime counter or the <c>EquipmentLife</c> of a piece of equipment.
    /// </summary>
    public sealed record MachineryLifetimeVariable
    {
        /// <summary>
        /// The variable's NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The variable's browse name.
        /// </summary>
        public required QualifiedName BrowseName { get; init; }

        /// <summary>
        /// The remaining life.
        /// </summary>
        public double? Remaining { get; init; }

        /// <summary>
        /// The value the counter started from.
        /// </summary>
        public double? StartValue { get; init; }

        /// <summary>
        /// The value at which the end of life is reached.
        /// </summary>
        public double? LimitValue { get; init; }

        /// <summary>
        /// The warning thresholds, when published.
        /// </summary>
        public ArrayOf<double> WarningValues { get; init; }
    }

    /// <summary>
    /// One piece of OPC 40001-1 machinery equipment.
    /// </summary>
    public sealed record MachineryEquipmentItem
    {
        /// <summary>
        /// The equipment's NodeId.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// The equipment's browse name.
        /// </summary>
        public required QualifiedName BrowseName { get; init; }

        /// <summary>
        /// The semantic identifier of the equipment kind. The only mandatory
        /// property OPC 40001-1 puts on an equipment.
        /// </summary>
        public string? MachineryEquipmentTypeId { get; init; }

        /// <summary>
        /// Free-text description.
        /// </summary>
        public LocalizedText Description { get; init; }

        /// <summary>
        /// Serial number.
        /// </summary>
        public string? SerialNumber { get; init; }

        /// <summary>
        /// The equipment's lifetime indication, when it publishes one.
        /// </summary>
        public MachineryLifetimeVariable? EquipmentLife { get; init; }
    }
}
