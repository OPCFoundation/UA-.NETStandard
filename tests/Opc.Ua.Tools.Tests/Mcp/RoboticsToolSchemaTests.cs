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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Asserts the generated MCP InputSchema of the Robot Intent tools: typed
    /// nested inputs and arrays, required members, exact enum sets and
    /// defaults, and the absence of every legacy JSON-string parameter.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsToolSchemaTests
    {
        [Test]
        public void WaitMissionToolIsRegistered()
        {
            Assert.That(ResolveToolNames(), Does.Contain("robotics_wait_mission"));
        }

        [Test]
        public void NoToolExposesLegacyJsonStringParameters()
        {
            foreach (McpServerTool tool in ResolveTools())
            {
                string schema = tool.ProtocolTool.InputSchema.GetRawText();
                foreach (string name in kLegacyJsonParameters)
                {
                    Assert.That(schema, Does.Not.Contain(name),
                        $"{tool.ProtocolTool.Name} still exposes '{name}'.");
                }
            }
        }

        [Test]
        public void DirectSubmitRequiresControllerAndInput()
        {
            JsonElement schema = Schema("robotics_submit_linear_move");

            Assert.That(Required(schema), Is.EquivalentTo(kControllerAndInput));
        }

        [Test]
        public void DirectSubmitExposesNestedTypedPose()
        {
            JsonElement position = Path(
                Schema("robotics_submit_linear_move"),
                "properties", "input", "properties", "target", "properties", "position");

            Assert.Multiple(() =>
            {
                Assert.That(Types(position), Does.Contain("object"));
                Assert.That(Path(position, "properties", "x").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
                Assert.That(Path(position, "properties", "y").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
                Assert.That(Path(position, "properties", "z").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
            });
        }

        [Test]
        public void DirectSubmitExposesExactTerminationEnum()
        {
            JsonElement termination = Path(
                Schema("robotics_submit_linear_move"),
                "properties", "input", "properties", "blend", "properties", "termination");

            Assert.That(Enums(termination), Is.EqualTo(kTerminationModes));
        }

        [Test]
        public void JointMoveExposesTypedNumericArrayAndAxisCountDefault()
        {
            JsonElement schema = Schema("robotics_submit_joint_move");
            JsonElement jointTargets = Path(
                schema, "properties", "input", "properties", "jointTargets");
            JsonElement axisCount = Path(schema, "properties", "axisCount");

            Assert.Multiple(() =>
            {
                Assert.That(Types(jointTargets), Does.Contain("array"));
                Assert.That(Path(jointTargets, "items").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
                Assert.That(axisCount.GetProperty("default").GetInt32(), Is.Zero);
            });
        }

        [Test]
        public void TrajectoryExposesTypedPointArray()
        {
            JsonElement points = Path(
                Schema("robotics_submit_trajectory"),
                "properties", "input", "properties", "points");
            JsonElement item = Path(points, "items");

            Assert.Multiple(() =>
            {
                Assert.That(Types(points), Does.Contain("array"));
                Assert.That(Path(item, "properties", "timeFromStart").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
                Assert.That(Types(Path(item, "properties", "positions")), Does.Contain("array"));
                Assert.That(Types(Path(item, "properties", "velocities")), Does.Contain("array"));
            });
        }

        [Test]
        public void ForceExposesTypedDirectionArray()
        {
            JsonElement direction = Path(
                Schema("robotics_submit_force"),
                "properties", "input", "properties", "direction");

            Assert.Multiple(() =>
            {
                Assert.That(Types(direction), Does.Contain("array"));
                Assert.That(Path(direction, "items").GetProperty("type").GetString(),
                    Is.EqualTo("number"));
            });
        }

        [Test]
        public void ProcessIntentExposesTypedNamedValueAttributeArray()
        {
            JsonElement attributes = Path(
                Schema("robotics_submit_dispense"),
                "properties", "input", "properties", "attributes");
            JsonElement item = Path(attributes, "items");

            Assert.Multiple(() =>
            {
                Assert.That(Types(attributes), Does.Contain("array"));
                Assert.That(item.GetProperty("properties").TryGetProperty("name", out _), Is.True);
                Assert.That(item.GetProperty("properties").TryGetProperty("dataType", out _), Is.True);
                Assert.That(item.GetProperty("properties").TryGetProperty("value", out _), Is.True);
            });
        }

        [Test]
        public void CallProgramExposesTypedNamedValueArgumentArray()
        {
            JsonElement arguments = Path(
                Schema("robotics_submit_call_program"),
                "properties", "input", "properties", "arguments");

            Assert.That(Types(arguments), Does.Contain("array"));
        }

        [Test]
        public void SetOutputExposesExplicitTypedValue()
        {
            JsonElement value = Path(
                Schema("robotics_submit_set_output"),
                "properties", "input", "properties", "value");

            Assert.Multiple(() =>
            {
                Assert.That(Types(value), Does.Contain("object"));
                Assert.That(value.GetProperty("properties").TryGetProperty("dataType", out _), Is.True);
                Assert.That(value.GetProperty("properties").TryGetProperty("value", out _), Is.True);
            });
        }

        [Test]
        public void MissionSubmitRequiresControllerMissionAndSteps()
        {
            JsonElement schema = Schema("robotics_submit_mission");

            Assert.That(
                Required(schema),
                Is.EquivalentTo(kMissionSubmitRequired));
        }

        [Test]
        public void MissionSubmitExposesTypedStepArray()
        {
            JsonElement steps = Path(Schema("robotics_submit_mission"), "properties", "steps");
            JsonElement item = Path(steps, "items");

            Assert.Multiple(() =>
            {
                Assert.That(Types(steps), Does.Contain("array"));
                Assert.That(Path(item, "properties", "stepId").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
                Assert.That(Path(item, "properties", "released").GetProperty("type").GetString(),
                    Is.EqualTo("boolean"));
                Assert.That(item.GetProperty("properties").TryGetProperty("intent", out _), Is.True);
            });
        }

        [Test]
        public void MissionStepIntentExposesClosedKindEnum()
        {
            JsonElement kind = Path(
                Schema("robotics_submit_mission"),
                "properties", "steps", "items", "properties", "intent", "properties", "kind");

            Assert.That(Enums(kind), Is.EqualTo(kIntentKinds));
        }

        [Test]
        public void MissionStepIntentExposesEveryTypedPayload()
        {
            JsonElement intent = Path(
                Schema("robotics_submit_mission"),
                "properties", "steps", "items", "properties", "intent");
            JsonElement properties = intent.GetProperty("properties");

            string[] payloads = kIntentPayloads;

            Assert.Multiple(() =>
            {
                foreach (string payload in payloads)
                {
                    Assert.That(properties.TryGetProperty(payload, out _), Is.True,
                        $"mission intent must expose the typed '{payload}' payload.");
                }
            });
        }

        [Test]
        public void MissionSubmitExposesOptionalTypedTransitionArray()
        {
            JsonElement transitions = Path(
                Schema("robotics_submit_mission"), "properties", "transitions");
            JsonElement item = Path(transitions, "items");

            Assert.Multiple(() =>
            {
                Assert.That(Types(transitions), Does.Contain("array"));
                Assert.That(Types(transitions), Does.Contain("null"));
                Assert.That(item.GetProperty("properties").TryGetProperty("fromStepId", out _), Is.True);
                Assert.That(item.GetProperty("properties").TryGetProperty("toStepId", out _), Is.True);
                Assert.That(
                    Enums(Path(item, "properties", "divergenceKind")),
                    Is.EqualTo(kDivergenceKinds));
            });
        }

        [Test]
        public void MissionUpdateRequiresTypedHorizonStepArray()
        {
            JsonElement schema = Schema("robotics_update_mission");
            JsonElement horizonSteps = Path(schema, "properties", "horizonSteps");

            Assert.Multiple(() =>
            {
                Assert.That(Required(schema), Does.Contain("horizonSteps"));
                Assert.That(Types(horizonSteps), Does.Contain("array"));
                Assert.That(
                    Path(horizonSteps, "items", "properties", "stepId").GetProperty("type").GetString(),
                    Is.EqualTo("string"));
            });
        }

        [Test]
        public void ListOperationsExposesExactSelectorEnumsAndPaging()
        {
            JsonElement query = Path(Schema("robotics_list_operations"), "properties", "query");
            JsonElement properties = query.GetProperty("properties");

            Assert.Multiple(() =>
            {
                Assert.That(Enums(properties.GetProperty("work")),
                    Is.EqualTo(kWorkSelectors));
                Assert.That(Enums(properties.GetProperty("detail")),
                    Is.EqualTo(kDetailLevels));
                Assert.That(Types(properties.GetProperty("pageSize")), Does.Contain("integer"));
                Assert.That(Types(properties.GetProperty("pageSize")), Does.Contain("null"));
                Assert.That(properties.TryGetProperty("missionId", out _), Is.True);
                Assert.That(properties.TryGetProperty("cursor", out _), Is.True);
            });
        }

        [Test]
        public void ListMissionsExposesSelectorEnumsAndPaging()
        {
            JsonElement properties = Path(Schema("robotics_list_missions"), "properties", "query")
                .GetProperty("properties");

            Assert.Multiple(() =>
            {
                Assert.That(Enums(properties.GetProperty("work")),
                    Is.EqualTo(kWorkSelectors));
                Assert.That(Enums(properties.GetProperty("detail")),
                    Is.EqualTo(kDetailLevels));
                Assert.That(properties.TryGetProperty("pageSize", out _), Is.True);
            });
        }

        [Test]
        public void WaitMissionExposesBoundedTimeoutAndRequiredSelectors()
        {
            JsonElement schema = Schema("robotics_wait_mission");

            Assert.Multiple(() =>
            {
                Assert.That(
                    Required(schema),
                    Is.EquivalentTo(kWaitMissionRequired));
                Assert.That(
                    Path(schema, "properties", "timeoutMs").GetProperty("default").GetInt32(),
                    Is.EqualTo(2000));
            });
        }

        [Test]
        public void EverySubmitToolRequiresControllerAndTypedInput()
        {
            foreach (McpServerTool tool in ResolveTools()
                .Where(t => t.ProtocolTool.Name.StartsWith("robotics_submit_", StringComparison.Ordinal) &&
                    t.ProtocolTool.Name != "robotics_submit_mission"))
            {
                JsonElement schema = tool.ProtocolTool.InputSchema;
                string[] required = Required(schema);

                Assert.That(required, Does.Contain("controller"), tool.ProtocolTool.Name);
                Assert.That(required, Does.Contain("input"), tool.ProtocolTool.Name);
                Assert.That(
                    Types(Path(schema, "properties", "input")), Does.Contain("object"),
                    tool.ProtocolTool.Name);
            }
        }

        private static readonly string[] kControllerAndInput = ["controller", "input"];

        private static readonly string[] kTerminationModes = ["Exact", "Blend"];

        private static readonly string[] kDivergenceKinds = ["Alternative", "Parallel"];

        private static readonly string[] kWorkSelectors = ["All", "Active", "Terminal"];

        private static readonly string[] kDetailLevels = ["Summary", "Full"];

        private static readonly string[] kMissionSubmitRequired =
            ["controller", "missionId", "missionUpdateId", "steps"];

        private static readonly string[] kWaitMissionRequired =
            ["controller", "missionId", "missionNodeId"];

        private static readonly string[] kIntentKinds =
        [
            "JointMove", "LinearMove", "CircularMove", "Trajectory", "CartesianPath",
            "Force", "ArcWeld", "SpotWeld", "Dispense", "Fasten", "Palletise",
            "SurfaceFinish", "Grasp", "Release", "Pick", "Place", "ToolChange",
            "SetOutput", "CallProgram", "Wait"
        ];

        private static readonly string[] kLegacyJsonParameters =
        [
            "intentJson", "stepsJson", "transitionsJson", "horizonStepsJson",
            "argumentsJson", "attributesJson"
        ];

        private static readonly string[] kIntentPayloads =
        [
            "jointMove", "linearMove", "circularMove", "trajectory", "cartesianPath",
            "force", "arcWeld", "spotWeld", "dispense", "fasten", "palletise",
            "surfaceFinish", "grasp", "release", "pick", "place", "toolChange",
            "setOutput", "callProgram", "wait"
        ];

        private static JsonElement Schema(string toolName)
        {
            McpServerTool tool = ResolveTools()
                .FirstOrDefault(t => string.Equals(
                    t.ProtocolTool.Name, toolName, StringComparison.Ordinal))
                ?? throw new AssertionException($"Tool '{toolName}' is not registered.");
            return tool.ProtocolTool.InputSchema;
        }

        private static JsonElement Path(JsonElement element, params string[] segments)
        {
            JsonElement current = element;
            foreach (string segment in segments)
            {
                Assert.That(current.TryGetProperty(segment, out JsonElement next), Is.True,
                    $"schema is missing '{segment}' in [{string.Join('.', segments)}]");
                current = next;
            }
            return current;
        }

        private static string[] Required(JsonElement schema)
        {
            if (!schema.TryGetProperty("required", out JsonElement required))
            {
                return [];
            }
            return [.. required.EnumerateArray().Select(e => e.GetString()!)];
        }

        private static string[] Types(JsonElement element)
        {
            JsonElement type = element.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String)
            {
                return [type.GetString()!];
            }
            return [.. type.EnumerateArray().Select(e => e.GetString()!)];
        }

        private static string[] Enums(JsonElement element)
        {
            return [.. element.GetProperty("enum")
                .EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)];
        }

        private static IReadOnlyList<McpServerTool> ResolveTools()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpRobotics();
            services.AddMcpServer().WithOpcUaRoboticsTools(McpToolProfile.Robotics);

            using ServiceProvider provider = services.BuildServiceProvider();
            return [.. provider.GetServices<McpServerTool>()];
        }

        private static HashSet<string> ResolveToolNames()
        {
            return [.. ResolveTools().Select(t => t.ProtocolTool.Name)];
        }
    }
}
#endif
