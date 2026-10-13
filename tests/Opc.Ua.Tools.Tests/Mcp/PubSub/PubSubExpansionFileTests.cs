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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Exercises standard FileType transactions below the generated clients and real root-confined file transfer.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionFileTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExportStreamsActualVersionAndRedactsPrivateFieldsBeforePublishingAsync(bool redact)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            if (redact)
            {
                peer.Model.PublishedDataSets[0].ExtensionFields =
                    [new KeyValuePair { Key = new QualifiedName("private"), Value = Variant.From(Marker) }];
            }
            peer.RefreshDownload();

            CallToolResult result = await peer.Tools.ExportAsync("export.xml", Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.False, RemoteModelTestSession.Payload(result).GetRawText());
            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(output.GetProperty("completeConfiguration").GetBoolean(), Is.EqualTo(!redact));
            Assert.That(output.GetProperty("redactedItems").GetInt32(), Is.EqualTo(redact ? 1 : 0));
            Assert.That(output.GetProperty("credentialsIncluded").GetBoolean(), Is.False);
            byte[] artifact = await File.ReadAllBytesAsync(peer.Path("export.xml")).ConfigureAwait(false);
            PubSubConfigurationDataType decoded = peer.Decode(artifact);
            Assert.That(decoded.PublishedDataSets[0].DataSetMetaData.ConfigurationVersion.MajorVersion,
                Is.EqualTo(17));
            Assert.That(decoded.PublishedDataSets[0].DataSetMetaData.ConfigurationVersion.MinorVersion,
                Is.EqualTo(23));
            Assert.That(decoded.PublishedDataSets[0].DataSetMetaData.Description.Text,
                Is.EqualTo(peer.Model.PublishedDataSets[0].DataSetMetaData.Description.Text));
            Assert.That(decoded.PublishedDataSets[0].ExtensionFields, Is.Empty);
            Assert.That(Encoding.UTF8.GetString(artifact), Does.Not.Contain(Marker));
            Assert.That(peer.ReadCalls, Is.GreaterThan(1));
            Assert.That(peer.CloseCalls, Is.EqualTo(1));
            Assert.That(peer.OpenMode, Is.EqualTo(1));
            Assert.That(peer.Context.Remote.Calls[^1].MethodId, Is.EqualTo(MethodIds.FileType_Close));
            Assert.That(Directory.GetFiles(peer.Root, ".opcua-mcp-*"), Is.Empty);
            Assert.That(peer.Context.Transports.Factories, Is.Zero);
        }

        [TestCase("existing.xml")]
        [TestCase("outside-root")]
        [TestCase(".")]
        [TestCase("missing-parent")]
        public async Task InvalidOrExistingExportDestinationNeverOpensRemoteFileAsync(string destination)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            await File.WriteAllTextAsync(peer.Path("existing.xml"), "original").ConfigureAwait(false);
            destination = destination switch
            {
                "outside-root" => Path.Combine("..", "escape.xml"),
                "missing-parent" => Path.Combine("missing", "export.xml"),
                _ => destination
            };

            CallToolResult result = await peer.Tools.ExportAsync(destination, Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(await File.ReadAllTextAsync(peer.Path("existing.xml")).ConfigureAwait(false),
                Is.EqualTo("original"));
            Assert.That(peer.Context.Remote.Calls, Is.Empty);
            Assert.That(Directory.GetFiles(peer.Root), Has.Length.EqualTo(1));
        }

        [TestCase("oversized-frame")]
        [TestCase("invalid-xml")]
        [TestCase("read-failure")]
        public async Task FailedRemoteReadAlwaysClosesAndNeverPublishesAnArtifactAsync(string failure)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.OversizedRead = failure == "oversized-frame";
            peer.ReadFailure = failure == "read-failure";
            if (failure == "invalid-xml")
            {
                peer.Download = Encoding.UTF8.GetBytes("<not-a-configuration");
            }

            CallToolResult result = await peer.Tools.ExportAsync("export.xml", Session).ConfigureAwait(false);

            StatusCode expected = failure switch
            {
                "oversized-frame" => StatusCodes.BadEncodingLimitsExceeded,
                "invalid-xml" => StatusCodes.BadDecodingError,
                _ => StatusCodes.BadCommunicationError
            };
            RemoteModelTestSession.Error(result, expected);
            Assert.That(peer.CloseCalls, Is.EqualTo(1));
            Assert.That(peer.PendingBytes, Is.Zero);
            Assert.That(Directory.GetFiles(peer.Root), Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ExportEnforcesBothProtocolAndHostFileByteBudgetsAsync(bool protocolLimit)
        {
            await using FilePeer peer = await FilePeer.CreateAsync(
                configurationLimit: protocolLimit ? 1024 : 1_048_576,
                transferLimit: protocolLimit ? 1_048_576 : 1024).ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();

            CallToolResult result = await peer.Tools.ExportAsync("export.xml", Session).ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(RemoteModelTestSession.Payload(result).GetProperty("errorType").GetString(),
                Is.EqualTo(nameof(IOException)));
            Assert.That(peer.CloseCalls, Is.EqualTo(1));
            Assert.That(peer.ReadCalls, Is.GreaterThan(0));
            Assert.That(Directory.GetFiles(peer.Root), Is.Empty,
                "Neither an incomplete destination nor a staged .tmp file may survive.");
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        public async Task ImportReportsActualApplyAndPartialOutcomesWithoutExposingConfigurationValuesAsync(
            bool applied, bool partial)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.Apply = applied;
            peer.Partial = partial;
            byte[] source = peer.EncodeReplacement();
            await File.WriteAllBytesAsync(peer.Path("import.xml"), source).ConfigureAwait(false);

            CallToolResult result = await peer.Tools.ImportAsync("import.xml", !partial, Session)
                .ConfigureAwait(false);

            JsonElement output = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.EqualTo(partial));
            Assert.That(output.GetProperty("serverReportedChangesApplied").GetBoolean(), Is.EqualTo(applied));
            Assert.That(output.GetProperty("requestedCompleteUpdate").GetBoolean(), Is.EqualTo(!partial));
            Assert.That(output.GetProperty("partial").GetBoolean(), Is.EqualTo(partial));
            Assert.That(output.GetProperty("results").GetArrayLength(), Is.EqualTo(2));
            Assert.That(output.GetProperty("results")[0].GetProperty("code").GetUInt32(),
                Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(output.GetProperty("results")[1].GetProperty("code").GetUInt32(),
                Is.EqualTo(partial ? StatusCodes.BadNodeIdUnknown.Code : StatusCodes.Good.Code));
            Assert.That(output.GetProperty("configurationValueCount").GetInt32(), Is.EqualTo(1));
            Assert.That(output.GetProperty("configurationValuesOmitted").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("changedNodeIds")[0].GetString(),
                Is.EqualTo("nsu=urn:test:remote-model;s=changed"));
            Assert.That(output.GetRawText(), Does.Not.Contain(Marker));
            Assert.That(peer.Model.Connections[0].Name, Is.EqualTo(applied ? "replacement" : "McpConnection"));
            Assert.That(peer.WriteCalls, Is.GreaterThan(1));
            Assert.That(peer.CommitCalls, Is.EqualTo(1));
            Assert.That(peer.RequireCompleteUpdate, Is.EqualTo(!partial));
            Assert.That(peer.CloseCalls, Is.Zero, "An acknowledged CloseAndUpdate consumes the remote handle.");
            Assert.That(peer.PendingBytes, Is.Zero);
            Assert.That(peer.OpenMode, Is.EqualTo(6));
            Assert.That(await File.ReadAllBytesAsync(peer.Path("import.xml")).ConfigureAwait(false),
                Is.EqualTo(source));
        }

        [TestCase(1)]
        [TestCase(2)]
        public async Task WriteFailureDiscardsStagingWithoutAttemptingApplyAsync(int failedWrite)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.FailWrite = failedWrite;
            await File.WriteAllBytesAsync(peer.Path("import.xml"), peer.EncodeReplacement()).ConfigureAwait(false);

            CallToolResult result = await peer.Tools.ImportAsync("import.xml", true, Session).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadCommunicationError);
            Assert.That(peer.WriteCalls, Is.EqualTo(failedWrite));
            Assert.That(peer.CommitCalls, Is.Zero);
            Assert.That(peer.CloseCalls, Is.EqualTo(1));
            Assert.That(peer.PendingBytes, Is.Zero);
            Assert.That(peer.Model.Connections[0].Name, Is.EqualTo("McpConnection"));
            Assert.That(peer.Context.Remote.Calls[^1].MethodId, Is.EqualTo(MethodIds.FileType_Close));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedCommitIsNeverRetriedEvenWhenTheRemoteAlreadyConsumedTheHandleAsync(bool consumed)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            peer.CommitFailure = true;
            peer.ConsumeOnFailure = consumed;
            await File.WriteAllBytesAsync(peer.Path("import.xml"), peer.EncodeReplacement()).ConfigureAwait(false);

            CallToolResult result = await peer.Tools.ImportAsync("import.xml", true, Session).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadTimeout);
            Assert.That(peer.CommitCalls, Is.EqualTo(1));
            Assert.That(peer.CloseCalls, Is.EqualTo(1));
            Assert.That(peer.PendingBytes, Is.Zero);
            Assert.That(peer.Model.Connections[0].Name, Is.EqualTo(consumed ? "replacement" : "McpConnection"));
            Assert.That(RemoteModelTestSession.Payload(result).TryGetProperty(
                "serverReportedChangesApplied", out _), Is.False,
                "A lost response cannot be converted into a fabricated apply verdict.");
        }

        [Test]
        public async Task CancellationAfterStagingDiscardsUsingAnIndependentCleanupTokenAsync()
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            using var cancellation = new CancellationTokenSource();
            peer.AfterWrite = cancellation.Cancel;
            await File.WriteAllBytesAsync(peer.Path("import.xml"), peer.EncodeReplacement()).ConfigureAwait(false);

            await Assert.ThatAsync(async () =>
            {
                _ = await peer.Tools.ImportAsync("import.xml", true, Session, cancellation.Token)
                    .ConfigureAwait(false);
            }, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(peer.WriteCalls, Is.EqualTo(1));
            Assert.That(peer.CommitCalls, Is.Zero);
            Assert.That(peer.CloseCalls, Is.EqualTo(1));
            Assert.That(peer.Context.Remote.CallTokens[^1].IsCancellationRequested, Is.False);
            Assert.That(peer.PendingBytes, Is.Zero);
            Assert.That(peer.Model.Connections[0].Name, Is.EqualTo("McpConnection"));
        }

        [Test]
        public async Task PendingCommitDoesNotReportCompletionOrApplyUntilAcknowledgedAsync()
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            peer.Context.Remote.Session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.Is<ArrayOf<CallMethodRequest>>(requests => requests.Count == 1 &&
                        requests[0].MethodId == MethodIds.PubSubConfigurationType_CloseAndUpdate),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                    new ValueTask<CallResponse>(CommitAsync(requests[0], token)));
            await File.WriteAllBytesAsync(peer.Path("import.xml"), peer.EncodeReplacement(), lifetime.Token)
                .ConfigureAwait(false);

            Task<CallToolResult> operation = peer.Tools.ImportAsync("import.xml", true, Session, lifetime.Token);
            CallToolResult result;
            try
            {
                await entered.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                Assert.That(peer.PendingBytes, Is.GreaterThan(16384));
                Assert.That(peer.Model.Connections[0].Name, Is.EqualTo("McpConnection"));
                Assert.That(peer.CommitCalls, Is.Zero);
            }
            finally
            {
                release.TrySetResult();
                result = await operation.ConfigureAwait(false);
            }
            Assert.That(result.IsError, Is.False);
            Assert.That(peer.Model.Connections[0].Name, Is.EqualTo("replacement"));
            Assert.That(peer.CommitCalls, Is.EqualTo(1));
            Assert.That(peer.CloseCalls, Is.Zero);

            async Task<CallResponse> CommitAsync(CallMethodRequest request, CancellationToken token)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [peer.Call(request)]
                };
            }
        }

        [TestCase("private")]
        [TestCase("unapproved-endpoint")]
        [TestCase("invalid-xml")]
        [TestCase("outside-root")]
        public async Task UnsafeImportIsRejectedBeforeAnyRemoteFileIsOpenedAsync(string kind)
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            if (kind == "private")
            {
                peer.Model.Connections[0].ConnectionProperties =
                    [new KeyValuePair { Key = new QualifiedName("credential"), Value = Variant.From(Marker) }];
            }
            if (kind == "unapproved-endpoint")
            {
                peer.Model.Connections[0].Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = "opc.udp://127.0.0.2:4849",
                    NetworkInterface = "test-interface"
                });
            }
            peer.RefreshDownload();
            byte[] source = kind == "invalid-xml" ? Encoding.UTF8.GetBytes("invalid") : peer.Download;
            await File.WriteAllBytesAsync(peer.Path("import.xml"), source).ConfigureAwait(false);

            CallToolResult result = await peer.Tools.ImportAsync(
                kind == "outside-root" ? Path.Combine("..", "import.xml") : "import.xml", true, Session)
                .ConfigureAwait(false);

            if (kind == "unapproved-endpoint")
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadUserAccessDenied);
            }
            else if (kind == "invalid-xml")
            {
                RemoteModelTestSession.Error(result, StatusCodes.BadDecodingError);
            }
            else
            {
                RemoteModelTestSession.ArgumentError(result);
            }
            Assert.That(peer.Context.Remote.Calls, Is.Empty);
            Assert.That(peer.PendingBytes, Is.Zero);
            Assert.That(await File.ReadAllBytesAsync(peer.Path("import.xml")).ConfigureAwait(false),
                Is.EqualTo(source));
        }

        [Test]
        public async Task MissingOptionalRemoteConfigurationFileDoesNotTriggerAReplacementOrGenericCallAsync()
        {
            await using FilePeer peer = await FilePeer.CreateAsync().ConfigureAwait(false);
            using IDisposable owner = peer.Context.Enter();
            Assert.That(peer.Context.Remote.Translations.Remove(
                (ObjectIds.PublishSubscribe, new QualifiedName(BrowseNames.PubSubConfiguration))), Is.True);

            CallToolResult result = await peer.Tools.ExportAsync("export.xml", Session).ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(peer.Context.Remote.Calls, Is.Empty);
            Assert.That(Directory.GetFiles(peer.Root), Is.Empty);
            Assert.That(peer.Context.Transports.Factories, Is.Zero);
        }

        private const string Session = PubSubExpansionTestContext.SessionName;
        private const string Marker = "private-configuration-marker";

        /// <summary>
        /// A strict standard FileType peer with observable read, staged-write, commit and discard state.
        /// </summary>
        private sealed class FilePeer : IAsyncDisposable
        {
            private FilePeer(PubSubExpansionTestContext context, string root)
            {
                Context = context;
                Root = root;
                PubSubApplicationInput input = PubSubExpansionTestContext.Input();
                var source = new McpPubSubDataSetSource(input, new McpPubSubOptions(),
                    context.Remote.Context, context.Clock);
                Model = McpPubSubConfiguration.Build(input, source.BuildMetaData());
                Model.PublishedDataSets[0].DataSetMetaData.Description = new LocalizedText(new string('x', 40000));
                Model.PublishedDataSets[0].DataSetMetaData.ConfigurationVersion =
                    new ConfigurationVersionDataType { MajorVersion = 17, MinorVersion = 23 };
                RefreshDownload();
                context.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
                context.Remote.Child(ObjectIds.PublishSubscribe,
                    new QualifiedName(BrowseNames.PubSubConfiguration), context.Remote.Id("configuration"));
                context.Remote.Type(context.Remote.Id("configuration"), ObjectTypeIds.PubSubConfigurationType);
                context.Remote.OnCall = Call;
            }

            internal PubSubExpansionTestContext Context { get; }
            internal string Root { get; }
            internal PubSubConfigurationTools Tools => Context.Services.GetRequiredService<PubSubConfigurationTools>();
            internal PubSubConfigurationDataType Model { get; private set; }
            internal byte[] Download { get; set; } = [];
            internal bool OversizedRead { get; set; }
            internal bool ReadFailure { get; set; }
            internal int FailWrite { get; set; }
            internal bool Apply { get; set; } = true;
            internal bool Partial { get; set; }
            internal bool CommitFailure { get; set; }
            internal bool ConsumeOnFailure { get; set; }
            internal Action? AfterWrite { get; set; }
            internal int ReadCalls { get; private set; }
            internal int WriteCalls { get; private set; }
            internal int CommitCalls { get; private set; }
            internal int CloseCalls { get; private set; }
            internal byte OpenMode { get; private set; }
            internal bool RequireCompleteUpdate { get; private set; }
            internal long PendingBytes => m_staged.Length;

            internal static async Task<FilePeer> CreateAsync(
                int configurationLimit = 1_048_576, long transferLimit = 1_048_576)
            {
                string root = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory,
                    "PubSubExpansionFiles-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                PubSubApplicationInput input = PubSubExpansionTestContext.Input();
                var options = new McpPubSubOptions
                {
                    MaxConfigurationBytes = configurationLimit,
                    ModelRules =
                    [
                        new McpPubSubModelRule
                        {
                            SessionName = Session, NodeId = ObjectIds.PublishSubscribe.ToString(),
                            Operations = ["pubsub_configuration_export", "pubsub_configuration_import"]
                        }
                    ],
                    DatagramRules =
                    [
                        new McpPubSubDatagramRule
                        {
                            Endpoint = input.Connection.Endpoint,
                            NetworkInterface = input.Connection.NetworkInterface!,
                            LocalAddress = "127.0.0.1", Operations = ["configure-publish"]
                        }
                    ]
                };
                try
                {
                    PubSubExpansionTestContext context = await PubSubExpansionTestContext.CreateAsync(
                        options: options, transferRoot: root, maxTransferBytes: transferLimit).ConfigureAwait(false);
                    return new FilePeer(context, root);
                }
                catch
                {
                    Directory.Delete(root, recursive: true);
                    throw;
                }
            }

            internal string Path(string name)
            {
                return System.IO.Path.Combine(Root, name);
            }

            internal void RefreshDownload()
            {
                Download = Encode(Model);
            }

            internal byte[] EncodeReplacement()
            {
                var replacement = (PubSubConfigurationDataType)Model.Clone();
                replacement.Connections[0].Name = "replacement";
                return Encode(replacement);
            }

            internal PubSubConfigurationDataType Decode(byte[] bytes)
            {
                using var stream = new MemoryStream(bytes);
                return UaPubSubConfigurationHelper.LoadConfiguration(stream, Context.Sessions.Telemetry);
            }

            internal CallMethodResult Call(CallMethodRequest request)
            {
                Assert.That(request.ObjectId, Is.EqualTo(Context.Remote.Id("configuration")));
                ArrayOf<Variant> input = request.InputArguments;
                if (request.MethodId == MethodIds.FileType_Open)
                {
                    Assert.That(input[0].TryGetValue(out byte mode), Is.True);
                    Assert.That(mode, Is.AnyOf((byte)1, (byte)6));
                    Assert.That(m_open, Is.False);
                    OpenMode = mode;
                    m_open = true;
                    m_position = 0;
                    m_staged.SetLength(0);
                    return Good(Variant.From(41u));
                }
                Assert.That(input[0].TryGetValue(out uint handle), Is.True);
                Assert.That(handle, Is.EqualTo(41u));
                if (request.MethodId == MethodIds.FileType_Close)
                {
                    CloseCalls++;
                    bool wasOpen = m_open;
                    m_open = false;
                    m_staged.SetLength(0);
                    return new CallMethodResult
                    {
                        StatusCode = wasOpen ? StatusCodes.Good : StatusCodes.BadInvalidState
                    };
                }
                Assert.That(m_open, Is.True, "Every operation must use a live handle owned by this file peer.");
                if (request.MethodId == MethodIds.FileType_Read)
                {
                    ReadCalls++;
                    Assert.That(OpenMode, Is.EqualTo(1));
                    Assert.That(input[1].TryGetValue(out int requested), Is.True);
                    Assert.That(requested, Is.InRange(1, 16384));
                    if (ReadFailure)
                    {
                        return new CallMethodResult { StatusCode = StatusCodes.BadCommunicationError };
                    }
                    int length = Math.Min(requested, Download.Length - m_position);
                    byte[] block = OversizedRead ? new byte[requested + 1] :
                        Download.AsSpan(m_position, length).ToArray();
                    m_position += length;
                    return Good(Variant.From(ByteString.From(block)));
                }
                if (request.MethodId == MethodIds.FileType_Write)
                {
                    WriteCalls++;
                    Assert.That(OpenMode, Is.EqualTo(6));
                    Assert.That(input[1].TryGetValue(out ByteString block), Is.True);
                    Assert.That(block.Length, Is.InRange(1, 16384));
                    if (WriteCalls == FailWrite)
                    {
                        return new CallMethodResult { StatusCode = StatusCodes.BadCommunicationError };
                    }
                    m_staged.Write(block.Span);
                    AfterWrite?.Invoke();
                    return Good();
                }
                Assert.That(request.MethodId, Is.EqualTo(MethodIds.PubSubConfigurationType_CloseAndUpdate));
                Assert.That(input.Count, Is.EqualTo(3));
                Assert.That(input[1].TryGetValue(out bool complete), Is.True);
                RequireCompleteUpdate = complete;
                Assert.That(input[2].TryGetValue(out ArrayOf<ExtensionObject> references), Is.True);
                Assert.That(references, Is.Empty);
                CommitCalls++;
                if (!CommitFailure || ConsumeOnFailure)
                {
                    if (Apply)
                    {
                        Model = Decode(m_staged.ToArray());
                    }
                    m_staged.SetLength(0);
                    m_open = false;
                }
                if (CommitFailure)
                {
                    return new CallMethodResult { StatusCode = StatusCodes.BadTimeout };
                }
                ArrayOf<StatusCode> statuses =
                    [StatusCodes.Good, Partial ? StatusCodes.BadNodeIdUnknown : StatusCodes.Good];
                ArrayOf<ExtensionObject> values =
                [
                    new ExtensionObject(new PubSubConfigurationValueDataType
                    {
                        Name = "private", Identifier = Variant.From(Marker)
                    })
                ];
                ArrayOf<NodeId> nodes = [Context.Remote.Id("changed")];
                return Good(Variant.From(Apply), Variant.From(statuses), Variant.From(values), Variant.From(nodes));
            }

            public async ValueTask DisposeAsync()
            {
                m_staged.Dispose();
                await Context.DisposeAsync().ConfigureAwait(false);
                Directory.Delete(Root, recursive: true);
            }

            private byte[] Encode(PubSubConfigurationDataType model)
            {
                using var stream = new MemoryStream();
                UaPubSubConfigurationHelper.SaveConfiguration(model, stream, Context.Sessions.Telemetry);
                return stream.ToArray();
            }

            private static CallMethodResult Good(params Variant[] values)
            {
                return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = values.ToArrayOf() };
            }

            private readonly MemoryStream m_staged = new();
            private int m_position;
            private bool m_open;
        }
    }
}
#endif
