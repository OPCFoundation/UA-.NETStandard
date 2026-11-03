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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.PADIM;
using Opc.Ua.PADIM.Server;

namespace PadimSample
{
    /// <summary>
    /// Publishes deterministic acquisition values and commits demonstration-only configuration writes.
    /// </summary>
    internal sealed class DemonstrationDevice :
        IPadimValueProvider, IPadimWriteProvider, IPadimAuthorizer, IPadimOperationProvider
    {
        /// <summary>
        /// Creates the in-memory provider with explicit demonstration write authorization.
        /// </summary>
        public DemonstrationDevice(bool allowWrites)
        {
            m_allowWrites = allowWrites;
        }

        /// <summary>
        /// Creates a multivariable transmitter, positioner, discrete input and pH analyser.
        /// </summary>
        public async ValueTask ConfigureAsync(IDiPostSetupContext context)
        {
            DiNodeManager manager = context.Manager;
            QualifiedName Name(string value) => new(value, manager.InstanceNamespaceIndex);
            var transmitter = await manager.CreatePadimDeviceAsync(Name("Transmitter")).ConfigureAwait(false);
            transmitter.WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("OPC Foundation");
                identity.ManufacturerUri = "urn:OPCFoundation";
                identity.Model = new LocalizedText("Deterministic transmitter");
                identity.SerialNumber = "PADIM-DEMO-001";
                identity.ProductInstanceUri = "urn:OPCFoundation:PadimDemo:transmitter";
                identity.ProductCode = "PADIM-DEMO";
                identity.SoftwareRevision = "1";
                identity.HardwareRevision = "1";
                identity.RevisionCounter = 0;
            });
            transmitter.Device.AssetId!.Value = "Transmitter-001";
            transmitter.Device.DeviceHealth!.Value = Opc.Ua.Di.DeviceHealthEnumeration.NORMAL;
            PadimDevice device = transmitter.BindPadim();
            device.WithHealthAlarms("health", this);
            device.WithAdministration(this, this, this);
            AnalogSignalObjectState temperature = await device.AddMeasurementAsync(
                Name("Temperature"), PadimMeasurementKind.Temperature, "TT-001").ConfigureAwait(false);
            device.BindValue(temperature.AnalogSignal!, "temperature", this)
                .WithEngineeringUnits(
                    new EUInformation("degC", "http://www.opcfoundation.org/UA/units/un/cefact")
                    {
                        UnitId = 4408652
                    },
                    new Opc.Ua.Range { Low = -40, High = 150 })
                .WithSimulation(Variant.From(25f), this, this)
                .WithDamping(this, this, this);
            device.BindValue(
                ((TemperatureMeasurementVariableState)temperature.AnalogSignal!).SensorType!,
                "temperature/sensor", this);
            AnalogSignalObjectState pressure = await device.AddMeasurementAsync(
                Name("Pressure"), PadimMeasurementKind.Pressure, "PT-001").ConfigureAwait(false);
            device.BindValue(pressure.AnalogSignal!, "pressure", this).WithEngineeringUnits(
                new EUInformation("Pa", "http://www.opcfoundation.org/UA/units/un/cefact")
                {
                    UnitId = 5259596
                },
                new Opc.Ua.Range { Low = 0, High = 1_000_000 });
            var positioner = (ControlSignalObjectState)await device.AddSignalAsync(
                Name("Position"), PadimSignalKind.Control, "CV-001").ConfigureAwait(false);
            device.BindValue(positioner.ControlSignal!, "position", this)
                .WithEngineeringUnits(
                    new EUInformation("%", "http://www.opcfoundation.org/UA/units/un/cefact") { UnitId = 20529 },
                    new Opc.Ua.Range { Low = 0, High = 100 })
                .WithWrites(this, this);
            device.BindValue(positioner.ControlSignal!.Setpoint!, "position/setpoint", this).WithWrites(this, this);
            device.BindValue(positioner.ControlSignal.ActuatorType!, "position/actuator", this).WithWrites(this, this);
            device.BindValue(positioner.ControlSignal.OperatingDirection!, "position/direction", this)
                .WithWrites(this, this);
            device.WithAutoAdjustPositioner(positioner, "position", this, this);
            var discrete = (TwoStateDiscreteSignalObjectState)await device.AddSignalAsync(
                Name("Switch"), PadimSignalKind.TwoStateDiscrete, "LS-001").ConfigureAwait(false);
            device.BindValue(discrete.TwoStateDiscreteSignal!, "switch", this);
            var analyser = await manager.CreatePadimDeviceAsync(Name("PhMeter"), PadimDeviceKind.PhMeter)
                .ConfigureAwait(false);
            analyser.WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("OPC Foundation");
                identity.ManufacturerUri = "urn:OPCFoundation";
                identity.Model = new LocalizedText("Deterministic pH meter");
                identity.SerialNumber = "PADIM-DEMO-002";
                identity.ProductInstanceUri = "urn:OPCFoundation:PadimDemo:ph";
                identity.ProductCode = "PADIM-DEMO";
                identity.SoftwareRevision = "1";
                identity.HardwareRevision = "1";
                identity.RevisionCounter = 0;
            });
            analyser.Device.AssetId!.Value = "PH-001";
            analyser.Device.DeviceHealth!.Value = Opc.Ua.Di.DeviceHealthEnumeration.NORMAL;
            PadimDevice ph = analyser.BindPadim();
            ph.WithHealthAlarms("health", this);
            var phSignal = (PhSignalState)await ph.AddSignalAsync(
                Name("Ph"), PadimSignalKind.Ph, "AT-001").ConfigureAwait(false);
            ph.BindValue(phSignal.AnalogSignal!, "ph", this);
            var pat = (PatMeasurementVariableState)phSignal.AnalogSignal!;
            pat.AddPatMeasurandDescription(manager.SystemContext, property =>
                property.Value = new()
                {
                    PatDictionary = PatDictionaryEnum.User_defined,
                    Label = LocalizedText.From("pH"),
                    Id = LocalizedText.From("urn:OPCFoundation:PadimDemo:measurand:ph")
                });
            lock (m_gate)
            {
                m_values["ph/measurand"] = pat.PatMeasurandDescription!.WrappedValue;
            }
            ph.BindValue(pat.PatMeasurandDescription!, "ph/measurand", this);
            ph.WithZeroPointAdjustment(phSignal, "ph", this, this);
            ushort padimNamespace = manager.SystemContext.NamespaceUris.GetIndexOrAppend(Opc.Ua.PADIM.Namespaces.PADIM);
            QualifiedName Member(string value) => new(value, padimNamespace);
            await ph.ConfigureSignalConditionsAsync(phSignal, conditions =>
            {
                conditions.Bind(Member("PhMeasuringMethod"), "ph/method", this);
                conditions.Bind(Member("SensingElementTemperature"), "ph/temperature", this);
                conditions.Bind(Member("SensingElementImpedance"), "ph/impedance", this);
            }, context.CancellationToken).ConfigureAwait(false);
            await ph.AddCalibrationAsync(phSignal, Name("Calibration"), calibration =>
            {
                calibration.Bind(Member("CalibrationTimestamp"), "ph/calibratedAt", this);
                calibration.Bind(Member("SensorSlope"), "ph/slope", this);
                calibration.Bind(Member("SensorAsymmetryPotential"), "ph/asymmetry", this);
                calibration.Group(Member("CalibrationPointSet"), points =>
                    points.Group(Member("<CalibrationPointIdentifier>"), point =>
                    {
                        point.Bind(Member("CalibrationActualValue"), "ph/point/actual", this);
                        point.Bind(Member("CalibrationSetpoint"), "ph/point/setpoint", this);
                    }, Name("Neutral")));
            }, context.CancellationToken).ConfigureAwait(false);
            await device.RegisterAsync(context.CancellationToken).ConfigureAwait(false);
            await ph.RegisterAsync(context.CancellationToken).ConfigureAwait(false);
            lock (m_gate)
            {
                m_devices = [device, ph];
            }
            await RefreshAsync(context.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Advances the deterministic sample timestamp and publishes each bound field.
        /// </summary>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<PadimDevice> devices;
            lock (m_gate)
            {
                m_tick++;
                if (m_positionerFinishesAt.HasValue && m_tick >= m_positionerFinishesAt.Value)
                {
                    m_values["position"] = Variant.From(50f);
                    m_values["dateOfLastChange"] = Variant.From(Timestamp());
                    m_positionerFinishesAt = null;
                }
                devices = m_devices;
            }
            for (int ii = 0; ii < devices.Count; ii++)
            {
                await devices[ii].RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public ValueTask<DataValue> ReadAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!m_values.TryGetValue(key, out Variant value))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
                return new ValueTask<DataValue>(new DataValue(value, StatusCodes.Good, Timestamp()));
            }
        }

        /// <inheritdoc />
        public ValueTask<StatusCode> WriteAsync(
            string key,
            Variant value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                m_values[key] = value;
                m_values["dateOfLastChange"] = Variant.From(Timestamp());
            }
            return new ValueTask<StatusCode>(StatusCodes.Good);
        }

        /// <inheritdoc />
        public ValueTask<StatusCode> FactoryResetAsync(
            ResetModeEnum mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
        }

        /// <inheritdoc />
        public ValueTask<StatusCode> ZeroPointAdjustmentAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!StringComparer.Ordinal.Equals(key, "ph"))
                {
                    return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
                }
                m_values["ph"] = Variant.From(7f);
                m_values["ph/calibratedAt"] = Variant.From(Timestamp());
            }
            return new ValueTask<StatusCode>(StatusCodes.Good);
        }

        /// <inheritdoc />
        public ValueTask<StatusCode> AutoAdjustPositionerAsync(
            string key,
            ExecutionModeEnum mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                if (!StringComparer.Ordinal.Equals(key, "position"))
                {
                    return new ValueTask<StatusCode>(StatusCodes.BadNotSupported);
                }
                if ((int)mode == 255)
                {
                    if (!m_positionerFinishesAt.HasValue)
                    {
                        return new ValueTask<StatusCode>(StatusCodes.BadInvalidState);
                    }
                    m_positionerFinishesAt = null;
                }
                else if (m_positionerFinishesAt.HasValue)
                {
                    return new ValueTask<StatusCode>(StatusCodes.BadInvalidState);
                }
                else
                {
                    m_positionerFinishesAt = m_tick + 2;
                }
            }
            return new ValueTask<StatusCode>(StatusCodes.Good);
        }

        /// <inheritdoc />
        public ValueTask<bool> AuthorizeAsync(
            ISystemContext caller,
            NodeId nodeId,
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<bool>(m_allowWrites);
        }

        /// <summary>
        /// Returns the deterministic provider clock while its state is protected.
        /// </summary>
        private DateTime Timestamp()
        {
            return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(m_tick);
        }

        /// <summary>The explicitly enabled demonstration authorization policy.</summary>
        private readonly bool m_allowWrites;

        /// <summary>Protects the provider's in-memory values.</summary>
        private readonly System.Threading.Lock m_gate = new();

        /// <summary>The provider-owned parameter state, independent of namespace indexes.</summary>
        private readonly Dictionary<string, Variant> m_values = new(StringComparer.Ordinal)
        {
            ["temperature"] = Variant.From(21.5f),
            ["health"] = Variant.From(0),
            ["temperature/Damping"] = Variant.From(1f),
            ["temperature/sensor"] = Variant.From(0u),
            ["displayLanguage"] = Variant.From("en"),
            ["dateOfLastChange"] = Variant.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            ["pressure"] = Variant.From(101325f),
            ["position"] = Variant.From(50f),
            ["position/setpoint"] = Variant.From(50f),
            ["position/actuator"] = Variant.From(0u),
            ["position/direction"] = Variant.From(0u),
            ["switch"] = Variant.From(true),
            ["ph"] = Variant.From(7f),
            ["ph/method"] = Variant.From(0u),
            ["ph/temperature"] = Variant.From(25f),
            ["ph/impedance"] = Variant.From(100f),
            ["ph/slope"] = Variant.From(59.16f),
            ["ph/asymmetry"] = Variant.From(0f),
            ["ph/calibratedAt"] = Variant.From(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            ["ph/point/actual"] = Variant.From(7f),
            ["ph/point/setpoint"] = Variant.From(7f)
        };

        /// <summary>The device bindings borrowed from the server owner.</summary>
        private ArrayOf<PadimDevice> m_devices;

        /// <summary>The number of refresh cycles since initialization.</summary>
        private int m_tick;

        /// <summary>The refresh cycle completing an accepted virtual commissioning operation.</summary>
        private int? m_positionerFinishesAt;
    }
}
