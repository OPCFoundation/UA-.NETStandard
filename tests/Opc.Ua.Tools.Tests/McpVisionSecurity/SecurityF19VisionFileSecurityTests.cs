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
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpAuthorization;
using Opc.Ua.Tools.Tests.McpCompanion;
using Opc.Ua.Vision;
using Opc.Ua.Vision.MachineVision;
using TransferTypes = Opc.Ua.Vision.MachineVision.ObjectTypeIds;
using UaMethods = Opc.Ua.MethodIds;
using UaObjects = Opc.Ua.ObjectIds;
using UaTypes = Opc.Ua.ObjectTypeIds;

namespace Opc.Ua.Tools.Tests.McpVisionSecurity
{
    /// <summary>
    /// Exercises the actual Vision file tool with distinct MCP callers sharing one privileged UA session.
    /// </summary>
    [TestFixture]
    public sealed class SecurityF19VisionFileSecurityTests
    {
        /// <summary>
        /// A paused authorized upload cannot acquire an attacker prefix through a guessed raw Vision handle.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF19VisionCannotWriteProtectedTrustFile()
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync(
                shareSessionWithAttacker: true).ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            await using var accessor = new VisionClientAccessor(fixture.Manager);
            RemoteModelTestSession remote = fixture.Approved;
            NodeId trust = remote.Id("privileged-trust-list");
            remote.Type(trust, UaTypes.TrustListType);
            using var staged = new MemoryStream();
            using var encoded = new MemoryStream();
            var legitimate = new TrustListDataType
            {
                SpecifiedLists = 1,
                TrustedCertificates = [ByteString.From("*+"u8.ToArray())]
            };
            using (var encoder = new BinaryEncoder(encoded, remote.Context, leaveOpen: true))
            {
                legitimate.Encode(encoder);
            }
            byte[] trustedBytes = encoded.ToArray();
            var opened = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ArrayOf<ByteString> applied = [];
            int storeWrites = 0;
            remote.OnCall = call =>
            {
                if (call.MethodId == UaMethods.FileType_Open)
                {
                    Assert.That(call.ObjectId, Is.EqualTo(trust));
                    return new CallMethodResult { OutputArguments = [Variant.From(11u)] };
                }
                if (call.MethodId == UaMethods.FileType_Write)
                {
                    Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(11u)));
                    Assert.That(call.InputArguments[1].TryGetValue(out ByteString data), Is.True);
                    staged.Write(data.Span);
                    return new CallMethodResult();
                }
                if (call.MethodId == UaMethods.TrustListType_CloseAndUpdate)
                {
                    staged.Position = 0;
                    using var decoder = new BinaryDecoder(staged, remote.Context, leaveOpen: true);
                    var pending = new TrustListDataType();
                    pending.Decode(decoder);
                    applied = pending.TrustedCertificates;
                    return new CallMethodResult { OutputArguments = [Variant.From(true)] };
                }
                Assert.That(call.MethodId, Is.EqualTo(UaMethods.ServerConfigurationType_ApplyChanges));
                storeWrites++;
                return new CallMethodResult();
            };
            Task authorized = StageAndApplyAsync();
            _ = await Task.WhenAny(opened.Task, authorized).ConfigureAwait(false);
            if (!opened.Task.IsCompleted)
            {
                await authorized.ConfigureAwait(false);
            }
            uint raw = await opened.Task.ConfigureAwait(false);
            try
            {
                using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Attacker);
                Assert.That(fixture.Manager.GetSessionOrThrow("Prod-A"), Is.SameAs(remote.Session.Object),
                    "The attacker really can borrow this same UA session for ordinary operations.");
                int calls = remote.Calls.Count;
                ServiceResultException? write = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await VisionMachineTools.FileAsync(accessor, trust.ToString(), "Write",
                        handle: raw, dataBase64: "AQID", sessionName: "Prod-A").ConfigureAwait(false));
                Assert.That(write!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                ServiceResultException? open = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await VisionMachineTools.FileAsync(accessor, trust.ToString(), "Open", mode: 6,
                        sessionName: "Prod-A").ConfigureAwait(false));
                Assert.That(open!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(remote.Calls, Has.Count.EqualTo(calls),
                    "No attacker FileType call reaches the shared UA session.");
                Assert.That(staged.Length, Is.Zero);
                Assert.That(storeWrites, Is.Zero);
                Assert.That(fixture.SecretReads, Is.Zero);
            }
            finally
            {
                firstWrite.TrySetResult();
                await authorized.ConfigureAwait(false);
            }
            Assert.That(staged.ToArray(), Is.EqualTo(trustedBytes));
            Assert.That(applied, Is.EqualTo(legitimate.TrustedCertificates));
            Assert.That(storeWrites, Is.EqualTo(1), "Only the operator's exact staged list is applied.");
            Assert.That(remote.Calls.Count(call => call.MethodId == UaMethods.FileType_Write), Is.EqualTo(1));

            async Task StageAndApplyAsync()
            {
                using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
                _ = fixture.Manager.OperationPolicy.RequireSession(McpCapability.CertificateAdministration, "Prod-A");
                var trustList = new TrustListTypeClient(remote.Session.Object, trust, fixture.Manager.Telemetry);
                uint handle = await trustList.OpenAsync(6).ConfigureAwait(false);
                opened.TrySetResult(handle);
                await firstWrite.Task.ConfigureAwait(false);
                await trustList.WriteAsync(handle, ByteString.From(trustedBytes)).ConfigureAwait(false);
                Assert.That(await trustList.CloseAndUpdateAsync(handle).ConfigureAwait(false), Is.True);
                var configuration = new ServerConfigurationTypeClient(
                    remote.Session.Object, UaObjects.ServerConfiguration, fixture.Manager.Telemetry);
                await configuration.ApplyChangesAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stolen handles, raw guesses, wrong files and stale registrations/generations fail before UA dispatch.
        /// </summary>
        [TestCase("Caller")]
        [TestCase("Target")]
        [TestCase("RawHandle")]
        [TestCase("Registration")]
        [TestCase("Generation")]
        [Category("SecurityRegression")]
        public async Task SecurityF19VisionHandlesAreCallerTargetGenerationBound(string violation)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync(
                shareSessionWithAttacker: true).ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            await using var accessor = new VisionClientAccessor(fixture.Manager);
            (NodeId transfer, NodeId file) = ConfigureTransfer(fixture.Approved, "Recipe");
            fixture.Approved.OnCall = call => call.MethodId == UaMethods.TemporaryFileTransferType_GenerateFileForWrite
                ? new CallMethodResult { OutputArguments = [Variant.From(file), Variant.From(11u)] }
                : new CallMethodResult();
            uint handle;
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                JsonElement generated = await VisionMachineTools.FileAsync(
                    accessor, transfer.ToString(), "GenerateWrite", sessionName: "Prod-A").ConfigureAwait(false);
                handle = Handle(generated, fixture.Approved);
            }
            if (violation == "Registration")
            {
                await fixture.RegisterHostAsync(fixture.Evil).ConfigureAwait(false);
            }
            if (violation == "Generation")
            {
                fixture.Approved.Session.SetupGet(session => session.SessionId)
                    .Returns(fixture.Approved.Id("new-session"));
            }
            int calls = fixture.Approved.Calls.Count;
            using (fixture.Callers.EnterTrustedCaller(violation == "Caller"
                ? SecurityF17SessionContext.Attacker : SecurityF17SessionContext.Victim))
            {
                ServiceResultException? refused = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await VisionMachineTools.FileAsync(accessor,
                        violation == "Target" ? fixture.Approved.Id("different-file").ToString() : file.ToString(),
                        "Write", handle: violation == "RawHandle" ? 11u : handle, dataBase64: "AQID",
                        sessionName: "Prod-A").ConfigureAwait(false));
                Assert.That(refused!.StatusCode, Is.EqualTo(violation == "Generation"
                    ? StatusCodes.BadSessionIdInvalid : StatusCodes.BadUserAccessDenied));
            }
            Assert.That(fixture.Approved.Calls, Has.Count.EqualTo(calls));
            Assert.That(fixture.Evil.Calls, Is.Empty);
            Assert.That(fixture.SecretReads, Is.Zero);
            await accessor.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Approved.Calls.Count(call => call.MethodId == UaMethods.FileType_Close),
                Is.EqualTo(violation is "Generation" or "Registration" ? 0 : 1));
        }

        /// <summary>
        /// Neither an ordinary temporary-transfer type nor a protected returned file establishes Vision provenance.
        /// </summary>
        [TestCase("TransferType")]
        [TestCase("ReturnedTrustList")]
        [TestCase("ProtectedSubtype")]
        [Category("SecurityRegression")]
        public async Task SecurityF19GenerationCannotLaunderProtectedOrUnverifiedFiles(string violation)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            await using var accessor = new VisionClientAccessor(fixture.Manager);
            RemoteModelTestSession remote = fixture.Approved;
            (NodeId transfer, NodeId file) = ConfigureTransfer(remote, "Recipe");
            if (violation == "TransferType")
            {
                remote.Type(transfer, UaTypes.TemporaryFileTransferType);
            }
            else if (violation == "ReturnedTrustList")
            {
                remote.Type(file, UaTypes.TrustListType);
            }
            else
            {
                NodeId subtype = remote.Id("protected-file-subtype");
                remote.Type(file, subtype);
                remote.Subtype(subtype, UaTypes.TrustListType);
            }
            remote.OnCall = _ => new CallMethodResult
            {
                OutputArguments = [Variant.From(file), Variant.From(11u)]
            };
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            ServiceResultException? refused = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await VisionMachineTools.FileAsync(accessor, transfer.ToString(), "GenerateWrite",
                    sessionName: "Prod-A").ConfigureAwait(false));
            Assert.That(refused!.StatusCode, Is.EqualTo(violation == "TransferType"
                ? StatusCodes.BadTypeMismatch : StatusCodes.BadUserAccessDenied));
            Assert.That(remote.Calls, Has.Count.EqualTo(violation == "TransferType" ? 0 : 1));
            Assert.That(remote.Calls.Any(call => call.MethodId == UaMethods.FileType_Write), Is.False);
            Assert.That(remote.Calls.Any(call => call.MethodId == UaMethods.FileType_Close), Is.False,
                "A malicious returned node/handle must not trigger privileged cleanup dispatch.");
            Assert.That(fixture.SecretReads, Is.Zero);
        }

        /// <summary>
        /// Legitimate specialized transfers preserve document options, exact chunks and commit/close semantics.
        /// </summary>
        [TestCase("Recipe", 0)]
        [TestCase("Recipe", 3)]
        [TestCase("Configuration", 3)]
        [TestCase("Result", 3)]
        [TestCase("Recipe", 1048576)]
        [Category("SecurityRegression")]
        public async Task SecurityF19VerifiedTransfersPreserveBytesCommitAndCleanup(string kind, int length)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync()
                .ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            await using var accessor = new VisionClientAccessor(fixture.Manager);
            RemoteModelTestSession remote = fixture.Approved;
            remote.Context.MaxByteStringLength = 2 * 1024 * 1024;
            (NodeId transfer, NodeId file) = ConfigureTransfer(remote, kind);
            byte[] bytes = new byte[length];
            Array.Fill(bytes, (byte)42);
            var writes = new List<byte>();
            remote.OnCall = call =>
            {
                if (call.MethodId == UaMethods.TemporaryFileTransferType_GenerateFileForWrite ||
                    call.MethodId == UaMethods.TemporaryFileTransferType_GenerateFileForRead)
                {
                    Assert.That(call.ObjectId, Is.EqualTo(transfer));
                    AssertDocumentOptions(call.InputArguments[0], kind);
                    return new CallMethodResult
                    {
                        OutputArguments = call.MethodId == UaMethods.TemporaryFileTransferType_GenerateFileForWrite
                            ? [Variant.From(file), Variant.From(11u)]
                            : [Variant.From(file), Variant.From(11u), Variant.From(NodeId.Null)]
                    };
                }
                Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(11u)),
                    "Opaque MCP handles must be translated back only on the captured server session.");
                if (call.MethodId == UaMethods.FileType_Write)
                {
                    Assert.That(call.ObjectId, Is.EqualTo(file));
                    Assert.That(call.InputArguments[1].TryGetValue(out ByteString written), Is.True);
                    writes.AddRange(written.Span.ToArray());
                    return new CallMethodResult();
                }
                if (call.MethodId == UaMethods.TemporaryFileTransferType_CloseAndCommit)
                {
                    Assert.That(call.ObjectId, Is.EqualTo(transfer));
                    return new CallMethodResult { OutputArguments = [Variant.From(NodeId.Null)] };
                }
                if (call.MethodId == UaMethods.FileType_Read)
                {
                    Assert.That(call.ObjectId, Is.EqualTo(file));
                    Assert.That(call.InputArguments[1], Is.EqualTo(Variant.From(Math.Max(1, length))));
                    return new CallMethodResult { OutputArguments = [Variant.From(ByteString.From(bytes))] };
                }
                Assert.That(call.MethodId, Is.EqualTo(UaMethods.FileType_Close));
                return new CallMethodResult();
            };
            using IDisposable caller = fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim);
            uint writeHandle = Handle(await VisionMachineTools.FileAsync(accessor, transfer.ToString(),
                "GenerateWrite", kind, "document", sessionName: "Prod-A").ConfigureAwait(false), remote);
            await VisionMachineTools.FileAsync(accessor, file.ToString(), "Write",
                handle: writeHandle, dataBase64: Convert.ToBase64String(bytes), sessionName: "Prod-A")
                .ConfigureAwait(false);
            await VisionMachineTools.FileAsync(accessor, transfer.ToString(), "Commit", handle: writeHandle,
                sessionName: "Prod-A").ConfigureAwait(false);
            await VisionMachineTools.FileAsync(accessor, file.ToString(), "Close", handle: writeHandle,
                sessionName: "Prod-A").ConfigureAwait(false);
            uint readHandle = Handle(await VisionMachineTools.FileAsync(accessor, transfer.ToString(),
                "GenerateRead", kind, "document", sessionName: "Prod-A").ConfigureAwait(false), remote);
            JsonElement data = await VisionMachineTools.FileAsync(accessor, file.ToString(), "Read",
                handle: readHandle, length: Math.Max(1, length), sessionName: "Prod-A").ConfigureAwait(false);
            using (var decoder = new JsonDecoder(data.GetRawText(), remote.Context))
            {
                Assert.That(decoder.ReadVariant("Data").TryGetValue(out ByteString actual), Is.True);
                Assert.That(actual.Span.ToArray(), Is.EqualTo(bytes));
            }
            await VisionMachineTools.FileAsync(accessor, file.ToString(), "Close",
                handle: readHandle, sessionName: "Prod-A").ConfigureAwait(false);
            Assert.That(writes, Is.EqualTo(bytes));
            Assert.That(remote.Calls.Count(call => call.MethodId == UaMethods.FileType_Close), Is.EqualTo(1));
            Assert.That(remote.Calls.Count(call => call.MethodId == UaMethods.TemporaryFileTransferType_CloseAndCommit),
                Is.EqualTo(1));
            Assert.That(remote.Calls.Any(call => call.MethodId == UaMethods.FileType_Open), Is.False);
            int completedCalls = remote.Calls.Count;
            ServiceResultException? stale = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await VisionMachineTools.FileAsync(accessor, file.ToString(), "Write",
                    handle: writeHandle, dataBase64: "AQID", sessionName: "Prod-A").ConfigureAwait(false));
            Assert.That(stale!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(remote.Calls, Has.Count.EqualTo(completedCalls));
        }

        /// <summary>
        /// Completion reads and waits use only the returned completion object and its caller-owned transfer handle.
        /// </summary>
        [TestCase("ReadCompletion")]
        [TestCase("WaitForCompletion")]
        [Category("SecurityRegression")]
        public async Task SecurityF19CompletionRequiresOwnedReturnedProvenance(string operation)
        {
            await using SecurityF17SessionContext fixture = await SecurityF17SessionContext.CreateAsync(
                shareSessionWithAttacker: true).ConfigureAwait(false);
            await fixture.RegisterHostAsync(fixture.Approved).ConfigureAwait(false);
            await using var accessor = new VisionClientAccessor(fixture.Manager);
            RemoteModelTestSession remote = fixture.Approved;
            (NodeId transfer, NodeId file) = ConfigureTransfer(remote, "Result");
            NodeId completion = remote.Id("completion");
            NodeId current = remote.Id("current-state");
            NodeId stateId = remote.Id("state-id");
            remote.Type(completion, UaTypes.FileTransferStateMachineType);
            remote.Child(completion, new QualifiedName(BrowseNames.CurrentState), current);
            remote.Child(current, new QualifiedName(BrowseNames.Id), stateId);
            ConfigureCompoundPaths(remote);
            remote.Values[(current, Attributes.Value)] = new DataValue(Variant.From(new LocalizedText("Idle")));
            remote.Values[(stateId, Attributes.Value)] =
                new DataValue(Variant.From(UaObjects.FileTransferStateMachineType_Idle));
            remote.OnCall = call => call.MethodId == UaMethods.TemporaryFileTransferType_GenerateFileForRead
                ? new CallMethodResult
                {
                    OutputArguments = [Variant.From(file), Variant.From(11u), Variant.From(completion)]
                }
                : new CallMethodResult();
            uint handle;
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                handle = Handle(await VisionMachineTools.FileAsync(accessor, transfer.ToString(),
                    "GenerateRead", "Result", "document", sessionName: "Prod-A").ConfigureAwait(false), remote);
            }
            int reads = remote.Reads.Count;
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Attacker))
            {
                ServiceResultException? stolen = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await VisionMachineTools.FileAsync(accessor, completion.ToString(), operation,
                        handle: handle, sessionName: "Prod-A").ConfigureAwait(false));
                Assert.That(stolen!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            using (fixture.Callers.EnterTrustedCaller(SecurityF17SessionContext.Victim))
            {
                ServiceResultException? wrongTarget = Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await VisionMachineTools.FileAsync(accessor, remote.Id("unrelated-state-machine").ToString(),
                        operation, handle: handle, sessionName: "Prod-A").ConfigureAwait(false));
                Assert.That(wrongTarget!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(remote.Reads, Has.Count.EqualTo(reads));
                JsonElement completed = await VisionMachineTools.FileAsync(accessor, completion.ToString(),
                    operation, handle: handle, sessionName: "Prod-A").ConfigureAwait(false);
                Assert.That(completed.GetProperty("state").GetString(), Is.EqualTo("Idle"));
                Assert.That(completed.GetProperty("stateId").GetString(),
                    Is.EqualTo(UaObjects.FileTransferStateMachineType_Idle.ToString()));
                await VisionMachineTools.FileAsync(accessor, file.ToString(), "Close", handle: handle,
                    sessionName: "Prod-A").ConfigureAwait(false);
            }
            Assert.That(remote.Calls.Count(call => call.MethodId == UaMethods.FileType_Close), Is.EqualTo(1));
            Assert.That(fixture.SecretReads, Is.Zero);
        }

        /// <summary>
        /// Configures real model metadata rather than bypassing type or protected-ownership classification.
        /// </summary>
        private static (NodeId Transfer, NodeId File) ConfigureTransfer(RemoteModelTestSession remote, string kind)
        {
            remote.Context.Factory.Builder.AddOpcUaMachineVision().Commit();
            remote.Session.SetupGet(session => session.Factory).Returns(remote.Context.Factory);
            remote.Context.NamespaceUris.GetIndexOrAppend(MachineVisionModels.ControlNamespaceUri);
            NodeId transfer = remote.Id("verified-transfer");
            NodeId file = remote.Id("verified-file");
            remote.Type(transfer, kind switch
            {
                "Recipe" => TransferTypes.RecipeTransferType,
                "Configuration" => TransferTypes.ConfigurationTransferType,
                _ => TransferTypes.ResultTransferType
            });
            remote.Type(file, UaTypes.FileType);
            return (transfer, file);
        }

        /// <summary>
        /// Checks that only the opaque MCP reference, never the underlying raw UA number, is published.
        /// </summary>
        private static uint Handle(JsonElement generated, RemoteModelTestSession remote)
        {
            using var decoder = new JsonDecoder(generated.GetRawText(), remote.Context);
            Assert.That(decoder.ReadVariant("Handle").TryGetValue(out uint handle), Is.True);
            Assert.That(handle, Is.Not.Zero.And.Not.EqualTo(11u));
            return handle;
        }

        /// <summary>
        /// Resolves the actual generated state client's compound paths through the fixture's explicit child facts.
        /// </summary>
        private static void ConfigureCompoundPaths(RemoteModelTestSession remote)
        {
            remote.Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var results = new BrowsePathResult[paths.Count];
                    for (int index = 0; index < paths.Count; index++)
                    {
                        BrowsePath path = paths[index];
                        remote.Paths.Add(path);
                        NodeId current = path.StartingNode;
                        var result = new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                        foreach (RelativePathElement element in path.RelativePath.Elements)
                        {
                            if (!remote.Translations.TryGetValue((current, element.TargetName),
                                    out BrowsePathResult? step) ||
                                StatusCode.IsBad(step.StatusCode) ||
                                step.Targets.Count != 1 ||
                                step.Targets[0].RemainingPathIndex != uint.MaxValue)
                            {
                                result = new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                                break;
                            }
                            result = step;
                            current = McpModelAccess.ModelNode(remote.Session.Object, step.Targets[0].TargetId);
                        }
                        results[index] = result;
                    }
                    return ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results
                    });
                });
        }

        /// <summary>
        /// Checks the selected companion type's real typed document options.
        /// </summary>
        private static void AssertDocumentOptions(Variant options, string kind)
        {
            switch (kind)
            {
                case "Recipe":
                    Assert.That(options.TryGetStructure<RecipeTransferOptions>(out RecipeTransferOptions? recipe),
                        Is.True);
                    Assert.That(recipe!.InternalId.Id, Is.EqualTo("document"));
                    break;
                case "Configuration":
                    Assert.That(options.TryGetStructure<ConfigurationTransferOptions>(
                        out ConfigurationTransferOptions? configuration), Is.True);
                    Assert.That(configuration!.InternalId.Id, Is.EqualTo("document"));
                    break;
                default:
                    Assert.That(options.TryGetStructure<ResultTransferOptions>(out ResultTransferOptions? result),
                        Is.True);
                    Assert.That(result!.Id.Id, Is.EqualTo("document"));
                    break;
            }
        }
    }
}
#endif
