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

namespace Opc.Ua.MachineTool.WMTP
{
    /// <summary>
    /// Named raw readings preserve quality and both OPC UA timestamps. Missing optional members have IsNull set.
    /// </summary>
    public sealed record WmtpMeasurementReading
    {
        /// <summary>
        /// Gets the measurement object identifier.
        /// </summary>
        public NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the actual inherited AnalogSignal variable for bounded value observation.
        /// This is distinct from the measurement object targeted by configuration methods.
        /// </summary>
        public NodeId ValueNodeId { get; init; }

        /// <summary>
        /// Gets its namespace-qualified application name.
        /// </summary>
        public QualifiedName BrowseName { get; init; }

        /// <summary>
        /// Gets the original scalar type, quality and timestamps of the analog signal.
        /// </summary>
        public DataValue Sample { get; init; }

        /// <summary>
        /// Gets the actual engineering unit.
        /// </summary>
        public DataValue EngineeringUnits { get; init; }

        /// <summary>
        /// Gets the nominal range.
        /// </summary>
        public DataValue EURange { get; init; }

        /// <summary>
        /// Gets the instrument range, when published.
        /// </summary>
        public DataValue InstrumentRange { get; init; }

        /// <summary>
        /// Gets the sensor resolution, when published.
        /// </summary>
        public DataValue ValuePrecision { get; init; }

        /// <summary>
        /// Gets the released physical measurement code.
        /// </summary>
        public DataValue TypeOfMeasurement { get; init; }

        /// <summary>
        /// Gets the released sample category code.
        /// </summary>
        public DataValue TypeOfSample { get; init; }

        /// <summary>
        /// Gets the published UInt32 trigger interval.
        /// </summary>
        public DataValue TriggerSettings { get; init; }

        /// <summary>
        /// Gets the published Double delta condition.
        /// </summary>
        public DataValue DeltaCondition { get; init; }

        /// <summary>
        /// Gets acquisition-time metadata independent of server receipt.
        /// </summary>
        public DataValue Timestamp { get; init; }

        /// <summary>
        /// Gets the device sample index.
        /// </summary>
        public DataValue Index { get; init; }

        /// <summary>
        /// Gets the measurement window.
        /// </summary>
        public DataValue MeasurementPeriod { get; init; }

        /// <summary>
        /// Gets the internal sensor update interval.
        /// </summary>
        public DataValue InternalUpdateInterval { get; init; }

        /// <summary>
        /// Gets relative uncertainty in percent.
        /// </summary>
        public DataValue RelativeUncertainty { get; init; }

        /// <summary>
        /// Gets absolute uncertainty in the measurement unit.
        /// </summary>
        public DataValue AbsoluteUncertainty { get; init; }

        /// <summary>
        /// Gets the inherited ProcessValues status.
        /// </summary>
        public DataValue Status { get; init; }

        /// <summary>
        /// Gets the accumulated duration above high or high-high.
        /// </summary>
        public DataValue OperationAboveHigh { get; init; }

        /// <summary>
        /// Gets the accumulated duration above high-high.
        /// </summary>
        public DataValue OperationAboveHighHigh { get; init; }
    }

    /// <summary>
    /// Device metadata with explicit optional absence and original quality/time rather than fabricated defaults.
    /// </summary>
    public sealed record WmtpPeripheralSnapshot
    {
        /// <summary>
        /// Gets the peripheral identifier.
        /// </summary>
        public NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the component manufacturer.
        /// </summary>
        public DataValue Manufacturer { get; init; }

        /// <summary>
        /// Gets the component serial number.
        /// </summary>
        public DataValue SerialNumber { get; init; }

        /// <summary>
        /// Gets calibration mode when present.
        /// </summary>
        public DataValue CalibrationMode { get; init; }

        /// <summary>
        /// Gets the battery's raw percent ProcessValue.
        /// </summary>
        public DataValue Battery { get; init; }

        /// <summary>
        /// Gets the license description, not any transferred bytes.
        /// </summary>
        public DataValue LicenseInformation { get; init; }

        /// <summary>
        /// Gets the license lifetime.
        /// </summary>
        public DataValue LicenseValidity { get; init; }

        /// <summary>
        /// Gets the next service date.
        /// </summary>
        public DataValue NextServiceDate { get; init; }

        /// <summary>
        /// Gets the last service date.
        /// </summary>
        public DataValue LastService { get; init; }

        /// <summary>
        /// Gets the lifecycle code.
        /// </summary>
        public DataValue LifeCycleStatus { get; init; }

        /// <summary>
        /// Gets remaining device lifetime.
        /// </summary>
        public DataValue RemainingLifetime { get; init; }

        /// <summary>
        /// Gets remaining cycles.
        /// </summary>
        public DataValue RemainingCycles { get; init; }

        /// <summary>
        /// Gets the service countdown.
        /// </summary>
        public DataValue NextService { get; init; }

        /// <summary>
        /// Gets the Machinery operation duration in hours.
        /// </summary>
        public DataValue OperationHours { get; init; }

        /// <summary>
        /// Gets operation duration since service in milliseconds.
        /// </summary>
        public DataValue OperationSinceService { get; init; }
    }
}
