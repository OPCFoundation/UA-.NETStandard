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
using Opc.Ua.MachineTool.WMTP;

namespace MachineToolSample.Server
{
    /// <summary>
    /// An explicitly selected simulation adapter for one peripheral, never a physical device driver.
    /// </summary>
    internal sealed class DemoPeripheral :
        IWmtpDeviceProvider, IWmtpMeasurementProvider, IWmtpCalibrationProvider,
        IWmtpDeviceTimeProvider, IWmtpTransferProvider
    {
        /// <summary>
        /// Creates independent simulated acquisition and request state.
        /// </summary>
        public DemoPeripheral(ServerOptions options, TimeProvider clock)
        {
            m_options = options;
            m_clock = clock;
            m_initialTime = clock.GetUtcNow();
            foreach (WmtpMeasurementKind kind in MeasurementKinds)
            {
                m_settings.Add(kind.ToString(), (1000, Variant.From(0d)));
            }
        }

        /// <summary>
        /// Gets the eight released measurement kinds without reflection-based enumeration.
        /// </summary>
        internal static ArrayOf<WmtpMeasurementKind> MeasurementKinds { get; } =
        [
            WmtpMeasurementKind.Temperature, WmtpMeasurementKind.RotationSpeed,
            WmtpMeasurementKind.Humidity, WmtpMeasurementKind.Acceleration,
            WmtpMeasurementKind.Force, WmtpMeasurementKind.Torque,
            WmtpMeasurementKind.Position, WmtpMeasurementKind.Dimension
        ];

        /// <inheritdoc/>
        public ValueTask<WmtpDeviceSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                DateTimeUtc time = m_clock.GetUtcNow() + m_deviceOffset;
                var life = new WmtpLifetime
                {
                    Start = Variant.From(100d),
                    Remaining = Variant.From(80d),
                    Limit = Variant.From(0d),
                    EngineeringUnits = Unit(4740434, "h")
                };
                return new ValueTask<WmtpDeviceSnapshot>(new WmtpDeviceSnapshot
                {
                    Observation = new DataValue(Variant.From(1u), StatusCodes.Good, time),
                    CalibrationMode = m_calibrating,
                    Battery = new DataValue(Variant.From(75d), StatusCodes.Good, time),
                    LicenseInformation = "Simulation entitlement; transferred bytes do not activate a license.",
                    LicenseValidity = life,
                    LastService = m_initialTime,
                    NextServiceDate = m_clock.GetUtcNow().AddDays(30),
                    LifeCycleStatus = 0,
                    RemainingLifetime = life,
                    RemainingCycles = life with { EngineeringUnits = Unit(4404786, "cycle") },
                    NextService = life,
                    OperationHours = 12,
                    OperationSinceService = 60000
                });
            }
        }

        /// <inheritdoc/>
        public ValueTask<WmtpMeasurementSample> ReadAsync(
            string sensor, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!m_settings.ContainsKey(sensor))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown, "Unknown simulated sensor.");
                }
                return new ValueTask<WmtpMeasurementSample>(new WmtpMeasurementSample
                {
                    Value = new DataValue(Variant.From(21.5), StatusCodes.Good,
                        m_options.FreezeMeasurements ? m_initialTime : m_clock.GetUtcNow() + m_deviceOffset),
                    Index = 1,
                    MeasurementPeriod = 50
                });
            }
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> ConfigureAsync(
            string sensor, uint triggerMilliseconds, Variant delta, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!m_settings.ContainsKey(sensor))
                {
                    return new ValueTask<StatusCode>(StatusCodes.BadNodeIdUnknown);
                }
                StatusCode accepted = Accept(WmtpOperation.ConfigureMeasurement, cancellationToken);
                if (StatusCode.IsGood(accepted))
                {
                    m_settings[sensor] = (triggerMilliseconds, delta.Copy());
                }
                return new ValueTask<StatusCode>(accepted);
            }
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> SetCalibrationAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                StatusCode accepted = Accept(WmtpOperation.Calibrate, cancellationToken);
                if (StatusCode.IsGood(accepted))
                {
                    m_calibrating = enabled;
                }
                return new ValueTask<StatusCode>(accepted);
            }
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> SetTimeAsync(
            DateTimeUtc utcTime, TimeZoneDataType timeZone, CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                StatusCode accepted = Accept(WmtpOperation.SetTime, cancellationToken);
                if (StatusCode.IsGood(accepted))
                {
                    m_deviceOffset = utcTime - new DateTimeUtc(m_clock.GetUtcNow());
                    Console.WriteLine(FormattableString.Invariant(
                        $"SIMULATED_TIME_ACCEPTED utc={utcTime} offsetMinutes={timeZone.Offset}"));
                }
                return new ValueTask<StatusCode>(accepted);
            }
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> TransferAsync(
            WmtpTransferKind kind, ByteString payload, CancellationToken cancellationToken = default)
        {
            lock (m_gate)
            {
                StatusCode accepted = payload.Length is < 1 or > 1024
                    ? StatusCodes.BadEncodingLimitsExceeded
                    : Accept(kind == WmtpTransferKind.Firmware
                        ? WmtpOperation.TransferFirmware : WmtpOperation.TransferLicense, cancellationToken);
                if (StatusCode.IsGood(accepted))
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"SIMULATED_TRANSFER_ACCEPTED kind={kind} size={payload.Length}; no installation/activation."));
                }
                return new ValueTask<StatusCode>(accepted);
            }
        }

        /// <summary>
        /// Uses the released measurement kinds and explicit UN/CEFACT units.
        /// </summary>
        internal static WmtpMeasurementDefinition Definition(WmtpMeasurementKind kind, bool advanced)
        {
            (int id, string symbol) = kind switch
            {
                WmtpMeasurementKind.Temperature => (4408652, "degC"),
                WmtpMeasurementKind.RotationSpeed => (5394509, "rpm"),
                WmtpMeasurementKind.Humidity => (20529, "%"),
                WmtpMeasurementKind.Acceleration => (5067595, "m/s2"),
                WmtpMeasurementKind.Force => (5129559, "N"),
                WmtpMeasurementKind.Torque => (20053, "N.m"),
                WmtpMeasurementKind.Position => (5067858, "m"),
                _ => (5066068, "mm")
            };
            return new WmtpMeasurementDefinition
            {
                Name = kind.ToString(),
                Kind = kind,
                EngineeringUnits = Unit(id, symbol),
                Definition = "Application-owned simulation; not a hardware reading.",
                Features = advanced ? WmtpMeasurementFeatures.All : WmtpMeasurementFeatures.None,
                EURange = new Opc.Ua.Range { Low = 0, High = 100 },
                InstrumentRange = new Opc.Ua.Range { Low = -100, High = 200 },
                ValuePrecision = 0.125,
                InternalUpdateInterval = 25,
                RelativeUncertainty = 0.5,
                AbsoluteUncertainty = 0.25,
                High = 80,
                HighHigh = 90,
                StaleAfter = TimeSpan.FromSeconds(2)
            };
        }

        private StatusCode Accept(WmtpOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_options.AllowCommands)
            {
                return StatusCodes.BadUserAccessDenied;
            }
            return m_options.RefuseCommands || !m_accepted.Add(operation)
                ? StatusCodes.BadResourceUnavailable : StatusCodes.Good;
        }

        private static EUInformation Unit(int id, string symbol) => new()
        {
            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
            UnitId = id,
            DisplayName = new LocalizedText(symbol)
        };

        private readonly ServerOptions m_options;
        private readonly TimeProvider m_clock;
        private readonly DateTimeUtc m_initialTime;
        private readonly Lock m_gate = new();
        private readonly HashSet<WmtpOperation> m_accepted = [];
        private readonly Dictionary<string, (uint Interval, Variant Delta)> m_settings = new(StringComparer.Ordinal);
        private bool m_calibrating;
        private TimeSpan m_deviceOffset;
    }

    /// <summary>
    /// Makes simulated writes opt-in and never authorizes aborting a different session's work.
    /// </summary>
    internal sealed class DemoAuthorization(ServerOptions options) : IWmtpAuthorization
    {
        /// <inheritdoc/>
        public ValueTask<StatusCode> AuthorizeAsync(
            WmtpRequest request, WmtpOperation operation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool sameOwner = request.ActiveSessionId.IsNull || request.ActiveSessionId == request.SessionId;
            bool allowed = operation == WmtpOperation.ReadRecords ||
                (options.AllowCommands && (operation != WmtpOperation.Abort || sameOwner));
            return new ValueTask<StatusCode>(allowed ? StatusCodes.Good : StatusCodes.BadUserAccessDenied);
        }
    }
}
