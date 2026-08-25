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
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Schema, registration, and behavior tests for the Vision inference MCP tools.
    /// Verifies tool names, InputSchema structure (required, enum, defaults, bounds),
    /// DI wiring, and record shapes without calling a live server.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class VisionInferenceToolsSchemaTests
    {
        private static readonly ArrayOf<string> s_segmentationLabels =
            new[] { "bg", "part", "defect" }.ToArrayOf();
        private static readonly string[] s_requestFields =
            ["pipeline", "expectedKind", "detail", "maxItems", "sessionName"];
        private static readonly string[] s_requestParameter =
            ["request"];
        private static readonly string[] s_requiredPipeline =
            ["pipeline"];
        private static readonly string[] s_expectedResultKinds =
            ["Auto", "Detection", "Inspection", "Segmentation"];
        private static readonly string[] s_resultDetails =
            ["Summary", "HandleOnly"];

        [Test]
        public void VisionProfileRegistersInferenceTools()
        {
            HashSet<string> tools = ResolveToolNames(McpToolProfile.Vision);

            Assert.Multiple(() =>
            {
                Assert.That(tools, Does.Contain("vision_run_inference"));
                Assert.That(tools, Does.Contain("vision_start_continuous_inference"));
                Assert.That(tools, Does.Contain("vision_stop_inference"));
            });
        }

        [Test]
        public void FullProfileIncludesVisionInferenceTools()
        {
            HashSet<string> tools = ResolveToolNames(McpToolProfile.Full);

            Assert.That(tools, Does.Contain("vision_run_inference"));
        }

        [Test]
        public void RunInferenceToolIsRegistered()
        {
            McpServerTool? tool = ResolveTool("vision_run_inference");

            Assert.That(tool, Is.Not.Null,
                "vision_run_inference must be registered.");
        }

        [Test]
        public async Task RunInferenceInputSchemaUsesExactlyOneStructuredRequest()
        {
            McpServerTool tool = (await ResolveToolWithContractSchemaAsync(
                "vision_run_inference").ConfigureAwait(false))!;
            JsonElement schema = tool.ProtocolTool.InputSchema;

            Assert.That(schema.TryGetProperty("properties", out JsonElement props), Is.True);
            string[] topLevelProperties = props.EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
            Assert.That(topLevelProperties, Is.EqualTo(s_requestParameter),
                "vision_run_inference must expose no legacy top-level scalar inputs.");

            Assert.That(schema.TryGetProperty("required", out JsonElement required), Is.True);
            string[] requiredFields = required.EnumerateArray()
                .Select(e => e.GetString()!).ToArray();
            Assert.That(requiredFields, Is.EqualTo(s_requestParameter));
        }

        [Test]
        public async Task RunInferenceInputSchemaHasExactNestedRequestContract()
        {
            McpServerTool tool = (await ResolveToolWithContractSchemaAsync(
                "vision_run_inference").ConfigureAwait(false))!;
            JsonElement schema = tool.ProtocolTool.InputSchema;
            JsonElement props = schema.GetProperty("properties");
            JsonElement request = props.GetProperty("request");
            Assert.That(request.GetProperty("type").GetString(), Is.EqualTo("object"));

            JsonElement requestProperties = request.GetProperty("properties");
            string[] propertyNames = requestProperties.EnumerateObject()
                .Select(property => property.Name)
                .ToArray();
            Assert.That(propertyNames, Is.EquivalentTo(s_requestFields));
            Assert.That(
                request.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray(),
                Is.EqualTo(s_requiredPipeline));

            AssertStringEnum(
                requestProperties.GetProperty("expectedKind"),
                s_expectedResultKinds,
                "Auto");
            AssertStringEnum(
                requestProperties.GetProperty("detail"),
                s_resultDetails,
                "Summary");

            JsonElement maxItems = requestProperties.GetProperty("maxItems");
            Assert.That(maxItems.GetProperty("default").GetInt32(), Is.EqualTo(20));
            Assert.That(maxItems.TryGetProperty("minimum", out JsonElement minimum), Is.True);
            Assert.That(minimum.GetInt32(), Is.Zero);
            Assert.That(maxItems.TryGetProperty("maximum", out JsonElement maximum), Is.True);
            Assert.That(maximum.GetInt32(), Is.EqualTo(100));
        }

        [Test]
        public void ReadPipelineToolUsesSelectorParam()
        {
            McpServerTool? tool = ResolveTool("vision_read_pipeline");
            Assert.That(tool, Is.Not.Null);

            JsonElement schema = tool!.ProtocolTool.InputSchema;
            JsonElement props = schema.GetProperty("properties");
            Assert.That(props.TryGetProperty("pipeline", out _), Is.True,
                "read_pipeline must accept 'pipeline' selector, not 'pipelineNodeId'.");
        }

        [Test]
        public void SubmitDetectionsToolUsesSelectorParam()
        {
            McpServerTool? tool = ResolveTool("vision_submit_detections");
            Assert.That(tool, Is.Not.Null);

            JsonElement schema = tool!.ProtocolTool.InputSchema;
            JsonElement props = schema.GetProperty("properties");
            Assert.That(props.TryGetProperty("pipeline", out _), Is.True,
                "submit_detections must accept 'pipeline' selector.");
        }

        [Test]
        public void StartContinuousToolUsesSelectorParam()
        {
            McpServerTool? tool = ResolveTool("vision_start_continuous_inference");
            Assert.That(tool, Is.Not.Null);

            JsonElement schema = tool!.ProtocolTool.InputSchema;
            JsonElement props = schema.GetProperty("properties");
            Assert.That(props.TryGetProperty("pipeline", out _), Is.True,
                "start_continuous must accept 'pipeline' selector.");
        }

        [Test]
        public void StopInferenceToolUsesSelectorParam()
        {
            McpServerTool? tool = ResolveTool("vision_stop_inference");
            Assert.That(tool, Is.Not.Null);

            JsonElement schema = tool!.ProtocolTool.InputSchema;
            JsonElement props = schema.GetProperty("properties");
            Assert.That(props.TryGetProperty("pipeline", out _), Is.True,
                "stop_inference must accept 'pipeline' selector.");
        }

        [Test]
        public void VisionInferenceRunResultRecordHasProvenance()
        {
            var result = new VisionInferenceRunResult
            {
                ResultId = "det-42",
                ResultNodeId = "ns=2;s=Results/det-42",
                Resolved = true,
                ResultKind = VisionResultKind.Detection,
                RequestedPipelineName = "BinPicking",
                RequestedPipelineNodeId = "ns=2;s=Vision/Pipelines/BinPicking",
                PipelineId = "ns=2;s=Vision/Pipelines/PublishedByResult",
                SensorId = "ns=2;s=Vision/Sensors/Camera1",
                ModelVersionUsed = "v2.1",
                CreationTime = "2024-06-01T12:00:00.0000000Z",
                FrameId = "world"
            };

            Assert.Multiple(() =>
            {
                Assert.That(result.ResultId, Is.EqualTo("det-42"));
                Assert.That(result.ResultNodeId, Is.EqualTo("ns=2;s=Results/det-42"));
                Assert.That(result.Resolved, Is.True);
                Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Detection));
                Assert.That(result.RequestedPipelineName, Is.EqualTo("BinPicking"));
                Assert.That(result.RequestedPipelineNodeId,
                    Is.EqualTo("ns=2;s=Vision/Pipelines/BinPicking"));
                Assert.That(result.PipelineId,
                    Is.EqualTo("ns=2;s=Vision/Pipelines/PublishedByResult"));
                Assert.That(result.SensorId, Is.EqualTo("ns=2;s=Vision/Sensors/Camera1"));
                Assert.That(result.ModelVersionUsed, Is.EqualTo("v2.1"));
                Assert.That(result.CreationTime,
                    Is.EqualTo("2024-06-01T12:00:00.0000000Z"));
                Assert.That(result.FrameId, Is.EqualTo("world"));
                Assert.That(result.Detection, Is.Null);
                Assert.That(result.Inspection, Is.Null);
                Assert.That(result.Segmentation, Is.Null);
            });
        }

        [Test]
        public void RunResultUsesEnumKindNotString()
        {
            var result = new VisionInferenceRunResult
            {
                ResultId = "x",
                ResultNodeId = string.Empty,
                ResultKind = VisionResultKind.Inspection
            };

            Assert.That(result.ResultKind, Is.EqualTo(VisionResultKind.Inspection));
        }

        [Test]
        public void RunResultMapsDetectionSummaryToLeanDtoWithoutPose()
        {
            var summary = new VisionDetectionSummary
            {
                TotalDetections = 2,
                Items = new VisionDetectionItem[]
                {
                    new()
                    {
                        DetectionId = "d1",
                        ClassLabel = "Part",
                        ClassId = 1,
                        Confidence = 0.9,
                        HasPose = true,
                        Pose = new Vision.VisionPose3DDataType
                        {
                            FrameId = "world"
                        }
                    }
                }.ToArrayOf()
            };

            VisionInferenceRunResult result = VisionInferenceRunResult.FromServiceResult(
                new VisionInferenceResult
            {
                ResultId = "r1",
                RequestedPipelineNodeId = new NodeId(1u, 2),
                ResultNodeId = new NodeId(2u, 2),
                Resolved = true,
                ResultKind = VisionResultKind.Detection,
                DetectionSummary = summary
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Detection, Is.Not.Null);
                Assert.That(result.Detection!.TotalDetections, Is.EqualTo(2));
                Assert.That(result.Detection.Items[0].DetectionId, Is.EqualTo("d1"));
                Assert.That(result.Detection.Items[0].HasPose, Is.True);
                Assert.That(
                    typeof(VisionInferenceDetectionItem).GetProperty("Pose"),
                    Is.Null,
                    "MCP results must not expose a full pose or covariance payload.");
            });
        }

        [Test]
        public void RunResultSummarySerializesItemsAsJsonArrays()
        {
            var result = new VisionInferenceRunResult
            {
                ResultId = "r1",
                ResultNodeId = "ns=2;s=R1",
                ResultKind = VisionResultKind.Detection,
                Detection = new VisionInferenceDetectionSummary
                {
                    TotalDetections = 1,
                    Items =
                    [
                        new VisionInferenceDetectionItem
                        {
                            DetectionId = "d1",
                            ClassLabel = "Part",
                            Confidence = 0.9
                        }
                    ]
                }
            };

            string json = JsonSerializer.Serialize(result);
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.RootElement
                        .GetProperty("Detection")
                        .GetProperty("Items")
                        .ValueKind,
                    Is.EqualTo(JsonValueKind.Array));
                Assert.That(json, Does.Not.Contain("\"memory\""));
            });
        }

        [Test]
        public void RunResultMapsInspectionSummaryToLeanDto()
        {
            var summary = new VisionInspectionSummary
            {
                Evaluation = Vision.VisionResultEvaluationEnum.Ok,
                PartId = "P1",
                RecipeId = "R1",
                TotalCharacteristics = 1,
                Items = new VisionCharacteristicItem[]
                {
                    new()
                    {
                        Name = "dim",
                        Status = Vision.VisionToleranceStatusEnum.InTolerance,
                        Deviation = 0.01
                    }
                }.ToArrayOf()
            };

            VisionInferenceRunResult result = VisionInferenceRunResult.FromServiceResult(
                new VisionInferenceResult
            {
                ResultId = "r2",
                RequestedPipelineNodeId = new NodeId(1u, 2),
                ResultNodeId = new NodeId(2u, 2),
                Resolved = true,
                ResultKind = VisionResultKind.Inspection,
                InspectionSummary = summary
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Inspection, Is.Not.Null);
                Assert.That(result.Inspection!.Evaluation,
                    Is.EqualTo(Vision.VisionResultEvaluationEnum.Ok));
                Assert.That(result.Inspection.Items[0].Name, Is.EqualTo("dim"));
            });
        }

        [Test]
        public void RunResultMapsSegmentationMetadataToLeanDto()
        {
            var summary = new VisionSegmentationSummary
            {
                LabelClasses = s_segmentationLabels,
                MaskWidth = 1280,
                MaskHeight = 720,
                MaskFormat = "Mono8"
            };

            VisionInferenceRunResult result = VisionInferenceRunResult.FromServiceResult(
                new VisionInferenceResult
            {
                ResultId = "r3",
                RequestedPipelineNodeId = new NodeId(1u, 2),
                ResultNodeId = new NodeId(2u, 2),
                Resolved = true,
                ResultKind = VisionResultKind.Segmentation,
                SegmentationSummary = summary
            });

            Assert.Multiple(() =>
            {
                Assert.That(result.Segmentation, Is.Not.Null);
                Assert.That(result.Segmentation!.LabelClasses, Has.Length.EqualTo(3));
                Assert.That(result.Segmentation.MaskWidth, Is.EqualTo(1280u));
                Assert.That(result.Segmentation.MaskHeight, Is.EqualTo(720u));
                Assert.That(result.Segmentation.MaskFormat, Is.EqualTo("Mono8"));
            });
        }

        [Test]
        public void RunInferenceToolRejectsUndefinedExpectedKind()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionInferenceTools.RunInferenceAsync(
                    null!,
                    new VisionInferenceRequest
                    {
                        Pipeline = "pipe",
                        ExpectedKind = (VisionExpectedResultKind)99,
                        Detail = VisionResultDetail.Summary,
                        MaxItems = 10
                    }).ConfigureAwait(false));
        }

        [Test]
        public void RunInferenceToolRejectsNegativeMaxItems()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionInferenceTools.RunInferenceAsync(
                    null!,
                    new VisionInferenceRequest
                    {
                        Pipeline = "pipe",
                        MaxItems = -1
                    }).ConfigureAwait(false));
        }

        [Test]
        public void RunInferenceToolRejectsMaxItemsAbove100()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionInferenceTools.RunInferenceAsync(
                    null!,
                    new VisionInferenceRequest
                    {
                        Pipeline = "pipe",
                        MaxItems = 101
                    }).ConfigureAwait(false));
        }

        [Test]
        public void RunInferenceToolRejectsInvalidDetailEnum()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionInferenceTools.RunInferenceAsync(
                    null!,
                    new VisionInferenceRequest
                    {
                        Pipeline = "pipe",
                        Detail = (VisionResultDetail)99
                    }).ConfigureAwait(false));
        }

        [Test]
        public void RunInferenceToolRejectsInvalidExpectedKindEnum()
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionInferenceTools.RunInferenceAsync(
                    null!,
                    new VisionInferenceRequest
                    {
                        Pipeline = "pipe",
                        ExpectedKind = (VisionExpectedResultKind)99
                    }).ConfigureAwait(false));
        }

        [Test]
        public void RunInferenceToolRejectsWhitespacePipelineBeforeResolvingSession()
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await VisionInferenceTools.RunInferenceAsync(
                    null!,
                    new VisionInferenceRequest
                    {
                        Pipeline = " "
                    }).ConfigureAwait(false));
        }

        private static HashSet<string> ResolveToolNames(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpVision();
            services.AddMcpServer()
                .WithOpcUaVisionTools(profile);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .Select(t => t.ProtocolTool.Name)
                .ToHashSet();
        }

        private static McpServerTool? ResolveTool(string toolName)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpVision();
            services.AddMcpServer()
                .WithOpcUaVisionTools(McpToolProfile.Vision);

            using ServiceProvider provider = services.BuildServiceProvider();
            return provider
                .GetServices<McpServerTool>()
                .FirstOrDefault(t => t.ProtocolTool.Name == toolName);
        }

        private static async Task<McpServerTool?> ResolveToolWithContractSchemaAsync(
            string toolName)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpVision();
            services.AddMcpServer()
                .WithOpcUaVisionTools(McpToolProfile.Vision);

            using ServiceProvider provider = services.BuildServiceProvider();
            McpServerTool[] tools = provider.GetServices<McpServerTool>().ToArray();
            var listToolsResult = new ListToolsResult
            {
                Tools = [.. tools.Select(tool => tool.ProtocolTool)]
            };
            McpRequestHandler<ListToolsRequestParams, ListToolsResult> handler =
                VisionMcpFilters.AddInferenceRequestSchema(
                    (_, _) => ValueTask.FromResult(listToolsResult));
            await handler(null!, default).ConfigureAwait(false);

            return tools.FirstOrDefault(tool => tool.ProtocolTool.Name == toolName);
        }

        private static void AssertStringEnum(
            JsonElement schema,
            string[] expectedValues,
            string expectedDefault)
        {
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("string"));
            Assert.That(
                schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray(),
                Is.EqualTo(expectedValues));
            Assert.That(schema.GetProperty("default").GetString(), Is.EqualTo(expectedDefault));
        }
    }
}
#endif
