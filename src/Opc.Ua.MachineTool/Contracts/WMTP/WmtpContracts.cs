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
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.WMTP
{
    /// <summary>
    /// Explicit optional peripheral surfaces; None publishes only the mandatory OPC 40505 layout.
    /// </summary>
    [Flags]
    public enum WmtpFeatures
    {
        /// <summary>The mandatory peripheral layout.</summary>
        None = 0,

        /// <summary>CalibrationMode and SwitchCalibrationMode.</summary>
        Calibration = 1,

        /// <summary>SetDeviceTime, including the explicit time zone.</summary>
        DeviceTime = 2,

        /// <summary>Authorized firmware byte transfer, not installation.</summary>
        Firmware = 4,

        /// <summary>License transfer, information and validity.</summary>
        License = 8,

        /// <summary>The battery ProcessValue.</summary>
        Battery = 16,

        /// <summary>Service dates and device-reported lifecycle status.</summary>
        ServiceInformation = 32,

        /// <summary>The basic service information and all specified counters.</summary>
        AdvancedServiceInformation = 64,

        /// <summary>The work-cycle record dataset.</summary>
        WorkRecords = 128,

        /// <summary>The independent service-cycle record dataset.</summary>
        ServiceRecords = 256,

        /// <summary>All released optional peripheral features.</summary>
        All = 511
    }

    /// <summary>
    /// The released TypeOfMeasurement values, not CLR-generated OPC UA enumeration data types.
    /// </summary>
    public enum WmtpMeasurementKind : uint
    {
        /// <summary>Temperature.</summary>
        Temperature,

        /// <summary>Rotation speed.</summary>
        RotationSpeed,

        /// <summary>Humidity.</summary>
        Humidity,

        /// <summary>Acceleration.</summary>
        Acceleration,

        /// <summary>Force.</summary>
        Force,

        /// <summary>Torque.</summary>
        Torque,

        /// <summary>Position.</summary>
        Position,

        /// <summary>Dimension.</summary>
        Dimension
    }

    /// <summary>
    /// The released TypeOfSample values from OPC 40505 table 48.
    /// </summary>
    public enum WmtpSampleKind : uint
    {
        /// <summary>Unspecified sample category.</summary>
        Unspecified,

        /// <summary>Instantaneous reading.</summary>
        Instantaneous,

        /// <summary>Arithmetic mean since the start of the work cycle.</summary>
        ArithmeticMean,

        /// <summary>Root mean square since the start of the work cycle.</summary>
        RootMeanSquare,

        /// <summary>Maximum in the current work cycle.</summary>
        Maximum,

        /// <summary>Minimum in the current work cycle.</summary>
        Minimum,

        /// <summary>Moving average.</summary>
        MovingAverage
    }

    /// <summary>
    /// Explicit optional measurement metadata and behavior.
    /// </summary>
    [Flags]
    public enum WmtpMeasurementFeatures
    {
        /// <summary>Only mandatory measurement nodes.</summary>
        None = 0,

        /// <summary>Status, high and high-high duration counters and their standard references.</summary>
        LimitCounters = 1,

        /// <summary>The sensor's internal update interval.</summary>
        InternalUpdateInterval = 2,

        /// <summary>Acquisition Timestamp, Index and MeasurementPeriod.</summary>
        WorkCycleInformation = 4,

        /// <summary>Relative and absolute uncertainty.</summary>
        Uncertainty = 8,

        /// <summary>A shared ProcessValues limit alarm.</summary>
        LimitAlarm = 16,

        /// <summary>All supported optional measurement surfaces.</summary>
        All = 31
    }

    /// <summary>
    /// Optional record methods, independently selected for each dataset.
    /// </summary>
    [Flags]
    public enum WmtpRecordFeatures
    {
        /// <summary>Abort, count and complete report only.</summary>
        None = 0,

        /// <summary>Delete all records.</summary>
        DeleteAll = 1,

        /// <summary>Delete an inclusive index range.</summary>
        DeleteIndex = 2,

        /// <summary>Delete an inclusive acquisition-time range.</summary>
        DeleteTime = 4,

        /// <summary>Count an inclusive acquisition-time range.</summary>
        TimeCount = 8,

        /// <summary>Report an inclusive index range.</summary>
        ReportIndex = 16,

        /// <summary>Report an inclusive acquisition-time range.</summary>
        ReportTime = 32,

        /// <summary>Report the first stored measurement.</summary>
        ReportFirst = 64,

        /// <summary>Report the last stored measurement.</summary>
        ReportLast = 128,

        /// <summary>All optional methods.</summary>
        All = 255
    }

    /// <summary>
    /// Immutable sensor configuration. The provider uses the same numeric type for samples and delta inputs.
    /// </summary>
    public sealed record WmtpMeasurementDefinition
    {
        /// <summary>
        /// Gets the application-owned name and unique sensor tag within this peripheral.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the physical measurement kind.
        /// </summary>
        public WmtpMeasurementKind Kind { get; init; }

        /// <summary>
        /// Gets the sample category.
        /// </summary>
        public WmtpSampleKind SampleKind { get; init; } = WmtpSampleKind.Instantaneous;

        /// <summary>
        /// Gets the exact numeric wire type.
        /// </summary>
        public BuiltInType DataType { get; init; } = BuiltInType.Double;

        /// <summary>
        /// Gets the explicitly supplied engineering unit.
        /// </summary>
        public EUInformation EngineeringUnits { get; init; } = new();

        /// <summary>
        /// Gets the nominal engineering range.
        /// </summary>
        public Range EURange { get; init; } = new();

        /// <summary>
        /// Gets the instrument's measurable range.
        /// </summary>
        public Range InstrumentRange { get; init; } = new();

        /// <summary>
        /// Gets the smallest measurement increment.
        /// </summary>
        public double ValuePrecision { get; init; }

        /// <summary>
        /// Gets the equipment-specific sensor definition.
        /// </summary>
        public string Definition { get; init; } = string.Empty;

        /// <summary>
        /// Gets the initial measurement interval in whole milliseconds, representable by TriggerSettings.
        /// </summary>
        public uint TriggerMilliseconds { get; init; } = 1000;

        /// <summary>
        /// Gets the exact initial delta input, retaining its numeric type.
        /// </summary>
        public Variant Delta { get; init; } = Variant.From(0d);

        /// <summary>
        /// Gets the optional features to publish.
        /// </summary>
        public WmtpMeasurementFeatures Features { get; init; }

        /// <summary>
        /// Gets the optional high threshold.
        /// </summary>
        public double? High { get; init; }

        /// <summary>
        /// Gets the optional high-high threshold.
        /// </summary>
        public double? HighHigh { get; init; }

        /// <summary>
        /// Gets the percentage measurement uncertainty.
        /// </summary>
        public double RelativeUncertainty { get; init; }

        /// <summary>
        /// Gets uncertainty in the measurement unit.
        /// </summary>
        public double AbsoluteUncertainty { get; init; }

        /// <summary>
        /// Gets the sensor's internal sampling interval in milliseconds.
        /// </summary>
        public double InternalUpdateInterval { get; init; }

        /// <summary>
        /// Gets the maximum acquisition age before a usable value becomes UncertainLastUsableValue.
        /// </summary>
        public TimeSpan StaleAfter { get; init; } = TimeSpan.FromMinutes(1);
    }

    /// <summary>
    /// A real acquisition, independent of when the server receives it.
    /// </summary>
    public sealed record WmtpMeasurementSample
    {
        /// <summary>
        /// Gets the original numeric type, quality and acquisition timestamp; bad-null means unavailable.
        /// </summary>
        public DataValue Value { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the acquisition's device index.
        /// </summary>
        public uint Index { get; init; }

        /// <summary>
        /// Gets the acquisition window in milliseconds.
        /// </summary>
        public double MeasurementPeriod { get; init; }
    }

    /// <summary>
    /// Device-reported lifetime information, using the standard DI lifetime semantics.
    /// </summary>
    public sealed record WmtpLifetime
    {
        /// <summary>
        /// Gets the remaining value, retaining the provider's numeric type.
        /// </summary>
        public Variant Remaining { get; init; }

        /// <summary>
        /// Gets the initial value in the same numeric type.
        /// </summary>
        public Variant Start { get; init; }

        /// <summary>
        /// Gets the expiration limit in the same numeric type.
        /// </summary>
        public Variant Limit { get; init; }

        /// <summary>
        /// Gets the explicit unit of the lifetime counter.
        /// </summary>
        public EUInformation EngineeringUnits { get; init; } = new();
    }

    /// <summary>
    /// Peripheral information. Absent selected optional observations are rejected before publication.
    /// </summary>
    public sealed record WmtpDeviceSnapshot
    {
        /// <summary>
        /// Gets the sample quality and source time applied to device metadata.
        /// </summary>
        public DataValue Observation { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the actual calibration state when provided.
        /// </summary>
        public bool? CalibrationMode { get; init; }

        /// <summary>
        /// Gets the original battery sample, expressed as percent.
        /// </summary>
        public DataValue Battery { get; init; }

        /// <summary>
        /// Gets the license description, never the transferred license bytes.
        /// </summary>
        public string? LicenseInformation { get; init; }

        /// <summary>
        /// Gets the license lifetime.
        /// </summary>
        public WmtpLifetime? LicenseValidity { get; init; }

        /// <summary>
        /// Gets the next service date when provided.
        /// </summary>
        public DateTimeUtc NextServiceDate { get; init; }

        /// <summary>
        /// Gets the last service date when provided.
        /// </summary>
        public DateTimeUtc LastService { get; init; }

        /// <summary>
        /// Gets the released lifecycle code: zero green, one yellow, two red; null means not provided.
        /// </summary>
        public uint? LifeCycleStatus { get; init; }

        /// <summary>
        /// Gets the remaining time before end of life.
        /// </summary>
        public WmtpLifetime? RemainingLifetime { get; init; }

        /// <summary>
        /// Gets the remaining work cycles.
        /// </summary>
        public WmtpLifetime? RemainingCycles { get; init; }

        /// <summary>
        /// Gets the remaining time before service.
        /// </summary>
        public WmtpLifetime? NextService { get; init; }

        /// <summary>
        /// Gets the total device operation duration in hours, as defined by Machinery.
        /// </summary>
        public double? OperationHours { get; init; }

        /// <summary>
        /// Gets the operation duration since service in milliseconds, the WMTP Duration unit.
        /// </summary>
        public double? OperationSinceService { get; init; }
    }

    /// <summary>
    /// Reads device observations without inferring activity, battery charge or elapsed equipment lifetime.
    /// </summary>
    public interface IWmtpDeviceProvider
    {
        /// <summary>
        /// Acquires the actual device metadata.
        /// </summary>
        ValueTask<WmtpDeviceSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Acquires and configures equipment sensors. A successful configuration must atomically accept both settings.
    /// </summary>
    public interface IWmtpMeasurementProvider
    {
        /// <summary>
        /// Acquires a reading for the specified per-peripheral sensor.
        /// </summary>
        ValueTask<WmtpMeasurementSample> ReadAsync(string sensor, CancellationToken cancellationToken = default);

        /// <summary>
        /// Accepts both proposed settings atomically, or returns a bad status without applying either.
        /// Cancellation before acceptance must leave the equipment unchanged.
        /// </summary>
        ValueTask<StatusCode> ConfigureAsync(
            string sensor, uint triggerMilliseconds, Variant delta, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements actual calibration-mode changes.
    /// </summary>
    public interface IWmtpCalibrationProvider
    {
        /// <summary>
        /// Accepts the requested mode atomically or refuses it with a bad status.
        /// </summary>
        ValueTask<StatusCode> SetCalibrationAsync(bool enabled, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements setting device time, retaining the explicit offset and daylight-saving flag.
    /// </summary>
    public interface IWmtpDeviceTimeProvider
    {
        /// <summary>
        /// Accepts the UTC time and zone or refuses them without changing equipment time.
        /// </summary>
        ValueTask<StatusCode> SetTimeAsync(
            DateTimeUtc utcTime, TimeZoneDataType timeZone, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The byte-transfer purpose; success never asserts installation or license activation.
    /// </summary>
    public enum WmtpTransferKind
    {
        /// <summary>Firmware data.</summary>
        Firmware,

        /// <summary>License data.</summary>
        License
    }

    /// <summary>
    /// Explicitly accepts bounded bytes for transfer to equipment; no flash or license engine is implied.
    /// </summary>
    public interface IWmtpTransferProvider
    {
        /// <summary>
        /// Accepts the transfer or returns a bad status. Implementations must not log or retain secrets unnecessarily.
        /// </summary>
        ValueTask<StatusCode> TransferAsync(
            WmtpTransferKind kind, ByteString payload, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Operations authorized by the application before any equipment callback.
    /// </summary>
    public enum WmtpOperation
    {
        /// <summary>Sensor trigger or delta change.</summary>
        ConfigureMeasurement,

        /// <summary>Calibration change.</summary>
        Calibrate,

        /// <summary>Device-time change.</summary>
        SetTime,

        /// <summary>Firmware transfer.</summary>
        TransferFirmware,

        /// <summary>License transfer.</summary>
        TransferLicense,

        /// <summary>Reading or counting records.</summary>
        ReadRecords,

        /// <summary>Deleting records.</summary>
        DeleteRecords,

        /// <summary>Aborting this peripheral's active operation.</summary>
        Abort
    }

    /// <summary>
    /// Caller identity supplied to authorization; no invented operation identifiers appear on the wire.
    /// </summary>
    public sealed record WmtpRequest
    {
        /// <summary>
        /// Gets the targeted peripheral.
        /// </summary>
        public NodeId PeripheralId { get; init; }

        /// <summary>
        /// Gets the calling session, or NodeId.Null for explicit in-process calls.
        /// </summary>
        public NodeId SessionId { get; init; }

        /// <summary>
        /// Gets the authenticated user when the transport supplied one.
        /// </summary>
        public IUserIdentity? Identity { get; init; }

        /// <summary>
        /// Gets the active operation's owner for an abort authorization decision.
        /// </summary>
        public NodeId ActiveSessionId { get; init; }
    }

    /// <summary>
    /// Application authorization, including explicit policy for aborting another caller's operation.
    /// </summary>
    public interface IWmtpAuthorization
    {
        /// <summary>
        /// Returns Good to grant the operation or a bad status to refuse it.
        /// </summary>
        ValueTask<StatusCode> AuthorizeAsync(
            WmtpRequest request, WmtpOperation operation, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Record selection with inclusive bounds. First and Last refer to storage order.
    /// </summary>
    public enum WmtpRecordSelection
    {
        /// <summary>Every record, never an implicit page.</summary>
        All,

        /// <summary>Inclusive device-index range.</summary>
        Index,

        /// <summary>Inclusive acquisition-time range.</summary>
        Time,

        /// <summary>The first stored measurement.</summary>
        First,

        /// <summary>The last stored measurement.</summary>
        Last
    }

    /// <summary>
    /// A query against one already selected peripheral dataset.
    /// </summary>
    public sealed record WmtpRecordQuery
    {
        /// <summary>
        /// Gets the selection.
        /// </summary>
        public WmtpRecordSelection Selection { get; init; }

        /// <summary>
        /// Gets the inclusive first device index.
        /// </summary>
        public uint FromIndex { get; init; }

        /// <summary>
        /// Gets the inclusive last device index.
        /// </summary>
        public uint ToIndex { get; init; }

        /// <summary>
        /// Gets the inclusive start acquisition time.
        /// </summary>
        public DateTimeUtc FromTime { get; init; }

        /// <summary>
        /// Gets the inclusive end acquisition time.
        /// </summary>
        public DateTimeUtc ToTime { get; init; }
    }

    /// <summary>
    /// One dataset's provider. Reads return independent copies and never consume records.
    /// </summary>
    public interface IWmtpRecordStore
    {
        /// <summary>
        /// Counts the selected records without consuming them.
        /// </summary>
        ValueTask<uint> CountAsync(WmtpRecordQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies every selected record or fails explicitly when its encoding/resource limits would be exceeded.
        /// </summary>
        ValueTask<ArrayOf<WMTPOutputDataType>> ReadAsync(
            WmtpRecordQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically deletes the selected records or fails without mutation.
        /// </summary>
        ValueTask DeleteAsync(WmtpRecordQuery query, CancellationToken cancellationToken = default);
    }
}
