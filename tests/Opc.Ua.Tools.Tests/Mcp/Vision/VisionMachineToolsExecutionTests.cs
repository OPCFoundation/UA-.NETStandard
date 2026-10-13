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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Vision.MachineVision;
using MvMethods = Opc.Ua.Vision.MachineVision.MethodIds;

namespace Opc.Ua.Tools.Tests.Mcp
{
    public sealed partial class VisionMachineToolsTests
    {
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(1048576)]
        public async Task CompleteResultDownloadReturnsExactBytesAndReleasesBothHandlesAsync(int length)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            harness.Context.MaxByteStringLength = 2 * 1024 * 1024;
            var management = new NodeId("results", 1);
            var transfer = new NodeId("transfer", 1);
            var file = new NodeId("file", 1);
            harness.Children[(harness.SystemId, "ResultManagement")] = management;
            harness.Children[(management, "ResultTransfer")] = transfer;
            var getResult = ExpandedNodeId.ToNodeId(
                MvMethods.ResultManagementType_GetResultById, harness.Context.NamespaceUris);
            var releaseResult = ExpandedNodeId.ToNodeId(
                MvMethods.ResultManagementType_ReleaseResultHandle, harness.Context.NamespaceUris);
            byte[] content = new byte[length];
            Array.Fill(content, (byte)42);
            int position = 0;
            harness.CallResult = call =>
            {
                if (call.MethodId == getResult)
                {
                    return new CallMethodResult
                    {
                        OutputArguments = [Variant.From(7u), Variant.FromStructure(new ResultDataType()),
                            Variant.From(0)]
                    };
                }
                if (call.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForRead)
                {
                    return new CallMethodResult
                    {
                        OutputArguments = [Variant.From(file), Variant.From(11u), Variant.From(NodeId.Null)]
                    };
                }
                if (call.MethodId == MethodIds.FileType_Read)
                {
                    Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(11u)));
                    Assert.That(call.InputArguments[1].TryGetValue(out int requested), Is.True);
                    int count = Math.Min(requested, content.Length - position);
                    var bytes = ByteString.From(content.AsMemory(position, count));
                    position += count;
                    return new CallMethodResult { OutputArguments = [Variant.From(bytes)] };
                }
                return new CallMethodResult
                {
                    OutputArguments = call.MethodId == releaseResult
                        ? [Variant.From(0)] : []
                };
            };
            JsonElement response = await VisionMachineTools.DownloadResultAsync(
                harness.Accessor, harness.SystemId.ToString(), "result").ConfigureAwait(false);
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("Data").TryGetValue(out ByteString downloaded), Is.True);
            Assert.That(downloaded.Span.ToArray(), Is.EqualTo(content));
            Assert.That(decoder.ReadVariant("Length"), Is.EqualTo(Variant.From((long)length)));
            Assert.That(harness.Calls.Count(c => c.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(harness.Calls.Count(c => c.MethodId == releaseResult),
                Is.EqualTo(1));
            Assert.That(harness.Calls[^1].InputArguments[0], Is.EqualTo(Variant.From(7u)));
        }

        [Test]
        public async Task OversizedResultDownloadFailsWithoutReturningPartialBytesAndReleasesHandlesAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("results", 1);
            var transfer = new NodeId("transfer", 1);
            harness.Children[(harness.SystemId, "ResultManagement")] = management;
            harness.Children[(management, "ResultTransfer")] = transfer;
            var get = ExpandedNodeId.ToNodeId(MvMethods.ResultManagementType_GetResultById,
                harness.Context.NamespaceUris);
            var release = ExpandedNodeId.ToNodeId(MvMethods.ResultManagementType_ReleaseResultHandle,
                harness.Context.NamespaceUris);
            harness.CallResult = call => new CallMethodResult
            {
                OutputArguments = call.MethodId == get
                    ? [Variant.From(7u), Variant.FromStructure(new ResultDataType()), Variant.From(0)]
                    : call.MethodId == global::Opc.Ua.MethodIds.TemporaryFileTransferType_GenerateFileForRead
                        ? [Variant.From(new NodeId("file", 1)), Variant.From(11u), Variant.From(NodeId.Null)]
                        : call.MethodId == global::Opc.Ua.MethodIds.FileType_Read
                            ? [Variant.From(ByteString.From(new byte[] { 1, 2, 3, 4 }))]
                            : call.MethodId == release ? [Variant.From(0)] : []
            };
            ServiceResultException? tooLarge = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await VisionMachineTools.DownloadResultAsync(
                    harness.Accessor, harness.SystemId.ToString(), "result", maximumBytes: 3).ConfigureAwait(false));
            Assert.That(tooLarge!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            Assert.That(harness.Calls.Count(c => c.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(harness.Calls.Count(c => c.MethodId == release), Is.EqualTo(1));
            Assert.That(harness.Calls[^1].InputArguments[0], Is.EqualTo(Variant.From(7u)));
        }

        [TestCase("Prepare")]
        [TestCase("Unprepare")]
        [TestCase("LinkProduct")]
        [TestCase("UnlinkProduct")]
        public async Task RecipeObjectOperationsPreserveThePublishedObjectIdentityAsync(string operation)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            harness.CallResult = _ => new CallMethodResult
            {
                OutputArguments = operation == "Prepare"
                    ? [Variant.From(true), Variant.From(-9)] : [Variant.From(-9)]
            };
            JsonElement response = await VisionMachineTools.RecipeObjectAsync(
                harness.Accessor, "ns=1;s=recipe", operation, "product").ConfigureAwait(false);
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("Error"), Is.EqualTo(Variant.From(-9)));
            Assert.That(harness.Calls.Single().ObjectId, Is.EqualTo(new NodeId("recipe", 1)));
            if (operation.Contains("Product", StringComparison.Ordinal))
            {
                Assert.That(harness.Calls[0].InputArguments[0].TryGetStructure<ProductIdDataType>(
                    out ProductIdDataType? product), Is.True);
                Assert.That(product!.Id, Is.EqualTo("product"));
            }
        }

        [TestCase("Recipe")]
        [TestCase("Configuration")]
        [TestCase("Result")]
        public async Task SpecializedTransfersRetainTheirTypedOptionsAndAlreadyOpenHandlesAsync(string kind)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            harness.CallResult = _ => new CallMethodResult
            {
                OutputArguments = [Variant.From(new NodeId("file", 1)), Variant.From(11u),
                    Variant.From(new NodeId("completion", 1))]
            };
            JsonElement response = await VisionMachineTools.FileAsync(
                harness.Accessor, "ns=1;s=transfer", "GenerateRead", kind, "document").ConfigureAwait(false);
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("Handle"), Is.EqualTo(Variant.From(11u)));
            Assert.That(decoder.ReadVariant("CompletionStateMachine"),
                Is.EqualTo(Variant.From(new NodeId("completion", 1))));
            Assert.That(harness.Calls, Has.Count.EqualTo(1), "The generated file is already open.");
            Variant options = harness.Calls[0].InputArguments[0];
            string? id = kind switch
            {
                "Recipe" => options.GetStructure<RecipeTransferOptions>().InternalId.Id,
                "Configuration" => options.GetStructure<ConfigurationTransferOptions>().InternalId.Id,
                _ => options.GetStructure<ResultTransferOptions>().Id.Id
            };
            Assert.That(id, Is.EqualTo("document"));
        }

        [Test]
        public async Task StateDetailsRetainTheDefaultResponseAndExposeOptionalReadingsAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var control = new NodeId("control", 1);
            var automatic = new NodeId("automatic", 1);
            var current = new NodeId("current", 1);
            var id = new NodeId("id", 1);
            var level = new NodeId("level", 1);
            harness.Children[(harness.SystemId, "VisionStateMachine")] = control;
            harness.Children[(control, "AutomaticModeStateMachine")] = automatic;
            harness.Children[(control, "CurrentState")] = current;
            harness.Children[(automatic, "CurrentState")] = current;
            harness.Children[(current, "Id")] = id;
            harness.Children[(harness.SystemId, "DiagnosticLevel")] = level;
            harness.Readings[(current, Attributes.Value)] = new DataValue(Variant.From(new LocalizedText("Ready")));
            harness.Readings[(id, Attributes.Value)] = new DataValue(
                Variant.From(Vision.MachineVision.ObjectIds.VisionAutomaticModeStateMachineType_Ready));
            harness.Readings[(level, Attributes.Value)] = DataValue.FromStatusCode(StatusCodes.BadNoData);
            JsonElement standard = await VisionMachineTools.ReadStateAsync(harness.Accessor,
                harness.SystemId.ToString()).ConfigureAwait(false);
            JsonElement detailed = await VisionMachineTools.ReadStateWithDetailsAsync(
                harness.Accessor, harness.SystemId.ToString(), includeDetails: true).ConfigureAwait(false);
            Assert.That(standard.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(s_basicStateFields));
            Assert.That(detailed.GetProperty("control").GetRawText(),
                Is.EqualTo(standard.GetProperty("control").GetRawText()));
            Assert.That(detailed.GetProperty("details").GetProperty("values").GetArrayLength(), Is.EqualTo(5));
            JsonElement reading = detailed.GetProperty("details").GetProperty("values")[0].GetProperty("reading");
            using var decoder = new JsonDecoder(reading.GetRawText(), harness.Context);
            Assert.That(decoder.ReadDataValue("DataValue").StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(harness.Calls, Is.Empty);
        }

        private static readonly string[] s_basicStateFields = ["control", "automatic"];

        [Test]
        public async Task ObservationToolReturnsUsableConditionSelectorsAndQualifiedFieldsAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync(control: false, assets: true)
                .ConfigureAwait(false);
            var manager = new Mock<ISubscriptionManager>();
            var subscription = new Mock<ISubscription>();
            var items = new Mock<IMonitoredItemCollection>();
            var item = new Mock<IMonitoredItem>();
            ISubscriptionManager? managerValue = manager.Object;
            IMonitoredItem? itemValue = item.Object;
            ISubscriptionNotificationHandler? handler = null;
            harness.Session.Setup(s => s.TryGetSubscriptionManager(out managerValue)).Returns(true);
            manager.Setup(m => m.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                It.IsAny<IOptionsMonitor<SubscriptionOptions>>()))
                .Callback<ISubscriptionNotificationHandler, IOptionsMonitor<SubscriptionOptions>>(
                    (notificationHandler, _) => handler = notificationHandler)
                .Returns(subscription.Object);
            subscription.SetupGet(s => s.MonitoredItems).Returns(items.Object);
            items.Setup(i => i.TryAdd(It.IsAny<string>(), It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(),
                out itemValue)).Returns(true);
            item.SetupGet(i => i.Created).Returns(true);
            var accessor = new VisionClientAccessor(harness.Manager);
            Task<JsonElement> observing = VisionMachineTools.ObserveAsync(accessor, "ns=2;s=notifier",
                mode: "Conditions", maximumNotifications: 1);
            var eventId = ByteString.From(new byte[] { 7, 8, 9 });
            var conditionId = new NodeId("condition", 2);
            QualifiedName[][] paths = EventRecordDecoderRegistry.Default.StandardFields;
            Variant[] fields = [.. paths.Select(path => path.Length == 0 ? Variant.From(conditionId) :
                path[^1].Name switch
                {
                    "EventId" => Variant.From(eventId),
                    "EventType" => Variant.From(ObjectTypeIds.AcknowledgeableConditionType),
                    _ => Variant.Null
                })];
            await handler!.OnEventDataNotificationAsync(subscription.Object, 1, DateTime.UtcNow,
                new EventNotification[] { new(item.Object, fields.ToArrayOf()) }, PublishState.None, [])
                    .ConfigureAwait(false);
            JsonElement response = await observing.ConfigureAwait(false);
            Assert.That(response.GetProperty("completionReason").GetString(), Is.EqualTo("LimitReached"));
            JsonElement notification = response.GetProperty("events")[0];
            Assert.That(notification.GetProperty("conditionNodeId").GetString(), Is.EqualTo(conditionId.ToString()));
            Assert.That(notification.GetProperty("eventIdBase64").GetString(), Is.EqualTo("BwgJ"));
            Assert.That(response.GetProperty("fieldPaths").GetArrayLength(), Is.EqualTo(paths.Length));
            Assert.That(response.GetProperty("refreshComplete").GetBoolean(), Is.False);
            subscription.Verify(s => s.DisposeAsync(), Times.Once);
            harness.Session.Verify(s => s.DisposeAsync(), Times.Never);
        }

        [Test]
        public async Task TransferWaitReportsIdleAndRejectsServerErrorAsync()
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var completion = new NodeId("completion", 1);
            var currentState = new NodeId("state", 1);
            var stateId = new NodeId("state-id", 1);
            harness.Children[(completion, "CurrentState")] = currentState;
            harness.Children[(currentState, "Id")] = stateId;
            harness.Readings[(currentState, Attributes.Value)] = new DataValue(Variant.From(new LocalizedText("Idle")));
            harness.Readings[(stateId, Attributes.Value)] =
                new DataValue(Variant.From(ObjectIds.FileTransferStateMachineType_Idle));
            JsonElement result = await VisionMachineTools.FileAsync(
                harness.Accessor, completion.ToString(), "WaitForCompletion").ConfigureAwait(false);
            Assert.That(result.GetProperty("state").GetString(), Is.EqualTo("Idle"));
            Assert.That(result.GetProperty("stateId").GetString(),
                Is.EqualTo(new NodeId(Objects.FileTransferStateMachineType_Idle).ToString()));
            harness.Readings[(stateId, Attributes.Value)] =
                new DataValue(Variant.From(ObjectIds.FileTransferStateMachineType_Error));
            Assert.ThrowsAsync<System.IO.IOException>(async () =>
                await VisionMachineTools.FileAsync(harness.Accessor, completion.ToString(), "WaitForCompletion")
                    .ConfigureAwait(false));
            Assert.That(harness.Calls, Is.Empty, "Completion inspection must never commit or retry the transfer.");
        }

        [TestCase("List")]
        [TestCase("Add")]
        [TestCase("Prepare")]
        [TestCase("Unprepare")]
        [TestCase("PrepareProduct")]
        [TestCase("UnprepareProduct")]
        [TestCase("Remove")]
        [TestCase("UnlinkProduct")]
        [TestCase("Release")]
        public async Task RecipeOperationsPreserveTypedOutputsAndDomainErrorsAsync(string operation)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("recipes", 1);
            harness.Children[(harness.SystemId, "RecipeManagement")] = management;
            var id = new RecipeIdInternalDataType { Id = "inside" };
            ArrayOf<Variant> outputs = operation switch
            {
                "List" => [Variant.From(false), Variant.From(1u), Variant.From(9u),
                    Variant.FromStructure(ArrayOf.Wrapped([id])), Variant.From(-17)],
                "Add" => [Variant.FromStructure(id), Variant.From(new NodeId("recipe", 1)),
                    Variant.From(new NodeId("product", 1)), Variant.From(true), Variant.From(-17)],
                "Prepare" => [Variant.FromStructure(id), Variant.From(false), Variant.From(-17)],
                "Unprepare" or "PrepareProduct" or "UnprepareProduct" =>
                    [Variant.FromStructure(id), Variant.From(-17)],
                _ => [Variant.From(-17)]
            };
            harness.CallResult = _ => new CallMethodResult { OutputArguments = outputs };
            JsonElement response = await VisionMachineTools.RecipesAsync(
                harness.Accessor, harness.SystemId.ToString(), operation, "outside", "inside", "product",
                maximumResults: 1, startIndex: 2, handle: 9).ConfigureAwait(false);
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("Error"), Is.EqualTo(Variant.From(-17)));
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(management));
            if (operation == "List")
            {
                Assert.That(decoder.ReadVariant("Handle"), Is.EqualTo(Variant.From(9u)));
                Assert.That(decoder.ReadVariant("IsComplete"), Is.EqualTo(Variant.From(false)));
                Assert.That(decoder.ReadVariant("Recipes").TryGetStructure(
                    out ArrayOf<RecipeIdInternalDataType> recipes), Is.True);
                Assert.That(recipes[0].Id, Is.EqualTo("inside"));
                Assert.That(harness.Calls[0].InputArguments[3], Is.EqualTo(Variant.From(1u)));
                Assert.That(harness.Calls[0].InputArguments[4], Is.EqualTo(Variant.From(2u)));
            }
            if (operation == "Release")
            {
                Assert.That(harness.Calls[0].InputArguments[0], Is.EqualTo(Variant.From(9u)));
            }
        }

        [TestCase("List")]
        [TestCase("Get")]
        [TestCase("Add")]
        [TestCase("Activate")]
        [TestCase("Remove")]
        [TestCase("Release")]
        public async Task ConfigurationOperationsRetainMetadataAndExplicitMutationAsync(string operation)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var management = new NodeId("configurations", 1);
            harness.Children[(harness.SystemId, "ConfigurationManagement")] = management;
            var data = new ConfigurationDataType
            {
                ExternalId = new ConfigurationIdDataType { Id = "external", Version = "2", EncodingMask = 1 }
            };
            ArrayOf<Variant> outputs = operation switch
            {
                "List" => [Variant.From(true), Variant.From(1u), Variant.From(19u),
                    Variant.FromStructure(ArrayOf.Wrapped([data])), Variant.From(0)],
                "Get" => [Variant.From(19u), Variant.FromStructure(data), Variant.From(0)],
                "Add" => [Variant.FromStructure(data.ExternalId), Variant.From(new NodeId("config", 1)),
                    Variant.From(true), Variant.From(0)],
                _ => [Variant.From(0)]
            };
            harness.CallResult = _ => new CallMethodResult { OutputArguments = outputs };
            JsonElement response = await VisionMachineTools.ConfigurationsAsync(harness.Accessor,
                harness.SystemId.ToString(), operation, id: "external", version: "2", handle: 19)
                .ConfigureAwait(false);
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("Error"), Is.EqualTo(Variant.From(0)));
            Assert.That(harness.Calls, Has.Count.EqualTo(1), "No implicit activation or retry.");
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(management));
            if (operation is "Get" or "Add" or "Activate" or "Remove")
            {
                Assert.That(harness.Calls[0].InputArguments[0].TryGetStructure<ConfigurationIdDataType>(
                    out ConfigurationIdDataType? identifier), Is.True);
                Assert.That(identifier!.Id, Is.EqualTo("external"));
                Assert.That(identifier.Version, Is.EqualTo("2"));
                Assert.That(identifier.EncodingMask, Is.EqualTo(1));
            }
        }

        [TestCase("SelectAutomatic")]
        [TestCase("Halt")]
        [TestCase("Reset")]
        [TestCase("Stop")]
        [TestCase("Abort")]
        [TestCase("EnableSimulation")]
        [TestCase("DisableSimulation")]
        [TestCase("Sync")]
        [TestCase("ConfirmAll")]
        public async Task ExplicitCommandsDoNotRetryOrChangeModeImplicitlyAsync(string command)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var control = new NodeId("control", 1);
            var automatic = new NodeId("automatic", 1);
            harness.Children[(harness.SystemId, "VisionStateMachine")] = control;
            harness.Children[(control, "AutomaticModeStateMachine")] = automatic;
            harness.CallResult = _ => new CallMethodResult
            {
                OutputArguments = command == "ConfirmAll" ? [] : [Variant.From(-23)]
            };
            JsonElement response = await VisionMachineTools.CommandAsync(harness.Accessor,
                harness.SystemId.ToString(), command, cause: 5, causeDescription: "operator")
                .ConfigureAwait(false);
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("Error"), Is.EqualTo(Variant.From(command == "ConfirmAll" ? 0 : -23)));
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(command switch
            {
                "SelectAutomatic" or "Halt" or "Reset" or "ConfirmAll" => control,
                "Sync" => harness.SystemId,
                _ => automatic
            }));
            if (command is "EnableSimulation" or "DisableSimulation")
            {
                Assert.That(harness.Calls[0].InputArguments[0],
                    Is.EqualTo(Variant.From(command == "EnableSimulation")));
            }
        }

        [TestCase("StartSingleJob")]
        [TestCase("StartContinuous")]
        public async Task StartCommandsPreserveCompleteRecipeIdentifierAndVariantParametersAsync(string command)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            var control = new NodeId("control", 1);
            var automatic = new NodeId("automatic", 1);
            harness.Children[(harness.SystemId, "VisionStateMachine")] = control;
            harness.Children[(control, "AutomaticModeStateMachine")] = automatic;
            harness.CallResult = _ => new CallMethodResult
            {
                OutputArguments = [Variant.FromStructure(new JobIdDataType { Id = "job-1" }), Variant.From(0)]
            };
            using var encoder = new JsonEncoder(harness.Context);
            encoder.WriteVariantArray("Parameters", [Variant.From(42), Variant.From("source")]);
            encoder.WriteEncodeable("ExternalRecipeId", new RecipeIdExternalDataType
            {
                Id = "recipe",
                Version = "v2",
                Hash = ByteString.From(new byte[] { 1, 2, 3 }),
                HashAlgorithm = "SHA256",
                EncodingMask = 7
            });
            string parameters = encoder.CloseAndReturnText();
            JsonElement response = await VisionMachineTools.CommandAsync(harness.Accessor,
                harness.SystemId.ToString(), command, recipeId: "recipe", parametersJson: parameters)
                .ConfigureAwait(false);
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(automatic));
            Assert.That(harness.Calls[0].InputArguments[2].TryGetStructure<RecipeIdExternalDataType>(
                out RecipeIdExternalDataType? recipe), Is.True);
            Assert.That(recipe!.Version, Is.EqualTo("v2"));
            Assert.That(recipe.Hash, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3 })));
            Assert.That(harness.Calls[0].InputArguments[4].TryGetValue(out ArrayOf<Variant> sent), Is.True);
            Assert.That(sent.ToArray(), Is.EqualTo([Variant.From(42), Variant.From("source")]));
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            Assert.That(decoder.ReadVariant("JobId").TryGetStructure<JobIdDataType>(out JobIdDataType? job), Is.True);
            Assert.That(job!.Id, Is.EqualTo("job-1"));
        }

        [TestCase("Open")]
        [TestCase("Read")]
        [TestCase("Write")]
        [TestCase("Close")]
        [TestCase("GetPosition")]
        [TestCase("SetPosition")]
        public async Task ChunkedFileOperationsRetainHandleAndPayloadSemanticsAsync(string operation)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            harness.CallResult = _ => new CallMethodResult
            {
                OutputArguments = operation switch
                {
                    "Open" => [Variant.From(31u)],
                    "Read" => [Variant.From(ByteString.From(new byte[] { 1, 2, 3 }))],
                    "GetPosition" => [Variant.From(19ul)],
                    _ => []
                }
            };
            JsonElement response = await VisionMachineTools.FileAsync(harness.Accessor, "ns=1;s=file",
                operation, handle: 31, length: 3, dataBase64: "AQID", position: 19, mode: 3).ConfigureAwait(false);
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(new NodeId("file", 1)));
            Assert.That(harness.Calls[0].InputArguments[0],
                Is.EqualTo(operation == "Open" ? Variant.From((byte)3) : Variant.From(31u)));
            if (operation == "Write")
            {
                Assert.That(harness.Calls[0].InputArguments[1],
                    Is.EqualTo(Variant.From(ByteString.From(new byte[] { 1, 2, 3 }))));
            }
            using var decoder = new JsonDecoder(response.GetRawText(), harness.Context);
            if (operation == "Read")
            {
                Assert.That(decoder.ReadVariant("Data"),
                    Is.EqualTo(Variant.From(ByteString.From(new byte[] { 1, 2, 3 }))));
            }
            if (operation == "GetPosition")
            {
                Assert.That(decoder.ReadVariant("Position"), Is.EqualTo(Variant.From(19ul)));
            }
        }

        [TestCase("Acknowledge")]
        [TestCase("Confirm")]
        public async Task ConditionActionsForwardExactEventIdAndCommentAsync(string operation)
        {
            await using ToolSession harness = await ToolSession.CreateAsync().ConfigureAwait(false);
            harness.CallResult = _ => new CallMethodResult { OutputArguments = [] };
            await VisionMachineTools.ConditionAsync(harness.Accessor, "ns=1;s=condition", "AQID",
                operation, "reviewed").ConfigureAwait(false);
            Assert.That(harness.Calls, Has.Count.EqualTo(1));
            Assert.That(harness.Calls[0].ObjectId, Is.EqualTo(new NodeId("condition", 1)));
            Assert.That(harness.Calls[0].InputArguments[0],
                Is.EqualTo(Variant.From(ByteString.From(new byte[] { 1, 2, 3 }))));
            Assert.That(harness.Calls[0].InputArguments[1],
                Is.EqualTo(Variant.From(new LocalizedText("reviewed"))));
        }
    }
}
#endif
