/* Copyright (c) 2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the MIT License. See LICENSE.txt in the project root. */

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
using WotMethods = Opc.Ua.WotCon.MethodIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Exercises each remote tool's real transfer callback, confinement and wire-level commit.
    /// Does not retest the separately owned document-conversion or client helper implementation.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RemoteDocumentTransferTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task UploadStreamsExactBytesToExactFileAndPreservesCommitEvenAfterCancellationAsync(
            bool wot, bool cancelAtCommit)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            fixture.Options.MaxTransferBytes = 8192;
            byte[] payload = [.. Enumerable.Range(0, 4105).Select(index => (byte)(index % 251))];
            await File.WriteAllBytesAsync(Path.Combine(root.Path, "source.bin"), payload).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            NodeId owner = fixture.Id(wot ? "td-file" : "version-v7");
            NodeId commitMethod = wot
                ? fixture.Model(WotMethods.WoTAssetFileType_CloseAndUpdate) : MethodIds.FileType_Close;
            var received = new List<byte>();
            fixture.OnCall = request =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(owner));
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From((byte)6)]));
                    return Good(Variant.From(71u));
                }
                if (request.MethodId == MethodIds.FileType_Write)
                {
                    Assert.That(request.InputArguments.Count, Is.EqualTo(2));
                    Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(71u)));
                    Assert.That(request.InputArguments[1].TryGetValue(out ByteString chunk), Is.True);
                    Assert.That(chunk.Length, Is.InRange(1, 4096));
                    received.AddRange(chunk.ToArray()!);
                    return Good();
                }
                Assert.That(request.MethodId, Is.EqualTo(commitMethod));
                Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From(71u)]));
                if (cancelAtCommit)
                {
                    cancellation.Cancel();
                }
                return new CallMethodResult
                {
                    StatusCode = StatusCodes.GoodResultsMayBeIncomplete,
                    InputArgumentResults = [StatusCodes.Good],
                    OutputArguments = []
                };
            };

            CallToolResult result = await UploadAsync(fixture, wot, "source.bin", cancellation.Token)
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("filePath").GetString(), Is.EqualTo("source.bin"));
            Assert.That(json.GetProperty("bytesTransferred").GetInt64(), Is.EqualTo(payload.Length));
            Assert.That(received, Is.EqualTo(payload));
            Assert.That(fixture.Calls.Select(call => call.MethodId), Is.EqualTo(
            [
                MethodIds.FileType_Open, MethodIds.FileType_Write, MethodIds.FileType_Write, commitMethod
            ]));
            Assert.That(fixture.Calls.Select(call => call.ObjectId), Is.All.EqualTo(owner));
            Assert.That(fixture.CallTokens, Is.All.EqualTo(cancellation.Token));
            Assert.That(fixture.Operations[^1], Is.EqualTo("call"),
                "No registry refresh, enable or post-commit read is permitted.");
            JsonElement commit = wot ? json : json.GetProperty("commit");
            Assert.That(commit.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            Assert.That(json.GetProperty(wot ? "assetNodeId" : "versionNodeId").GetString(),
                Is.EqualTo(fixture.Id(wot ? "asset" : "version-v7").ToString()));
            Assert.That(cancellation.IsCancellationRequested, Is.EqualTo(cancelAtCommit));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(root.Path, "source.bin")).ConfigureAwait(false),
                Is.EqualTo(payload));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DownloadStreamsShortReadsToNewConfinedArtifactAndClosesOnlyOwnedHandleAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(mode: MessageSecurityMode.None).ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            NodeId owner = fixture.Id(wot ? "td-file" : "resource");
            var chunks = new Queue<ByteString>([ByteString.From([1, 2]), ByteString.From([3]), ByteString.Empty]);
            fixture.OnCall = request =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(owner));
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From((byte)1)]));
                    return Good(Variant.From(19u));
                }
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From(19u), Variant.From(4096)]));
                    return Good(Variant.From(chunks.Dequeue()));
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From(19u)]));
                return Good();
            };

            CallToolResult result = await DownloadAsync(fixture, wot, "artifact.bin").ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("filePath").GetString(), Is.EqualTo("artifact.bin"));
            Assert.That(json.GetProperty("bytesTransferred").GetInt64(), Is.EqualTo(3));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(root.Path, "artifact.bin")).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(chunks, Is.Empty, "A short FileType read is not end-of-document.");
            Assert.That(fixture.Calls.Select(call => call.MethodId), Is.EqualTo(
            [
                MethodIds.FileType_Open, MethodIds.FileType_Read, MethodIds.FileType_Read,
                MethodIds.FileType_Read, MethodIds.FileType_Close
            ]));
            Assert.That(Directory.GetFiles(root.Path).Select(Path.GetFileName), Is.EqualTo(s_downloadArtifacts));
        }

        [TestCase(false, "escape")]
        [TestCase(false, "oversize")]
        [TestCase(false, "missing-root")]
        [TestCase(true, "escape")]
        [TestCase(true, "oversize")]
        [TestCase(true, "missing-root")]
        public async Task UploadLocalRefusalPreventsOpeningRemoteFileAsync(bool wot, string refusal)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            await File.WriteAllBytesAsync(Path.Combine(root.Path, "source.bin"), [1, 2, 3]).ConfigureAwait(false);
            if (refusal == "oversize")
            {
                fixture.Options.MaxTransferBytes = 2;
            }
            else if (refusal == "missing-root")
            {
                fixture.Options.TransferRoot = null;
            }
            CallToolResult result = await UploadAsync(fixture, wot,
                refusal == "escape" ? "../escape.bin" : "source.bin").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("errorType").GetString(),
                Is.EqualTo(refusal == "escape" ? nameof(ArgumentException) :
                    refusal == "oversize" ? nameof(IOException) : nameof(InvalidOperationException)));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(fixture.Paths.Any(path => path.StartingNode == fixture.Id("asset")), Is.False,
                "Local policy must be checked before opening the asset's transfer callback.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DownloadRejectsEscapeAndExistingOutputBeforeRemoteOpenAsync(bool wot, bool existing)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            await File.WriteAllBytesAsync(Path.Combine(root.Path, "keep.bin"), [97, 98]).ConfigureAwait(false);

            CallToolResult result = await DownloadAsync(
                fixture, wot, existing ? "keep.bin" : "../escape.bin").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("errorType").GetString(),
                Is.EqualTo(existing ? nameof(IOException) : nameof(ArgumentException)));
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(root.Path, "keep.bin")).ConfigureAwait(false),
                Is.EqualTo("ab"u8.ToArray()));
            Assert.That(Directory.GetFiles(root.Path).Select(Path.GetFileName), Is.EqualTo(s_preservedArtifacts));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DownloadReadFailureClosesHandleAndNeverPublishesPartialArtifactAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            int reads = 0;
            fixture.OnCall = request =>
            {
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    return Good(Variant.From(41u));
                }
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    return ++reads == 1 ? Good(Variant.From(ByteString.From([9, 8])))
                        : new CallMethodResult { StatusCode = StatusCodes.BadUserAccessDenied };
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                Assert.That(request.InputArguments.ToArray(), Is.EqualTo([Variant.From(41u)]));
                return Good();
            };

            CallToolResult result = await DownloadAsync(fixture, wot, "partial.bin").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(fixture.Calls.Select(call => call.MethodId), Is.EqualTo(
            [
                MethodIds.FileType_Open, MethodIds.FileType_Read, MethodIds.FileType_Read, MethodIds.FileType_Close
            ]));
            Assert.That(fixture.CallTokens[^1], Is.EqualTo(CancellationToken.None));
            Assert.That(Directory.GetFiles(root.Path), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DownloadEnforcesRunningByteLimitAndRemovesIncompleteOutputAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            fixture.Options.MaxTransferBytes = 3;
            int reads = 0;
            fixture.OnCall = request =>
            {
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    return Good(Variant.From(41u));
                }
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    reads++;
                    return Good(Variant.From(ByteString.From([1, 2])));
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                return Good();
            };

            CallToolResult result = await DownloadAsync(fixture, wot, "oversize.bin").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("errorType").GetString(),
                Is.EqualTo(nameof(IOException)));
            Assert.That(reads, Is.EqualTo(2));
            Assert.That(fixture.Calls.Count(call => call.MethodId == MethodIds.FileType_Close), Is.EqualTo(1));
            Assert.That(Directory.GetFiles(root.Path), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UploadCommitRefusalRetainsPrimaryStatusAndDoesNotInvokeBusinessFollowUpAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            fixture.WotRoots();
            fixture.Asset();
            await File.WriteAllBytesAsync(Path.Combine(root.Path, "source.bin"), [1, 2, 3]).ConfigureAwait(false);
            int commitAttempts = 0;
            NodeId commitMethod = wot
                ? fixture.Model(WotMethods.WoTAssetFileType_CloseAndUpdate) : MethodIds.FileType_Close;
            fixture.OnCall = request =>
            {
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    return Good(Variant.From(67u));
                }
                if (request.MethodId == MethodIds.FileType_Write)
                {
                    return Good();
                }
                if (request.MethodId == commitMethod && commitAttempts++ == 0)
                {
                    return new CallMethodResult { StatusCode = StatusCodes.BadInvalidState };
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.FileType_Close),
                    "Only handle cleanup is permitted after failure.");
                return new CallMethodResult { StatusCode = StatusCodes.BadUserAccessDenied };
            };

            CallToolResult result = await UploadAsync(fixture, wot, "source.bin").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadInvalidState);
            Assert.That(fixture.Calls, Has.Count.EqualTo(4));
            Assert.That(fixture.Calls[2].MethodId, Is.EqualTo(commitMethod));
            Assert.That(fixture.Calls[3].MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(fixture.CallTokens[^1], Is.EqualTo(CancellationToken.None));
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty("bytesTransferred", out _), Is.False);
        }

        [Test]
        public async Task RegistryUploadRejectsLogicalResourceAliasBeforeFileMutationAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.RegistryHierarchy();
            await File.WriteAllBytesAsync(Path.Combine(root.Path, "source.bin"), [1]).ConfigureAwait(false);

            CallToolResult result = await fixture.Registry.UploadAsync(
                fixture.Id("resource").ToString(), "source.bin", "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTypeMismatch);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(root.Path, "source.bin")).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 1 }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UploadRefusesUnencryptedSessionWithoutOpeningRemoteFileAsync(bool wot)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync(mode: MessageSecurityMode.Sign).ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.WotRoots();
            await File.WriteAllBytesAsync(Path.Combine(root.Path, "source.bin"), [1]).ConfigureAwait(false);

            CallToolResult result = await UploadAsync(fixture, wot, "source.bin").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadSecurityModeInsufficient);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ResolveContentUsesOpaqueIdentityExplicitNamespaceAndRangedReadsAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            ushort ns = fixture.Context.NamespaceUris.GetIndexOrAppend("urn:test:content");
            var contentNode = new NodeId(ByteString.From([42, 43]), ns);
            var chunks = new Queue<ByteString>([ByteString.From([3, 4]), ByteString.From([5]), ByteString.Empty]);
            fixture.OnRead = request =>
            {
                Assert.That(request.NodeId, Is.EqualTo(contentNode));
                Assert.That(request.AttributeId, Is.EqualTo(Attributes.Value));
                return new DataValue(Variant.From(chunks.Dequeue()));
            };

            CallToolResult result = await fixture.Registry.ResolveContentAsync(
                "urn:test:content", Convert.ToBase64String("*+"u8.ToArray()), "resolved.bin", "selected")
                .ConfigureAwait(false);
            JsonElement json = RemoteModelTestSession.Payload(result);

            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("bytesTransferred").GetInt64(), Is.EqualTo(3));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(root.Path, "resolved.bin")).ConfigureAwait(false),
                Is.EqualTo(new byte[] { 3, 4, 5 }));
            Assert.That(fixture.Reads.Select(read => read.IndexRange), Is.EqualTo(s_contentReadRanges));
            Assert.That(fixture.ReadTimestamps, Is.All.EqualTo(TimestampsToReturn.Neither));
            Assert.That(fixture.Browses, Is.Empty, "Content identity is not resolved through logical Resource Xid.");
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("missing-content")]
        [TestCase("wrong-type")]
        [TestCase("failed-read")]
        public async Task ResolveContentFailureNeverPublishesEmptyOrPartialSuccessAsync(string failure)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.Context.NamespaceUris.GetIndexOrAppend("urn:test:content");
            fixture.OnRead = _ => failure switch
            {
                "missing-content" => DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown),
                "wrong-type" => new DataValue(Variant.From("not bytes")),
                _ => DataValue.FromStatusCode(StatusCodes.BadUserAccessDenied)
            };

            CallToolResult result = await fixture.Registry.ResolveContentAsync(
                "urn:test:content", "AQI=", "failed.bin", "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, failure switch
            {
                "missing-content" => StatusCodes.BadNodeIdUnknown,
                "wrong-type" => StatusCodes.BadTypeMismatch,
                _ => StatusCodes.BadUserAccessDenied
            });
            Assert.That(Directory.GetFiles(root.Path), Is.Empty);
            Assert.That(fixture.Reads, Has.Count.EqualTo(1));
            Assert.That(fixture.Calls, Is.Empty);
        }

        [Test]
        public async Task ResolveContentEnforcesHostBudgetDuringRangedReadAndRemovesPartialArtifactAsync()
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.Context.NamespaceUris.GetIndexOrAppend("urn:test:content");
            fixture.Options.MaxTransferBytes = 3;
            fixture.OnRead = _ => new DataValue(Variant.From(ByteString.From([1, 2])));

            CallToolResult result = await fixture.Registry.ResolveContentAsync(
                "urn:test:content", "AQI=", "bounded.bin", "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("errorType").GetString(),
                Is.EqualTo(nameof(IOException)));
            Assert.That(fixture.Reads.Select(read => read.IndexRange), Is.EqualTo(s_boundedReadRanges));
            Assert.That(Directory.GetFiles(root.Path), Is.Empty);
            Assert.That(fixture.Calls, Is.Empty);
        }

        [TestCase("missing-namespace")]
        [TestCase("invalid-key")]
        [TestCase("escape")]
        [TestCase("oversize-key")]
        public async Task ResolveContentRejectsInvalidLocalInputsBeforeReadingUaAsync(string refusal)
        {
            await using RemoteModelTestSession fixture =
                await RemoteModelTestSession.CreateAsync().ConfigureAwait(false);
            using var root = new TransferDirectory(fixture);
            fixture.Context.NamespaceUris.GetIndexOrAppend("urn:test:content");
            int count = fixture.Context.NamespaceUris.Count;
            CallToolResult result = await fixture.Registry.ResolveContentAsync(
                refusal == "missing-namespace" ? "urn:absent" : "urn:test:content",
                refusal == "invalid-key" ? "not-base64" : refusal == "oversize-key" ? new string('A', 4097) : "AQI=",
                refusal == "escape" ? "../outside.bin" : "artifact.bin", "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            JsonElement json = RemoteModelTestSession.Payload(result);
            if (refusal == "missing-namespace")
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadNodeIdUnknown);
            }
            else
            {
                Assert.That(json.GetProperty("errorType").GetString(), Is.EqualTo(refusal switch
                {
                    "invalid-key" => nameof(FormatException),
                    "oversize-key" => nameof(ArgumentOutOfRangeException),
                    _ => nameof(ArgumentException)
                }));
            }
            Assert.That(fixture.Context.NamespaceUris.Count, Is.EqualTo(count));
            Assert.That(fixture.Reads, Is.Empty);
            Assert.That(fixture.Calls, Is.Empty);
            Assert.That(Directory.GetFiles(root.Path), Is.Empty);
        }

        private static CallMethodResult Good(params Variant[] output)
        {
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output.ToArrayOf() };
        }

        private static Task<CallToolResult> UploadAsync(
            RemoteModelTestSession fixture, bool wot, string path, CancellationToken ct = default)
        {
            return wot
                ? fixture.Wot.UploadThingDescriptionAsync(
                    fixture.Id("asset").ToString(), path, sessionName: "selected", ct: ct)
                : fixture.Registry.UploadAsync(fixture.Id("version-v7").ToString(), path, "selected", ct);
        }

        private static Task<CallToolResult> DownloadAsync(RemoteModelTestSession fixture, bool wot, string path)
        {
            return wot
                ? fixture.Wot.DownloadThingDescriptionAsync(
                    fixture.Id("asset").ToString(), path, sessionName: "selected")
                : fixture.Registry.DownloadAsync(fixture.Id("resource").ToString(), path, "selected");
        }

        private static readonly string[] s_downloadArtifacts = ["artifact.bin"];
        private static readonly string[] s_preservedArtifacts = ["keep.bin"];
        private static readonly string[] s_contentReadRanges = ["0:4095", "2:4097", "3:4098"];
        private static readonly string[] s_boundedReadRanges = ["0:4095", "2:4097"];

        private sealed class TransferDirectory : IDisposable
        {
            public TransferDirectory(RemoteModelTestSession fixture)
            {
                Path = Directory.CreateTempSubdirectory("mcp-remote-transfer-").FullName;
                fixture.Options.TransferRoot = Path;
                fixture.Options.MaxTransferBytes = 4096;
            }

            public string Path { get; }

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
#endif
