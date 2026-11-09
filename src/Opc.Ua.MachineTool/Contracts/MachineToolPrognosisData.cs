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

namespace Opc.Ua.MachineTool
{
    public sealed partial record MachineToolSnapshot
    {
        /// <summary>
        /// Gets the complete explicitly supplied prognosis inventory, or null when the feature is not configured.
        /// </summary>
        public MachineToolPrognosisObservation? Prognoses { get; init; }
    }

    /// <summary>
    /// Selects a concrete released prognosis type or an explicitly configured generated companion subtype.
    /// </summary>
    public enum MachineToolPrognosisKind
    {
        /// <summary>
        /// A future maintenance action.
        /// </summary>
        Maintenance,

        /// <summary>
        /// A future manual intervention.
        /// </summary>
        ManualActivity,

        /// <summary>
        /// A future part-loading requirement.
        /// </summary>
        PartLoad,

        /// <summary>
        /// A future part-unloading requirement.
        /// </summary>
        PartUnload,

        /// <summary>
        /// A future process setup or fixture change.
        /// </summary>
        ProcessChangeover,

        /// <summary>
        /// The retained standard production-job end forecast.
        /// </summary>
        ProductionJobEnd,

        /// <summary>
        /// A future replacement of an accessible tool.
        /// </summary>
        ToolChange,

        /// <summary>
        /// A future tool load. This standard type deliberately has no ToolNodeId component.
        /// </summary>
        ToolLoad,

        /// <summary>
        /// A future tool unload.
        /// </summary>
        ToolUnload,

        /// <summary>
        /// A future utility refill or replacement.
        /// </summary>
        UtilityChange,

        /// <summary>
        /// An explicitly configured concrete companion subtype, such as GMS CalibrationPrognosisType.
        /// </summary>
        Companion
    }

    /// <summary>
    /// The complete current set of forecasts. Empty is a valid observation, not an unavailable provider.
    /// </summary>
    public sealed record MachineToolPrognosisObservation
    {
        /// <summary>
        /// Gets the current provider-reported forecasts.
        /// </summary>
        public ArrayOf<MachineToolPrognosis> Entries { get; init; }
    }

    /// <summary>
    /// A provider prediction with original acquisition quality and time; the runtime never estimates a date itself.
    /// </summary>
    public sealed record MachineToolPrognosis
    {
        /// <summary>
        /// Gets the stable application identity and browse name of this forecast.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// Gets an optional loaded companion namespace for the forecast's browse name.
        /// </summary>
        public string? NamespaceUri { get; init; }

        /// <summary>
        /// Gets the exact concrete prognosis kind.
        /// </summary>
        public MachineToolPrognosisKind Kind { get; init; }

        /// <summary>
        /// Gets the actual concrete type for a Companion forecast.
        /// </summary>
        public ExpandedNodeId CompanionTypeId { get; init; }

        /// <summary>
        /// Gets the original UtcTime prediction sample, independent of the time of publication.
        /// </summary>
        public DataValue PredictedTime { get; init; }

        /// <summary>
        /// Gets the maintenance, manual-intervention or process-change activity.
        /// </summary>
        public LocalizedText Activity { get; init; }

        /// <summary>
        /// Gets the location required by part, tool and process-change prognoses.
        /// </summary>
        public LocalizedText Location { get; init; }

        /// <summary>
        /// Gets the optional tool identifier, matching the affected tool when it is modeled.
        /// </summary>
        public string? ToolIdentifier { get; init; }

        /// <summary>
        /// Gets the optional tool name, matching the affected tool when it is modeled.
        /// </summary>
        public string? ToolName { get; init; }

        /// <summary>
        /// Gets the optional part identifier or modern output SerialNumber.
        /// </summary>
        public string? PartIdentifier { get; init; }

        /// <summary>
        /// Gets the part name or modern output ItemNumber; a resolved retained part can supply its actual name.
        /// </summary>
        public string? PartName { get; init; }

        /// <summary>
        /// Gets the actual job identifier or modern JobOrderID for a production-job end forecast.
        /// </summary>
        public string? SourceIdentifier { get; init; }

        /// <summary>
        /// Gets the utility requiring a future change.
        /// </summary>
        public string? UtilityName { get; init; }

        /// <summary>
        /// Gets an explicit affected-entity reference. When omitted, an unambiguous reported identifier resolves
        /// against this machine's prospective inventory. References never cross instance boundaries.
        /// </summary>
        public MachineToolPrognosisTarget? Target { get; init; }

        /// <summary>
        /// Gets the acquisition metadata for the descriptive and reference components.
        /// </summary>
        public MachineToolSampleInfo Sample { get; init; } = MachineToolSampleInfo.Unavailable;
    }

    /// <summary>
    /// Identifies an affected entity by an actual NodeId or stable per-instance slot keys, never by a fabricated ID.
    /// </summary>
    public sealed record MachineToolPrognosisTarget
    {
        /// <summary>
        /// Gets an existing node to validate against this machine's prospective inventory.
        /// This is an alternative to the key fields.
        /// </summary>
        public NodeId NodeId { get; init; }

        /// <summary>
        /// Gets the affected tool's machine-unique slot key.
        /// </summary>
        public string? ToolKey { get; init; }

        /// <summary>
        /// Gets the retained job slot key, or its identifier when no separate key was configured.
        /// </summary>
        public string? JobKey { get; init; }

        /// <summary>
        /// Gets the affected part's containing part-set key, used together with JobKey and PartKey.
        /// </summary>
        public string? PartSetKey { get; init; }

        /// <summary>
        /// Gets the retained part slot key.
        /// </summary>
        public string? PartKey { get; init; }
    }
}
