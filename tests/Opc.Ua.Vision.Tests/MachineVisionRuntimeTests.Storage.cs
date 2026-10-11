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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    public sealed partial class MachineVisionRuntimeTests
    {
        [Test]
        public async Task RecipeTransferCommitsContentThenPreparationEnablesReadyAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            await CallAsync(fixture, system.ControlState, system.ControlState.SelectModeAutomatic!, [])
                .ConfigureAwait(false);
            RecipeManagementState recipes = system.State.RecipeManagement!;
            ArrayOf<Variant> added = await CallAsync(fixture, recipes, recipes.AddRecipe!,
                [
                    Variant.FromStructure(new RecipeIdExternalDataType { Id = "recipe" }),
                    Variant.FromStructure(new ProductIdDataType { Id = "product" })
                ]).ConfigureAwait(false);
            Assert.That(added[0].TryGetStructure<RecipeIdInternalDataType>(out RecipeIdInternalDataType? id), Is.True);
            ArrayOf<Variant> transfer = await CallAsync(fixture, recipes.RecipeTransfer!,
                recipes.RecipeTransfer!.GenerateFileForWrite!,
                [Variant.FromStructure(new RecipeTransferOptions { InternalId = id! })]).ConfigureAwait(false);
            Assert.That(transfer[0].TryGetValue(out NodeId fileId), Is.True);
            Assert.That(fileId.NamespaceIndex, Is.EqualTo(system.State.NodeId.NamespaceIndex));
            Assert.That(transfer[1].TryGetValue(out uint fileHandle), Is.True);
            FileState file = fixture.Manager.FindPredefinedNode<FileState>(fileId)!;
            await CallAsync(fixture, file, file.Write!,
                [Variant.From(fileHandle), Variant.From(ByteString.From(new byte[] { 1, 2, 3 }))])
                .ConfigureAwait(false);
            await CallAsync(fixture, recipes.RecipeTransfer, recipes.RecipeTransfer.CloseAndCommit!,
                [Variant.From(fileHandle)]).ConfigureAwait(false);
            MachineVisionDocument? document = await system.Store.ReadAsync(MachineVisionDocumentKind.Recipe, id!.Id!)
                .ConfigureAwait(false);
            Assert.That(document!.Content.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(fileId), Is.Null);
            await CallAsync(fixture, recipes, recipes.PrepareRecipe!,
                [
                    Variant.FromStructure(new RecipeIdExternalDataType()),
                    Variant.FromStructure(id)
                ]).ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState!.Value.Text, Is.EqualTo("Ready"));
            await CallAsync(fixture, recipes, recipes.UnprepareRecipe!,
                [
                    Variant.FromStructure(new RecipeIdExternalDataType()),
                    Variant.FromStructure(id)
                ]).ConfigureAwait(false);
            Assert.That(system.AutomaticState.CurrentState.Value.Text, Is.EqualTo("Initialized"));
        }

        [Test]
        public async Task ClosingAnUncommittedRecipeTransferDiscardsItsBytesAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            RecipeManagementState recipes = system.State.RecipeManagement!;
            ArrayOf<Variant> added = await CallAsync(fixture, recipes, recipes.AddRecipe!,
                [
                    Variant.FromStructure(new RecipeIdExternalDataType { Id = "recipe" }),
                    Variant.FromStructure(new ProductIdDataType())
                ]).ConfigureAwait(false);
            added[0].TryGetStructure<RecipeIdInternalDataType>(out RecipeIdInternalDataType? id);
            ArrayOf<Variant> transfer = await CallAsync(fixture, recipes.RecipeTransfer!,
                recipes.RecipeTransfer!.GenerateFileForWrite!,
                [Variant.FromStructure(new RecipeTransferOptions { InternalId = id! })]).ConfigureAwait(false);
            transfer[0].TryGetValue(out NodeId fileId);
            transfer[1].TryGetValue(out uint fileHandle);
            FileState file = fixture.Manager.FindPredefinedNode<FileState>(fileId)!;
            await CallAsync(fixture, file, file.Write!,
                [Variant.From(fileHandle), Variant.From(ByteString.From(new byte[] { 1, 2 }))]).ConfigureAwait(false);
            await CallAsync(fixture, file, file.Close!, [Variant.From(fileHandle)]).ConfigureAwait(false);
            MachineVisionDocument? document = await system.Store.ReadAsync(MachineVisionDocumentKind.Recipe, id!.Id!)
                .ConfigureAwait(false);
            Assert.That(document!.Content.IsNull, Is.True);
            Assert.That(fixture.Manager.FindPredefinedNode<NodeState>(fileId), Is.Null);
        }

        [Test]
        public async Task ResultVariablesAndEventsPreserveOptionalMetadataPresenceAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            MachineVisionSystemHandle system = await fixture.CreateBuildContext()
                .AddMachineVisionSystemAsync("Inspection", Provider().Object).ConfigureAwait(false);
            ResultReadyEventState? notification = null;
            system.State.EventReported += (_, _, value) =>
            {
                if (value is ResultReadyEventState ready)
                {
                    notification = ready;
                }
            };
            await system.PublishResultAsync(new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "transferable" },
                ResultState = 1,
                ResultContent = [Variant.From(42)],
                EncodingMask = (uint)ResultDataTypeFields.ResultContent
            }, ByteString.From(new byte[] { 1, 2, 3 })).ConfigureAwait(false);
            Assert.That(notification, Is.Not.Null);
            Assert.That(notification!.ProcessingTimes, Is.Null, "Missing processing times are not fabricated.");
            Assert.That(notification.ResultContent!.Value[0].TryGetValue(out int content), Is.True);
            Assert.That(content, Is.EqualTo(42));
            var children = new List<BaseInstanceState>();
            system.State.ResultManagement!.Results!.GetChildren(fixture.Manager.SystemContext, children);
            Assert.That(children, Has.Count.EqualTo(1));
            var result = (ResultVariableState)children[0];
            Assert.That(result.HasTransferableDataOnFile, Is.Not.Null);
            Assert.That(result.HasTransferableDataOnFile!.Value, Is.True);
            await system.PublishResultAsync(new ResultDataType
            {
                ResultId = new ResultIdDataType { Id = "transferable" },
                ResultState = 2
            }).ConfigureAwait(false);
            Assert.That(result.HasTransferableDataOnFile.Value, Is.False);
            Assert.That(result.ResultContent!.StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            Assert.That(result.ResultContent.WrappedValue.IsNull, Is.True);
        }

        [Test]
        public async Task ConfigurationPersistenceFailureDoesNotApplyHardwareStateAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            var backing = new InMemoryMachineVisionStore();
            await backing.WriteAsync(new MachineVisionDocument(MachineVisionDocumentKind.Configuration, "configuration",
                Variant.FromStructure(new ConfigurationDataType
                {
                    InternalId = new ConfigurationIdDataType { Id = "configuration" }
                }), ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);
            var store = new Mock<IMachineVisionStore>(MockBehavior.Strict);
            store.Setup(instance => instance.ReadAsync(It.IsAny<MachineVisionDocumentKind>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((MachineVisionDocumentKind kind, string id, CancellationToken ct) =>
                    backing.ReadAsync(kind, id, ct));
            store.Setup(instance => instance.ListAsync(
                It.IsAny<MachineVisionDocumentKind>(), It.IsAny<CancellationToken>()))
                .Returns((MachineVisionDocumentKind kind, CancellationToken ct) => backing.ListAsync(kind, ct));
            store.Setup(instance => instance.WriteAsync(
                It.IsAny<MachineVisionDocument>(), It.IsAny<CancellationToken>()))
                .Throws(new IOException("Persistent state is unavailable."));
            Mock<IMachineVisionOperationsProvider> provider = Provider();
            MachineVisionSystemHandle system = await fixture.CreateBuildContext().AddMachineVisionSystemAsync(
                "Inspection", provider.Object,
                new MachineVisionSystemOptions { Features = MachineVisionFeatures.Configurations }, store.Object)
                .ConfigureAwait(false);
            ConfigurationManagementState management = system.State.ConfigurationManagement!;
            ServiceResult result = await management.ActivateConfiguration!.CallAsync(
                SessionContext(fixture, new NodeId(1u)), management.NodeId,
                [Variant.FromStructure(new ConfigurationIdDataType { Id = "configuration" })], [], [])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInternalError));
            provider.Verify(instance => instance.ActivateConfigurationAsync(
                It.IsAny<MachineVisionDocument>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await backing.ReadAsync(MachineVisionDocumentKind.Runtime, "ActiveConfiguration")
                .ConfigureAwait(false), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RefusedConfigurationRestoresPreviousDurableIntentAsync(bool previouslyActive)
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.Control).ConfigureAwait(false);
            var store = new InMemoryMachineVisionStore();
            foreach (string id in new[] { "previous", "refused" })
            {
                await store.WriteAsync(new MachineVisionDocument(MachineVisionDocumentKind.Configuration, id,
                    Variant.FromStructure(new ConfigurationDataType
                    {
                        InternalId = new ConfigurationIdDataType { Id = id }
                    }), ByteString.From(new byte[] { 1 }))).ConfigureAwait(false);
            }
            Mock<IMachineVisionOperationsProvider> provider = Provider();
            provider.Setup(instance => instance.ActivateConfigurationAsync(
                It.Is<MachineVisionDocument>(document => document.Id == "refused"), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MachineVisionOperationResult>(
                    new MachineVisionOperationResult(ServiceResult.Good, -9)));
            MachineVisionSystemHandle system = await fixture.CreateBuildContext().AddMachineVisionSystemAsync(
                "Inspection", provider.Object,
                new MachineVisionSystemOptions { Features = MachineVisionFeatures.Configurations }, store)
                .ConfigureAwait(false);
            ConfigurationManagementState management = system.State.ConfigurationManagement!;
            if (previouslyActive)
            {
                await CallAsync(fixture, management, management.ActivateConfiguration!,
                    [Variant.FromStructure(new ConfigurationIdDataType { Id = "previous" })]).ConfigureAwait(false);
            }
            ArrayOf<Variant> output = await CallAsync(fixture, management, management.ActivateConfiguration!,
                [Variant.FromStructure(new ConfigurationIdDataType { Id = "refused" })]).ConfigureAwait(false);
            Assert.That(output[0].TryGetValue(out int error), Is.True);
            Assert.That(error, Is.EqualTo(-9));
            MachineVisionDocument? intent = await store.ReadAsync(
                MachineVisionDocumentKind.Runtime, "ActiveConfiguration")
                .ConfigureAwait(false);
            if (previouslyActive)
            {
                Assert.That(intent, Is.Not.Null);
                Assert.That(intent!.Metadata.TryGetValue(out string activeId), Is.True);
                Assert.That(activeId, Is.EqualTo("previous"));
                Assert.That(management.ActiveConfiguration!.Value.InternalId.Id, Is.EqualTo("previous"));
            }
            else
            {
                Assert.That(intent, Is.Null);
            }
        }

        [Test]
        public async Task SharedFileBindingIsolatesReadersAndCommitsOneWriterAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var file = new FileState(null);
            file.Create(fixture.Manager.SystemContext, new NodeId("file", fixture.Manager.NamespaceIndex),
                new QualifiedName("File", fixture.Manager.NamespaceIndex), new LocalizedText("File"), true);
            ByteString committed = default;
            await using IFileTypeBinding binding = new FileTypeBinder().Bind(
                file, fixture.Manager.SystemContext, ByteString.From(new byte[] { 10, 20, 30 }),
                (content, _) =>
                {
                    committed = content;
                    return default;
                },
                commitOnClose: true);
            var owner = new NodeId(1u);
            var other = new NodeId(2u);
            uint first = await binding.OpenAsync(owner, 1).ConfigureAwait(false);
            uint second = await binding.OpenAsync(other, 1).ConfigureAwait(false);
            SessionSystemContext ownerContext = SessionContext(fixture, owner);
            SessionSystemContext otherContext = SessionContext(fixture, other);
            ArrayOf<Variant> a = await InvokeAsync(ownerContext, file, file.Read!,
                [Variant.From(first), Variant.From(1)]).ConfigureAwait(false);
            ArrayOf<Variant> b = await InvokeAsync(otherContext, file, file.Read!,
                [Variant.From(second), Variant.From(1)]).ConfigureAwait(false);
            a[0].TryGetValue(out ByteString aBytes);
            b[0].TryGetValue(out ByteString bBytes);
            Assert.That(aBytes.Span.ToArray(), Is.EqualTo(new byte[] { 10 }));
            Assert.That(bBytes.Span.ToArray(), Is.EqualTo(new byte[] { 10 }));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await binding.CloseAsync(other, first).ConfigureAwait(false));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await binding.OpenAsync(owner, 6).ConfigureAwait(false));
            await binding.CloseAsync(owner, first).ConfigureAwait(false);
            await binding.CloseAsync(other, second).ConfigureAwait(false);
            uint writer = await binding.OpenAsync(owner, 6).ConfigureAwait(false);
            await InvokeAsync(ownerContext, file, file.Write!,
                [Variant.From(writer), Variant.From(ByteString.From(new byte[] { 42 }))]).ConfigureAwait(false);
            await binding.CloseAsync(owner, writer).ConfigureAwait(false);
            Assert.That(committed.Span.ToArray(), Is.EqualTo(new byte[] { 42 }));
            Assert.That(file.OpenCount!.Value, Is.Zero);
            Assert.That(file.Size!.Value, Is.EqualTo(1));
        }

        [Test]
        public async Task FileCommitCanRefreshItsPublishedContentWithoutDeadlockingAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var file = new FileState(null);
            file.Create(fixture.Manager.SystemContext, new NodeId("file", fixture.Manager.NamespaceIndex),
                new QualifiedName("File", fixture.Manager.NamespaceIndex), new LocalizedText("File"), true);
            IFileTypeBinding? current = null;
            await using IFileTypeBinding binding = new FileTypeBinder().Bind(
                file, fixture.Manager.SystemContext, ByteString.From(new byte[] { 1 }),
                async (content, ct) => await current!.UpdateContentAsync(content, ct).ConfigureAwait(false));
            current = binding;
            NodeId owner = new(1u);
            uint writer = await binding.OpenAsync(owner, 6).ConfigureAwait(false);
            await InvokeAsync(SessionContext(fixture, owner), file, file.Write!,
                [Variant.From(writer), Variant.From(ByteString.From(new byte[] { 4, 2 }))]).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await binding.CommitAsync(owner, writer, deadline.Token).ConfigureAwait(false);
            Assert.That(file.OpenCount!.Value, Is.Zero);
            Assert.That(file.Size!.Value, Is.EqualTo(2));
            uint reader = await binding.OpenAsync(owner, 1).ConfigureAwait(false);
            ArrayOf<Variant> read = await InvokeAsync(SessionContext(fixture, owner), file, file.Read!,
                [Variant.From(reader), Variant.From(2)]).ConfigureAwait(false);
            Assert.That(read[0].TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes.Span.ToArray(), Is.EqualTo(new byte[] { 4, 2 }));
        }

        [TestCase(-1)]
        [TestCase(0)]
        public async Task BoundFileRejectsNonPositiveReadLengthsAsync(int length)
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var file = new FileState(null);
            file.Create(fixture.Manager.SystemContext, new NodeId("file", fixture.Manager.NamespaceIndex),
                new QualifiedName("File", fixture.Manager.NamespaceIndex), new LocalizedText("File"), true);
            await using IFileTypeBinding binding = new FileTypeBinder().Bind(
                file, fixture.Manager.SystemContext, ByteString.From(new byte[] { 1, 2, 3 }));
            NodeId owner = new(1u);
            uint reader = await binding.OpenAsync(owner, 1).ConfigureAwait(false);
            ServiceResult result = await file.Read!.CallAsync(
                SessionContext(fixture, owner), file.NodeId, [Variant.From(reader), Variant.From(length)], [], [])
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task BoundFileClampsPositionsBeyondEndOfFileAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var file = new FileState(null);
            file.Create(fixture.Manager.SystemContext, new NodeId("file", fixture.Manager.NamespaceIndex),
                new QualifiedName("File", fixture.Manager.NamespaceIndex), new LocalizedText("File"), true);
            await using IFileTypeBinding binding = new FileTypeBinder().Bind(
                file, fixture.Manager.SystemContext, ByteString.From(new byte[] { 1, 2, 3 }));
            NodeId owner = new(1u);
            uint reader = await binding.OpenAsync(owner, 1).ConfigureAwait(false);
            SessionSystemContext context = SessionContext(fixture, owner);
            await InvokeAsync(context, file, file.SetPosition!, [Variant.From(reader), Variant.From(ulong.MaxValue)])
                .ConfigureAwait(false);
            ArrayOf<Variant> position = await InvokeAsync(context, file, file.GetPosition!, [Variant.From(reader)])
                .ConfigureAwait(false);
            Assert.That(position[0].TryGetValue(out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(3));
            ArrayOf<Variant> end = await InvokeAsync(context, file, file.Read!, [Variant.From(reader), Variant.From(1)])
                .ConfigureAwait(false);
            Assert.That(end[0].TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes.IsEmpty, Is.True);
        }

        [Test]
        public async Task FileSystemStoreSurvivesRestartAndRecoversAnInterruptedReplacementAsync()
        {
            await using var fixture = new VisionServerFixture();
            await fixture.StartAsync(MachineVisionParts.All).ConfigureAwait(false);
            string directory = Path.Combine(Path.GetTempPath(), "VisionStore-" + Guid.NewGuid().ToString("N"));
            var provider = new PhysicalFileSystemProvider(directory);
            try
            {
                await using (var first = new FileSystemMachineVisionStore(
                    provider, fixture.Manager.SystemContext.AsMessageContext()))
                {
                    await first.WriteAsync(new MachineVisionDocument(
                        MachineVisionDocumentKind.Result, "../logical-id",
                        Variant.FromStructure(new ResultIdDataType { Id = "persisted" }),
                        ByteString.From(new byte[] { 3, 2, 1 }))).ConfigureAwait(false);
                }
                string stored = Directory.GetFiles(directory, "*.bin", SearchOption.AllDirectories)[0];
                File.Move(stored, stored + ".previous");
                await using var second = new FileSystemMachineVisionStore(
                    provider, fixture.Manager.SystemContext.AsMessageContext());
                MachineVisionDocument? recovered = await second.ReadAsync(
                    MachineVisionDocumentKind.Result, "../logical-id")
                    .ConfigureAwait(false);
                Assert.That(recovered, Is.Not.Null);
                Assert.That(recovered!.Metadata.TryGetStructure<ResultIdDataType>(out ResultIdDataType? id), Is.True);
                Assert.That(id!.Id, Is.EqualTo("persisted"));
                Assert.That(recovered.Content.Span.ToArray(), Is.EqualTo(new byte[] { 3, 2, 1 }));
                Assert.That(File.Exists(stored), Is.True);
                Assert.That(File.Exists(stored + ".previous"), Is.False);
                Assert.That((await second.ListAsync(MachineVisionDocumentKind.Result).ConfigureAwait(false)).Count,
                    Is.EqualTo(1));
                Assert.That(await second.DeleteAsync(MachineVisionDocumentKind.Result, "../logical-id")
                    .ConfigureAwait(false), Is.True);
                Assert.That(await second.ReadAsync(MachineVisionDocumentKind.Result, "../logical-id")
                    .ConfigureAwait(false), Is.Null);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static SessionSystemContext SessionContext(VisionServerFixture fixture, NodeId session)
        {
            return new SessionSystemContext(fixture.Manager.SystemContext.Telemetry)
            {
                SessionId = session,
                NamespaceUris = fixture.Manager.SystemContext.NamespaceUris,
                EncodeableFactory = fixture.Manager.SystemContext.EncodeableFactory,
                TypeTable = fixture.Manager.SystemContext.TypeTable
            };
        }

        private static async ValueTask<ArrayOf<Variant>> InvokeAsync(
            ISystemContext context, NodeState parent, MethodState method, ArrayOf<Variant> arguments)
        {
            var errors = new List<ServiceResult>();
            var output = new List<Variant>();
            ServiceResult result = await method.CallAsync(context, parent.NodeId, arguments, errors, output)
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
