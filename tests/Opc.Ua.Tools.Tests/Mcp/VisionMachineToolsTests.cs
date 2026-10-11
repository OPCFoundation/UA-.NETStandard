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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Pins the released-model tool catalog and its bounded input contracts.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed partial class VisionMachineToolsTests
    {
        [TestCase(McpToolProfile.Vision)]
        [TestCase(McpToolProfile.Full)]
        public void ReleasedToolsAreRegisteredWithoutReplacingDraftTools(McpToolProfile profile)
        {
            using ServiceProvider provider = Services(profile).BuildServiceProvider();
            string[] names = [.. provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name)];
            Assert.That(names.Where(name => name.StartsWith("vision_machine_", StringComparison.Ordinal)),
                Is.EquivalentTo(s_names.Concat(s_followupNames)));
            foreach (string name in s_names.Concat(s_followupNames))
            {
                Assert.That(names.Count(candidate => candidate == name), Is.EqualTo(1), name);
            }
            Assert.That(names, Does.Contain("vision_run_inference"));
            Assert.That(names, Is.Unique);
        }

        [Test]
        public void CoreProfileDoesNotGainVisionCommands()
        {
            using ServiceProvider provider = Services(McpToolProfile.Core).BuildServiceProvider();
            Assert.That(provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name),
                Has.None.StartsWith("vision_machine_"));
        }

        /// <summary>
        /// Keeps every released-model wire parameter usable without reading the implementation.
        /// </summary>
        [TestCase(McpToolProfile.Vision)]
        [TestCase(McpToolProfile.Full)]
        public void ReleasedToolSchemasDescribeEveryWireParameter(McpToolProfile profile)
        {
            using ServiceProvider provider = Services(profile).BuildServiceProvider();
            McpServerTool[] tools = [.. provider.GetServices<McpServerTool>()];
            Assert.Multiple(() =>
            {
                foreach (string name in s_names.Concat(s_followupNames))
                {
                    JsonElement schema = tools.Single(tool => tool.ProtocolTool.Name == name).ProtocolTool.InputSchema;
                    JsonElement properties = schema.GetProperty("properties");
                    Assert.That(properties.EnumerateObject(), Is.Not.Empty, name);
                    foreach (JsonProperty parameter in properties.EnumerateObject())
                    {
                        bool hasDescription = parameter.Value.TryGetProperty(
                            "description", out JsonElement description);
                        Assert.That(hasDescription, Is.True, $"{name}.{parameter.Name}");
                        if (hasDescription)
                        {
                            Assert.That(description.ValueKind, Is.EqualTo(JsonValueKind.String));
                            Assert.That(description.GetString(), Has.Length.GreaterThanOrEqualTo(20),
                                $"{name}.{parameter.Name}");
                        }
                    }
                }
            });
        }

        [Test]
        public void FileSchemaCarriesChunkLimitsAndRequiredSelectors()
        {
            using ServiceProvider provider = Services(McpToolProfile.Vision).BuildServiceProvider();
            McpServerTool tool = provider.GetServices<McpServerTool>()
                .Single(candidate => candidate.ProtocolTool.Name == "vision_machine_file");
            JsonElement schema = tool.ProtocolTool.InputSchema;
            string?[] required =
                [.. schema.GetProperty("required").EnumerateArray().Select(field => field.GetString())];
            Assert.That(required, Does.Contain("nodeId"));
            Assert.That(required, Does.Contain("operation"));
            JsonElement length = schema.GetProperty("properties").GetProperty("length");
            Assert.That(length.GetProperty("minimum").GetInt32(), Is.EqualTo(1));
            Assert.That(length.GetProperty("maximum").GetInt32(), Is.EqualTo(1048576));
        }

        [TestCase(0u)]
        [TestCase(101u)]
        public void OversizedMetadataPagesFailBeforeSessionAccess(uint maximum)
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.ResultsAsync(null!, "i=1", "List", maximumResults: maximum)
                    .ConfigureAwait(false));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1048577)]
        public void InvalidFileChunkLengthsFailBeforeSessionAccess(int length)
        {
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.FileAsync(null!, "i=1", "Read", length: length).ConfigureAwait(false));
        }

        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase(/*lang=json,strict*/ "{\"Parameters\":42}")]
        public void MalformedStartParametersFailBeforeSessionAccess(string json)
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await VisionMachineTools.CommandAsync(null!, "i=1", "StartSingleJob", parametersJson: json)
                    .ConfigureAwait(false));
        }

        [Test]
        public void OversizedStructuredFiltersFailBeforeSessionAccess()
        {
            string json = "{\"ignored\":\"" + new string('x', 1024 * 1024) + "\"}";
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.ResultsAsync(null!, "i=1", "List", filtersJson: json).ConfigureAwait(false));
        }

        [TestCase("vision_machine_capabilities", "")]
        [TestCase("vision_machine_relationships", "nodeId")]
        [TestCase("vision_machine_observe", "notifierNodeId")]
        [TestCase("vision_machine_upload_recipe", "systemNodeId,dataBase64")]
        [TestCase("vision_machine_upload_configuration", "systemNodeId,dataBase64")]
        [TestCase("vision_machine_download_result", "systemNodeId,resultId")]
        public void FollowupSchemasExposeOnlyRequiredWireSelectors(string name, string requiredNames)
        {
            using ServiceProvider provider = Services(McpToolProfile.Vision).BuildServiceProvider();
            JsonElement schema = provider.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == name).ProtocolTool.InputSchema;
            string?[] required = schema.TryGetProperty("required", out JsonElement fields)
                ? [.. fields.EnumerateArray().Select(field => field.GetString())] : [];
            Assert.That(required, Is.EquivalentTo(requiredNames.Split(',', StringSplitOptions.RemoveEmptyEntries)));
            string[] properties =
                [.. schema.GetProperty("properties").EnumerateObject().Select(property => property.Name)];
            Assert.Multiple(() =>
            {
                Assert.That(properties, Does.Contain("sessionName"));
                Assert.That(properties, Does.Not.Contain("accessor"));
                Assert.That(properties, Does.Not.Contain("ct"));
                Assert.That(properties, Does.Not.Contain("filePath"));
            });
        }

        [TestCase("vision_machine_observe", "durationSeconds", 1, 60, 5)]
        [TestCase("vision_machine_observe", "maximumNotifications", 1, 100, 50)]
        [TestCase("vision_machine_relationships", "maximumResults", 1, 100, 50)]
        [TestCase("vision_machine_relationships", "startIndex", 0, 10000, 0)]
        [TestCase("vision_machine_download_result", "maximumBytes", 1, 1048576, 1048576)]
        public void FollowupSchemasPinBoundsAndDefaults(
            string toolName, string fieldName, int minimum, int maximum, int defaultValue)
        {
            using ServiceProvider provider = Services(McpToolProfile.Vision).BuildServiceProvider();
            JsonElement schema = provider.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == toolName).ProtocolTool.InputSchema;
            JsonElement field = schema.GetProperty("properties").GetProperty(fieldName);
            Assert.Multiple(() =>
            {
                Assert.That(field.GetProperty("minimum").GetInt32(), Is.EqualTo(minimum));
                Assert.That(field.GetProperty("maximum").GetInt32(), Is.EqualTo(maximum));
                Assert.That(field.GetProperty("default").GetInt32(), Is.EqualTo(defaultValue));
            });
        }

        [Test]
        public void EnrichedStateRetainsOldSelectorsAndOptInDefault()
        {
            using ServiceProvider provider = Services(McpToolProfile.Vision).BuildServiceProvider();
            JsonElement schema = provider.GetServices<McpServerTool>()
                .Single(tool => tool.ProtocolTool.Name == "vision_machine_read_state").ProtocolTool.InputSchema;
            Assert.Multiple(() =>
            {
                Assert.That(schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(s_stateRequired));
                Assert.That(schema.GetProperty("properties").EnumerateObject().Select(value => value.Name),
                Is.EquivalentTo(s_stateFields));
                Assert.That(schema.GetProperty("properties").GetProperty("includeDetails")
                .GetProperty("default").GetBoolean(), Is.False);
            });
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(61)]
        public void ObservationRejectsInvalidWindowsBeforeSessionAccess(int seconds)
        {
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.ObserveAsync(null!, "i=2253", durationSeconds: seconds)
                .ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo("Window"));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(101)]
        public void ObservationRejectsInvalidCountsBeforeSessionAccess(int count)
        {
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.ObserveAsync(null!, "i=2253", maximumNotifications: count)
                .ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo("MaximumNotifications"));
        }

        [TestCase("")]
        [TestCase("events")]
        [TestCase("All")]
        public void ObservationRejectsUnknownModesBeforeSessionAccess(string mode)
        {
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
                await VisionMachineTools.ObserveAsync(null!, "i=2253", mode: mode).ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo("mode"));
        }

        [Test]
        public void ObservationRefusesRefreshInEventMode()
        {
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
                await VisionMachineTools.ObserveAsync(null!, "i=2253", refreshConditions: true)
                .ConfigureAwait(false))!;
            Assert.That(error.Message, Does.Contain("condition mode"));
        }

        [TestCase(0u)]
        [TestCase(101u)]
        public void RelationshipsRejectInvalidPageSizesBeforeSessionAccess(uint size)
        {
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.RelationshipsAsync(null!, "i=1", maximumResults: size)
                .ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo("maximum"));
        }

        [Test]
        public void RelationshipsRejectAnOffsetAboveTheTraversalBound()
        {
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.RelationshipsAsync(null!, "i=1", startIndex: 10001)
                .ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo("startIndex"));
        }

        [TestCase("Unknown", "Both", "relationship")]
        [TestCase("SameEntity", "Unknown", "direction")]
        [TestCase("sameentity", "Both", "relationship")]
        [TestCase("SameEntity", "forward", "direction")]
        public void RelationshipsRejectUnknownSelectorsBeforeSessionAccess(
            string relationship, string direction, string parameter)
        {
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
                await VisionMachineTools.RelationshipsAsync(null!, "i=1", relationship, direction)
                .ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo(parameter));
        }

        [TestCase(false, 1048577)]
        [TestCase(true, 1048577)]
        [TestCase(false, 1048578)]
        [TestCase(true, 1048578)]
        public void UploadRejectsDecodedContentAboveOneMiBEvenAtTheEncodedLimit(bool configuration, int length)
        {
            string content = Convert.ToBase64String(new byte[length]);
            Assert.That(content, Has.Length.EqualTo(1398104));
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                if (configuration)
                {
                    await VisionMachineTools.UploadConfigurationAsync(null!, "i=1", content).ConfigureAwait(false);
                }
                else
                {
                    await VisionMachineTools.UploadRecipeAsync(null!, "i=1", content).ConfigureAwait(false);
                }
            })!;
            Assert.That(error.ParamName, Is.EqualTo("dataBase64"));
            Assert.That(error.Message, Does.Contain("file chunks"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UploadRejectsOversizedEncodedContentBeforeDecoding(bool configuration)
        {
            string content = new('!', 1398105);
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                if (configuration)
                {
                    await VisionMachineTools.UploadConfigurationAsync(null!, "i=1", content).ConfigureAwait(false);
                }
                else
                {
                    await VisionMachineTools.UploadRecipeAsync(null!, "i=1", content).ConfigureAwait(false);
                }
            })!;
            Assert.That(error.ParamName, Is.EqualTo("dataBase64"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UploadRejectsMalformedBase64BeforeSessionAccess(bool configuration)
        {
            Assert.ThrowsAsync<FormatException>(async () =>
            {
                if (configuration)
                {
                    await VisionMachineTools.UploadConfigurationAsync(null!, "i=1", "%%%=").ConfigureAwait(false);
                }
                else
                {
                    await VisionMachineTools.UploadRecipeAsync(null!, "i=1", "%%%=").ConfigureAwait(false);
                }
            });
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1048577)]
        public void DownloadRejectsInvalidLimitsBeforeSessionAccess(int limit)
        {
            ArgumentOutOfRangeException error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await VisionMachineTools.DownloadResultAsync(null!, "i=1", "result", maximumBytes: limit)
                .ConfigureAwait(false))!;
            Assert.That(error.ParamName, Is.EqualTo("maximumBytes"));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task CapabilitiesReportIndependentNamespacesWithoutCallingTheServerAsync(
            bool control, bool assets, bool perception)
        {
            await using ToolSession harness = await ToolSession.CreateAsync(control, assets, perception)
                .ConfigureAwait(false);
            JsonElement result = await VisionMachineTools.CapabilitiesAsync(harness.Accessor).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("supportsControl").GetBoolean(), Is.EqualTo(control));
                Assert.That(result.GetProperty("supportsAssets").GetBoolean(), Is.EqualTo(assets));
                Assert.That(result.GetProperty("interfaces").GetArrayLength(), Is.Zero);
                Assert.That(harness.Browses, Is.Empty);
                Assert.That(harness.Calls, Is.Empty);
                Assert.That(harness.Session.Object.Connected, Is.True);
            });
            harness.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [Test]
        public async Task CapabilitiesKeepAbsentInstanceInterfacesExplicitAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            JsonElement result = await VisionMachineTools.CapabilitiesAsync(
                harness.Accessor, harness.SystemId.ToString()).ConfigureAwait(false);
            JsonElement[] interfaces = [.. result.GetProperty("interfaces").EnumerateArray()];
            Assert.That(interfaces.Select(item => item.GetProperty("path").GetString()),
                Is.EquivalentTo(s_absentInterfaces));
            foreach (JsonElement item in interfaces)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(item.GetProperty("available").GetBoolean(), Is.False);
                    Assert.That(item.GetProperty("nodeId").ValueKind, Is.EqualTo(JsonValueKind.Null));
                    Assert.That(item.GetProperty("methods").GetArrayLength(), Is.Zero);
                });
            }
            Assert.That(harness.Browses.Single().NodeId, Is.EqualTo(harness.SystemId));
            Assert.That(harness.Calls, Is.Empty);
        }

        [Test]
        public async Task CapabilitiesDoNotInferExecutabilityFromTheLoadedMethodTypeAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("recipes", 1);
            var method = new NodeId("add", 1);
            harness.Children[(harness.SystemId, "RecipeManagement")] = management;
            harness.References[management] =
            [
                new ReferenceDescription
                {
                    NodeId = method,
                    BrowseName = new QualifiedName("AddRecipe", 1),
                    NodeClass = NodeClass.Method
                }
            ];
            harness.Readings[(method, Attributes.Executable)] = new DataValue(Variant.From(true));
            harness.Readings[(method, Attributes.UserExecutable)] =
                new DataValue(Variant.From(false), StatusCodes.BadUserAccessDenied);
            JsonElement result = await VisionMachineTools.CapabilitiesAsync(
                harness.Accessor, harness.SystemId.ToString()).ConfigureAwait(false);
            JsonElement recipe = result.GetProperty("interfaces").EnumerateArray()
                .Single(item => item.GetProperty("path").GetString() == "RecipeManagement");
            JsonElement published = recipe.GetProperty("methods")[0];
            using var executable = new JsonDecoder(published.GetProperty("executable").GetRawText(), harness.Context);
            using var userExecutable = new JsonDecoder(
                published.GetProperty("userExecutable").GetRawText(), harness.Context);
            DataValue permitted = executable.ReadDataValue("DataValue");
            DataValue denied = userExecutable.ReadDataValue("DataValue");
            Assert.Multiple(() =>
            {
                Assert.That(recipe.GetProperty("available").GetBoolean(), Is.True);
                Assert.That(recipe.GetProperty("methods").GetArrayLength(), Is.EqualTo(1));
                Assert.That(published.GetProperty("nodeId").GetString(), Is.EqualTo(method.ToString()));
                Assert.That(published.GetProperty("browseName").GetString(), Is.EqualTo("1:AddRecipe"));
                Assert.That(permitted.WrappedValue, Is.EqualTo(Variant.From(true)));
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(denied.WrappedValue, Is.EqualTo(Variant.From(false)));
                Assert.That(harness.Calls, Is.Empty);
            });
        }

        [TestCase("SameEntity", "Both")]
        [TestCase("SameEntity", "Forward")]
        [TestCase("SameEntity", "Inverse")]
        [TestCase("OpticalPath", "Forward")]
        [TestCase("DataTransmission", "Inverse")]
        [TestCase("Interface", "Both")]
        public async Task RelationshipsPreserveSelectedTypesDirectionAndEndpointIdentityAsync(
            string relationship, string direction)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var target = new NodeId("endpoint", 2);
            var vendorReference = new NodeId("vendor-reference", 2);
            var type = new NodeId("vendor-interface", 2);
            harness.References[harness.SystemId] =
            [
                new ReferenceDescription
                {
                    NodeId = target, ReferenceTypeId = vendorReference, IsForward = false,
                    BrowseName = new QualifiedName("Camera", 2), DisplayName = new LocalizedText("Camera A"),
                    TypeDefinition = type, NodeClass = NodeClass.Object
                }
            ];
            JsonElement result = await VisionMachineTools.RelationshipsAsync(
                harness.Accessor, harness.SystemId.ToString(), relationship, direction).ConfigureAwait(false);
            NodeId expectedReference = relationship switch
            {
                "SameEntity" => ReferenceTypeIds.RepresentsSameEntityAs,
                "Interface" => ReferenceTypeIds.HasInterface,
                "OpticalPath" => ExpandedNodeId.ToNodeId(
                    Vision.MachineVision.AMCM.ReferenceTypeIds.HasOpticalPathTo,
                    harness.Context.NamespaceUris),
                _ => ExpandedNodeId.ToNodeId(
                    Vision.MachineVision.AMCM.ReferenceTypeIds.TransmitsDataTo,
                    harness.Context.NamespaceUris)
            };
            JsonElement edge = result.GetProperty("references")[0];
            BrowseDescription browse = harness.Browses.Single();
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("references").GetArrayLength(), Is.EqualTo(1));
                Assert.That(result.GetProperty("nextIndex").GetUInt32(), Is.EqualTo(1u));
                Assert.That(result.GetProperty("isComplete").GetBoolean(), Is.True);
                Assert.That(edge.GetProperty("nodeId").GetString(), Is.EqualTo(target.ToString()));
                Assert.That(edge.GetProperty("referenceTypeId").GetString(), Is.EqualTo(vendorReference.ToString()));
                Assert.That(edge.GetProperty("typeDefinition").GetString(), Is.EqualTo(type.ToString()));
                Assert.That(edge.GetProperty("browseName").GetString(), Is.EqualTo("2:Camera"));
                Assert.That(edge.GetProperty("displayName").GetString(), Is.EqualTo("Camera A"));
                Assert.That(edge.GetProperty("isForward").GetBoolean(), Is.False);
                Assert.That(edge.GetProperty("nodeClass").GetString(), Is.EqualTo("Object"));
                Assert.That(browse.NodeId, Is.EqualTo(harness.SystemId));
                Assert.That(browse.ReferenceTypeId, Is.EqualTo(expectedReference));
                Assert.That(browse.BrowseDirection, Is.EqualTo(Enum.Parse<BrowseDirection>(direction)));
                Assert.That(browse.IncludeSubtypes, Is.True);
                Assert.That(harness.MaximumBrowseResults, Is.EqualTo(50u));
                Assert.That(harness.Calls, Is.Empty);
            });
        }

        [TestCase(0u, 2, false, 2u)]
        [TestCase(2u, 1, true, 3u)]
        [TestCase(3u, 0, true, 3u)]
        [TestCase(10000u, 0, true, 10000u)]
        public async Task RelationshipPagesDeduplicateBeforeApplyingOffsetsAsync(
            uint offset, int count, bool complete, uint next)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            static ReferenceDescription Edge(string id) => new()
            {
                NodeId = new NodeId(id, 2),
                ReferenceTypeId = ReferenceTypeIds.RepresentsSameEntityAs,
                IsForward = true
            };
            harness.References[harness.SystemId] = [Edge("a"), Edge("a"), Edge("b"), Edge("c")];
            JsonElement result = await VisionMachineTools.RelationshipsAsync(
                harness.Accessor, harness.SystemId.ToString(), maximumResults: 2, startIndex: offset)
                .ConfigureAwait(false);
            string[] expected = offset == 0 ? ["ns=2;s=a", "ns=2;s=b"] : offset == 2 ? ["ns=2;s=c"] : [];
            Assert.Multiple(() =>
            {
                Assert.That(result.GetProperty("references").GetArrayLength(), Is.EqualTo(count));
                Assert.That(result.GetProperty("references").EnumerateArray()
                    .Select(edge => edge.GetProperty("nodeId").GetString()), Is.EqualTo(expected));
                Assert.That(result.GetProperty("nextIndex").GetUInt32(), Is.EqualTo(next));
                Assert.That(result.GetProperty("isComplete").GetBoolean(), Is.EqualTo(complete));
                Assert.That(harness.Browses, Has.Count.EqualTo(1));
                Assert.That(harness.Calls, Is.Empty);
            });
        }

        [TestCase("Events", false, 1, 1)]
        [TestCase("Events", false, 60, 100)]
        [TestCase("Conditions", true, 5, 50)]
        public async Task ObservationAcceptedBoundsRefuseAClientWithoutOwnedSubscriptionsAsync(
            string mode, bool refresh, int duration, int count)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await VisionMachineTools.ObserveAsync(
                    harness.Accessor, "i=2253", mode, refresh, duration, count).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(error.Message, Does.Contain("subscription"));
                Assert.That(harness.Calls, Is.Empty);
                Assert.That(harness.Browses, Is.Empty);
                Assert.That(harness.Session.Object.Connected, Is.True);
            });
            harness.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [Test]
        public async Task ObservationCallerCancellationIsNotAnEmptySuccessfulWindowAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            OperationCanceledException error = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await VisionMachineTools.ObserveAsync(harness.Accessor, "i=2253", ct: cancellation.Token)
                    .ConfigureAwait(false))!;
            Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(harness.Calls, Is.Empty);
            Assert.That(harness.Browses, Is.Empty);
            harness.Session.Verify(session => session.Dispose(), Times.Never);
        }

        [TestCase(false, 0)]
        [TestCase(false, 1)]
        [TestCase(false, 1048576)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(true, 1048576)]
        public async Task UploadReturnsRegisteredIdWithoutImplicitTransferOrActivationAsync(
            bool configuration, int contentLength)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("management", 1);
            var add = ExpandedNodeId.ToNodeId(configuration
                ? Vision.MachineVision.MethodIds.ConfigurationManagementType_AddConfiguration
                : Vision.MachineVision.MethodIds.RecipeManagementType_AddRecipe, harness.Context.NamespaceUris);
            string operation = configuration ? "AddConfiguration" : "AddRecipe";
            harness.Children[(harness.SystemId, configuration ? "ConfigurationManagement" : "RecipeManagement")] =
                management;
            harness.Children[(management, operation)] = add;
            harness.CallResult = _ => new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = configuration
                    ? [Variant.FromStructure(new ConfigurationIdDataType { Id = "internal-17" }),
                        Variant.From(NodeId.Null), Variant.From(false), Variant.From(0)]
                    : [Variant.FromStructure(new RecipeIdInternalDataType { Id = "internal-17" }),
                        Variant.From(NodeId.Null), Variant.From(NodeId.Null), Variant.From(false), Variant.From(0)]
            };
            const string identifier =
                /*lang=json,strict*/
                """
                {"EncodingMask":7,"Id":"external-7","Version":"v3","Hash":"AQID","HashAlgorithm":"SHA256"}
                """;
            string data = Convert.ToBase64String(new byte[contentLength]);
            JsonElement result = configuration
                ? await VisionMachineTools.UploadConfigurationAsync(harness.Accessor,
                    harness.SystemId.ToString(), data, identifierJson: identifier).ConfigureAwait(false)
                : await VisionMachineTools.UploadRecipeAsync(harness.Accessor,
                    harness.SystemId.ToString(), data, productId: "product-9", identifierJson: identifier)
                    .ConfigureAwait(false);
            CallMethodRequest call = harness.Calls.Single();
            BinaryIdBaseDataType sent = configuration
                ? call.InputArguments[0].GetStructure<ConfigurationIdDataType>()
                : call.InputArguments[0].GetStructure<RecipeIdExternalDataType>();
            using var decoder = new JsonDecoder(result.GetRawText(), harness.Context);
            Variant returned = decoder.ReadVariant("InternalId");
            string? returnedId = configuration
                ? returned.GetStructure<ConfigurationIdDataType>().Id
                : returned.GetStructure<RecipeIdInternalDataType>().Id;
            Assert.Multiple(() =>
            {
                Assert.That(call.ObjectId, Is.EqualTo(management));
                Assert.That(call.MethodId, Is.EqualTo(add));
                Assert.That(sent.Id, Is.EqualTo("external-7"));
                Assert.That(sent.Version, Is.EqualTo("v3"));
                Assert.That(sent.Hash, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3 })));
                Assert.That(sent.HashAlgorithm, Is.EqualTo("SHA256"));
                Assert.That(sent.EncodingMask, Is.EqualTo(7u));
                Assert.That(result.EnumerateObject().Select(field => field.Name),
                    Is.EquivalentTo(s_uploadFields));
                Assert.That(returnedId, Is.EqualTo("internal-17"));
                Assert.That(decoder.ReadVariant("Error"), Is.EqualTo(Variant.From(0)));
                Assert.That(harness.Calls, Has.Count.EqualTo(1), "No prepare, activation, acquisition or retry.");
            });
            if (!configuration)
            {
                Assert.That(call.InputArguments[1].GetStructure<ProductIdDataType>().Id, Is.EqualTo("product-9"));
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task UploadPreservesServiceAndDomainRefusalsWithoutRetryAsync(
            bool configuration, bool serviceRefusal)
        {
            int domainError = serviceRefusal ? 0 : 17;
            StatusCode serviceStatus = serviceRefusal ? StatusCodes.BadUserAccessDenied : StatusCodes.Good;
            StatusCode expectedStatus = serviceRefusal ? StatusCodes.BadUserAccessDenied : StatusCodes.BadInvalidState;
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("management", 1);
            var method = ExpandedNodeId.ToNodeId(configuration
                ? Vision.MachineVision.MethodIds.ConfigurationManagementType_AddConfiguration
                : Vision.MachineVision.MethodIds.RecipeManagementType_AddRecipe, harness.Context.NamespaceUris);
            string operation = configuration ? "AddConfiguration" : "AddRecipe";
            harness.Children[(harness.SystemId, configuration ? "ConfigurationManagement" : "RecipeManagement")] =
                management;
            harness.Children[(management, operation)] = method;
            harness.CallResult = _ => new CallMethodResult
            {
                StatusCode = serviceStatus,
                OutputArguments = configuration
                    ? [Variant.FromStructure(new ConfigurationIdDataType { Id = "refused" }),
                        Variant.From(NodeId.Null), Variant.From(true), Variant.From(domainError)]
                    : [Variant.FromStructure(new RecipeIdInternalDataType { Id = "refused" }),
                        Variant.From(NodeId.Null), Variant.From(NodeId.Null), Variant.From(true),
                        Variant.From(domainError)]
            };
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                if (configuration)
                {
                    await VisionMachineTools.UploadConfigurationAsync(harness.Accessor,
                        harness.SystemId.ToString(), "AQ==", externalId: "external").ConfigureAwait(false);
                }
                else
                {
                    await VisionMachineTools.UploadRecipeAsync(harness.Accessor,
                        harness.SystemId.ToString(), "AQ==", externalId: "external").ConfigureAwait(false);
                }
            })!;
            Assert.That(error.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].MethodId, Is.EqualTo(method));
            if (domainError != 0)
            {
                Assert.That(error.Message, Does.Contain(operation).And.Contain("17"));
            }
        }

        [TestCase(false, "other", "", "id")]
        [TestCase(false, "", "v4", "version")]
        [TestCase(true, "other", "", "id")]
        [TestCase(true, "", "v4", "version")]
        public async Task UploadRefusesConflictingStructuredSelectorsBeforeMutationAsync(
            bool configuration, string id, string version, string parameter)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            const string identifier = /*lang=json,strict*/ """{"EncodingMask":1,"Id":"external","Version":"v3"}""";
            ArgumentException error = Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                if (configuration)
                {
                    await VisionMachineTools.UploadConfigurationAsync(harness.Accessor,
                        harness.SystemId.ToString(), "AQ==", id, version, identifier).ConfigureAwait(false);
                }
                else
                {
                    await VisionMachineTools.UploadRecipeAsync(harness.Accessor,
                        harness.SystemId.ToString(), "AQ==", externalId: id, version: version,
                        identifierJson: identifier).ConfigureAwait(false);
                }
            })!;
            Assert.That(error.ParamName, Is.EqualTo(parameter));
            Assert.That(harness.Calls, Is.Empty);
            Assert.That(harness.Browses, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DownloadReleasesMetadataWhenTheOptionalTransferIsAbsentAsync(bool releaseFails)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("results", 1);
            var get = ExpandedNodeId.ToNodeId(
                Vision.MachineVision.MethodIds.ResultManagementType_GetResultById, harness.Context.NamespaceUris);
            var release = ExpandedNodeId.ToNodeId(
                Vision.MachineVision.MethodIds.ResultManagementType_ReleaseResultHandle,
                harness.Context.NamespaceUris);
            harness.Children[(harness.SystemId, "ResultManagement")] = management;
            harness.Children[(management, "GetResultById")] = get;
            harness.Children[(management, "ReleaseResultHandle")] = release;
            harness.CallResult = call => call.MethodId == get
                ? new CallMethodResult
                {
                    OutputArguments = [Variant.From(71u), Variant.FromStructure(new ResultDataType()),
                        Variant.From(0)]
                }
                : new CallMethodResult
                {
                    StatusCode = releaseFails ? StatusCodes.BadUserAccessDenied : StatusCodes.Good,
                    OutputArguments = [Variant.From(0)]
                };
            if (releaseFails)
            {
                AggregateException error = Assert.ThrowsAsync<AggregateException>(async () =>
                    await VisionMachineTools.DownloadResultAsync(harness.Accessor,
                        harness.SystemId.ToString(), "result-4").ConfigureAwait(false))!;
                Assert.That(error.InnerExceptions.Select(exception => ((ServiceResultException)exception).StatusCode),
                    Is.EqualTo([StatusCodes.BadNotSupported, StatusCodes.BadUserAccessDenied]));
            }
            else
            {
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await VisionMachineTools.DownloadResultAsync(harness.Accessor,
                        harness.SystemId.ToString(), "result-4").ConfigureAwait(false))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                Assert.That(error.Message, Does.Contain("ResultTransfer"));
            }
            Assert.Multiple(() =>
            {
                Assert.That(harness.Calls.Select(call => call.MethodId), Is.EqualTo([get, release]));
                Assert.That(harness.Calls[0].InputArguments[0].GetStructure<ResultIdDataType>().Id,
                    Is.EqualTo("result-4"));
                Assert.That(harness.Calls[1].InputArguments[0], Is.EqualTo(Variant.From(71u)));
                Assert.That(harness.CallTokens[1].CanBeCanceled, Is.False);
            });
            harness.Session.Verify(session => session.Dispose(), Times.Never);
        }

        private sealed class ToolSession : IAsyncDisposable
        {
            private ToolSession(bool control, bool assets, bool perception)
            {
                Provider = Services(McpToolProfile.Vision).BuildServiceProvider();
                Manager = Provider.GetRequiredService<OpcUaSessionManager>();
                Context = ServiceMessageContext.Create(Manager.Telemetry);
                Context.Factory.Builder.AddOpcUaMachineVision().Commit();
                if (control)
                {
                    Context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.ControlNamespaceUri);
                }
                if (assets)
                {
                    Context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.AssetNamespaceUri);
                }
                if (perception)
                {
                    Context.NamespaceUris.GetIndexOrAppend(Vision.Perception.Namespaces.Vision);
                }
                Session.SetupGet(session => session.Connected).Returns(true);
                Session.SetupGet(session => session.ConfiguredEndpoint).Returns(new ConfiguredEndpoint(
                    null, new EndpointDescription(), EndpointConfiguration.Create()));
                Session.SetupGet(session => session.NamespaceUris).Returns(Context.NamespaceUris);
                Session.SetupGet(session => session.MessageContext).Returns(Context);
                Session.SetupGet(session => session.Factory).Returns(Context.Factory);
                Session.SetupGet(session => session.OperationLimits).Returns(new OperationLimits());
                Session.SetupGet(session => session.ServerCapabilities).Returns(new ServerCapabilities());
                Session.SetupGet(session => session.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
                var cache = new Mock<INodeCache>();
                cache.Setup(nodeCache => nodeCache.IsTypeOfAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<bool>(true));
                Session.SetupGet(session => session.NodeCache).Returns(cache.Object);
                Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>((_, paths, _) =>
                        new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                Results = paths.ToList().Select(path =>
                                {
                                    NodeId node = path.StartingNode;
                                    foreach (RelativePathElement element in path.RelativePath.Elements)
                                    {
                                        if (!Children.TryGetValue((node, element.TargetName.Name!), out node))
                                        {
                                            return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                                        }
                                    }
                                    return new BrowsePathResult
                                    {
                                        Targets = [new BrowsePathTarget
                                        {
                                            TargetId = node, RemainingPathIndex = uint.MaxValue
                                        }]
                                    };
                                }).ToArrayOf()
                            }));
                Session.Setup(session => session.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                        (_, _, maximum, requests, _) =>
                        {
                            MaximumBrowseResults = maximum;
                            Browses.AddRange(requests);
                            return new ValueTask<BrowseResponse>(new BrowseResponse
                            {
                                Results = requests.ToList().Select(request => new BrowseResult
                                {
                                    References = References.TryGetValue(request.NodeId,
                                        out ReferenceDescription[]? refs)
                                        ? refs.ToArrayOf() : []
                                }).ToArrayOf()
                            });
                        });
                Session.Setup(session => session.ReadAsync(It.IsAny<RequestHeader>(), It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(), It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                        (_, _, _, requests, _) => new ValueTask<ReadResponse>(new ReadResponse
                        {
                            Results = requests.ToList().Select(request =>
                                Readings.TryGetValue((request.NodeId, request.AttributeId), out DataValue value)
                                    ? value : DataValue.FromStatusCode(StatusCodes.BadNotReadable)).ToArrayOf()
                        }));
                Session.Setup(session => session.CallAsync(It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, token) =>
                    {
                        Calls.AddRange(requests);
                        CallTokens.AddRange(requests.ToList().Select(_ => token));
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            Results = requests.ToList().Select(CallResult).ToArrayOf()
                        });
                    });
                Accessor = new VisionClientAccessor(Manager);
            }

            public static async Task<ToolSession> CreateAsync(
                bool control = true, bool assets = true, bool perception = false)
            {
                var harness = new ToolSession(control, assets, perception);
                await harness.Manager.RegisterExistingSessionAsync("vision", harness.Session.Object, "Anonymous")
                    .ConfigureAwait(false);
                return harness;
            }

            public NodeId SystemId { get; } = new("system", 1);
            public Mock<ISession> Session { get; } = new();
            public ServiceMessageContext Context { get; }
            public ServiceProvider Provider { get; }
            public OpcUaSessionManager Manager { get; }
            public VisionClientAccessor Accessor { get; }
            public Dictionary<(NodeId, string), NodeId> Children { get; } = [];
            public Dictionary<NodeId, ReferenceDescription[]> References { get; } = [];
            public Dictionary<(NodeId, uint), DataValue> Readings { get; } = [];
            public List<BrowseDescription> Browses { get; } = [];
            public uint MaximumBrowseResults { get; private set; }
            public List<CallMethodRequest> Calls { get; } = [];
            public List<CancellationToken> CallTokens { get; } = [];

            public Func<CallMethodRequest, CallMethodResult> CallResult { get; set; } =
                _ => new CallMethodResult { StatusCode = StatusCodes.BadNotSupported };

            public ValueTask DisposeAsync()
            {
                return Provider.DisposeAsync();
            }
        }

        private static ServiceCollection Services(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpVision();
            services.AddMcpServer().WithOpcUaVisionTools(profile);
            return services;
        }

        private static readonly string[] s_names =
        [
            "vision_machine_list",
            "vision_machine_read_state",
            "vision_machine_command",
            "vision_machine_read_asset",
            "vision_machine_recipes",
            "vision_machine_configurations",
            "vision_machine_results",
            "vision_machine_report_safety",
            "vision_machine_file",
            "vision_machine_recipe_object",
            "vision_machine_condition"
        ];

        private static readonly string[] s_followupNames =
        [
            "vision_machine_capabilities",
            "vision_machine_relationships",
            "vision_machine_observe",
            "vision_machine_upload_recipe",
            "vision_machine_upload_configuration",
            "vision_machine_download_result"
        ];

        private static readonly string[] s_stateRequired = ["systemNodeId"];
        private static readonly string[] s_stateFields = ["systemNodeId", "includeDetails", "sessionName"];
        private static readonly string[] s_uploadFields = ["InternalId", "Error"];

        private static readonly string[] s_absentInterfaces =
        [
            "VisionStateMachine", "RecipeManagement", "RecipeManagement/RecipeTransfer",
            "ConfigurationManagement", "ConfigurationManagement/ConfigurationTransfer",
            "ResultManagement", "ResultManagement/ResultTransfer", "SafetyStateManagement"
        ];
    }
}
#endif
