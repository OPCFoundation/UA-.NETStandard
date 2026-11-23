// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.MachineTool.WMTP;

namespace CompanionSuite
{
    /// <summary>
    /// Supplies one independent simulated WMTP acquisition; no Bluetooth or device configuration is attempted.
    /// </summary>
    internal sealed class SimulatedPeripheral : IWmtpDeviceProvider, IWmtpMeasurementProvider, IWmtpAuthorization
    {
        /// <summary>
        /// Captures immutable device-specific observations.
        /// </summary>
        internal SimulatedPeripheral(int number, DateTimeUtc acquired)
        {
            m_number = number;
            m_acquired = acquired;
        }

        /// <summary>
        /// Gets a bounded temperature channel whose value and metadata are independently acquired.
        /// </summary>
        internal static WmtpMeasurementDefinition Measurement => new()
        {
            Name = "Temperature",
            Kind = WmtpMeasurementKind.Temperature,
            EngineeringUnits = new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 4408652,
                DisplayName = new LocalizedText("°C")
            },
            EURange = new Range { Low = 0, High = 100 },
            InstrumentRange = new Range { Low = -40, High = 150 }
        };

        /// <summary>
        /// Gets one independent retained acquisition, not a synthesized read-response header.
        /// </summary>
        internal WMTPOutputDataType Record => new()
        {
            Index = (uint)m_number,
            ActualValue = 200.0 + m_number,
            Timestamp = m_acquired,
            TypeOfMeasurement = (uint)WmtpMeasurementKind.Temperature,
            EngineeringUnits = Measurement.EngineeringUnits,
            EURange = new Range { Low = 0, High = 300 },
            InstrumentRange = new Range { Low = -40, High = 400 },
            SignalTag = "Temperature",
            Definition = "Explicitly simulated retained acquisition"
        };

        /// <inheritdoc/>
        public ValueTask<WmtpDeviceSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<WmtpDeviceSnapshot>(new WmtpDeviceSnapshot
            {
                Observation = new DataValue(Variant.Null, StatusCodes.Good, m_acquired),
                Battery = new DataValue(Variant.From(70.0 + m_number), StatusCodes.Good, m_acquired)
            });
        }

        /// <inheritdoc/>
        public ValueTask<WmtpMeasurementSample> ReadAsync(
            string sensor, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sensor != "Temperature")
            {
                throw new ServiceResultException(StatusCodes.BadNoEntryExists);
            }
            return new ValueTask<WmtpMeasurementSample>(new WmtpMeasurementSample
            {
                Value = new DataValue(Variant.From(20.0 + m_number), StatusCodes.Good, m_acquired),
                Index = (uint)m_number
            });
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> ConfigureAsync(
            string sensor, uint triggerMilliseconds, Variant delta, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc/>
        public ValueTask<StatusCode> AuthorizeAsync(
            WmtpRequest request, WmtpOperation operation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<StatusCode>(operation == WmtpOperation.ReadRecords
                ? StatusCodes.Good : StatusCodes.BadUserAccessDenied);
        }

        /// <summary>The independent peripheral identity.</summary>
        private readonly int m_number;

        /// <summary>The explicit acquisition timestamp.</summary>
        private readonly DateTimeUtc m_acquired;
    }
}
