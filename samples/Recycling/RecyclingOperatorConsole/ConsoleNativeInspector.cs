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

using System.Globalization;
using Opc.Ua.Client;
using Opc.Ua.Extrusion.Client;
using Opc.Ua.GranulateDrying.Client;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Recycling.Model;
using Opc.Ua.Scales.Client;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Bounded read-only native inspection using explicit endpoints, stable asset identities and real companion clients.
/// </summary>
internal sealed class ConsoleNativeInspector(
    ConsoleConnections connections, ConsoleOptions options, ITelemetryContext telemetry) : IDisposable
{
    /// <inheritdoc/>
    public void Dispose() => m_gate.Dispose();

    /// <summary>
    /// Reads one independently mapped process without acquiring or renewing process membership.
    /// </summary>
    internal async Task<InspectorDto> ReadAsync(
        ProcessDto process, CommittedSnapshot snapshot, CancellationToken cancellationToken)
    {
        InspectorDto Unavailable(string detail) => new(
            process, snapshot.RunId, snapshot.ConfigurationId, ConsoleFeed.Text(snapshot.Tick), false, detail, []);
        if (!options.ProcessEndpoints.TryGetValue(process.Name, out string? endpoint))
        {
            return Unavailable("No explicit native endpoint mapping; membership is not native connectivity.");
        }
        if (!await m_gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Unavailable("Another native read is active; explicitly retry this inspection.");
        }
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            ManagedSession session = await connections.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            await using var sessionScope = session.ConfigureAwait(false);
            session.MessageContext.Factory.Builder.AddOpcUaRecyclingModel().Commit();
            int index = session.NamespaceUris.GetIndex(RecyclingModelMapping.NamespaceUri);
            if (index <= 0)
            {
                return Unavailable("Mapped endpoint does not expose the recycling model.");
            }
            ushort model = checked((ushort)index);
            var proxy = new RecyclingProcessViewTypeClient(
                session, new NodeId(Model.Objects.ProcessView, model), telemetry);
            DataValue line = await ReadRawAsync(session,
                new NodeId(Model.Variables.ProcessView_LineId, model), timeout.Token).ConfigureAwait(false);
            DataValue role = await ReadRawAsync(session,
                new NodeId(Model.Variables.ProcessView_Process, model), timeout.Token).ConfigureAwait(false);
            DataValue available = await ReadRawAsync(session,
                new NodeId(Model.Variables.ProcessView_Available, model), timeout.Token).ConfigureAwait(false);
            if (!StatusCode.IsGood(line.StatusCode) ||
                !line.WrappedValue.TryGetValue(out string lineId) || lineId != options.LineId ||
                !StatusCode.IsGood(role.StatusCode) || !role.WrappedValue.TryGetValue(out int processIndex) ||
                !Enum.TryParse(process.Name, out RecyclingProcess expected) || processIndex != (int)expected)
            {
                return Unavailable("Native line/process identity does not match the explicitly configured mapping.");
            }
            if (!StatusCode.IsGood(available.StatusCode) ||
                !available.WrappedValue.TryGetValue(out bool usable) || !usable)
            {
                return Unavailable("Process endpoint is reachable but its native projection is unavailable.");
            }
            CommittedSnapshot native = RecyclingModelMapping.FromWire(
                await proxy.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false));
            if (native.RunId != snapshot.RunId || native.ConfigurationId != snapshot.ConfigurationId)
            {
                return Unavailable("Native run/configuration is incompatible with the coordinator overview.");
            }
            var machinery = new MachineryClient(session, telemetry);
            var readings = new List<ReadingDto>();
            var budget = new InspectionBudget();
            await foreach (MachineEntry machine in machinery.EnumerateMachinesAsync(timeout.Token)
                .ConfigureAwait(false))
            {
                if (readings.Count >= 120 || budget.NodesRemaining <= 0)
                {
                    break;
                }
                MachineIdentification? identity = await machinery.ReadIdentificationAsync(
                    machine.NodeId, timeout.Token).ConfigureAwait(false);
                StageSnapshot? stage = native.Stages.Span.ToArray().FirstOrDefault(item =>
                    item.Process == expected && identity?.ProductInstanceUri ==
                        "urn:recycling-sample:" + Uri.EscapeDataString(options.LineId) + ":" + item.Name);
                if (stage == null)
                {
                    continue;
                }
                ArrayOf<ReferenceDescription> children = await ChildrenAsync(session, machine.NodeId, timeout.Token)
                    .ConfigureAwait(false);
                ReferenceDescription? stageIndex = children.Span.ToArray().FirstOrDefault(item =>
                    item.BrowseName.Name == "StageIndex" && item.BrowseName.NamespaceIndex == model);
                if (stageIndex == null)
                {
                    continue;
                }
                DataValue position = await ReadRawAsync(session,
                    ExpandedNodeId.ToNodeId(stageIndex.NodeId, session.NamespaceUris), timeout.Token)
                    .ConfigureAwait(false);
                if (!StatusCode.IsGood(position.StatusCode) ||
                    !position.WrappedValue.TryGetValue(out int actual) || actual != stage.Index)
                {
                    return Unavailable("Native asset identity and stable stage index disagree.");
                }
                await ReadEquipmentAsync(session, machine.NodeId, stage.Name, native, snapshot.Tick,
                    readings, budget, timeout.Token).ConfigureAwait(false);
            }
            CommittedSnapshot final = RecyclingModelMapping.FromWire(
                await proxy.ReadSnapshotAsync(timeout.Token).ConfigureAwait(false));
            if (final.RunId != native.RunId || final.ConfigurationId != native.ConfigurationId)
            {
                return Unavailable("Native reset/configuration changed during inspection; mixed data discarded.");
            }
            bool mixedTicks = final.Tick != native.Tick || native.Tick > snapshot.Tick;
            if (mixedTicks)
            {
                readings = readings.ConvertAll(static reading => reading with
                {
                    Tick = string.Empty,
                    ProjectionLagTicks = null
                });
            }
            return new InspectorDto(process, snapshot.RunId, snapshot.ConfigurationId,
                ConsoleFeed.Text(snapshot.Tick), true,
                mixedTicks
                    ? "Observation spans logical ticks; original source times retained, per-reading tick unavailable."
                    : "Native companion values are synthetic signals, not calibrated equipment telemetry.",
                readings.ToArray());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ConsoleLog.NativeFailed(telemetry.CreateLogger<ConsoleNativeInspector>(), exception);
            return Unavailable("Native endpoint unavailable or unauthorized; coordinator overview is independent.");
        }
        finally
        {
            m_gate.Release();
        }
    }

    /// <summary>
    /// Reads a bounded stage subtree and preserves bad optional DataValues instead of throwing away the inspection.
    /// </summary>
    private async Task ReadEquipmentAsync(
        ManagedSession session, NodeId root, string stageName, CommittedSnapshot native, long overviewTick,
        List<ReadingDto> readings, InspectionBudget budget, CancellationToken cancellationToken)
    {
        var pending = new Queue<(NodeId Node, string Name, int Depth)>();
        pending.Enqueue((root, stageName, 0));
        while (pending.Count > 0 && readings.Count < 120 && budget.NodesRemaining-- > 0)
        {
            (NodeId node, string name, int depth) = pending.Dequeue();
            if (!budget.Seen.Add(node))
            {
                continue;
            }
            ArrayOf<ReferenceDescription> children = await ChildrenAsync(session, node, cancellationToken)
                .ConfigureAwait(false);
            foreach (ReferenceDescription child in children.Span.ToArray())
            {
                if (readings.Count >= 120)
                {
                    break;
                }
                NodeId id = ExpandedNodeId.ToNodeId(child.NodeId, session.NamespaceUris);
                string childName = name + " / " + child.BrowseName.Name;
                if (child.NodeClass == NodeClass.Object && depth < 6 && pending.Count < 200)
                {
                    await ReadTypedEquipmentAsync(session, id, child.TypeDefinition, childName, native,
                        overviewTick, readings, cancellationToken).ConfigureAwait(false);
                    pending.Enqueue((id, childName, depth + 1));
                }
                else if (child.NodeClass == NodeClass.Variable)
                {
                    DataValue sample = await ReadRawAsync(session, id, cancellationToken).ConfigureAwait(false);
                    string? value = Scalar(sample.WrappedValue);
                    if (value == null && StatusCode.IsGood(sample.StatusCode))
                    {
                        continue;
                    }
                    string unit = string.Empty;
                    string unitQuality = StatusCodes.BadNotSupported.ToString();
                    ArrayOf<ReferenceDescription> properties = await ChildrenAsync(session, id, cancellationToken)
                        .ConfigureAwait(false);
                    ReferenceDescription? eu = properties.Span.ToArray().FirstOrDefault(static property =>
                        property.BrowseName.NamespaceIndex == 0 && property.BrowseName.Name == "EngineeringUnits");
                    if (eu != null)
                    {
                        DataValue metadata = await ReadRawAsync(session,
                            ExpandedNodeId.ToNodeId(eu.NodeId, session.NamespaceUris), cancellationToken)
                            .ConfigureAwait(false);
                        unitQuality = metadata.StatusCode.ToString();
                        if (StatusCode.IsGood(metadata.StatusCode) && metadata.WrappedValue.TryGetValue<EUInformation>(
                            out EUInformation? information, session.MessageContext) && information != null)
                        {
                            unit = information.DisplayName.Text ?? string.Empty;
                        }
                    }
                    string uri = session.NamespaceUris.GetString(child.BrowseName.NamespaceIndex) ??
                        "Unknown namespace";
                    Add(readings, childName, sample, unit,
                        uri == RecyclingModelMapping.NamespaceUri ? "Synthetic sample" : uri + " / synthetic signal",
                        native, overviewTick, unitQuality);
                }
            }
        }
    }

    /// <summary>
    /// Uses the real optional companion interfaces. A missing optional field is local to that field.
    /// </summary>
    private async Task ReadTypedEquipmentAsync(
        ManagedSession session, NodeId node, ExpandedNodeId definition, string name, CommittedSnapshot native,
        long overviewTick, List<ReadingDto> readings, CancellationToken cancellationToken)
    {
        if (readings.Count >= 120)
        {
            return;
        }
        NodeId type = ExpandedNodeId.ToNodeId(definition, session.NamespaceUris);
        try
        {
            if (type == ExpandedNodeId.ToNodeId(Opc.Ua.Pumps.ObjectTypeIds.PumpType, session.NamespaceUris))
            {
                var pumps = new PumpsClient(session, telemetry);
                var values = await pumps.ReadMeasurementsAsync(node, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                foreach (Opc.Ua.Pumps.PumpValue sample in values)
                {
                    Add(readings, name + " / " + sample.Name,
                        new DataValue(sample.Value, sample.StatusCode,
                            sample.SourceTimestamp ?? DateTime.UnixEpoch, DateTime.UnixEpoch),
                        sample.EngineeringUnits?.DisplayName.Text ?? string.Empty,
                        "OPC 40223 PumpType / synthetic signal", native, overviewTick);
                }
            }
            else if (session.NamespaceUris.GetString(type.NamespaceIndex) == Opc.Ua.Scales.Namespaces.Scales &&
                await session.NodeCache.IsTypeOfAsync(type,
                    new ScalesClient(session, telemetry).ScaleDeviceTypeId, cancellationToken).ConfigureAwait(false))
            {
                Opc.Ua.Scales.ScaleReading? weight = await new ScalesClient(session, telemetry)
                    .TryReadCurrentWeightAsync(node, cancellationToken).ConfigureAwait(false);
                if (weight != null)
                {
                    Add(readings, name + " / current gross weight",
                        new DataValue(Variant.From(weight.Gross), weight.StatusCode,
                            weight.Timestamp, DateTime.UnixEpoch),
                        weight.EngineeringUnits?.DisplayName.Text ?? string.Empty,
                        "OPC 40200 WeightItemType / synthetic signal", native, overviewTick);
                }
            }
            else if (type == ExpandedNodeId.ToNodeId(
                Opc.Ua.Extrusion.Filter.ObjectTypeIds.Filter_InterfaceType, session.NamespaceUris))
            {
                ClientRead<bool> present = await new ExtrusionClient(session, telemetry).Filter(node)
                    .IsPresentVariable().ReadAsync(cancellationToken).ConfigureAwait(false);
                Add(readings, name + " / is present", present.DataValue, string.Empty,
                    "OPC 40084-6 Filter_InterfaceType / synthetic signal", native, overviewTick);
                ClientRead<double> pressure = await new ExtrusionClient(session, telemetry).Filter(node)
                    .BackflushPressureVariable().ReadAsync(cancellationToken).ConfigureAwait(false);
                Add(readings, name + " / backflush pressure", pressure.DataValue, string.Empty,
                    "OPC 40084-6 Filter_InterfaceType / synthetic signal", native, overviewTick);
            }
            else if (type == ExpandedNodeId.ToNodeId(
                Opc.Ua.Extrusion.Extruder.ObjectTypeIds.Extruder_InterfaceType, session.NamespaceUris))
            {
                ClientRead<bool> present = await new ExtrusionClient(session, telemetry).Extruder(node)
                    .IsPresentVariable().ReadAsync(cancellationToken).ConfigureAwait(false);
                Add(readings, name + " / is present", present.DataValue, string.Empty,
                    "OPC 40084-3 Extruder_InterfaceType / synthetic signal", native, overviewTick);
            }
            else if (type == ExpandedNodeId.ToNodeId(
                Opc.Ua.Extrusion.Pelletizer.ObjectTypeIds.Pelletizer_InterfaceType, session.NamespaceUris))
            {
                ClientRead<bool> present = await new ExtrusionClient(session, telemetry).Pelletizer(node)
                    .IsPresentVariable().ReadAsync(cancellationToken).ConfigureAwait(false);
                Add(readings, name + " / is present", present.DataValue, string.Empty,
                    "OPC 40084-8 Pelletizer_InterfaceType / synthetic signal", native, overviewTick);
            }
            else if (type == ExpandedNodeId.ToNodeId(
                Opc.Ua.GranulateDrying.ObjectTypeIds.GranulateDryingDeviceType, session.NamespaceUris))
            {
                var dryer = new GranulateDryingClient(session, telemetry);
                ArrayOf<GranulateDryingBinClient> bins = await dryer.Device(node).BinsAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (GranulateDryingBinClient bin in bins.Span.ToArray())
                {
                    GranulateDryingReading<double> temperature = await bin.Temperature.ReadAsync(cancellationToken)
                        .ConfigureAwait(false);
                    Add(readings, name + " / bin temperature", temperature.DataValue, string.Empty,
                        "OPC 40082-6 GranulateDrying BinType / synthetic signal", native, overviewTick);
                }
            }
            else if (type == ExpandedNodeId.ToNodeId(
                Opc.Ua.Vision.MachineVision.ObjectTypeIds.VisionSystemType, session.NamespaceUris))
            {
                await ReadVisionAsync(session, node, name, native, overviewTick, readings, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (ServiceResultException exception) when (IsOptionalAbsence(exception.StatusCode))
        {
            ConsoleLog.NativeOptionalUnavailable(telemetry.CreateLogger<ConsoleNativeInspector>(),
                exception.StatusCode);
            Add(readings, name + " / optional companion reading",
                DataValue.FromStatusCode(exception.StatusCode), string.Empty,
                "Optional native companion member unavailable", native, overviewTick);
        }
    }

    /// <summary>
    /// Retrieves the released machine-vision result and releases every successful result handle on all paths.
    /// </summary>
    private async Task ReadVisionAsync(
        ManagedSession session, NodeId node, string name, CommittedSnapshot native, long overviewTick,
        List<ReadingDto> readings, CancellationToken cancellationToken)
    {
        var vision = new MachineVisionClient(session, telemetry);
        ResultManagementTypeClient results = await vision.System(node).ResultsAsync(cancellationToken)
            .ConfigureAwait(false);
        (_, uint handle, _, bool simulated, _, _, _, _, _, _, _, _, _, DateTimeUtc time, _,
            ArrayOf<Variant> content, int error) = await results.GetResultComponentsByIdAsync(
                new ResultIdDataType { Id = "latest" }, 0, cancellationToken).ConfigureAwait(false);
        if (error != 0)
        {
            Add(readings, name + " / vision result", DataValue.FromStatusCode(StatusCodes.BadNoData),
                string.Empty, "OPC 40100 result domain error " + error, native, overviewTick);
            return;
        }
        int releaseStatus = 0;
        try
        {
            Add(readings, name + " / simulated result",
                new DataValue(Variant.From(simulated), StatusCodes.Good, time.ToDateTime(), DateTime.UnixEpoch),
                string.Empty, "OPC 40100 ResultManagementType", native, overviewTick);
            if (content.Count > 0 && content[0].TryGetValue<ExecutionDataType>(
                out ExecutionDataType? execution, session.MessageContext) && execution != null)
            {
                Add(readings, name + " / inspected execution",
                    new DataValue(Variant.From(execution.Id), StatusCodes.Good,
                        time.ToDateTime(), DateTime.UnixEpoch),
                    string.Empty, "OPC 40100 result payload / synthetic sample execution", native, overviewTick);
            }
        }
        finally
        {
            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            releaseStatus = await results.ReleaseResultHandleAsync(handle, release.Token).ConfigureAwait(false);
        }
        if (releaseStatus != 0)
        {
            throw new ServiceResultException(StatusCodes.BadUnexpectedError,
                "Machine vision result handle release failed.");
        }
    }

    /// <summary>
    /// Reads raw DataValues so BadNoData and Uncertain remain individually visible.
    /// </summary>
    private static async Task<DataValue> ReadRawAsync(
        ManagedSession session, NodeId node, CancellationToken cancellationToken)
    {
        ReadResponse response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
            [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }], cancellationToken)
            .ConfigureAwait(false);
        return response.Results.Count == 1 ? response.Results[0] :
            throw new ServiceResultException(StatusCodes.BadUnexpectedError);
    }

    /// <summary>
    /// Browses only hierarchical children and excludes controls from the reading interface.
    /// </summary>
    private static ValueTask<ArrayOf<ReferenceDescription>> ChildrenAsync(
        ManagedSession session, NodeId parent, CancellationToken cancellationToken) =>
        new Browser(session, new BrowserOptions
        {
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            ResultMask = (uint)BrowseResultMask.All
        }).BrowseAsync(parent, cancellationToken);

    /// <summary>
    /// Enforces a total cross-machine reading budget without changing source quality or units.
    /// </summary>
    private static void Add(
        List<ReadingDto> readings, string name, DataValue sample, string unit, string origin,
        CommittedSnapshot native, long overviewTick, string unitQuality = "MetadataUnavailable")
    {
        if (readings.Count < 120)
        {
            readings.Add(new ReadingDto(name, StatusCode.IsBad(sample.StatusCode) ? null : Scalar(sample.WrappedValue),
                unit, sample.StatusCode.ToString(), sample.SourceTimestamp.ToDateTime(), origin, native.RunId,
                ConsoleFeed.Text(native.Tick),
                native.Tick <= overviewTick ? ConsoleFeed.Text(overviewTick - native.Tick) : null, unitQuality));
        }
    }

    /// <summary>
    /// Recognizes missing optional members, not transport/session failures.
    /// </summary>
    private static bool IsOptionalAbsence(StatusCode status) =>
        status == StatusCodes.BadNoData || status == StatusCodes.BadNotSupported ||
        status == StatusCodes.BadNotFound || status == StatusCodes.BadNodeIdUnknown ||
        status == StatusCodes.BadNoMatch;

    /// <summary>
    /// Formats only known scalar UA types without boxed-value access or reflection.
    /// </summary>
    private static string? Scalar(Variant value)
    {
        if (value.TryGetValue(out bool flag))
        {
            return flag ? "true" : "false";
        }
        if (value.TryGetValue(out string text))
        {
            return text;
        }
        if (value.TryGetValue(out LocalizedText localized))
        {
            return localized.Text;
        }
        if (value.TryGetValue(out double number))
        {
            return number.ToString("R", CultureInfo.InvariantCulture);
        }
        if (value.TryGetValue(out float single))
        {
            return single.ToString("R", CultureInfo.InvariantCulture);
        }
        if (value.TryGetValue(out long integer))
        {
            return ConsoleFeed.Text(integer);
        }
        if (value.TryGetValue(out ulong unsigned))
        {
            return unsigned.ToString(CultureInfo.InvariantCulture);
        }
        if (value.TryGetValue(out int small))
        {
            return ConsoleFeed.Text(small);
        }
        return value.TryGetValue(out uint unsignedSmall)
            ? unsignedSmall.ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Shared traversal budget across all selected process stages.
    /// </summary>
    private sealed class InspectionBudget
    {
        /// <summary>
        /// Remaining traversed nodes.
        /// </summary>
        internal int NodesRemaining { get; set; } = 200;

        /// <summary>
        /// Nodes already traversed during this inspection.
        /// </summary>
        internal HashSet<NodeId> Seen { get; } = [];
    }

    /// <summary>Only one active native inspection, with no unbounded wait queue.</summary>
    private readonly SemaphoreSlim m_gate = new(1, 1);
}
