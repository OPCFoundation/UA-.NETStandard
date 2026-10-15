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

using System.Text.Json.Serialization;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Browser state; exact integer quantities are invariant decimal strings.
/// </summary>
internal sealed record ConsoleState(
    string Connection, string Detail, bool Available, DateTime? ReceivedUtc, DateTime? SourceUtc,
    string Quality, string LineId, string RunId, string Scenario, string ConfigurationId,
    string Revision, string Tick, string LogicalSeconds, string Lifecycle, string BlockingReason,
    SummaryDto Summary, ProcessDto[] Processes, HoldDto[] Holds, TrendDto[] Trends, CommandResult[] Commands);

/// <summary>
/// Exact cumulative production totals, without invented capacities or instantaneous rates.
/// </summary>
internal sealed record SummaryDto(
    string? InputMg, string? WipMg, string? PackagedMg, string? ElectricalJoules, string? ThermalJoules,
    int? PackageCount, int? HeldCount);

/// <summary>
/// Coordinator membership is independent of console-to-process connectivity.
/// </summary>
internal sealed record ProcessDto(
    string Name, bool Initialized, DateTime? LeaseExpiresUtc, bool NativeConfigured, StageDto[] Stages);

/// <summary>
/// Synthetic per-stage counters and explicitly optional observations.
/// </summary>
internal sealed record StageDto(
    int Index, string Name, string State, bool Enabled, bool JamAlarm, bool StrandBreakAlarm,
    string InventoryMg, string TransferredMg, string ElectricalJoules, string ThermalJoules, string Executions,
    double? Ph, DateTime? PhSourceUtc, string Origin);

/// <summary>
/// Persistent material inspection, distinct from an equipment fault.
/// </summary>
internal sealed record HoldDto(
    string Id, string Tick, int Stage, string Reason, string Disposition, string DisposedBy,
    string DispositionRevision);

/// <summary>
/// A bounded logical-time observation; gaps never imply interpolation.
/// </summary>
internal sealed record TrendDto(
    string RunId, string Tick, string LogicalSeconds, DateTime ReceivedUtc, bool Gap,
    string WipMg, string PackagedMg, string ElectricalJoules, string ThermalJoules);

/// <summary>
/// Coherence-validated supplementary equipment inspection.
/// </summary>
internal sealed record InspectorDto(
    ProcessDto Process, string RunId, string ConfigurationId, string Tick, bool NativeAvailable,
    string NativeDetail, ReadingDto[] Readings);

/// <summary>
/// Individually qualified and timestamped native reading.
/// </summary>
internal sealed record ReadingDto(
    string Name, string? Value, string Unit, string Quality, DateTime? SourceUtc, string Origin,
    string RunId, string Tick, string? ProjectionLagTicks, string UnitQuality = "MetadataUnavailable");

/// <summary>
/// Exact milligram mass components, summed in C#.
/// </summary>
internal sealed record MassDto(
    string NaturalPolymer, string ColoredPolymer, string DensePolymer, string Labels, string Dirt,
    string Water, string Dry, string Total);

/// <summary>
/// Source contribution retained by the simulator.
/// </summary>
internal sealed record SourceDto(string SourceId, string SourceZone, MassDto Mass);

/// <summary>
/// A run-scoped parcel, package or residue with inherited holds and sources.
/// </summary>
internal sealed record MaterialDto(
    string Id, string Tick, string Kind, int Stage, string Form, MassDto Mass,
    string[] HoldIds, SourceDto[] Sources);

/// <summary>
/// Bounded material page; cursor identifiers are never JavaScript numbers.
/// </summary>
internal sealed record MaterialPage(string RunId, string? NextAfter, MaterialDto[] Items);

/// <summary>
/// A typed genealogy execution, not a browser reconstruction.
/// </summary>
internal sealed record ExecutionDto(
    string Id, string Tick, int Stage, string[] Inputs, string[] Outputs, MassDto Input,
    MassDto Addition, MassDto Output, MassDto Reject, MassDto Evaporation);

/// <summary>
/// Bounded typed genealogy page.
/// </summary>
internal sealed record HistoryPage(string RunId, string? NextAfter, ExecutionDto[] Items);

/// <summary>
/// Retained explicit command envelope; retry changes only the Retry flag.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CommandRequest(
    string RequestId, string RunId, string ExpectedRevision, string Kind,
    string Process = "", string HoldId = "0", string Disposition = "", bool Confirmed = false,
    bool Retry = false);

/// <summary>
/// An honest workstation receipt, including ambiguity after transport failure.
/// </summary>
internal sealed record CommandResult(
    string RequestId, string State, string Kind, string RunId, string ExpectedRevision,
    string? Revision, string? Tick, string Status, string Message, DateTime UpdatedUtc);

/// <summary>
/// Short-lived local browser session and per-session command token.
/// </summary>
internal sealed record SessionDto(string Role, string CsrfToken, DateTime ExpiresUtc);

/// <summary>
/// Explicit bounded HTTP error response.
/// </summary>
internal sealed record ErrorDto(string Error);

/// <summary>
/// NativeAOT-compatible serialization for every browser response and request.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConsoleState))]
[JsonSerializable(typeof(InspectorDto))]
[JsonSerializable(typeof(MaterialPage))]
[JsonSerializable(typeof(HistoryPage))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(SessionDto))]
[JsonSerializable(typeof(ErrorDto))]
internal sealed partial class ConsoleJsonContext : JsonSerializerContext;
