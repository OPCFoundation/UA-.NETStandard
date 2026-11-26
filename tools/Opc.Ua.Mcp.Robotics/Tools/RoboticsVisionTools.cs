// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools that combine Vision perception with Robot Intent actuation on
    /// one OPC UA session.
    /// </summary>
    [McpServerToolType]
    public sealed class RoboticsVisionTools
    {
        /// <summary>
        /// Runs one Vision inference and submits the resulting pick.
        /// </summary>
        [McpServerTool(Name = "robotics_vision_pick")]
        [Description("Runs a single Vision inference on a pipeline, selects one detection " +
            "deterministically, and submits exactly one piece of Robot Intent work on the same OPC UA " +
            "session: a Pick intent when no destination is given, or a two-step released Pick/Place " +
            "mission when it is. Use it to close the perception-to-motion loop in one call instead of " +
            "chaining vision_run_inference with robotics_submit_pick. Filters detections by exact " +
            "detectionId, exact classLabel, and minimumConfidence, then selects the highest confidence " +
            "detection with ordinal detectionId tie-breaking. Reports the full perception provenance - " +
            "result, pipeline, sensor, model version, frame, and the selected detection with its pose - " +
            "alongside the authoritative submission outcome. Refusals such as ParameterInvalid, " +
            "CapabilityNotSupported, SafetyLimitExceeded, ControlNotOwned, or QueueFull are returned " +
            "verbatim. Command authority is never acquired as a side effect, and the tool never waits " +
            "for completion, retries, or cancels.")]
        public static Task<VisionGuidedPickResult> VisionPickAsync(
            VisionGuidedRoboticsManager manager,
            [Description("The vision-guided pick request.")] VisionGuidedPickRequest request,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(manager);

            return manager.PickAsync(request, ct);
        }
    }
}
