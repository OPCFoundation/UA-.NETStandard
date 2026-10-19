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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Exercises the released models through real node-manager and method-state dispatch.
    /// </summary>
    [TestFixture]
    [Category("Vision")]
    public sealed partial class MachineVisionRuntimeTests
    {
        [Test]
        public async Task FullSystemCreatesEveryManagementAndStepFamilyAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(system.State.RecipeManagement, Is.Not.Null);
                Assert.That(system.State.ConfigurationManagement, Is.Not.Null);
                Assert.That(system.State.ResultManagement, Is.Not.Null);
                Assert.That(system.State.SafetyStateManagement, Is.Not.Null);
                Assert.That(system.ControlState.PreoperationalStepModel, Is.Not.Null);
                Assert.That(system.ControlState.HaltedStepModel, Is.Not.Null);
                Assert.That(system.ControlState.ErrorStepModel, Is.Not.Null);
                Assert.That(system.AutomaticState.InitializedStepModel, Is.Not.Null);
                Assert.That(system.AutomaticState.ReadyStepModel, Is.Not.Null);
                Assert.That(system.AutomaticState.SingleExecutionStepModel, Is.Not.Null);
                Assert.That(system.AutomaticState.ContinuousExecutionStepModel, Is.Not.Null);
            });
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Preoperational"));
            Assert.That(
                system.State.RecipeManagement!.RecipeTransfer!.GenerateFileForWrite!.InputArguments!.Value.Count,
                Is.EqualTo(1));
        }

        [Test]
        public async Task AutomaticModeRunsOneJobPublishesResultAndReturnsReadyAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object,
                    new MachineVisionSystemOptions { InitiallyReady = true }).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            var complete =
                new TaskCompletionSource<ReadyEventState>(TaskCreationOptions.RunContinuationsAsynchronously);
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is ReadyEventState ready)
                {
                    complete.TrySetResult(ready);
                }
            };
            ArrayOf<Variant> output = await CallAsync(
                fixture, system.AutomaticState, system.AutomaticState.StartSingleJob!,
                [
                    Variant.FromStructure(new MeasIdDataType { Id = "measurement" }),
                    Variant.FromStructure(new PartIdDataType { Id = "part" }),
                    Variant.FromStructure(new RecipeIdExternalDataType()),
                    Variant.FromStructure(new ProductIdDataType()),
                    Variant.From(ArrayOf<Variant>.Empty)
                ]).ConfigureAwait(false);
            Assert.That(output[0].TryGetStructure<JobIdDataType>(out JobIdDataType? job), Is.True);
            Assert.That(job!.Id, Is.Not.Empty);
            ReadyEventState completed = await complete.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(completed.JobId!.Value.Id, Is.EqualTo(job.Id));
            Assert.That(completed.SourceNode!.Value, Is.EqualTo(system.AutomaticState.NodeId));
            MachineVisionDocument? document = await system.Store.ReadAsync(MachineVisionDocumentKind.Result, "result")
                .ConfigureAwait(false);
            Assert.That(document, Is.Not.Null);
            Assert.That(document!.Metadata.TryGetStructure<ResultDataType>(out ResultDataType? result), Is.True);
            Assert.That(result!.JobId.Id, Is.EqualTo(job.Id));
            Assert.That(result.MeasId.Id, Is.EqualTo("measurement"));
            Assert.That(result.ResultContent[0].TryGetValue(out int answer), Is.True);
            Assert.That(answer, Is.EqualTo(42));
        }

        [Test]
        public async Task AssetBuilderPublishesEveryComponentCategoryAndLiveHealthAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            MachineVisionAssetBuilder builder = fixture.CreateBuildContext().AddMachineVisionAsset("InspectionAsset");
            builder.WithIdentification(Identification("System", "urn:vision:system"));
#if NET5_0_OR_GREATER
            foreach (MachineVisionItemKind kind in Enum.GetValues<MachineVisionItemKind>())
#else
            foreach (MachineVisionItemKind kind in Enum.GetValues(typeof(MachineVisionItemKind)))
#endif
            {
                builder.AddItem(kind, kind.ToString())
                    .WithIdentification(Identification(kind.ToString()))
                    .WithHealth(health => health.Set("Temperature", Variant.From(22.5)));
            }
            MachineVisionAssetHandle handle = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(handle.State.Identification!.ProductInstanceUri!.Value, Is.EqualTo("urn:vision:system"));
            await handle.UpdateAsync(nodes => nodes.WithHealth(
                health => health.Set("Temperature", Variant.From(35.5)))).ConfigureAwait(false);
            Assert.That(handle.State.Health!.Temperature!.Value, Is.EqualTo(35.5));
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(handle.State.Health.Temperature.NodeId),
                Is.SameAs(handle.State.Health.Temperature));
        }

        [Test]
        public async Task MemoryStoreClonesMetadataAndRefusesCapacityOverflowWithoutEvictionAsync()
        {
            var store = new InMemoryMachineVisionStore(1, 4);
            var metadata = new ResultIdDataType { Id = "original" };
            await store.WriteAsync(new MachineVisionDocument(
                MachineVisionDocumentKind.Result, "one", Variant.FromStructure(metadata),
                ByteString.From(new byte[] { 1 })))
                .ConfigureAwait(false);
            metadata.Id = "changed";
            await Assert.ThrowsAsync<ServiceResultException>(async () => await store.WriteAsync(
                new MachineVisionDocument(MachineVisionDocumentKind.Result, "two", Variant.Null, default))
                .ConfigureAwait(false));
            MachineVisionDocument? saved = await store.ReadAsync(MachineVisionDocumentKind.Result, "one")
                .ConfigureAwait(false);
            Assert.That(saved!.Metadata.TryGetStructure<ResultIdDataType>(out ResultIdDataType? identity), Is.True);
            Assert.That(identity!.Id, Is.EqualTo("original"));
            Assert.That(saved.Content.Span[0], Is.EqualTo(1));
            Assert.That(await store.ReadAsync(MachineVisionDocumentKind.Result, "two").ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task ReportedErrorPreservesApplicationDetailsAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            VisionConditionState condition = await ReportCameraErrorAsync(system).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(condition.Message!.Value.Text, Is.EqualTo("Camera disconnected"));
                Assert.That(condition.ErrorString!.Value, Is.EqualTo("Camera disconnected"));
                Assert.That(condition.ErrorCode!.Value, Is.EqualTo(42));
                Assert.That(condition.BlockReaction!.Value, Is.True);
                Assert.That(condition.StopReaction!.Value, Is.True);
                Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Error"));
            });
        }

        [Test]
        public async Task PersistentErrorPublishesCallableConfirmationAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            VisionConditionState condition = await ReportCameraErrorAsync(system, persistent: true)
                .ConfigureAwait(false);
            Assert.That(condition, Is.InstanceOf<VisionPersistentErrorConditionState>());
            Assert.That(condition.Confirm, Is.Not.Null);
            Assert.That(condition.Confirm!.Executable, Is.True);
            Assert.That(condition.ConfirmedState!.Id!.Value, Is.False);
            await CallAsync(fixture, condition, condition.Acknowledge!,
                [condition.EventId!.Value, new LocalizedText("acknowledged")]).ConfigureAwait(false);
            await CallAsync(fixture, condition, condition.Confirm!,
                [condition.EventId!.Value, new LocalizedText("confirmed")]).ConfigureAwait(false);
            Assert.That(condition.ConfirmedState.Id.Value, Is.True);
            Assert.That(condition.Retain!.Value, Is.True, "Confirmation must not clear an unresolved error.");
            await system.ReportErrorAsync("camera", new LocalizedText("Camera reconnected"), false)
                .ConfigureAwait(false);
            Assert.That(condition.Retain.Value, Is.False);
        }

        [Test]
        public async Task AcknowledgementUsesReportedEventIdAndRetainsActiveErrorAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            VisionConditionState condition = await ReportCameraErrorAsync(system).ConfigureAwait(false);
            ByteString reported = condition.EventId!.Value;
            await CallAsync(fixture, condition, condition.Acknowledge!,
                [reported, new LocalizedText("acknowledged")]).ConfigureAwait(false);
            Assert.That(condition.AckedState!.Id!.Value, Is.True);
            Assert.That(condition.Retain!.Value, Is.True);
            await system.ReportErrorAsync("camera", new LocalizedText("Camera reconnected"), false)
                .ConfigureAwait(false);
            Assert.That(condition.Retain.Value, Is.False);
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Preoperational"));
        }

        [Test]
        public async Task PersistentErrorRequiresConfirmationAfterPhysicalResolutionAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            VisionConditionState condition = await ReportCameraErrorAsync(system, persistent: true)
                .ConfigureAwait(false);
            await system.ReportErrorAsync("camera", new LocalizedText("Camera reconnected"), false)
                .ConfigureAwait(false);
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Error"));
            await CallAsync(fixture, condition, condition.Acknowledge!,
                [condition.EventId!.Value, new LocalizedText("acknowledged")]).ConfigureAwait(false);
            Assert.That(system.ControlState.CurrentState.Value.Text, Is.EqualTo("Error"));
            await CallAsync(fixture, system.ControlState, system.ControlState.ConfirmAll!,
                [new LocalizedText("operator verified connection")]).ConfigureAwait(false);
            Assert.That(system.ControlState.CurrentState.Value.Text, Is.EqualTo("Preoperational"));
            Assert.That(condition.ConfirmedState!.Id!.Value, Is.True);
            Assert.That(condition.Retain!.Value, Is.False);
        }

        [Test]
        public async Task ErrorEventsCarryActualStateTransitionMetadataAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            var events = new List<TransitionEventState>();
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is ErrorEventState or ErrorResolvedEventState)
                {
                    events.Add((TransitionEventState)notification);
                }
            };
            await system.ReportErrorAsync("camera", new LocalizedText("Disconnected"), true).ConfigureAwait(false);
            await system.ReportErrorAsync("camera", new LocalizedText("Reconnected"), false).ConfigureAwait(false);
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0], Is.InstanceOf<ErrorEventState>());
            Assert.That(events[0].FromState!.Value.Text, Is.EqualTo("Preoperational"));
            Assert.That(events[0].ToState!.Value.Text, Is.EqualTo("Error"));
            Assert.That(events[1], Is.InstanceOf<ErrorResolvedEventState>());
            Assert.That(events[1].FromState!.Value.Text, Is.EqualTo("Error"));
            Assert.That(events[1].ToState!.Value.Text, Is.EqualTo("Preoperational"));
            Assert.That(events[1].SourceNode!.Value, Is.EqualTo(system.ControlState.NodeId));
            Assert.That(events[1].Transition!.Id!.Value.TryGetValue(out NodeId transition), Is.True);
            Assert.That(transition, Is.EqualTo(system.ControlState.LastTransition!.Id!.Value));
        }

        [Test]
        public async Task ResetCannotReenableProductionWhileErrorIsUnresolvedAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            await ReportCameraErrorAsync(system).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.Reset!,
                [Variant.From(0), Variant.From("reset")])
                .ConfigureAwait(false);
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Preoperational"));
            ServiceResult status = await system.ControlState.SelectModeAutomatic!.CallAsync(
                SessionContext(fixture, new NodeId(1u)), system.ControlState.NodeId, [], [], []).ConfigureAwait(false);
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(system.ControlState.CurrentState.Value.Text, Is.EqualTo("Preoperational"));
        }

        [Test]
        public async Task InformationAndDiagnosticsUseTheirSpecifiedSeverityBandsAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            var severities = new List<ushort>();
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is VisionEventState information)
                {
                    severities.Add(information.Severity!.Value);
                }
            };
            await system.ReportInformationAsync(new LocalizedText("information")).ConfigureAwait(false);
            await system.ReportInformationAsync(new LocalizedText("diagnostic"), 42).ConfigureAwait(false);
            Assert.That(severities, Is.EqualTo(new ushort[] { 300, 42 }));
        }

        [Test]
        public async Task MissingControlArgumentsPublishFailureDiagnosticAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            var warnings = new List<string>();
            NodeId source = NodeId.Null;
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is VisionWarningConditionState warning)
                {
                    warnings.Add(warning.ErrorString!.Value);
                    source = warning.SourceNode!.Value;
                }
            };
            ServiceResult result = await system.ControlState.Reset!.CallAsync(
                SessionContext(fixture, new NodeId(1u)), system.ControlState.NodeId, [],
                [], []).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("Reset"));
            Assert.That(source, Is.EqualTo(system.ControlState.Reset.NodeId));
        }

        [Test]
        public async Task WarningConditionsRemainNonBlockingThroughAcknowledgementAndConfirmationAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object,
                    new MachineVisionSystemOptions { InitiallyReady = true }).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            VisionWarningConditionState? warning = null;
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is VisionWarningConditionState condition && condition.ConditionName!.Value == "lens")
                {
                    warning = condition;
                }
            };
            await system.ReportWarningAsync("lens", new LocalizedText("Lens maintenance due"), true, true, 123)
                .ConfigureAwait(false);
            Assert.That(warning, Is.Not.Null);
            Assert.That(warning!.Severity!.Value, Is.EqualTo(500));
            Assert.That(warning.ErrorCode!.Value, Is.EqualTo(123));
            Assert.That(warning.StopReaction!.Value, Is.False);
            Assert.That(warning.BlockReaction!.Value, Is.False);
            Assert.That(system.AutomaticState.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            await system.ReportWarningAsync("lens", new LocalizedText("Maintenance complete"), false)
                .ConfigureAwait(false);
            Assert.That(warning.Retain!.Value, Is.True);
            await CallAsync(fixture, warning, warning.Acknowledge!,
                [warning.EventId!.Value, new LocalizedText("received")]).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.ConfirmAll!,
                [new LocalizedText("verified")])
                .ConfigureAwait(false);
            Assert.That(warning.ConfirmedState!.Id!.Value, Is.True);
            Assert.That(warning.Retain.Value, Is.False);
            Assert.That(system.AutomaticState.CurrentState.Value.Text, Is.EqualTo("Ready"));
        }

        private static async ValueTask<VisionConditionState> ReportCameraErrorAsync(
            MachineVisionSystemHandle system, bool persistent = false)
        {
            VisionConditionState? reported = null;
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is VisionConditionState condition && condition.ConditionName!.Value == "camera")
                {
                    reported = condition;
                }
            };
            await system.ReportErrorAsync("camera", new LocalizedText("Camera disconnected"), true, persistent, 42)
                .ConfigureAwait(false);
            Assert.That(reported, Is.Not.Null);
            return reported!;
        }

        [Test]
        public async Task InitializedStepSequenceDelaysReadyUntilSyncAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object,
                    new MachineVisionSystemOptions { InitiallyReady = true }).ConfigureAwait(false);
            await system.SetStepSequenceAsync(5, true).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState!.Value.Text, Is.EqualTo("Initialized"));
            VisionStepModelStateMachineState step = system.AutomaticState.InitializedStepModel!;
            Assert.That(step.CurrentState!.Value.Text, Is.EqualTo("Wait"));
            await CallAsync(fixture, step, step.Sync!, [Variant.From(0), Variant.From("next")]).ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState.Value.Text, Is.EqualTo("Ready"));
        }

        [Test]
        public async Task ResetClearsProviderReadinessAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object,
                    new MachineVisionSystemOptions { InitiallyReady = true }).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.Reset!,
                [Variant.From(0), Variant.From("reset")])
                .ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState!.Value.Text, Is.EqualTo("Initialized"));
        }

        [Test]
        public async Task DisabledDiagnosticsCannotMaterializeSystemStateAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object,
                    new MachineVisionSystemOptions { Features = MachineVisionFeatures.None }).ConfigureAwait(false);
            Assert.That(system.State.SystemState, Is.Null);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await system.SetSystemStateAsync(new SystemStateDescriptionDataType()).ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(system.State.SystemState, Is.Null);
        }

        [Test]
        public async Task ResultFilterHonorsExternalRecipeVersionAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            foreach (string version in new[] { "one", "two" })
            {
                await system.PublishResultAsync(new ResultDataType
                {
                    ResultId = new ResultIdDataType { Id = version },
                    ResultState = 123,
                    ExternalRecipeId = new RecipeIdExternalDataType
                    {
                        Id = "recipe",
                        Version = version,
                        EncodingMask = (uint)BinaryIdBaseDataTypeFields.Version
                    },
                    EncodingMask = (uint)ResultDataTypeFields.ExternalRecipeId
                }).ConfigureAwait(false);
            }
            ResultManagementState results = system.State.ResultManagement!;
            ArrayOf<Variant> output = await CallAsync(fixture, results, results.GetResultListFiltered!,
            [
                Variant.From(123),
                Variant.FromStructure(new MeasIdDataType()),
                Variant.FromStructure(new PartIdDataType()),
                Variant.FromStructure(new RecipeIdExternalDataType
                {
                    Id = "recipe", Version = "two", EncodingMask = (uint)BinaryIdBaseDataTypeFields.Version
                }),
                Variant.FromStructure(new RecipeIdInternalDataType()),
                Variant.FromStructure(new ConfigurationIdDataType()),
                Variant.FromStructure(new ConfigurationIdDataType()),
                Variant.FromStructure(new ProductIdDataType()),
                Variant.FromStructure(new JobIdDataType()),
                Variant.From(10u),
                Variant.From(0u),
                Variant.From(1000)
            ]).ConfigureAwait(false);
            Assert.That(output[3].TryGetStructure(out ArrayOf<ResultDataType> found), Is.True);
            Assert.That(found.Count, Is.EqualTo(1));
            Assert.That(found[0].ResultId.Id, Is.EqualTo("two"));
        }

        [Test]
        public async Task AssetsCanShareAComponentWithoutReparentingOrReplacingItAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            MachineVisionAssetBuilder first = context.AddMachineVisionAsset("First")
                .WithIdentification(Identification("first", "urn:vision:first"));
            MachineVisionNodeBuilder component = first.AddItem(MachineVisionItemKind.ImageSensor, "SharedCamera")
                .WithIdentification(Identification("camera"));
            await first.BuildAsync().ConfigureAwait(false);
            var camera = (BaseObjectState)component.State;
            NodeState? originalParent = camera.Parent;
            NodeId originalType = camera.TypeDefinitionId;
            NodeId originalReference = camera.ReferenceTypeId;
            MachineVisionAssetBuilder second = context.AddMachineVisionAsset("Second")
                .WithIdentification(Identification("second", "urn:vision:second"));
            second.AddItem(MachineVisionItemKind.ImageSensor, camera);
            MachineVisionAssetHandle asset = await second.BuildAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(camera.Parent, Is.SameAs(originalParent));
                Assert.That(camera.TypeDefinitionId, Is.EqualTo(originalType));
                Assert.That(camera.ReferenceTypeId, Is.EqualTo(originalReference));
                Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(camera.NodeId), Is.SameAs(camera));
                Assert.That(asset.State.ImageSensors!.ReferenceExists(
                    ReferenceTypeIds.Organizes, false, camera.NodeId), Is.True);
            });
        }

        [Test]
        public async Task TwoStagedAssetsCannotPublishTheSameRootNameAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            IVisionBuildContext context = fixture.CreateBuildContext();
            MachineVisionAssetBuilder first = context.AddMachineVisionAsset("Duplicate")
                .WithIdentification(Identification("first", "urn:vision:first"));
            MachineVisionAssetBuilder second = context.AddMachineVisionAsset("Duplicate")
                .WithIdentification(Identification("second", "urn:vision:second"));
            MachineVisionAssetHandle published = await first.BuildAsync().ConfigureAwait(false);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await second.BuildAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
            Assert.That(context.Root.FindChild(context.Context, published.State.BrowseName),
                Is.SameAs(published.State));
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(published.State.NodeId),
                Is.SameAs(published.State));
        }

        [Test]
        public async Task ApplicationSynchronizationUsesAutomaticStepTransitionAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            Mock<IMachineVisionOperationsProvider> provider = Provider();
            provider.Setup(instance => instance.ExecuteStepAsync(
                It.IsAny<MachineVisionStepRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionStepResult>(
                    new MachineVisionStepResult(MachineVisionOperationResult.Good, true)));
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", provider.Object).ConfigureAwait(false);
            await system.SetStepSequenceAsync(1, true).ConfigureAwait(false);
            var transitions = new List<NodeId>();
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is StateChangedEventState transition)
                {
                    Assert.That(transition.Transition!.Id!.Value.TryGetValue(out NodeId transitionId), Is.True);
                    transitions.Add(transitionId);
                }
            };
            MachineVisionOperationResult result = await system.SynchronizeStepAsync(1, 42, "hardware trigger")
                .ConfigureAwait(false);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(transitions, Does.Contain(NodeId.Create(
                VisionStepModelStateMachineTypeIds.TransitionIds.WaitToStepAuto,
                MachineVisionModels.ControlNamespaceUri, fixture.Manager.SystemContext.NamespaceUris)));
            Assert.That(system.ControlState.PreoperationalStepModel!.CurrentState!.Value.Text, Is.EqualTo("Wait"));
            provider.Verify(instance => instance.ExecuteStepAsync(
                It.Is<MachineVisionStepRequest>(request => request.ParentState == 1 &&
                    request.StepIndex == 0 &&
                    request.Cause == 42 &&
                    request.CauseDescription == "hardware trigger"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StopAndAbortCancelSingleAndContinuousJobsAsync(bool continuous, bool abort)
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            Mock<IMachineVisionOperationsProvider> provider = Provider();
            var started =
                new TaskCompletionSource<MachineVisionJob>(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            provider.Setup(instance => instance.ExecuteAsync(
                It.IsAny<MachineVisionJob>(), It.IsAny<CancellationToken>()))
                .Returns((MachineVisionJob job, CancellationToken token) =>
                    WaitForCancellationAsync(job, started, canceled, token));
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", provider.Object, new MachineVisionSystemOptions
                {
                    Features = MachineVisionFeatures.Simulation,
                    InitiallyReady = true
                }).ConfigureAwait(false);
            var readyEvents = new List<ReadyEventState>();
            system.State.EventReported += (_, _, notification) =>
            {
                if (notification is ReadyEventState ready)
                {
                    readyEvents.Add(ready);
                }
            };
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            Assert.That(readyEvents, Is.Empty, "Initialization is not a completed job.");
            await CallAsync(fixture, system.AutomaticState, system.AutomaticState.SimulationMode!,
                [Variant.From(true), Variant.From(0), Variant.From("test")]).ConfigureAwait(false);
            MethodState start = continuous
                ? system.AutomaticState.StartContinuous! : system.AutomaticState.StartSingleJob!;
            await CallAsync(fixture, system.AutomaticState, start,
            [
                Variant.FromStructure(new MeasIdDataType { Id = "measurement" }),
                Variant.FromStructure(new PartIdDataType { Id = "part" }),
                Variant.FromStructure(new RecipeIdExternalDataType()),
                Variant.FromStructure(new ProductIdDataType()),
                Variant.From(ArrayOf<Variant>.Empty)
            ]).ConfigureAwait(false);
            MachineVisionJob job = await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(job.Continuous, Is.EqualTo(continuous));
            Assert.That(job.Simulated, Is.True);
            Assert.That(system.AutomaticState.CurrentState!.Value.Text,
                Is.EqualTo(continuous ? "ContinuousExecution" : "SingleExecution"));
            MethodState stop = abort ? system.AutomaticState.Abort! : system.AutomaticState.Stop!;
            await CallAsync(fixture, system.AutomaticState, stop, [Variant.From(42), Variant.From("operator")])
                .ConfigureAwait(false);
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState.Value.Text, Is.EqualTo("Ready"));
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Operational"));
            Assert.That(readyEvents, Has.Count.EqualTo(1));
            Assert.That(readyEvents[0].JobId!.Value.Id, Is.EqualTo(job.JobId.Id));
            Assert.That(readyEvents[0].SourceNode!.Value, Is.EqualTo(system.AutomaticState.NodeId));
            MachineVisionControlAction action = abort
                ? MachineVisionControlAction.Abort : MachineVisionControlAction.Stop;
            provider.Verify(instance => instance.ControlAsync(action, 42, "operator", It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task FailedHaltPreservesDomainErrorAndEntersErrorStateAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            Mock<IMachineVisionOperationsProvider> provider = Provider();
            provider.Setup(instance => instance.ControlAsync(
                MachineVisionControlAction.Halt, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(
                    new MachineVisionOperationResult(ServiceResult.Good, -7)));
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", provider.Object).ConfigureAwait(false);
            ArrayOf<Variant> result = await CallAsync(fixture, system.ControlState, system.ControlState.Halt!,
                [Variant.From(0), Variant.From("shutdown")]).ConfigureAwait(false);
            Assert.That(result[0].TryGetValue(out int error), Is.True);
            Assert.That(error, Is.EqualTo(-7));
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Error"));
            Assert.That(system.ControlState.Halt!.Executable, Is.True);
            Assert.That(system.ControlState.Reset!.Executable, Is.True);
        }

        [Test]
        public async Task ThrowingControlAndStepProvidersEnterErrorRatherThanLeaveStuckWorkAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            Mock<IMachineVisionOperationsProvider> provider = Provider();
            provider.Setup(instance => instance.ControlAsync(
                MachineVisionControlAction.Halt, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Hardware shutdown failed."));
            provider.Setup(instance => instance.ExecuteStepAsync(
                It.IsAny<MachineVisionStepRequest>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Hardware synchronization failed."));
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", provider.Object).ConfigureAwait(false);
            await system.SetStepSequenceAsync(1, true).ConfigureAwait(false);
            VisionStepModelStateMachineState step = system.ControlState.PreoperationalStepModel!;
            ServiceResult status = await step.Sync!.CallAsync(
                SessionContext(fixture, new NodeId(1u)), step.NodeId, [Variant.From(0), Variant.From("next")], [], [])
                .ConfigureAwait(false);
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(system.ControlState.CurrentState!.Value.Text, Is.EqualTo("Error"));
            Assert.That(step.CurrentState!.Id!.Value.IsNull, Is.True);
            await CallAsync(fixture, system.ControlState, system.ControlState.Reset!,
                [Variant.From(0), Variant.From("reset")]).ConfigureAwait(false);
            status = await system.ControlState.Halt!.CallAsync(
                SessionContext(fixture, new NodeId(1u)), system.ControlState.NodeId,
                [Variant.From(0), Variant.From("halt")], [], []).ConfigureAwait(false);
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            Assert.That(system.ControlState.CurrentState.Value.Text, Is.EqualTo("Error"));
            Assert.That(system.ControlState.Halt.Executable, Is.True);
        }

        [Test]
        public async Task AssetHealthSupportsCoreAnalogMetadataAndRecursiveStatesAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            MachineVisionAssetBuilder builder = fixture.CreateBuildContext().AddMachineVisionAsset("Asset")
                .WithIdentification(Identification("asset", "urn:vision:asset"));
            builder.Nodes.WithHealth(health =>
            {
                health.Set("Temperature", Variant.From(25.0)).Child("Temperature")
                    .Set("EURange", Variant.FromStructure(new Range { Low = -20, High = 80 }));
                health.Child("State").Set("SubStates",
                    Variant.FromStructure<MachineVision.AMCM.SEMI_E10SystemStateDataType>(
                        [new() { Id = 2, Priority = 10 }]));
            });
            MachineVisionAssetHandle asset = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(asset.State.Health!.Temperature!.EURange!.Value.Low, Is.EqualTo(-20));
            Assert.That(asset.State.Health.Temperature.EURange.Value.High, Is.EqualTo(80));
            Assert.That(asset.State.Health.State!.SubStates!.WrappedValue.TryGetStructure(
                    out ArrayOf<MachineVision.AMCM.SEMI_E10SystemStateDataType> states), Is.True);
            Assert.That(states.Count, Is.EqualTo(1));
            Assert.That(states[0].Id, Is.EqualTo(2));
        }

        [Test]
        public async Task ReleasedConformanceTracksConfiguredUnitsWithoutInventedProfilesAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            Assert.That(fixture.Manager.ConformanceUnits.IsEmpty, Is.True);
            IVisionBuildContext context = fixture.CreateBuildContext();
            MachineVisionSystemHandle control = await context.AddMachineVisionSystemAsync(
                "Minimal", Provider().Object, new MachineVisionSystemOptions { Features = MachineVisionFeatures.None })
                .ConfigureAwait(false);
            ushort ns = (ushort)context.Context.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri);
            Assert.That(control.ConformanceUnits.ToArray(), Does.Contain(new QualifiedName("Automatic Mode", ns)));
            Assert.That(control.ConformanceUnits.ToArray(), Does.Not.Contain(new QualifiedName("Recipe File", ns)));
            Assert.That(control.ServerProfiles.IsEmpty, Is.True);
            MachineVisionAssetBuilder builder = context.AddMachineVisionAsset("Conformance")
                .WithIdentification(Identification("conformance", "urn:vision:conformance"));
            builder.Nodes.WithMaintenance(maintenance => maintenance.Set("CalibrationNeeded", Variant.From(true)));
            MachineVisionNodeBuilder camera = builder.AddItem(MachineVisionItemKind.ImageSensor, "Camera")
                .WithIdentification(Identification("camera"))
                .WithHealth(health => health.Set("Temperature", Variant.From(25.0)));
            MachineVisionNodeBuilder lens = builder.AddItem(MachineVisionItemKind.Lens, "Lens")
                .WithIdentification(Identification("lens"));
            builder.HasOpticalPathTo(lens.State, camera.State);
            MachineVisionAssetHandle asset = await builder.BuildAsync().ConfigureAwait(false);
            ns = (ushort)context.Context.NamespaceUris.GetIndex(MachineVisionModels.AssetNamespaceUri);
            Assert.That(asset.ConformanceUnits.ToArray(), Is.EquivalentTo(new QualifiedName[]
            {
                new("VSA Basic", ns), new("VSA Identification", ns), new("VSA Maintenance Information", ns),
                new("VSA Find ImageSensors", ns), new("VSA ImageSensor Identification", ns),
                new("VSA ImageSensor Health Information", ns),
                new("VSA Find Lenses", ns), new("VSA Lens Identification", ns), new("VSA Optical Paths", ns)
            }));
            Assert.That(asset.ServerProfiles.IsEmpty, Is.True);
            Assert.That(fixture.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(asset.ConformanceUnits.ToArray()));
            Assert.That(fixture.Manager.ConformanceUnits.ToArray(), Is.SupersetOf(control.ConformanceUnits.ToArray()));
            await control.DisposeAsync().ConfigureAwait(false);
            Assert.That(control.ConformanceUnits.IsEmpty, Is.True);
            Assert.That(fixture.Manager.ConformanceUnits.ToArray(), Is.EquivalentTo(asset.ConformanceUnits.ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidAssetUpdateDoesNotPublishPartialValuesOrChildrenAsync(bool existingHealth)
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Assets).ConfigureAwait(false);
            MachineVisionAssetBuilder builder = fixture.CreateBuildContext().AddMachineVisionAsset("Asset")
                .WithIdentification(Identification("asset", "urn:vision:asset"));
            if (existingHealth)
            {
                builder.Nodes.WithHealth(health => health.Set("Temperature", Variant.From(25.0)));
            }
            MachineVisionAssetHandle asset = await builder.BuildAsync().ConfigureAwait(false);
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UpdateAsync(nodes => nodes.WithHealth(health =>
                {
                    health.Set("Temperature", Variant.From(26.0))
                        .Set("Temperature", Variant.From("invalid"));
                })).ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
            if (existingHealth)
            {
                Assert.That(asset.State.Health!.Temperature!.Value, Is.EqualTo(25.0));
            }
            else
            {
                Assert.That(asset.State.Health, Is.Null);
            }
        }

        [Test]
        public async Task AssetUpdatesRejectIdentityChangesEmptyCategoriesAndDisposedHandlesAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Assets).ConfigureAwait(false);
            MachineVisionAssetHandle asset = await fixture.CreateBuildContext().AddMachineVisionAsset("Asset")
                .WithIdentification(Identification("asset", "urn:vision:asset")).BuildAsync().ConfigureAwait(false);
            NodeId original = asset.State.NodeId;
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UpdateAsync(nodes =>
                    nodes.State.NodeId = new NodeId("replacement", original.NamespaceIndex))
                    .ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(asset.State.NodeId, Is.EqualTo(original));
            failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UpdateAsync(nodes => nodes.Child("Lenses")).ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(asset.State.Lenses, Is.Null);
            await asset.DisposeAsync().ConfigureAwait(false);
            failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UpdateAsync(nodes =>
                    nodes.WithHealth(health => health.Set("Temperature", Variant.From(25.0))))
                    .ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(asset.State.Health, Is.Null);
            Assert.That(asset.ConformanceUnits.IsEmpty, Is.True);
        }

        private static async IAsyncEnumerable<MachineVisionAcquisition> WaitForCancellationAsync(
            MachineVisionJob job,
            TaskCompletionSource<MachineVisionJob> started,
            TaskCompletionSource<bool> canceled,
            [EnumeratorCancellation] CancellationToken ct)
        {
            started.TrySetResult(job);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            finally
            {
                canceled.TrySetResult(true);
            }
            yield break;
        }

        private static MachineryIdentificationData Identification(string serial, string? uri = null)
        {
            return new MachineryIdentificationData
            {
                Manufacturer = new LocalizedText("OPC Foundation"),
                SerialNumber = serial,
                ProductInstanceUri = uri
            };
        }

        private static Mock<IMachineVisionOperationsProvider> Provider()
        {
            var provider = new Mock<IMachineVisionOperationsProvider>(MockBehavior.Strict);
            provider.Setup(instance => instance.ControlAsync(
                It.IsAny<MachineVisionControlAction>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(instance => instance.PrepareRecipeAsync(
                It.IsAny<MachineVisionDocument>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(instance => instance.ActivateConfigurationAsync(
                It.IsAny<MachineVisionDocument>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(instance => instance.ValidateDocumentAsync(
                It.IsAny<MachineVisionDocument>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(instance => instance.StartJobAsync(
                It.IsAny<MachineVisionJob>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(MachineVisionOperationResult.Good));
            provider.Setup(instance => instance.ExecuteStepAsync(
                It.IsAny<MachineVisionStepRequest>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionStepResult>(
                    new MachineVisionStepResult(MachineVisionOperationResult.Good, false)));
            provider.Setup(instance => instance.ExecuteAsync(
                It.IsAny<MachineVisionJob>(), It.IsAny<CancellationToken>()))
                .Returns((MachineVisionJob _, CancellationToken ct) => ExecuteAsync(ct));
            return provider;
        }

        private static async IAsyncEnumerable<MachineVisionAcquisition> ExecuteAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield return new MachineVisionAcquisition(true, new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "result" },
                ResultState = 1,
                ResultContent = [Variant.From(42)],
                EncodingMask = (uint)ResultDataTypeFields.ResultContent
            });
        }

        private static async ValueTask<ArrayOf<Variant>> CallAsync(
            VisionServerFixture fixture, NodeState parent, MethodState method, ArrayOf<Variant> arguments)
        {
            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await method.CallAsync(
                SessionContext(fixture, new NodeId(1u)), parent.NodeId, arguments, errors, output)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
            foreach (ServiceResult error in errors)
            {
                Assert.That(ServiceResult.IsGood(error), Is.True, error.ToString());
            }
            return output.ToArrayOf();
        }
    }
}
