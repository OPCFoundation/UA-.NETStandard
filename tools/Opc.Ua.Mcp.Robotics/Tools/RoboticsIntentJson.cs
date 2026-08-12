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
using System.Globalization;
using System.Text.Json;
using Opc.Ua.Mcp.Serialization;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Mcp.Tools
{
    internal static class RoboticsIntentJson
    {
        public static IntentDataType BuildIntent(string intentKind, string? intentJson, uint axisCount = 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(intentKind);

            using JsonDocument document = ParseObject(intentJson);
            JsonElement root = document.RootElement;
            IntentDataType intent = intentKind.Trim().ToLowerInvariant() switch
            {
                "jointmove" or "joint_move" => BuildJointMove(root, axisCount),
                "linearmove" or "linear_move" => BuildLinearMove(root),
                "circularmove" or "circular_move" => BuildCircularMove(root),
                "trajectory" => BuildTrajectory(root),
                "cartesianpath" or "cartesian_path" => BuildCartesianPath(root),
                "force" => BuildForce(root),
                "arcweld" or "arc_weld" => BuildProcess(RobotIntentBuilder.ArcWeld(), root),
                "spotweld" or "spot_weld" => BuildProcess(RobotIntentBuilder.SpotWeld(), root),
                "dispense" => BuildProcess(RobotIntentBuilder.Dispense(), root),
                "fasten" => BuildProcess(RobotIntentBuilder.Fasten(), root),
                "palletise" or "palletize" => BuildProcess(RobotIntentBuilder.Palletise(), root),
                "surfacefinish" or "surface_finish" => BuildProcess(RobotIntentBuilder.SurfaceFinish(), root),
                "grasp" => RobotIntentBuilder.Grasp(GetNode(root, "tool"), GetDouble(root, "force")).Build(),
                "release" => RobotIntentBuilder.Release(GetNode(root, "tool")).Build(),
                "pick" => RobotIntentBuilder.Pick(GetNode(root, "source"), GetNode(root, "tool")).Build(),
                "place" => RobotIntentBuilder.Place(GetNode(root, "destination"), GetNode(root, "tool")).Build(),
                "toolchange" or "tool_change" => RobotIntentBuilder.ToolChange(
                    GetNode(root, "tool"),
                    GetNode(root, "dockStation")).Build(),
                "setoutput" or "set_output" => RobotIntentBuilder.SetOutput(
                    GetNode(root, "output"),
                    GetVariant(root, "value", GetString(root, "dataType"))).Build(),
                "callprogram" or "call_program" => BuildCallProgram(root),
                "wait" => BuildWait(root),
                _ => throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Unknown Robot Intent kind '{intentKind}'."),
                    nameof(intentKind))
            };
            ApplyCommon(root, intent);
            return intent;
        }

        public static ArrayOf<MissionStepDataType> BuildMissionSteps(string? stepsJson)
        {
            if (string.IsNullOrWhiteSpace(stepsJson))
            {
                return [];
            }

            using JsonDocument document = ParseDocument(stepsJson, nameof(stepsJson), "Mission steps JSON");
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Mission steps JSON must be an array.", nameof(stepsJson));
            }

            var steps = new List<MissionStepDataType>();
            uint sequence = 1;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                JsonElement intentElement = GetRequiredProperty(element, "intent");
                string kind = GetRequiredString(intentElement, "kind");
                string payload = intentElement.GetRawText();
                var step = new MissionStepDataType
                {
                    StepId = GetRequiredString(element, "stepId"),
                    SequenceId = GetUInt(element, "sequenceId", sequence),
                    Released = GetBool(element, "released", false),
                    Intent = BuildIntent(kind, payload),
                    ErrorPolicy = GetEnum(element, "errorPolicy", ErrorPolicyEnum.Abort),
                    FallbackStepId = GetString(element, "fallbackStepId") ?? string.Empty
                };
                steps.Add(step);
                sequence++;
            }

            return [.. steps];
        }

        public static ArrayOf<MissionTransitionDataType> BuildMissionTransitions(string? transitionsJson)
        {
            if (string.IsNullOrWhiteSpace(transitionsJson))
            {
                return [];
            }

            using JsonDocument document = ParseDocument(
                transitionsJson,
                nameof(transitionsJson),
                "Mission transitions JSON");
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Mission transitions JSON must be an array.", nameof(transitionsJson));
            }

            var transitions = new List<MissionTransitionDataType>();
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                transitions.Add(new MissionTransitionDataType
                {
                    FromStepId = GetRequiredString(element, "fromStepId"),
                    ToStepId = GetRequiredString(element, "toStepId"),
                    DivergenceKind = GetEnum(element, "divergenceKind", DivergenceKindEnum.Alternative),
                    Condition = MissionCondition.Always()
                });
            }

            return [.. transitions];
        }

        private static JointMoveIntentDataType BuildJointMove(JsonElement root, uint axisCount)
        {
            JointMoveIntentBuilder builder = RobotIntentBuilder.JointMove(axisCount);
            if (root.TryGetProperty("jointTargets", out JsonElement jointTargets))
            {
                return builder.ToJoints(GetDoubleArray(jointTargets, "jointTargets")).Build();
            }

            return builder.ToPose(GetPose(root, "targetPose")).Build();
        }

        private static LinearMoveIntentDataType BuildLinearMove(JsonElement root)
        {
            return RobotIntentBuilder.LinearMove(GetPose(root, "target"), GetDouble(root, "speedFraction", 0)).Build();
        }

        private static CircularMoveIntentDataType BuildCircularMove(JsonElement root)
        {
            return RobotIntentBuilder.CircularMove(GetPose(root, "viaPoint"), GetPose(root, "target")).Build();
        }

        private static TrajectoryIntentDataType BuildTrajectory(JsonElement root)
        {
            var points = new List<TrajectoryPointDataType>();
            foreach (JsonElement point in GetRequiredArray(root, "points").EnumerateArray())
            {
                points.Add(new TrajectoryPointDataType
                {
                    TimeFromStart = GetDouble(point, "timeFromStart"),
                    Positions = GetDoubleArray(point, "positions", "points"),
                    Velocities = TryGetDoubleArray(point, "velocities"),
                    Accelerations = TryGetDoubleArray(point, "accelerations")
                });
            }

            return RobotIntentBuilder.Trajectory().WithPoints([.. points]).Build();
        }

        private static CartesianPathIntentDataType BuildCartesianPath(JsonElement root)
        {
            var waypoints = new List<PathWaypointDataType>();
            foreach (JsonElement waypoint in GetRequiredArray(root, "waypoints").EnumerateArray())
            {
                waypoints.Add(new PathWaypointDataType
                {
                    Pose = GetPose(waypoint, "pose"),
                    Blend = GetBlend(waypoint)
                });
            }

            return RobotIntentBuilder.CartesianPath().WithWaypoints([.. waypoints]).Build();
        }

        private static ForceIntentDataType BuildForce(JsonElement root)
        {
            ForceIntentDataType intent = RobotIntentBuilder.Force(
                GetDoubleArray(root, "direction", "force"),
                GetDouble(root, "contactForce")).Build();
            intent.FrameId = GetString(root, "frameId") ?? string.Empty;
            intent.MaxDistance = GetDouble(root, "maxDistance", 0);
            intent.HoldForce = GetBool(root, "holdForce", false);
            return intent;
        }

        private static IntentDataType BuildProcess<TIntent>(ProcessIntentBuilder<TIntent> builder, JsonElement root)
            where TIntent : ProcessIntentDataType
        {
            NodeId processProgram = GetNode(root, "processProgram");
            if (!processProgram.IsNull)
            {
                builder.WithProcessProgram(processProgram);
            }
            builder.WithAttributes(GetAttributes(root, "attributes"));
            TIntent intent = builder.Build();
            ApplyProcessFields(root, intent);
            return intent;
        }

        private static CallProgramIntentDataType BuildCallProgram(JsonElement root)
        {
            CallProgramIntentDataType intent = RobotIntentBuilder.CallProgram(GetNode(root, "program")).Build();
            intent.Arguments = GetAttributes(root, "arguments");
            return intent;
        }

        private static WaitIntentDataType BuildWait(JsonElement root)
        {
            WaitIntentDataType intent = RobotIntentBuilder.Wait(GetDouble(root, "duration")).Build();
            intent.Signal = GetNode(root, "signal");
            return intent;
        }

        private static void ApplyCommon(JsonElement root, IntentDataType intent)
        {
            intent.IntentId = GetString(root, "intentId") ?? intent.IntentId;
            string? label = GetString(root, "label");
            if (!string.IsNullOrEmpty(label))
            {
                intent.Label = new LocalizedText(label);
            }
            intent.BufferMode = GetEnum(root, "bufferMode", intent.BufferMode);
            intent.BlockingMode = GetEnum(root, "blockingMode", intent.BlockingMode);
            if (intent is MotionIntentDataType motion)
            {
                motion.ToolFrame = GetNode(root, "toolFrame");
                motion.Constraints = GetConstraints(root);
                motion.Blend = GetBlend(root);
            }
        }

        private static void ApplyProcessFields<TIntent>(JsonElement root, TIntent intent)
            where TIntent : ProcessIntentDataType
        {
            switch (intent)
            {
                case ArcWeldIntentDataType arcWeld:
                    arcWeld.Voltage = GetDouble(root, "voltage", arcWeld.Voltage);
                    arcWeld.WireFeedSpeed = GetDouble(root, "wireFeedSpeed", arcWeld.WireFeedSpeed);
                    arcWeld.TravelSpeed = GetDouble(root, "travelSpeed", arcWeld.TravelSpeed);
                    arcWeld.SeamTrackingEnabled = GetBool(root, "seamTrackingEnabled", arcWeld.SeamTrackingEnabled);
                    arcWeld.WeldProcedureRef = GetString(root, "weldProcedureRef") ?? arcWeld.WeldProcedureRef;
                    break;
                case SpotWeldIntentDataType spotWeld:
                    spotWeld.WeldSchedule = GetUInt(root, "weldSchedule", spotWeld.WeldSchedule);
                    spotWeld.GunForce = GetDouble(root, "gunForce", spotWeld.GunForce);
                    break;
                case DispenseIntentDataType dispense:
                    dispense.FlowRate = GetDouble(root, "flowRate", dispense.FlowRate);
                    dispense.BeadWidth = GetDouble(root, "beadWidth", dispense.BeadWidth);
                    dispense.PurgeCycles = GetUInt(root, "purgeCycles", dispense.PurgeCycles);
                    break;
                case FastenIntentDataType fasten:
                    fasten.Joint = GetNode(root, "joint");
                    fasten.ProgramNumber = GetUInt(root, "programNumber", fasten.ProgramNumber);
                    fasten.TargetTorque = GetDouble(root, "targetTorque", fasten.TargetTorque);
                    break;
                case PalletiseIntentDataType palletise:
                    palletise.Pattern = GetNode(root, "pattern");
                    palletise.Layer = GetUInt(root, "layer", palletise.Layer);
                    palletise.Row = GetUInt(root, "row", palletise.Row);
                    palletise.Column = GetUInt(root, "column", palletise.Column);
                    break;
                case SurfaceFinishIntentDataType surfaceFinish:
                    surfaceFinish.ContactForce = GetDouble(root, "contactForce", surfaceFinish.ContactForce);
                    surfaceFinish.FeedRate = GetDouble(root, "feedRate", surfaceFinish.FeedRate);
                    surfaceFinish.ToolSpeed = GetDouble(root, "toolSpeed", surfaceFinish.ToolSpeed);
                    surfaceFinish.StepOver = GetDouble(root, "stepOver", surfaceFinish.StepOver);
                    break;
            }
        }

        private static JsonDocument ParseObject(string? json)
        {
            JsonDocument document = ParseDocument(
                string.IsNullOrWhiteSpace(json) ? "{}" : json,
                nameof(json),
                "Intent JSON");
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new ArgumentException("Intent JSON must be an object.", nameof(json));
            }
            return document;
        }

        /// <summary>
        /// Parses agent-supplied JSON, reporting a syntax error as an argument
        /// error.
        /// </summary>
        /// <remarks>
        /// The text comes from a language model, so malformed input is expected
        /// rather than exceptional. <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>
        /// signals it with a <see cref="JsonException"/>, which does not match
        /// what the tool descriptions promise the agent, and which a caller
        /// distinguishing bad input from a genuine fault would have to know to
        /// catch separately. Every rejection from this class is an
        /// <see cref="ArgumentException"/>; the original error is kept as the
        /// inner exception so the position information is not lost.
        /// </remarks>
        private static JsonDocument ParseDocument(string json, string paramName, string description)
        {
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{description} is not valid JSON: {ex.Message}"),
                    paramName,
                    ex);
            }
        }

        private static Pose3DDataType GetPose(JsonElement root, string name)
        {
            JsonElement pose = GetRequiredObject(root, name);
            ArrayOf<double> position = GetDoubleArray(pose, "position", name);
            ArrayOf<double> orientation = GetDoubleArray(pose, "orientation", name);
            if (position.Count != 3)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{name}.position' must hold exactly 3 numbers (x, y, z) but held {position.Count}."));
            }
            if (orientation.Count != 4)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{name}.orientation' must hold exactly 4 numbers (quaternion x, y, z, w) " +
                        $"but held {orientation.Count}."));
            }

            return RobotIntentBuilder.Pose(
                position[0],
                position[1],
                position[2],
                orientation[0],
                orientation[1],
                orientation[2],
                orientation[3],
                GetString(pose, "frameId") ?? string.Empty);
        }

        private static MotionConstraintsDataType GetConstraints(JsonElement root)
        {
            if (!root.TryGetProperty("constraints", out JsonElement constraints))
            {
                return new MotionConstraintsDataType
                {
                    SpeedFraction = GetDouble(root, "speedFraction", 0),
                    CartesianSpeed = GetDouble(root, "cartesianSpeed", 0)
                };
            }

            return new MotionConstraintsDataType
            {
                SpeedFraction = GetDouble(constraints, "speedFraction", 0),
                CartesianSpeed = GetDouble(constraints, "cartesianSpeed", 0),
                CartesianAcceleration = GetDouble(constraints, "cartesianAcceleration", 0),
                Jerk = GetDouble(constraints, "jerk", 0)
            };
        }

        private static BlendDataType GetBlend(JsonElement root)
        {
            JsonElement blend = root.TryGetProperty("blend", out JsonElement nested) ? nested : root;
            return new BlendDataType
            {
                Termination = GetEnum(blend, "termination", TerminationModeEnum.Exact),
                Radius = GetDouble(blend, "radius", 0)
            };
        }

        private static ArrayOf<KeyValuePair> GetAttributes(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement attributes) ||
                attributes.ValueKind == JsonValueKind.Null)
            {
                return [];
            }

            if (attributes.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"{propertyName} must be a JSON object."));
            }

            var values = new List<KeyValuePair>();
            foreach (JsonProperty attribute in attributes.EnumerateObject())
            {
                values.Add(new KeyValuePair
                {
                    Key = new QualifiedName(attribute.Name),
                    Value = OpcUaJsonHelper.JsonElementToVariant(attribute.Value)
                });
            }

            return [.. values];
        }

        private static ArrayOf<double> TryGetDoubleArray(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out JsonElement value)
                ? GetDoubleArray(value, propertyName)
                : [];
        }

        private static ArrayOf<double> GetDoubleArray(JsonElement root, string propertyName, string owner)
        {
            _ = GetRequiredProperty(root, propertyName);
            return GetDoubleArray(
                root.GetProperty(propertyName),
                string.Create(CultureInfo.InvariantCulture, $"{owner}.{propertyName}"));
        }

        private static ArrayOf<double> GetDoubleArray(JsonElement element, string description)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"'{description}' must be a JSON array of numbers but was {element.ValueKind}."));
            }

            var values = new List<double>();
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetDouble(out double value))
                {
                    throw new ArgumentException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"'{description}' must hold only numbers a Double can represent."));
                }
                values.Add(value);
            }

            return [.. values];
        }

        private static JsonElement GetRequiredArray(JsonElement root, string propertyName)
        {
            JsonElement value = GetRequiredProperty(root, propertyName);
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Property '{propertyName}' must be a JSON array but was {value.ValueKind}."));
            }
            return value;
        }

        private static JsonElement GetRequiredObject(JsonElement root, string propertyName)
        {
            JsonElement value = GetRequiredProperty(root, propertyName);
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Property '{propertyName}' must be a JSON object but was {value.ValueKind}."));
            }
            return value;
        }

        private static NodeId GetNode(JsonElement root, string propertyName)
        {
            string? value = GetString(root, propertyName);
            if (string.IsNullOrWhiteSpace(value))
            {
                return NodeId.Null;
            }

            // NodeId.Parse signals a bad identifier with either ArgumentException
            // or ServiceResultException depending on the error; TryParse collapses
            // both into the argument error the tool descriptions promise.
            if (!NodeId.TryParse(value, out NodeId nodeId))
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Property '{propertyName}' is not a valid NodeId: '{value}'."),
                    propertyName);
            }

            return nodeId;
        }

        private static Variant GetVariant(JsonElement root, string propertyName, string? dataType)
        {
            return root.TryGetProperty(propertyName, out JsonElement value)
                ? OpcUaJsonHelper.JsonElementToVariant(value, dataType)
                : Variant.Null;
        }

        private static JsonElement GetRequiredProperty(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Missing required property '{propertyName}'."));
            }
            return value;
        }

        private static string GetRequiredString(JsonElement root, string propertyName)
        {
            string? value = GetString(root, propertyName);
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Missing required property '{propertyName}'."))
                : value;
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static double GetDouble(JsonElement root, string propertyName, double defaultValue = 0)
        {
            return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : defaultValue;
        }

        private static uint GetUInt(JsonElement root, string propertyName, uint defaultValue)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement value) ||
                value.ValueKind != JsonValueKind.Number)
            {
                return defaultValue;
            }
            if (!value.TryGetUInt32(out uint parsed))
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Property '{propertyName}' must be a whole number a UInt32 can represent."));
            }
            return parsed;
        }

        private static bool GetBool(JsonElement root, string propertyName, bool defaultValue)
        {
            return root.TryGetProperty(propertyName, out JsonElement value) &&
                (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : defaultValue;
        }

        private static TEnum GetEnum<TEnum>(JsonElement root, string propertyName, TEnum defaultValue)
            where TEnum : struct
        {
            string? value = GetString(root, propertyName);
            return value != null && Enum.TryParse(value, ignoreCase: true, out TEnum parsed)
                ? parsed
                : defaultValue;
        }
    }
}
