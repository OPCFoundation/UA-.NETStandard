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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// MCP tools for discovering Vision sensors, inference pipelines, and
    /// coordinate frames on a connected OPC UA server.
    /// </summary>
    [McpServerToolType]
    public sealed class VisionDiscoveryTools
    {
        /// <summary>
        /// Lists sensors exposed under the Vision root.
        /// </summary>
        [McpServerTool(Name = "vision_list_sensors")]
        [Description("Lists the cameras and 3D sensors this server exposes, under Server/Vision/Sensors. " +
            "Start here: a sensor is what produces imagery, so its NodeId is the input to " +
            "vision_read_sensor, vision_get_frame and vision_read_extrinsic_calibration. Prefer " +
            "vision_list_pipelines when you want the inference bindings that interpret imagery, and " +
            "vision_list_frames when you want coordinate frames. Discovery only: never requests command " +
            "authority, and yields nothing on a server without the Vision namespace. Returns one entry " +
            "per sensor.")]
        public static async Task<ArrayOf<VisionNodeEntry>> ListSensorsAsync(
            VisionClientAccessor accessor,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionClient client = accessor.CreateClient(sessionName);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in client.EnumerateSensorsAsync(ct).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return [.. entries];
        }

        /// <summary>
        /// Lists inference pipelines exposed under the Vision root.
        /// </summary>
        [McpServerTool(Name = "vision_list_pipelines")]
        [Description("Lists the inference pipelines under Server/Vision/Pipelines. A pipeline is where " +
            "perception actually runs: it binds a sensor to a deployment and publishes results, and its " +
            "NodeId is what vision_run_inference, vision_start_continuous_inference, vision_read_pipeline " +
            "and the vision_submit_* feedback tools all take. Read its InferenceLocation to learn whether " +
            "the work happens on the server or off it. Prefer vision_list_sensors when you want the imaging " +
            "hardware rather than the perception bound to it. Discovery only: never requests command " +
            "authority, and yields nothing on a server without the Vision namespace. Returns one entry per " +
            "pipeline.")]
        public static async Task<ArrayOf<VisionNodeEntry>> ListPipelinesAsync(
            VisionClientAccessor accessor,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionClient client = accessor.CreateClient(sessionName);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in client.EnumeratePipelinesAsync(ct).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return [.. entries];
        }

        /// <summary>
        /// Lists coordinate frames exposed under the Vision root.
        /// </summary>
        [McpServerTool(Name = "vision_list_frames")]
        [Description("Lists the coordinate frames under Server/Vision/Frames — the named right-handed " +
            "systems that give a pose meaning, such as the robot base, the flange, the tool centre point " +
            "and the camera. Their names are what vision_compose_pose takes to re-express a detection from " +
            "camera coordinates into something a robot can act on. Prefer vision_list_calibrations when you " +
            "want the transforms measured between two frames rather than the frames themselves. Discovery " +
            "only: never requests command authority, and yields nothing on a server without the Vision " +
            "namespace. Returns one entry per frame.")]
        public static async Task<ArrayOf<VisionNodeEntry>> ListFramesAsync(
            VisionClientAccessor accessor,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionClient client = accessor.CreateClient(sessionName);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in client.EnumerateFramesAsync(ct).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return [.. entries];
        }

        /// <summary>
        /// Lists the calibrations attached to a sensor.
        /// </summary>
        [McpServerTool(Name = "vision_list_calibrations")]
        [Description("Lists the calibrations attached to a Vision sensor either directly via HasCalibration " +
            "or nested under the sensor's Calibrations folder. Use this to find the extrinsic calibration " +
            "NodeId needed by vision_read_extrinsic_calibration for a hand-eye lookup. Use " +
            "vision_read_sensor instead when you only need identity, imaging members and the mount frame. " +
            "Discovery only; never requests authority. Returns an array of VisionNodeEntry.")]
        public static async Task<ArrayOf<VisionNodeEntry>> ListCalibrationsAsync(
            VisionClientAccessor accessor,
            [Description("Sensor NodeId, for example ns=2;s=Vision/Sensors/Camera1.")] string sensorNodeId,
            [Description("Session name to use; defaults to the only active session.")] string? sessionName = null,
            CancellationToken ct = default)
        {
            VisionSensorClient sensor = accessor.OpenSensor(sensorNodeId, sessionName);
            var entries = new List<VisionNodeEntry>();
            await foreach (VisionNodeEntry entry in sensor.EnumerateCalibrationsAsync(ct).ConfigureAwait(false))
            {
                entries.Add(entry);
            }
            return [.. entries];
        }
    }
}
