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
