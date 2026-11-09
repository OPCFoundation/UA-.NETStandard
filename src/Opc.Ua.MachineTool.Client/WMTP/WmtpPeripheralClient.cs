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
using Opc.Ua.MachineTool.WMTP;
using Model = Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Client
{
    /// <summary>
    /// Per-peripheral workflows over generated proxies and the existing facade's bounded browsing/reading services.
    /// </summary>
    public sealed class WmtpPeripheralClient
    {
        /// <summary>
        /// Creates a workflow using an already selected WMTP-capable session.
        /// </summary>
        internal WmtpPeripheralClient(MachineToolClient owner, WirelessMachineToolPeripheralTypeClient proxy)
        {
            m_owner = owner;
            Proxy = proxy;
        }

        /// <summary>
        /// Gets the actual generated advanced proxy.
        /// </summary>
        public WirelessMachineToolPeripheralTypeClient Proxy { get; }

        /// <summary>
        /// Gets the peripheral identifier.
        /// </summary>
        public NodeId NodeId => Proxy.ObjectId;

        /// <summary>
        /// Returns the generated sensor proxy, retaining the standard Number input for delta configuration.
        /// </summary>
        public WMTPMeasurementTypeClient GetMeasurementProxy(NodeId measurementId)
        {
            m_owner.EnsureWmtpActive();
            if (measurementId.IsNull)
            {
                throw new ArgumentException("A measurement identifier is required.", nameof(measurementId));
            }
            return new WMTPMeasurementTypeClient(m_owner.Session, measurementId, m_owner.Telemetry);
        }

        /// <summary>
        /// Resolves the work dataset without creating another session.
        /// </summary>
        public async ValueTask<WmtpRecordClient> GetWorkRecordsAsync(CancellationToken cancellationToken = default)
        {
            m_owner.EnsureWmtpActive();
            WMTPWorkCycleDataTypeClient work =
                await Proxy.GetWMTPWorkCycleDataAsync(m_owner.Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Work-cycle records are not published.");
            return new WmtpRecordClient(m_owner, work, null);
        }

        /// <summary>
        /// Resolves the separate service dataset without creating another session.
        /// </summary>
        public async ValueTask<WmtpRecordClient> GetServiceRecordsAsync(CancellationToken cancellationToken = default)
        {
            m_owner.EnsureWmtpActive();
            WMTPServiceCycleDataTypeClient service =
                await Proxy.GetWMTPServiceCycleDataAsync(m_owner.Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Service-cycle records are not published.");
            return new WmtpRecordClient(m_owner, null, service);
        }

        /// <summary>
        /// Discovers the bounded sensor inventory, including subtypes, and retains raw value quality/time.
        /// </summary>
        public async ValueTask<ArrayOf<WmtpMeasurementReading>> ReadMeasurementsAsync(
            int maximumMeasurements = 256, CancellationToken cancellationToken = default)
        {
            m_owner.EnsureWmtpActive();
            if (maximumMeasurements is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMeasurements));
            }
            FolderTypeClient folder =
                await Proxy.GetMeasurementsAsync(m_owner.Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "Measurements is mandatory.");
            var readings = new List<WmtpMeasurementReading>();
            NodeId measurementType = NodeId.Create(Model.ObjectTypes.WMTPMeasurementType,
                Model.Namespaces.WMTP, m_owner.Session.NamespaceUris);
            await foreach (ReferenceDescription reference in m_owner.BrowseWmtpChildrenAsync(
                folder.ObjectId, cancellationToken).ConfigureAwait(false))
            {
                NodeId type = ExpandedNodeId.ToNodeId(reference.TypeDefinition, m_owner.Session.NamespaceUris);
                NodeId id = ExpandedNodeId.ToNodeId(reference.NodeId, m_owner.Session.NamespaceUris);
                if (type.IsNull || id.IsNull ||
                    !await m_owner.Session.NodeCache.IsTypeOfAsync(type, measurementType, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                if (readings.Count == maximumMeasurements)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The sensor inventory exceeds the requested bound.");
                }
                readings.Add(await ReadMeasurementAsync(id, reference.BrowseName, cancellationToken)
                    .ConfigureAwait(false));
            }
            return readings.ToArrayOf();
        }

        /// <summary>
        /// Reads nameplate, battery, lifecycle, calibration and lifetime values while preserving optional absence.
        /// </summary>
        public async ValueTask<WmtpPeripheralSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
        {
            ArrayOf<NodeId> roots = await ResolveAsync(NodeId,
            [
                Name("DeviceInformation"), Name("DeviceConfiguration"),
                Name("LifetimeCounters", Opc.Ua.Machinery.Namespaces.Machinery),
                Name("OperationCounters", Opc.Ua.Machinery.Namespaces.Machinery)
            ], cancellationToken).ConfigureAwait(false);
            RequireMandatory(roots[0]);
            ArrayOf<NodeId> information = await ResolveAsync(roots[0],
            [
                Name("Identification", Opc.Ua.Di.Namespaces.OpcUaDi), Name("BatteryLevel"),
                Name("LicenseInformation"), Name("NextServiceDate"), Name("LastService"), Name("LifeCycleStatus")
            ], cancellationToken).ConfigureAwait(false);
            RequireMandatory(information[0]);
            ArrayOf<NodeId> identity = await ResolveAsync(information[0],
                [Name("Manufacturer", Opc.Ua.Di.Namespaces.OpcUaDi),
                    Name("SerialNumber", Opc.Ua.Di.Namespaces.OpcUaDi)],
                cancellationToken).ConfigureAwait(false);
            RequireMandatory(identity[0]);
            RequireMandatory(identity[1]);
            ArrayOf<NodeId> calibration = await ResolveAsync(roots[1], [Name("CalibrationMode")], cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<NodeId> battery = await ResolveAsync(information[1],
                [Name("AnalogSignal", Opc.Ua.PADIM.Namespaces.PADIM)], cancellationToken).ConfigureAwait(false);
            if (!information[1].IsNull)
            {
                RequireMandatory(battery[0]);
            }
            ArrayOf<NodeId> lifetime = await ResolveAsync(roots[2],
                [Name("LicenseValidity"), Name("RemainingLifetimeCounter"), Name("RemainingCycleCounter"),
                    Name("NextServiceCounter")], cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> operation = await ResolveAsync(roots[3],
                [Name("OperationDuration", Opc.Ua.Di.Namespaces.OpcUaDi), Name("OperationSinceLastServiceCounter")],
                cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadAsync(
                [identity[0], identity[1], calibration[0], battery[0], information[2], lifetime[0], information[3],
                    information[4], information[5], lifetime[1], lifetime[2], lifetime[3], operation[0], operation[1]],
                cancellationToken).ConfigureAwait(false);
            return new WmtpPeripheralSnapshot
            {
                NodeId = NodeId, Manufacturer = values[0], SerialNumber = values[1], CalibrationMode = values[2],
                Battery = values[3], LicenseInformation = values[4], LicenseValidity = values[5],
                NextServiceDate = values[6], LastService = values[7], LifeCycleStatus = values[8],
                RemainingLifetime = values[9], RemainingCycles = values[10], NextService = values[11],
                OperationHours = values[12], OperationSinceService = values[13]
            };
        }

        /// <summary>
        /// Requests a calibration transition. Cancellation only cancels this client's local request.
        /// </summary>
        public async ValueTask SetCalibrationAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            ConfigurationProxy configuration = await ConfigurationAsync(cancellationToken).ConfigureAwait(false);
            await configuration.InvokeAsync(
                Model.MethodIds.WirelessMachineToolPeripheralType_DeviceConfiguration_SwitchCalibrationMode,
                "SwitchCalibrationMode", [Variant.From(enabled ? (ushort)1 : (ushort)0)],
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends UTC time with an explicit TimeZoneDataType, not a machine-local DateTime.
        /// </summary>
        public async ValueTask SetDeviceTimeAsync(
            DateTimeUtc utcTime, TimeZoneDataType timeZone, CancellationToken cancellationToken = default)
        {
            if (timeZone == null)
            {
                throw new ArgumentNullException(nameof(timeZone));
            }
            ConfigurationProxy configuration = await ConfigurationAsync(cancellationToken).ConfigureAwait(false);
            await configuration.InvokeAsync(
                Model.MethodIds.WirelessMachineToolPeripheralType_DeviceConfiguration_SetDeviceTime,
                "SetDeviceTime", [Variant.From(utcTime), Variant.FromStructure(timeZone)],
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Transfers bytes using the released Byte[] argument, without claiming installation or activation.
        /// </summary>
        public async ValueTask TransferAsync(
            WmtpTransferKind kind, ByteString payload, int maximumBytes = 1024 * 1024,
            CancellationToken cancellationToken = default)
        {
            if (maximumBytes is < 1 or > 64 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            if (kind is not WmtpTransferKind.Firmware and not WmtpTransferKind.License)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (payload.IsNull || payload.Length == 0 || payload.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }
            ConfigurationProxy configuration = await ConfigurationAsync(cancellationToken).ConfigureAwait(false);
            ExpandedNodeId declaration = kind == WmtpTransferKind.Firmware ?
                Model.MethodIds.WirelessMachineToolPeripheralType_DeviceConfiguration_UpdateFirmware :
                Model.MethodIds.WirelessMachineToolPeripheralType_DeviceConfiguration_SendLicenseData;
            byte[] copy = payload.ToArray();
            try
            {
                await configuration.InvokeAsync(declaration,
                    kind == WmtpTransferKind.Firmware ? "UpdateFirmware" : "SendLicenseData",
                    [Variant.From(copy.ToArrayOf())], cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(copy, 0, copy.Length);
            }
        }

        /// <summary>
        /// Reads one sensor and its exact inherited namespaces rather than assuming a fixed namespace index.
        /// </summary>
        private async ValueTask<WmtpMeasurementReading> ReadMeasurementAsync(
            NodeId id, QualifiedName browseName, CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> nodes = await ResolveAsync(id,
            [
                Name("AnalogSignal", Opc.Ua.PADIM.Namespaces.PADIM),
                Name("TypeOfMeasurement"), Name("TypeOfSample"), Name("TriggerSettings"), Name("DeltaCondition"),
                Name("Timestamp"), Name("Index"), Name("MeasurementPeriod"), Name("InternalUpdateInterval"),
                Name("RelativeUncertainty"), Name("AbsoluteUncertainty"),
                Name("Status", Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues)
            ], cancellationToken).ConfigureAwait(false);
            for (int index = 0; index < 5; index++)
            {
                RequireMandatory(nodes[index]);
            }
            ArrayOf<NodeId> signal = await ResolveAsync(nodes[0],
                [new QualifiedName("EngineeringUnits"), new QualifiedName("EURange"),
                    new QualifiedName("InstrumentRange"), new QualifiedName("ValuePrecision")], cancellationToken)
                .ConfigureAwait(false);
            RequireMandatory(signal[0]);
            RequireMandatory(signal[1]);
            ArrayOf<NodeId> counters = await ResolveAsync(nodes[11],
                [Name("OperationAboveHigh"), Name("OperationAboveHighHigh")], cancellationToken).ConfigureAwait(false);
            ArrayOf<DataValue> values = await ReadAsync(
                [nodes[0], nodes[1], nodes[2], nodes[3], nodes[4], nodes[5], nodes[6], nodes[7],
                    nodes[8], nodes[9], nodes[10], nodes[11], signal[0], signal[1], signal[2], signal[3],
                    counters[0], counters[1]], cancellationToken).ConfigureAwait(false);
            return new WmtpMeasurementReading
            {
                NodeId = id, ValueNodeId = nodes[0], BrowseName = browseName,
                Sample = values[0], TypeOfMeasurement = values[1],
                TypeOfSample = values[2], TriggerSettings = values[3], DeltaCondition = values[4],
                Timestamp = values[5], Index = values[6], MeasurementPeriod = values[7],
                InternalUpdateInterval = values[8], RelativeUncertainty = values[9], AbsoluteUncertainty = values[10],
                Status = values[11], EngineeringUnits = values[12], EURange = values[13],
                InstrumentRange = values[14], ValuePrecision = values[15],
                OperationAboveHigh = values[16], OperationAboveHighHigh = values[17]
            };
        }

        /// <summary>
        /// Resolves nested declarations, preserving bad status versus absent optional child distinctions.
        /// </summary>
        private ValueTask<ArrayOf<NodeId>> ResolveAsync(
            NodeId parent, ArrayOf<QualifiedName> names, CancellationToken cancellationToken)
        {
            m_owner.EnsureWmtpActive();
            cancellationToken.ThrowIfCancellationRequested();
            return parent.IsNull ? new ValueTask<ArrayOf<NodeId>>(new NodeId[names.Count].ToArrayOf()) :
                m_owner.ResolveWmtpChildrenAsync(parent, names, cancellationToken);
        }

        /// <summary>
        /// Reads existing nodes in one request, leaving absent optional fields as DataValue.Null.
        /// </summary>
        private async ValueTask<ArrayOf<DataValue>> ReadAsync(
            ArrayOf<NodeId> nodes, CancellationToken cancellationToken)
        {
            var requests = new List<ReadValueId>();
            var positions = new List<int>();
            for (int index = 0; index < nodes.Count; index++)
            {
                if (!nodes[index].IsNull)
                {
                    requests.Add(new ReadValueId { NodeId = nodes[index], AttributeId = Attributes.Value });
                    positions.Add(index);
                }
            }
            var values = new DataValue[nodes.Count];
            if (requests.Count != 0)
            {
                ArrayOf<ReadValueId> request = requests.ToArrayOf();
                ReadResponse response = await m_owner.Session.ReadAsync(
                    null, 0, TimestampsToReturn.Both, request, cancellationToken).ConfigureAwait(false);
                ClientBase.ValidateResponse(response.Results, request);
                ClientBase.ValidateDiagnosticInfos(response.DiagnosticInfos, request);
                for (int index = 0; index < positions.Count; index++)
                {
                    values[positions[index]] = response.Results[index];
                }
            }
            return values.ToArrayOf();
        }

        /// <summary>
        /// Resolves the optional configuration folder before issuing any method.
        /// </summary>
        private async ValueTask<ConfigurationProxy> ConfigurationAsync(CancellationToken cancellationToken)
        {
            m_owner.EnsureWmtpActive();
            FolderTypeClient folder =
                await Proxy.GetDeviceConfigurationAsync(m_owner.Telemetry, cancellationToken).ConfigureAwait(false) ??
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Device configuration is not published.");
            return new ConfigurationProxy(m_owner.Session, folder.ObjectId, m_owner.Telemetry);
        }

        /// <summary>
        /// Constructs a namespace-qualified name without appending nonexistent server namespaces.
        /// </summary>
        private QualifiedName Name(string name, string namespaceUri = Model.Namespaces.WMTP)
        {
            int index = m_owner.Session.NamespaceUris.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "A required namespace is absent.");
            }
            return new QualifiedName(name, (ushort)index);
        }

        /// <summary>
        /// Rejects incomplete mandatory instance structure.
        /// </summary>
        private static void RequireMandatory(NodeId nodeId)
        {
            if (nodeId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError, "A mandatory WMTP node is absent.");
            }
        }

        /// <summary>
        /// Adapts nested Folder methods, which have no generated ObjectType proxy, to the existing proxy protocol.
        /// </summary>
        private sealed class ConfigurationProxy : ObjectTypeClient
        {
            /// <summary>
            /// Creates a typed protocol adapter over the resolved configuration object.
            /// </summary>
            internal ConfigurationProxy(ISessionClient session, NodeId objectId, ITelemetryContext telemetry)
                : base(session, objectId, telemetry)
            {
            }

            /// <summary>
            /// Uses the same declaration-id and instance-fallback mechanism as generated method proxies.
            /// </summary>
            internal async ValueTask InvokeAsync(
                ExpandedNodeId declaration, string name, ArrayOf<Variant> arguments,
                CancellationToken cancellationToken)
            {
                _ = await CallMethodAsync(
                    ExpandedNodeId.ToNodeId(declaration, Session.MessageContext.NamespaceUris),
                    Model.Namespaces.WMTP, name, cancellationToken, arguments.ToArray() ?? []).ConfigureAwait(false);
            }
        }

        /// <summary>The owning MachineTool facade, not a second connection or WMTP profile.</summary>
        private readonly MachineToolClient m_owner;
    }
}
