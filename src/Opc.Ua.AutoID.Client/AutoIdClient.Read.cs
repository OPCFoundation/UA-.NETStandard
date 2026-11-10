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

namespace Opc.Ua.AutoID.Client
{
    public sealed partial class AutoIdClient
    {
        /// <summary>
        /// Reads the state of a reader device in one browse and one read.
        /// </summary>
        /// <remarks>
        /// Every call costs one Browse of the children of the device (and BrowseNext only when the
        /// server pages the result) and one Read, whatever the options; <see cref="AutoIdReadOptions"/>
        /// only narrows the variables of the Read. The browse
        /// yields the NodeIds of the variables to monitor and the methods the device publishes. The
        /// optional variables a device does not publish are left at their defaults in the snapshot.
        /// A bad status for the required DeviceName or DeviceStatus is thrown as a
        /// <see cref="ServiceResultException"/>; unreadable optional values retain their defaults
        /// and the scan fields expose their individual statuses. To poll without browsing, pass the snapshot to
        /// <see cref="RefreshDeviceAsync"/>.
        /// </remarks>
        /// <param name="reader">The reader.</param>
        /// <param name="options">What to read; <see cref="AutoIdReadOptions.Default"/> when <see langword="null"/>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">The server rejected the browse or a read.</exception>
        public async ValueTask<AutoIdDeviceSnapshot> ReadDeviceAsync(
            AutoIdReaderEntry reader,
            AutoIdReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RequireReader(reader);
            options ??= AutoIdReadOptions.Default;
            int autoId = Session.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            int di = Session.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi);

            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(
                reader.NodeId,
                NodeClass.Variable | NodeClass.Method,
                cancellationToken).ConfigureAwait(false);
            var byName = new Dictionary<(int Namespace, string Name), NodeId>();
            var methods = new List<QualifiedName>();
            for (int index = 0; index < children.Count; index++)
            {
                ReferenceDescription child = children[index];
                var id = ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
                if (id.IsNull || child.BrowseName.Name == null)
                {
                    continue;
                }
                if (child.NodeClass == NodeClass.Method)
                {
                    methods.Add(child.BrowseName);
                }
                else
                {
                    byName[(child.BrowseName.NamespaceIndex, child.BrowseName.Name)] = id;
                }
            }
            NodeId VariableId(string name)
            {
                return byName.TryGetValue((autoId, name), out NodeId id) ? id : NodeId.Null;
            }

            var reads = new List<ReadValueId>();
            var slots = new Dictionary<string, int>();
            void Want(int ns, string name, bool wanted)
            {
                if (wanted && byName.TryGetValue((ns, name), out NodeId id))
                {
                    slots[name] = reads.Count;
                    reads.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Value });
                }
            }
            Want(autoId, BrowseNames.DeviceName, true);
            Want(autoId, BrowseNames.DeviceStatus, true);
            Want(autoId, BrowseNames.AutoIdModelVersion, options.IncludeNameplate || options.IncludeOptionalProperties);
            Want(autoId, BrowseNames.DeviceInfo, options.IncludeOptionalProperties);
            Want(autoId, BrowseNames.DeviceLocation, options.IncludeOptionalProperties);
            Want(autoId, BrowseNames.DeviceLocationName, options.IncludeOptionalProperties);
            Want(autoId, BrowseNames.AntennaNames, options.IncludeOptionalProperties);
            Want(autoId, BrowseNames.LastScanData, options.IncludeLastScan);
            Want(autoId, BrowseNames.LastScanTimestamp, options.IncludeLastScan);
            Want(di, Di.BrowseNames.Manufacturer, options.IncludeNameplate);
            Want(di, Di.BrowseNames.Model, options.IncludeNameplate);
            Want(di, Di.BrowseNames.SerialNumber, options.IncludeNameplate);

            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: reads.ToArray().ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != reads.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server returned {0} read results for {1} requested variables.",
                    response.Results.Count,
                    reads.Count);
            }

            DataValue Value(string name, bool required)
            {
                if (!slots.TryGetValue(name, out int slot))
                {
                    if (required)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNotFound,
                            "The device {0} does not expose the required variable {1}.",
                            reader.BrowseName.Name ?? string.Empty,
                            name);
                    }
                    return new DataValue(Variant.Null, StatusCodes.BadNotFound);
                }
                DataValue value = response.Results[slot];
                if (StatusCode.IsBad(value.StatusCode) && required)
                {
                    throw ServiceResultException.Create(
                        value.StatusCode,
                        "Reading {0} of {1} failed.",
                        name,
                        reader.BrowseName.Name ?? string.Empty);
                }
                return StatusCode.IsBad(value.StatusCode)
                    ? new DataValue(Variant.Null, value.StatusCode)
                    : value;
            }

            DataValue lastData = Value(BrowseNames.LastScanData, false);
            DataValue lastTimestamp = Value(BrowseNames.LastScanTimestamp, false);
            DataValue lastAntenna = Value(BrowseNames.LastScanAntenna, false);
            DataValue lastRssi = Value(BrowseNames.LastScanRSSI, false);
            DataValue deviceLocation = Value(BrowseNames.DeviceLocation, false);
            DataValue antennas = Value(BrowseNames.AntennaNames, false);
            DataValue status = Value(BrowseNames.DeviceStatus, true);
            Location? locationValue = null;
#pragma warning disable CS8600 // The generic structure accessor declares its out value as possibly null.
            if (deviceLocation.WrappedValue.TryGetValue(out Location decodedLocation, Session.MessageContext) &&
                decodedLocation is not null)
#pragma warning restore CS8600
            {
                locationValue = decodedLocation;
            }
            return new AutoIdDeviceSnapshot
            {
                NodeId = reader.NodeId,
                DeviceStatusId = VariableId(BrowseNames.DeviceStatus),
                LastScanDataId = VariableId(BrowseNames.LastScanData),
                LastScanTimestampId = VariableId(BrowseNames.LastScanTimestamp),
                LastScanAntennaId = VariableId(BrowseNames.LastScanAntenna),
                LastScanRssiId = VariableId(BrowseNames.LastScanRSSI),
                DeviceLocationId = VariableId(BrowseNames.DeviceLocation),
                Methods = methods.ToArray().ToArrayOf(),
                DeviceName = TextOf(Value(BrowseNames.DeviceName, true)),
                DeviceStatus = status.WrappedValue.TryGetValue(out DeviceStatusEnumeration deviceStatus)
                    ? deviceStatus
                    : default,
                ModelVersion = TextOf(Value(BrowseNames.AutoIdModelVersion, false)),
                DeviceInfo = TextOf(Value(BrowseNames.DeviceInfo, false)),
                DeviceLocationName = TextOf(Value(BrowseNames.DeviceLocationName, false)),
                DeviceLocation = locationValue,
                AntennaNames = antennas.WrappedValue.TryGetValue(
                    out ArrayOf<AntennaNameIdPair> names,
                    Session.MessageContext)
                    ? names
                    : default,
                LastScanData = lastData.WrappedValue,
                LastScanStatus = slots.ContainsKey(BrowseNames.LastScanData)
                    ? lastData.StatusCode
                    : StatusCodes.BadNotFound,
                LastScanTimestamp = StatusCode.IsNotBad(lastTimestamp.StatusCode) &&
                    lastTimestamp.WrappedValue.TryGetValue(out DateTimeUtc timestamp)
                    ? timestamp
                    : default,
                LastScanTimestampStatus = TimestampStatus(lastTimestamp),
                LastScanAntenna = StatusCode.IsNotBad(lastAntenna.StatusCode) &&
                    lastAntenna.WrappedValue.TryGetValue(out int antenna)
                    ? antenna
                    : null,
                LastScanAntennaStatus = TypedStatus(lastAntenna, lastAntenna.WrappedValue.TryGetValue(out int _)),
                LastScanRssi = StatusCode.IsNotBad(lastRssi.StatusCode) &&
                    lastRssi.WrappedValue.TryGetValue(out int rssi)
                    ? rssi
                    : null,
                LastScanRssiStatus = TypedStatus(lastRssi, lastRssi.WrappedValue.TryGetValue(out int _)),
                Manufacturer = Value(Di.BrowseNames.Manufacturer, false).WrappedValue
                    .TryGetValue(out LocalizedText manufacturer) ? manufacturer : default,
                Model = Value(Di.BrowseNames.Model, false).WrappedValue
                    .TryGetValue(out LocalizedText model) ? model : default,
                SerialNumber = TextOf(Value(Di.BrowseNames.SerialNumber, false))
            };
        }

        /// <summary>
        /// Reads the variables of a snapshot that change at runtime again, through the NodeIds the
        /// snapshot carries: <c>DeviceStatus</c> and, when the device publishes them,
        /// <c>LastScanData</c>, <c>LastScanTimestamp</c> and <c>DeviceLocation</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The call costs exactly one Read of one to four variables and no Browse. The client keeps no
        /// state: a caller that polls a device reads it once with <see cref="ReadDeviceAsync"/> and
        /// then passes the returned snapshot here on every cycle. All other members of the snapshot,
        /// including <see cref="AutoIdDeviceSnapshot.Methods"/> and the NodeIds, are copied unchanged.
        /// </para>
        /// <para>
        /// The snapshot is not validated against the server. When it no longer matches the device
        /// (the device or one of its variables was deleted, or the server assigned other NodeIds after
        /// a restart), the status of the Read surfaces: a bad status for <c>DeviceStatus</c>, for
        /// example <see cref="StatusCodes.BadNodeIdUnknown"/>, is thrown as a
        /// <see cref="ServiceResultException"/>, and a bad status for <c>LastScanData</c> is returned
        /// in <see cref="AutoIdDeviceSnapshot.LastScanStatus"/> with a null value. A bad
        /// <c>LastScanTimestamp</c> or <c>DeviceLocation</c> leaves that member at its default. In
        /// each of these cases the caller reads the device again with <see cref="ReadDeviceAsync"/>,
        /// which also picks up optional variables and methods the device published after the
        /// snapshot was taken.
        /// </para>
        /// </remarks>
        /// <param name="device">A snapshot returned by <see cref="ReadDeviceAsync"/>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>A copy of <paramref name="device"/> with the current values.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">The snapshot carries no <c>DeviceStatus</c> NodeId.</exception>
        /// <exception cref="ServiceResultException">
        /// The server rejected the Read, or reading <c>DeviceStatus</c> returned a bad status.
        /// </exception>
        public async ValueTask<AutoIdDeviceSnapshot> RefreshDeviceAsync(
            AutoIdDeviceSnapshot device,
            CancellationToken cancellationToken = default)
        {
            if (device is null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (device.DeviceStatusId.IsNull)
            {
                throw new ArgumentException(
                    "The snapshot carries no DeviceStatus NodeId; read the device with ReadDeviceAsync.",
                    nameof(device));
            }
            NodeId[] ids =
            [
                device.DeviceStatusId,
                device.LastScanDataId,
                device.LastScanTimestampId,
                device.DeviceLocationId
            ];
            var reads = new List<ReadValueId>(ids.Length);
            int[] slots = new int[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                slots[index] = -1;
                if (!ids[index].IsNull)
                {
                    slots[index] = reads.Count;
                    reads.Add(new ReadValueId { NodeId = ids[index], AttributeId = Attributes.Value });
                }
            }
            ReadResponse response = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: reads.ToArray().ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);
            if (response.Results.Count != reads.Count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The server returned {0} read results for {1} requested variables.",
                    response.Results.Count,
                    reads.Count);
            }
            DataValue Value(int index)
            {
                return slots[index] < 0
                    ? new DataValue(Variant.Null, StatusCodes.BadNotFound)
                    : response.Results[slots[index]];
            }

            DataValue status = Value(0);
            if (StatusCode.IsBad(status.StatusCode))
            {
                throw ServiceResultException.Create(
                    status.StatusCode,
                    "Reading {0} of {1} failed.",
                    BrowseNames.DeviceStatus,
                    device.NodeId);
            }
            DataValue lastData = Value(1);
            DataValue lastTimestamp = Value(2);
            DataValue deviceLocation = Value(3);
            Location? locationValue = null;
#pragma warning disable CS8600 // The generic structure accessor declares its out value as possibly null.
            if (StatusCode.IsNotBad(deviceLocation.StatusCode) &&
                deviceLocation.WrappedValue.TryGetValue(out Location decodedLocation, Session.MessageContext) &&
                decodedLocation is not null)
#pragma warning restore CS8600
            {
                locationValue = decodedLocation;
            }
            return device with
            {
                DeviceStatus = status.WrappedValue.TryGetValue(out DeviceStatusEnumeration deviceStatus)
                    ? deviceStatus
                    : default,
                LastScanData = StatusCode.IsBad(lastData.StatusCode) ? Variant.Null : lastData.WrappedValue,
                LastScanStatus = lastData.StatusCode,
                LastScanTimestamp = StatusCode.IsNotBad(lastTimestamp.StatusCode) &&
                    lastTimestamp.WrappedValue.TryGetValue(out DateTimeUtc timestamp)
                    ? timestamp
                    : default,
                LastScanTimestampStatus = TimestampStatus(lastTimestamp),
                LastScanAntenna = null,
                LastScanAntennaStatus = StatusCodes.BadNotFound,
                LastScanRssi = null,
                LastScanRssiStatus = StatusCodes.BadNotFound,
                DeviceLocation = locationValue
            };
        }

        /// <summary>
        /// Reads the last scan with matching acquisition timestamps, including available RFID
        /// antenna and RSSI values, without invoking a scan method.
        /// </summary>
        /// <remarks>
        /// Browses the device explicitly and retries at most three times when values change
        /// between reads. Individual bad statuses are retained; inconsistent good values cause
        /// BadDataLost. Ordinary device reads and refreshes retain their lightweight behavior.
        /// </remarks>
        /// <param name="reader">The reader whose acquisition to read.</param>
        /// <param name="cancellationToken">Cancels browsing, reading and any retries.</param>
        /// <exception cref="ServiceResultException">
        /// The server rejects the request, or good acquisition values remain inconsistent.
        /// </exception>
        public async ValueTask<AutoIdDeviceSnapshot> ReadLastScanAsync(
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            AutoIdDeviceSnapshot device = await ReadDeviceAsync(
                reader, AutoIdReadOptions.StatusOnly, cancellationToken).ConfigureAwait(false);
            NodeId[] ids =
            [
                device.LastScanDataId, device.LastScanTimestampId,
                device.LastScanAntennaId, device.LastScanRssiId
            ];
            var reads = new List<ReadValueId>();
            int[] slots = new int[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                slots[i] = -1;
                if (!ids[i].IsNull)
                {
                    slots[i] = reads.Count;
                    reads.Add(new ReadValueId { NodeId = ids[i], AttributeId = Attributes.Value });
                }
            }
            if (reads.Count == 0)
            {
                return device;
            }
            for (int attempt = 0; attempt <= k_lastScanReadAttempts; attempt++)
            {
                ReadResponse response = await Session.ReadAsync(
                    requestHeader: null,
                    maxAge: 0,
                    timestampsToReturn: TimestampsToReturn.Source,
                    nodesToRead: reads.ToArray().ToArrayOf(),
                    ct: cancellationToken).ConfigureAwait(false);
                ValidateResultCount(response.Results.Count, reads.Count, "read");
                DataValue Value(int field) => slots[field] >= 0
                    ? response.Results[slots[field]] : DataValue.FromStatusCode(StatusCodes.BadNotFound);
                DataValue data = Value(0);
                DataValue timestamp = Value(1);
                DataValue antenna = Value(2);
                DataValue rssi = Value(3);
                if (!IsLastScanConsistent(data, timestamp, antenna, rssi))
                {
                    continue;
                }
                return device with
                {
                    LastScanData = StatusCode.IsBad(data.StatusCode) ? Variant.Null : data.WrappedValue,
                    LastScanStatus = data.StatusCode,
                    LastScanTimestamp = StatusCode.IsNotBad(timestamp.StatusCode) &&
                        timestamp.WrappedValue.TryGetValue(out DateTimeUtc time) ? time : default,
                    LastScanTimestampStatus = TimestampStatus(timestamp),
                    LastScanAntenna = StatusCode.IsNotBad(antenna.StatusCode) &&
                        antenna.WrappedValue.TryGetValue(out int number) ? number : null,
                    LastScanAntennaStatus = TypedStatus(antenna, antenna.WrappedValue.TryGetValue(out int _)),
                    LastScanRssi = StatusCode.IsNotBad(rssi.StatusCode) &&
                        rssi.WrappedValue.TryGetValue(out int strength) ? strength : null,
                    LastScanRssiStatus = TypedStatus(rssi, rssi.WrappedValue.TryGetValue(out int _))
                };
            }
            throw ServiceResultException.Create(
                StatusCodes.BadDataLost,
                "LastScanData and its associated timestamp or RFID scan details changed during the read.");
        }

        /// <summary>
        /// Checks acquisition identity only where the server returned good values.
        /// </summary>
        private static bool IsLastScanConsistent(
            in DataValue lastData,
            in DataValue lastTimestamp,
            in DataValue lastAntenna,
            in DataValue lastRssi)
        {
            if (!StatusCode.IsGood(lastData.StatusCode) ||
                !StatusCode.IsGood(lastTimestamp.StatusCode) ||
                !lastTimestamp.WrappedValue.TryGetValue(out DateTimeUtc timestamp))
            {
                return true;
            }
            if (timestamp.IsNull || lastData.SourceTimestamp != timestamp || lastTimestamp.SourceTimestamp != timestamp)
            {
                return false;
            }
            return IsOptionalValueConsistent(lastAntenna, timestamp) &&
                IsOptionalValueConsistent(lastRssi, timestamp);
        }

        /// <summary>
        /// Allows omitted or unreadable optional values without hiding their statuses.
        /// </summary>
        private static bool IsOptionalValueConsistent(in DataValue value, DateTimeUtc timestamp)
        {
            return !StatusCode.IsGood(value.StatusCode) || value.SourceTimestamp == timestamp;
        }

        /// <summary>
        /// Preserves the timestamp's service status and checks its declared value type.
        /// </summary>
        private static StatusCode TimestampStatus(in DataValue value)
        {
            return StatusCode.IsBad(value.StatusCode)
                ? value.StatusCode
                : value.WrappedValue.TryGetValue(out DateTimeUtc _)
                    ? value.StatusCode
                    : StatusCodes.BadTypeMismatch;
        }

        private static string TextOf(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out string text) ? text ?? string.Empty : string.Empty;
        }

        /// <summary>The maximum number of additional reads for a changing acquisition.</summary>
        private const int k_lastScanReadAttempts = 3;
    }
}
