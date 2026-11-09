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
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Di.Client;
using UaLens.Plugins.Companions;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;

namespace UaLens.Tests.Companions;

/// <summary>
/// Qualifies the real DI provider and upload client through the single-use
/// workspace boundary. All protocol responses remain in memory.
/// </summary>
[TestFixture]
public sealed class DeviceUpdateLifecycleQualificationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ChunkedUploadPinsEveryReviewedByteAndLeavesInstallationSeparateAsync(bool delayedCompletion)
    {
        byte[] storage = CreatePackage();
        byte[] reviewed = storage.ToArray();
        ByteString digest = ByteString.From(SHA256.HashData(reviewed));
        var context = new DeviceUpdateTestContext { Package = new ByteString(storage) };
        var chunks = new List<int>();
        Func<CallMethodRequest, CancellationToken, CallMethodResult> upload = context.Server.CallHandler!;
        context.Server.CallHandler = (request, token) =>
        {
            CallMethodResult result = upload(request, token);
            if (request.MethodId == context.Write)
            {
                Assert.That(request.InputArguments[1].TryGetValue(out ByteString bytes), Is.True);
                chunks.Add(bytes.Length);
            }
            return request.MethodId == context.Commit
                ? Good([Variant.From(delayedCompletion ? context.Completion : NodeId.Null)])
                : result;
        };
        CompanionWorkspace workspace = context.CreateAuthorizedWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            CompanionOperationDraft draft = await PrepareAsync(
                workspace, context, "upload-package-sample", UploadInputs(context, digest)).ConfigureAwait(false);
            Assert.That(draft.TaskInput!.Review, Does.Contain("16403 verified bytes")
                .And.Contain(Convert.ToHexString(digest.Span)).And.Contain("No installation"));
            Assert.That(context.Server.Calls.IsEmpty, Is.True);
            Array.Fill(storage, (byte)0xE7);
            context.Package = ByteString.From([0x01, 0x02]);

            CompanionOperationResult result = await workspace.ExecuteTaskAsync(draft, true, true, null)
                .ConfigureAwait(false);

            Assert.That(context.Uploaded, Is.EqualTo(reviewed));
            Assert.That(chunks, Is.EqualTo(s_chunkSizes));
            Assert.That(context.UploadCallNames, Is.EqualTo(s_chunkedUploadMethods));
            Assert.That(context.CommitSucceeded, Is.True);
            Assert.That(Field(result.Values, "Package bytes").TryGetValue(out long uploaded), Is.True);
            Assert.That(uploaded, Is.EqualTo(reviewed.Length));
            Assert.That(Field(result.Values, "Package SHA-256").TryGetValue(out ByteString returnedDigest), Is.True);
            Assert.That(returnedDigest, Is.EqualTo(digest));
            Assert.That(Field(result.Values, "Completion state machine").TryGetValue(out NodeId completion), Is.True);
            Assert.That(completion, Is.EqualTo(delayedCompletion ? context.Completion : NodeId.Null));
            Assert.That(result.Summary, Does.Contain("No install was requested"));
            Assert.That(result.Summary.Contains("must be observed", StringComparison.Ordinal),
                Is.EqualTo(delayedCompletion));
            await AssertConsumedAsync(workspace, draft).ConfigureAwait(false);
            CompanionInspection state = await workspace.InspectAsync(context.Target).ConfigureAwait(false);
            AssertState(state.Values, context.Action("install-package-sample").Facet, "Idle",
                context.Action("install-package-sample").Facet.InitialState);
            Assert.That(context.Server.Calls[^1].MethodId, Is.EqualTo(context.Commit));
            context.Packages.Verify(value => value.ReadAsync(
                context.PackagePath, 67108864, It.IsAny<CancellationToken>()), Times.Once);
        }
        VerifyBorrowedSession(context);
    }

    [Test]
    public async Task CommitTimeoutConsumesPreparedUploadAndNeverReplaysOrInstallsAsync()
    {
        var context = new DeviceUpdateTestContext();
        Func<CallMethodRequest, CancellationToken, CallMethodResult> upload = context.Server.CallHandler!;
        var lostReply = new ServiceResultException(StatusCodes.BadTimeout, "Commit reply was not observed.");
        context.Server.CallHandler = (request, token) =>
        {
            CallMethodResult result = upload(request, token);
            if (request.MethodId == context.Commit)
            {
                // The server accepted the bytes, but the caller cannot infer that
                // from a reply which never arrives.
                throw lostReply;
            }
            return result;
        };
        CompanionWorkspace workspace = context.CreateAuthorizedWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            CompanionOperationDraft draft = await PrepareAsync(workspace, context,
                "upload-package-sample", context.Inputs("upload-package-sample")).ConfigureAwait(false);

            await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(draft, true, true, null),
                Throws.Exception.SameAs(lostReply)).ConfigureAwait(false);

            Assert.That(context.CommitSucceeded, Is.True, "A lost reply is not evidence of a rejected commit.");
            Assert.That(context.Uploaded, Is.EqualTo(new byte[] { 0x61, 0x62, 0x63 }));
            await AssertConsumedAsync(workspace, draft).ConfigureAwait(false);
            CompanionInspection observed = await workspace.InspectAsync(context.Target).ConfigureAwait(false);
            DeviceUpdateTestFacet installation = context.Action("install-package-sample").Facet;
            AssertState(observed.Values, installation, "Idle", installation.InitialState);
            Assert.That(context.UploadCallNames, Is.EqualTo(s_committedUploadMethods));
            context.Packages.Verify(value => value.ReadAsync(
                context.PackagePath, 67108864, It.IsAny<CancellationToken>()), Times.Once);
        }
        VerifyBorrowedSession(context);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CanceledPartialUploadCleansOnlyItsOpenHandleWithoutMaskingCancellationAsync(bool cleanupFails)
    {
        byte[] package = CreatePackage();
        ByteString digest = ByteString.From(SHA256.HashData(package));
        var context = new DeviceUpdateTestContext { Package = ByteString.From(package) };
        using var cancellation = new CancellationTokenSource();
        CancellationToken uploadToken = default;
        CancellationToken cleanupToken = default;
        var originalFailure = new OperationCanceledException(
            "Upload interrupted after one chunk.", cancellation.Token);
        Func<CallMethodRequest, CancellationToken, CallMethodResult> upload = context.Server.CallHandler!;
        context.Server.CallHandler = (request, token) =>
        {
            if (request.MethodId == context.Close)
            {
                cleanupToken = token;
                Assert.That(token.CanBeCanceled, Is.True);
                Assert.That(token.IsCancellationRequested, Is.False);
                Assert.That(token, Is.Not.EqualTo(uploadToken));
                Assert.That(request.ObjectId, Is.EqualTo(context.FileNode));
                Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
                Assert.That(handle, Is.EqualTo(92u), "Close owns the opened handle, not commit handle 71.");
                if (cleanupFails)
                {
                    throw new ServiceResultException(StatusCodes.BadCommunicationError, "Close reply was lost.");
                }
            }
            CallMethodResult result = upload(request, token);
            if (request.MethodId == context.Write)
            {
                uploadToken = token;
                cancellation.Cancel();
                throw originalFailure;
            }
            return result;
        };
        CompanionWorkspace workspace = context.CreateAuthorizedWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            CompanionOperationDraft draft = await PrepareAsync(
                workspace, context, "upload-package-sample", UploadInputs(context, digest)).ConfigureAwait(false);

            await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(draft, true, true, null, cancellation.Token),
                Throws.Exception.SameAs(originalFailure)).ConfigureAwait(false);

            Assert.That(uploadToken.IsCancellationRequested, Is.True);
            Assert.That(cleanupToken.CanBeCanceled, Is.True);
            Assert.That(context.Uploaded, Is.EqualTo(package[..8192]));
            Assert.That(context.CommitSucceeded, Is.False);
            await AssertConsumedAsync(workspace, draft).ConfigureAwait(false);
            Assert.That(context.UploadCallNames, Is.EqualTo(s_interruptedUploadMethods));
            context.Packages.Verify(value => value.ReadAsync(
                context.PackagePath, 67108864, It.IsAny<CancellationToken>()), Times.Once);
        }
        VerifyBorrowedSession(context);
    }

    [Test]
    public async Task AcceptedInstallWithFailedReadbackRequiresFreshInspectionAndCannotReplayAsync()
    {
        var context = new DeviceUpdateTestContext();
        DeviceUpdateTestAction install = context.Action("install-package-sample");
        NodeId installing = context.Server.Id("observed-installing-state");
        var readbackFailure = new ServiceResultException(StatusCodes.BadCommunicationError,
            "The install method returned but its resulting state could not be read.");
        context.Server.CallHandler = (request, _) =>
        {
            Assert.That(request.ObjectId, Is.EqualTo(install.Facet.Machine));
            Assert.That(request.MethodId, Is.EqualTo(install.MethodId));
            Assert.That(request.InputArguments[1].TryGetValue(out string revision), Is.True);
            Assert.That(revision, Is.EqualTo("2.7.9"));
            Assert.That(request.InputArguments[3].TryGetValue(out ByteString digest), Is.True);
            Assert.That(Convert.ToHexString(digest.Span), Is.EqualTo(DeviceUpdateTestContext.PackageDigest));
            context.SetState(install.Facet, "Installing", installing);
            context.Server.ReadHandler = (read, _) => read.NodeId == install.Facet.Current
                ? throw readbackFailure
                : context.Server.Read(read);
            return Good();
        };
        CompanionWorkspace workspace = context.CreateAuthorizedWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            CompanionOperationDraft draft = await PrepareAsync(workspace, context,
                "install-package-sample", context.Inputs("install-package-sample")).ConfigureAwait(false);

            await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(draft, true, true, null),
                Throws.Exception.SameAs(readbackFailure)).ConfigureAwait(false);

            await AssertConsumedAsync(workspace, draft).ConfigureAwait(false);
            context.Server.ReadHandler = null;
            CompanionInspection observed = await workspace.InspectAsync(context.Target).ConfigureAwait(false);
            AssertState(observed.Values, install.Facet, "Installing", installing);
            DeviceUpdateTestFacet confirmation = context.Action("confirm-sample").Facet;
            AssertState(observed.Values, confirmation, "Unconfirmed", confirmation.InitialState);
            CallMethodRequest onlyCall = context.Server.Calls.ToList().Single();
            Assert.That(onlyCall.ObjectId, Is.EqualTo(install.Facet.Machine));
            Assert.That(onlyCall.MethodId, Is.EqualTo(install.MethodId),
                "No confirmation, recovery, power-cycle or replay may be inferred from service acceptance.");
            context.VerifyNoPackageRead();
        }
        VerifyBorrowedSession(context);
    }

    private static byte[] CreatePackage()
    {
        return Enumerable.Range(0, (2 * SoftwareUpdateClient.DefaultUploadChunkSizeBytes) + 19)
            .Select(index => (byte)(((73 * index) + 19) & 0xFF)).ToArray();
    }

    private static ArrayOf<CompanionValue> UploadInputs(DeviceUpdateTestContext context, ByteString digest)
    {
        return
        [
            new("path", Variant.From(context.PackagePath)),
            new("packageId", Variant.From("qualification")),
            new("sha256", Variant.From(Convert.ToHexString(digest.Span)))
        ];
    }

    private static async Task<CompanionOperationDraft> PrepareAsync(
        CompanionWorkspace workspace, DeviceUpdateTestContext context, string operation,
        ArrayOf<CompanionValue> inputs)
    {
        await workspace.BindAsync(context.Server.Session.Object).ConfigureAwait(false);
        await workspace.DiscoverAsync("di").ConfigureAwait(false);
        await workspace.InspectAsync(context.Target).ConfigureAwait(false);
        return await workspace.PrepareTaskAsync(context.Target, operation, inputs).ConfigureAwait(false);
    }

    private static Task AssertConsumedAsync(CompanionWorkspace workspace, CompanionOperationDraft draft)
    {
        return Assert.ThatAsync(() => workspace.ExecuteTaskAsync(draft, true, true, null),
            Throws.InvalidOperationException.With.Message.Contains("Prepare this operation again"));
    }

    private static void AssertState(
        ArrayOf<CompanionValue> values, DeviceUpdateTestFacet facet, string text, NodeId stateId)
    {
        Assert.That(Field(values, facet.Name + " state").TryGetValue(out LocalizedText state), Is.True);
        Assert.That(state.Text, Is.EqualTo(text));
        Assert.That(Field(values, facet.Name + " state NodeId").TryGetValue(out NodeId observedId), Is.True);
        Assert.That(observedId, Is.EqualTo(stateId));
        Assert.That(Field(values, facet.Name + " state machine").TryGetValue(out NodeId machine), Is.True);
        Assert.That(machine, Is.EqualTo(facet.Machine));
    }

    private static void VerifyBorrowedSession(DeviceUpdateTestContext context)
    {
        Assert.That(context.Server.Session.Object.Connected, Is.True);
        context.Server.Session.Verify(session => session.Dispose(), Times.Never);
    }

    private static readonly int[] s_chunkSizes = [8192, 8192, 19];

    private static readonly string[] s_chunkedUploadMethods =
        ["GenerateFileForWrite", "Open", "Write", "Write", "Write", "Close", "CloseAndCommit"];

    private static readonly string[] s_committedUploadMethods =
        ["GenerateFileForWrite", "Open", "Write", "Close", "CloseAndCommit"];

    private static readonly string[] s_interruptedUploadMethods =
        ["GenerateFileForWrite", "Open", "Write", "Close"];
}
