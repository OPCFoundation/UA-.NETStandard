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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Client.Network
{
    /// <summary>
    /// Session-bound Part 22 discovery, observations and priority-mapping operations.
    /// The caller owns the session and streaming subscription; commands are never replayed.
    /// </summary>
    public sealed partial class NetworkModelClient
    {
        /// <summary>
        /// Creates a client over a caller-owned session.
        /// </summary>
        public NetworkModelClient(ISessionClient session, ITelemetryContext telemetry)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Session used for discovery and generated method proxies.
        /// </summary>
        public ISessionClient Session { get; }

        /// <summary>
        /// Telemetry passed to generated proxies.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Binds the generated standard interface proxy, including vendor subtypes.
        /// </summary>
        public IetfBaseNetworkInterfaceTypeClient GetInterface(NodeId id)
        {
            ValidateNodeId(id);
            return new IetfBaseNetworkInterfaceTypeClient(Session, id, Telemetry);
        }

        /// <summary>
        /// Binds the generated priority-mapping proxy.
        /// </summary>
        public PriorityMappingTableTypeClient GetMappingTable(NodeId id)
        {
            ValidateNodeId(id);
            return new PriorityMappingTableTypeClient(Session, id, Telemetry);
        }

        /// <summary>
        /// Binds the generated LLDP proxy and its typed local-system/port child accessors.
        /// </summary>
        public LldpInformationTypeClient GetLldp(NodeId id)
        {
            ValidateNodeId(id);
            return new LldpInformationTypeClient(Session, id, Telemetry);
        }

        /// <summary>
        /// Observes an actual published variable without taking ownership of the subscription.
        /// Each change retains source quality, acquisition time and overflow information.
        /// </summary>
        public IAsyncEnumerable<DataValueChange> ObserveAsync(
            NodeId variableId,
            IStreamingSubscription subscription,
            MonitoringOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ValidateNodeId(variableId);
            if (subscription is null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }
            return subscription.SubscribeDataChangesAsync(variableId, options, cancellationToken);
        }

        /// <summary>
        /// Reads an interface's mandatory parameters together. Missing mandatory fields and bad
        /// read quality remain explicit; they are not replaced with Up or zero-speed defaults.
        /// </summary>
        public async ValueTask<NetworkInterfaceValues> ReadInterfaceAsync(
            NodeId id,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NetworkParameterValue> parameters = await ReadParametersAsync(id, cancellationToken)
                .ConfigureAwait(false);
            return new NetworkInterfaceValues(id,
                FindParameter(parameters, BrowseNames.AdminStatus, true),
                FindParameter(parameters, BrowseNames.OperStatus, true),
                FindParameter(parameters, BrowseNames.Speed, true),
                FindParameter(parameters, BrowseNames.PhysAddress, false));
        }

        /// <summary>
        /// Reads a TSN stream's identity and state, preserving optional latency and SR-class data.
        /// </summary>
        public async ValueTask<NetworkStreamValues> ReadStreamAsync(
            NodeId id,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NetworkParameterValue> parameters = await ReadParametersAsync(id, cancellationToken)
                .ConfigureAwait(false);
            return new NetworkStreamValues(id,
                FindParameter(parameters, BrowseNames.StreamId, true),
                FindParameter(parameters, BrowseNames.StreamName, true),
                FindParameter(parameters, BrowseNames.State, true),
                FindParameter(parameters, BrowseNames.AccumulatedLatency, false),
                FindParameter(parameters, BrowseNames.SrClassId, false));
        }

        /// <summary>
        /// Reads standard PriorityMapppingEntries with its normative spelling and exact structure.
        /// Bad quality is returned without trying to interpret its payload.
        /// </summary>
        public async ValueTask<NetworkMappingValues> ReadMappingTableAsync(
            NodeId id,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NetworkParameterValue> parameters = await ReadParametersAsync(id, cancellationToken)
                .ConfigureAwait(false);
            NetworkParameterValue value = FindParameter(parameters, BrowseNames.PriorityMapppingEntries, true);
            ArrayOf<PriorityMappingEntryType> entries = default;
            if (!StatusCode.IsBad(value.DataValue.StatusCode) &&
                !value.DataValue.WrappedValue.TryGetStructure(out entries))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "PriorityMapppingEntries is not an array of PriorityMappingEntryType.");
            }
            return new NetworkMappingValues(value, entries);
        }

        /// <summary>
        /// Invokes the generated AddPriorityMappingEntry contract once, preserving operation
        /// status and argument diagnostics. PCP 255 and DSCP UInt32.MaxValue mean not used.
        /// </summary>
        public ValueTask<CallMethodResult> AddPriorityMappingEntryAsync(
            NodeId tableId,
            string mappingUri,
            string priorityLabel,
            byte pcp = byte.MaxValue,
            uint dscp = uint.MaxValue,
            CancellationToken cancellationToken = default)
        {
            if (mappingUri is null)
            {
                throw new ArgumentNullException(nameof(mappingUri));
            }
            if (priorityLabel is null)
            {
                throw new ArgumentNullException(nameof(priorityLabel));
            }
            return GetMappingTable(tableId).CallWithResultAsync(
                MethodIds.PriorityMappingTableType_AddPriorityMappingEntry,
                [new Variant(mappingUri), new Variant(priorityLabel), new Variant(pcp), new Variant(dscp)],
                cancellationToken);
        }

        /// <summary>
        /// Invokes the generated DeletePriorityMappingEntry contract once. A missing entry is
        /// BadBrowseNameInvalid; deletion is not silently treated as a successful no-op.
        /// </summary>
        public ValueTask<CallMethodResult> DeletePriorityMappingEntryAsync(
            NodeId tableId,
            string mappingUri,
            string priorityLabel,
            CancellationToken cancellationToken = default)
        {
            if (mappingUri is null)
            {
                throw new ArgumentNullException(nameof(mappingUri));
            }
            if (priorityLabel is null)
            {
                throw new ArgumentNullException(nameof(priorityLabel));
            }
            return GetMappingTable(tableId).CallWithResultAsync(
                MethodIds.PriorityMappingTableType_DeletePriorityMappingEntry,
                [new Variant(mappingUri), new Variant(priorityLabel)], cancellationToken);
        }

        /// <summary>
        /// Finds one namespace-zero standard parameter without matching a vendor's homonym.
        /// </summary>
        private static NetworkParameterValue FindParameter(
            ArrayOf<NetworkParameterValue> values,
            string name,
            bool mandatory)
        {
            foreach (NetworkParameterValue value in values)
            {
                if (value.BrowseName == new QualifiedName(name, 0))
                {
                    return value;
                }
            }
            return new NetworkParameterValue(NodeId.Null, new QualifiedName(name, 0),
                DataValue.FromStatusCode(mandatory ? StatusCodes.BadNotFound : StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Rejects an unspecified operation target.
        /// </summary>
        private static void ValidateNodeId(NodeId id)
        {
            if (id.IsNull)
            {
                throw new ArgumentException("A non-null NodeId is required.", nameof(id));
            }
        }
    }

    /// <summary>
    /// One discovered parameter, retaining both timestamps and the original value quality.
    /// </summary>
    public sealed record NetworkParameterValue(NodeId NodeId, QualifiedName BrowseName, DataValue DataValue);

    /// <summary>
    /// IETF interface values. Use Variant.TryGetValue with InterfaceAdminStatus, InterfaceOperStatus,
    /// UInt64 and String respectively after checking each field's status.
    /// </summary>
    public sealed record NetworkInterfaceValues(
        NodeId NodeId,
        NetworkParameterValue AdminStatus,
        NetworkParameterValue OperStatus,
        NetworkParameterValue Speed,
        NetworkParameterValue PhysicalAddress);

    /// <summary>
    /// Stream identity/state and optional parameters, each with independent quality and time.
    /// </summary>
    public sealed record NetworkStreamValues(
        NodeId NodeId,
        NetworkParameterValue StreamId,
        NetworkParameterValue StreamName,
        NetworkParameterValue State,
        NetworkParameterValue AccumulatedLatency,
        NetworkParameterValue SrClassId);

    /// <summary>
    /// Decoded mapping entries and their original acquisition. Entries are meaningful only
    /// when the original acquisition has readable quality.
    /// </summary>
    public sealed record NetworkMappingValues(
        NetworkParameterValue Acquisition,
        ArrayOf<PriorityMappingEntryType> Entries);
}
