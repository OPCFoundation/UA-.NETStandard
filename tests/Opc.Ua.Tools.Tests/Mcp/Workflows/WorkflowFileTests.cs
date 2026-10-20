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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpCompanion.Workflows
{
    /// <summary>
    /// Tests explicit file targets, stream limits, protected paths and owned-handle terminal behavior.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class WorkflowFileTests
    {
        /// <summary>
        /// File creation never asks for an open raw handle, and directory creation is a separate typed method.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CreatesUseExactParentTypedMethodAndNoImplicitOpenAsync(bool directory)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, _) = fixture.FileTree();
            NodeId created = fixture.Remote.Id("created");
            fixture.Remote.Reply(StatusCodes.GoodCompletesAsynchronously, Variant.From(created), Variant.From(71u));
            CallToolResult result = directory
                ? await fixture.Files.CreateDirectoryAsync(parent.ToString(), "new", "selected").ConfigureAwait(false)
                : await fixture.Files.CreateFileAsync(parent.ToString(), "new", "selected").ConfigureAwait(false);
            fixture.Remote.AssertCall(parent,
                directory ? MethodIds.FileDirectoryType_CreateDirectory : MethodIds.FileDirectoryType_CreateFile,
                directory ? [Variant.From("new")] : [Variant.From("new"), Variant.From(false)]);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodCompletesAsynchronously.Code));
            Assert.That(json.GetProperty("created").GetProperty("nodeId").GetString(), Is.EqualTo(created.ToString()));
            Assert.That(json.TryGetProperty("outputArguments", out _), Is.False);
        }

        /// <summary>
        /// Copy and move encode their distinct boolean while preserving both explicit endpoints.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MoveAndCopyPreserveSourceDestinationNameAndActualStatusAsync(bool copy)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, NodeId source) = fixture.FileTree();
            NodeId target = fixture.Remote.Id("target");
            fixture.Remote.Type(target, ObjectTypeIds.FileDirectoryType);
            fixture.Remote.Reply(StatusCodes.GoodClamped, Variant.From(fixture.Remote.Id("copy")));
            CallToolResult result = copy
                ? await fixture.Files.CopyAsync(parent.ToString(), source.ToString(), target.ToString(),
                    "copied.bin", sessionName: "selected").ConfigureAwait(false)
                : await fixture.Files.MoveAsync(parent.ToString(), source.ToString(), target.ToString(),
                    "copied.bin", sessionName: "selected").ConfigureAwait(false);
            fixture.Remote.AssertCall(parent, MethodIds.FileDirectoryType_MoveOrCopy,
                Variant.From(source), Variant.From(target), Variant.From(copy), Variant.From("copied.bin"));
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodClamped.Code));
        }

        /// <summary>
        /// Delete does not merely trust a caller-supplied parent.
        /// </summary>
        [Test]
        public async Task DeleteRefusesForeignChildBeforeInvokingServerMethodAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, _) = fixture.FileTree();
            NodeId foreign = fixture.Remote.Id("foreign-file");
            fixture.Remote.Type(foreign, ObjectTypeIds.FileType);
            CallToolResult result = await fixture.Files.DeleteAsync(
                parent.ToString(), foreign.ToString(), sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadNodeIdUnknown);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Explicit deletion preserves the actual server refusal and never retries.
        /// </summary>
        [Test]
        public async Task DeleteUsesExactChildAndPreservesServerDenialAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, NodeId source) = fixture.FileTree();
            fixture.Remote.Reply(StatusCodes.BadUserAccessDenied);
            CallToolResult result = await fixture.Files.DeleteAsync(
                parent.ToString(), source.ToString(), sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            fixture.Remote.AssertCall(parent, MethodIds.FileDirectoryType_DeleteFileSystemObject, Variant.From(source));
        }

        /// <summary>
        /// Recursive deletion is never inferred from selecting a directory.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NonemptyDirectoryRequiresExplicitRecursiveScopeAsync(bool recursive)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, NodeId child) = fixture.FileTree();
            fixture.Remote.Type(child, ObjectTypeIds.FileDirectoryType);
            fixture.Remote.Browse(child, ReferenceTypeIds.HasComponent,
                [fixture.Remote.Reference(fixture.Remote.Id("grandchild"), ObjectTypeIds.FileType)]);
            fixture.Remote.Reply(StatusCodes.Good);
            CallToolResult result = await fixture.Files.DeleteAsync(
                parent.ToString(), child.ToString(), recursive, "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(!recursive));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(recursive ? 1 : 0));
            if (recursive)
            {
                Assert.That(RemoteModelTestSession.Payload(result).GetProperty("recursiveRequested").GetBoolean(),
                    Is.True);
            }
        }

        /// <summary>
        /// Name checks happen before server-side side effects.
        /// </summary>
        [TestCase("..")]
        [TestCase("../escape")]
        [TestCase("sub\\file")]
        [TestCase("2:prefixed")]
        public async Task CreationRejectsPathsInsteadOfSilentlyCreatingParentsAsync(string name)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            CallToolResult result = await fixture.Files.CreateFileAsync("ns=1;s=directory", name, "selected")
                .ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Existing entries are not replaced by create/move/copy workflows.
        /// </summary>
        [Test]
        public async Task CreationRefusesVisibleDestinationCollisionAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, _) = fixture.FileTree();
            CallToolResult result = await fixture.Files.CreateFileAsync(parent.ToString(), "source.bin", "selected")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadBrowseNameDuplicated);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// General-purpose file tools cannot bypass protected semantic paths or subtypes.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProtectedTrustListOrConfigurationDescendantIsRefusedAsync(bool descendant)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            NodeId file = fixture.Remote.Id("protected");
            if (descendant)
            {
                fixture.Remote.Type(file, ObjectTypeIds.FileType);
                NodeId parent = fixture.Remote.Id("configuration");
                fixture.Remote.Type(parent, ObjectTypeIds.ServerConfigurationType);
                fixture.Remote.Browse(file, ReferenceTypeIds.HasComponent,
                    [fixture.Remote.Reference(parent, ObjectTypeIds.ServerConfigurationType)], BrowseDirection.Inverse);
            }
            else
            {
                NodeId subtype = fixture.Remote.Id("vendor-trust-list");
                fixture.Remote.Type(file, subtype);
                fixture.Remote.Subtype(subtype, ObjectTypeIds.TrustListType);
                fixture.Remote.Subtype(subtype, ObjectTypeIds.FileType);
            }
            using IDisposable caller = fixture.Enter();
            CallToolResult result = await fixture.Files.MetadataAsync(file.ToString(), "selected")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        /// <summary>
        /// Certificate authority does not turn general file tools into a bypass of the dedicated typed workflows.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProtectedFileRequiresDedicatedWorkflowEvenWithCertificateGrantAsync(bool descendant)
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
                certificateAdministration: true).ConfigureAwait(false);
            NodeId file = fixture.Remote.Id("protected-granted-file");
            if (descendant)
            {
                fixture.Remote.Type(file, ObjectTypeIds.FileType);
                NodeId configuration = fixture.Remote.Id("granted-configuration");
                fixture.Remote.Type(configuration, ObjectTypeIds.ServerConfigurationType);
                fixture.Remote.Browse(file, ReferenceTypeIds.HasComponent,
                    [fixture.Remote.Reference(configuration, ObjectTypeIds.ServerConfigurationType)],
                    BrowseDirection.Inverse);
            }
            else
            {
                NodeId subtype = fixture.Remote.Id("granted-trust-list-subtype");
                fixture.Remote.Type(file, subtype);
                fixture.Remote.Subtype(subtype, ObjectTypeIds.TrustListType);
                fixture.Remote.Subtype(subtype, ObjectTypeIds.FileType);
            }
            using IDisposable caller = fixture.Enter();
            Assert.That(fixture.Manager.OperationPolicy.RequireSession(
                McpCapability.CertificateAdministration, "selected"), Is.EqualTo(WorkflowTestContext.Owner));
            CallToolResult result = await fixture.Files.MetadataAsync(file.ToString(), "selected")
                .ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Remote.Reads, Is.Empty);
        }

        /// <summary>
        /// Listing recognizes advertised subtypes and closes an abandoned browse continuation.
        /// </summary>
        [Test]
        public async Task ListingPreservesSubtypeNamespaceAndReleasesEarlyContinuationAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync().ConfigureAwait(false);
            (NodeId parent, NodeId file) = fixture.FileTree();
            NodeId subtype = fixture.Remote.Id("vendor-file-type");
            fixture.Remote.Subtype(subtype, ObjectTypeIds.FileType);
            var continuation = ByteString.From(new byte[] { 4, 5 });
            fixture.Remote.Browse(parent, ReferenceTypeIds.HierarchicalReferences,
                [
                    fixture.Remote.Reference(file, subtype, "first"),
                    fixture.Remote.Reference(fixture.Remote.Id("second"), ObjectTypeIds.FileType, "second")
                ], continuation: continuation);
            CallToolResult result = await fixture.Files.ListAsync(
                parent.ToString(), maxResults: 1, sessionName: "selected").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("hasMore").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("items")[0].GetProperty("expandedNodeId").GetString(),
                Does.Contain("urn:test:remote-model"));
            Assert.That(json.GetProperty("items")[0].GetProperty("browseNamespaceUri").GetString(),
                Is.EqualTo("urn:test:remote-model"));
            Assert.That(fixture.Remote.Continuations, Has.Count.EqualTo(1));
            Assert.That(fixture.Remote.Continuations[0].Release, Is.True);
            Assert.That(fixture.Remote.Continuations[0].Point, Is.EqualTo(continuation));
        }

        /// <summary>
        /// The byte ceiling's adjacent values are checked before any destructive Open.
        /// </summary>
        [TestCase(8191, false)]
        [TestCase(8192, false)]
        [TestCase(8193, true)]
        public async Task UploadByteCeilingHasExactBoundaryBeforeRemoteOpenAsync(int length, bool refused)
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            (_, NodeId file) = fixture.FileTree();
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "input.bin"), new byte[length])
                .ConfigureAwait(false);
            SetupFileTransfer(fixture);
            CallToolResult result = await fixture.Files.UploadAsync(
                file.ToString(), "input.bin", true, "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.EqualTo(refused));
            if (refused)
            {
                Assert.That(fixture.Remote.Calls, Is.Empty);
            }
            else
            {
                long written = fixture.Remote.Calls.Where(call => call.MethodId == MethodIds.FileType_Write)
                    .Sum(call => call.InputArguments[1].TryGetValue(out ByteString bytes) ? bytes.Length : -1);
                Assert.That(written, Is.EqualTo(length));
                Assert.That(fixture.Remote.Calls[0].InputArguments[0], Is.EqualTo(Variant.From((byte)6)));
                Assert.That(fixture.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Close),
                    Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Replacement consent is explicit rather than being inferred from a source path.
        /// </summary>
        [Test]
        public async Task UploadRefusesImplicitOverwriteBeforeAnyServerOperationAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            CallToolResult result = await fixture.Files.UploadAsync(
                "ns=1;s=file", "input.bin", false, "selected").ConfigureAwait(false);
            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Remote.Browses, Is.Empty);
        }

        /// <summary>
        /// Downloads handle short chunks, preserve source bytes and close only the acquired handle.
        /// </summary>
        [Test]
        public async Task DownloadStreamsShortChunksWithoutOverwriteAndClosesOwnedHandleAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            (_, NodeId file) = fixture.FileTree();
            SetupFileTransfer(fixture,
                new Queue<ByteString>(
                    [ByteString.From(new byte[] { 1, 2 }), ByteString.From(new byte[] { 3 }), default]));
            CallToolResult result = await fixture.Files.DownloadAsync(
                file.ToString(), "result.bin", "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root!, "result.bin")).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(fixture.Remote.Calls[0].InputArguments[0], Is.EqualTo(Variant.From((byte)1)));
            Assert.That(fixture.Remote.Calls.Single(call => call.MethodId == MethodIds.FileType_Close)
                .InputArguments[0], Is.EqualTo(Variant.From(71u)));
            int calls = fixture.Remote.Calls.Count;
            CallToolResult existing = await fixture.Files.DownloadAsync(
                file.ToString(), "result.bin", "selected").ConfigureAwait(false);
            Assert.That(existing.IsError, Is.True);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(calls));
            Assert.That(Directory.GetFiles(fixture.Root!), Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Failed transfer cleanup removes unpublished local artifacts.
        /// </summary>
        [Test]
        public async Task OversizedDownloadDeletesPartialArtifactAndClosesExactlyOnceAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            (_, NodeId file) = fixture.FileTree();
            SetupFileTransfer(fixture, new Queue<ByteString>(
                [ByteString.From(new byte[4096]), ByteString.From(new byte[4096]), ByteString.From(new byte[1])]));
            CallToolResult result = await fixture.Files.DownloadAsync(
                file.ToString(), "result.bin", "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.True);
            Assert.That(Directory.GetFiles(fixture.Root!), Is.Empty);
            Assert.That(fixture.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
        }

        /// <summary>
        /// Upload failure returns honest partial-effect state rather than implying rollback.
        /// </summary>
        [Test]
        public async Task FailedUploadReportsPossiblePartialRemoteEffectsAndClosesOwnHandleAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            (_, NodeId file) = fixture.FileTree();
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "input.bin"), [3, 4])
                .ConfigureAwait(false);
            SetupFileTransfer(fixture, writeStatus: StatusCodes.BadUserAccessDenied);
            CallToolResult result = await fixture.Files.UploadAsync(
                file.ToString(), "input.bin", true, "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("remoteEffectsMayBePartial").GetBoolean(),
                Is.True);
            Assert.That(fixture.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
        }

        /// <summary>
        /// Temporary commit returns completion metadata and is not followed by an extra Close or cancellation error.
        /// </summary>
        [Test]
        public async Task TemporaryWriteCommitsOncePreservesPendingMetadataAndLateCancellationAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            NodeId transfer = fixture.Remote.Id("transfer");
            NodeId generated = fixture.Remote.Id("generated");
            NodeId completion = fixture.Remote.Id("completion");
            fixture.Remote.Type(transfer, ObjectTypeIds.TemporaryFileTransferType);
            fixture.Remote.Type(generated, ObjectTypeIds.FileType);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "input.bin"), [8, 9])
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForWrite)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(transfer));
                    Assert.That(request.InputArguments.Count, Is.EqualTo(1));
                    Assert.That(request.InputArguments[0].IsNull, Is.True);
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(generated), Variant.From(71u)]
                    };
                }
                if (request.MethodId == MethodIds.TemporaryFileTransferType_CloseAndCommit)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(transfer));
                    Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(71u)));
                    cancellation.Cancel();
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.GoodCompletesAsynchronously,
                        OutputArguments = [Variant.From(completion)]
                    };
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Write));
                Assert.That(request.ObjectId, Is.EqualTo(generated));
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(71u)));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            CallToolResult result = await fixture.Files.WriteTemporaryAsync(
                transfer.ToString(), "input.bin", sessionName: "selected", ct: cancellation.Token)
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodCompletesAsynchronously.Code));
            Assert.That(json.GetProperty("completionStateMachine").GetProperty("nodeId").GetString(),
                Is.EqualTo(completion.ToString()));
            Assert.That(json.GetProperty("remoteState").GetString(),
                Is.EqualTo("commitAcceptedProcessingMayBePending"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(3));
            Assert.That(fixture.Remote.Calls.Any(call => call.MethodId == MethodIds.FileType_Close), Is.False);
            Assert.That(json.TryGetProperty("handle", out _), Is.False);
        }

        /// <summary>
        /// Temporary reads use the generated handle directly, not a second Open or another caller's handle.
        /// </summary>
        [Test]
        public async Task TemporaryReadUsesGeneratedHandleAndPreservesGenerationStatusAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            NodeId transfer = fixture.Remote.Id("transfer");
            NodeId generated = fixture.Remote.Id("generated");
            fixture.Remote.Type(transfer, ObjectTypeIds.TemporaryFileTransferType);
            fixture.Remote.Type(generated, ObjectTypeIds.FileType);
            var chunks = new Queue<ByteString>([ByteString.From(new byte[] { 7 }), default]);
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForRead)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(transfer));
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.GoodClamped,
                        OutputArguments = [Variant.From(generated), Variant.From(71u), Variant.From(NodeId.Null)]
                    };
                }
                Assert.That(request.ObjectId, Is.EqualTo(generated));
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(71u)));
                return request.MethodId == MethodIds.FileType_Read
                    ? new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(chunks.Dequeue())]
                    }
                    : new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            CallToolResult result = await fixture.Files.ReadTemporaryAsync(
                transfer.ToString(), "generated.bin", sessionName: "selected").ConfigureAwait(false);
            Assert.That(result.IsError, Is.False);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodClamped.Code));
            Assert.That(fixture.Remote.Calls.Any(call => call.MethodId == MethodIds.FileType_Open), Is.False);
            Assert.That(fixture.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root!, "generated.bin"))
                .ConfigureAwait(false),
                Is.EqualTo(new byte[] { 7 }));
        }

        /// <summary>
        /// A private handle allocated before a protected-type refusal is closed without writing any bytes.
        /// </summary>
        [Test]
        public async Task TemporaryWriteRefusesProtectedGeneratedTargetAndClosesOnlyItsAllocatedHandleAsync()
        {
            await using WorkflowTestContext fixture =
                await WorkflowTestContext.CreateAsync(files: true).ConfigureAwait(false);
            NodeId transfer = fixture.Remote.Id("transfer");
            NodeId generated = fixture.Remote.Id("protected-generated");
            fixture.Remote.Type(transfer, ObjectTypeIds.TemporaryFileTransferType);
            fixture.Remote.Type(generated, ObjectTypeIds.TrustListType);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "input.bin"), [1])
                .ConfigureAwait(false);
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForWrite)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(generated), Variant.From(77u)]
                    };
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                Assert.That(request.ObjectId, Is.EqualTo(generated));
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(77u)));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            CallToolResult result = await fixture.Files.WriteTemporaryAsync(
                transfer.ToString(), "input.bin", sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Remote.Calls.Any(call => call.MethodId == MethodIds.FileType_Write), Is.False);
        }

        /// <summary>
        /// A host generation deadline produces an explicit timeout and cleanup, rather than caller cancellation.
        /// </summary>
        [Test]
        public async Task TemporaryReadDeadlineReportsTimeoutAndReleasesGeneratedHandleAsync()
        {
            await using WorkflowTestContext fixture = await WorkflowTestContext.CreateAsync(
options: new WorkflowOptions { MaxDurationMilliseconds = 1 }, files: true)
                .ConfigureAwait(false);
            NodeId transfer = fixture.Remote.Id("transfer");
            NodeId file = fixture.Remote.Id("generated");
            NodeId state = fixture.Remote.Id("completion");
            fixture.Remote.Type(transfer, ObjectTypeIds.TemporaryFileTransferType);
            fixture.Remote.Type(file, ObjectTypeIds.FileType);
            NodeId current = fixture.Remote.Property(
                state, new QualifiedName(BrowseNames.CurrentState), Variant.From(new LocalizedText("Idle")));
            fixture.Remote.Property(current, new QualifiedName(BrowseNames.Id),
                Variant.From(ObjectIds.FileTransferStateMachineType_Idle));
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.TemporaryFileTransferType_GenerateFileForRead)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(file), Variant.From(71u), Variant.From(state)]
                    };
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                Assert.That(request.ObjectId, Is.EqualTo(file));
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(71u)));
                return new CallMethodResult { StatusCode = StatusCodes.Good };
            };
            CallToolResult result = await fixture.Files.ReadTemporaryAsync(
                transfer.ToString(), "expired.bin", sessionName: "selected").ConfigureAwait(false);
            RemoteModelTestSession.Error(result, StatusCodes.BadTimeout);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("remoteState").GetString(),
                Is.EqualTo("readWindowExpired"));
            Assert.That(fixture.Remote.Calls.Count(call => call.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(Directory.GetFiles(fixture.Root!), Is.Empty);
        }

        /// <summary>
        /// Implements only the fixed FileType operations used by transfer tests.
        /// </summary>
        private static void SetupFileTransfer(
            WorkflowTestContext fixture, Queue<ByteString>? reads = null, StatusCode writeStatus = default)
        {
            fixture.Remote.OnCall = request =>
            {
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(71u)]
                    };
                }
                Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(71u)));
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    Assert.That(request.InputArguments[1], Is.EqualTo(Variant.From(4096)));
                    return new CallMethodResult
                    {
                        StatusCode = StatusCodes.Good,
                        OutputArguments = [Variant.From(reads!.Dequeue())]
                    };
                }
                return new CallMethodResult
                {
                    StatusCode = request.MethodId == MethodIds.FileType_Write ? writeStatus : StatusCodes.Good
                };
            };
        }
    }
}
#endif
