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
        /// The optional variables a device does not publish are left at their defaults in the
        /// snapshot; a bad status for a variable the device does publish is thrown as a
        /// <see cref="ServiceResultException"/>. <see cref="AutoIdReadOptions"/> narrows the read.
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
                NodeClass.Variable,
                cancellationToken).ConfigureAwait(false);
            var byName = new Dictionary<(int Namespace, string Name), NodeId>();
            for (int index = 0; index < children.Count; index++)
            {
                ReferenceDescription child = children[index];
                var id = ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
                if (!id.IsNull && child.BrowseName.Name != null)
                {
                    byName[(child.BrowseName.NamespaceIndex, child.BrowseName.Name)] = id;
                }
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
                if (StatusCode.IsBad(value.StatusCode) && !required)
                {
                    // An optional variable the session may not read is reported as absent.
                    return new DataValue(Variant.Null, StatusCodes.BadNotFound);
                }
                if (StatusCode.IsBad(value.StatusCode))
                {
                    throw ServiceResultException.Create(
                        value.StatusCode,
                        "Reading {0} of {1} failed.",
                        name,
                        reader.BrowseName.Name ?? string.Empty);
                }
                return value;
            }

            DataValue lastData = Value(BrowseNames.LastScanData, false);
            DataValue lastTimestamp = Value(BrowseNames.LastScanTimestamp, false);
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
                LastScanTimestamp = lastTimestamp.WrappedValue.TryGetValue(out DateTimeUtc timestamp)
                    ? timestamp
                    : default,
                Manufacturer = Value(Di.BrowseNames.Manufacturer, false).WrappedValue
                    .TryGetValue(out LocalizedText manufacturer) ? manufacturer : default,
                Model = Value(Di.BrowseNames.Model, false).WrappedValue
                    .TryGetValue(out LocalizedText model) ? model : default,
                SerialNumber = TextOf(Value(Di.BrowseNames.SerialNumber, false))
            };
        }

        private static string TextOf(DataValue value)
        {
            return value.WrappedValue.TryGetValue(out string text) ? text ?? string.Empty : string.Empty;
        }
    }
}
