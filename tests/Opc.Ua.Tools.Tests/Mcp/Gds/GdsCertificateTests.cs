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
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Gds;
using Opc.Ua.Mcp;
using GdsMethods = Opc.Ua.Gds.MethodIds;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Verifies certificate/public-artifact boundaries and session/caller-owned push transactions.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class GdsCertificateTests
    {
        [Test]
        public async Task PullMetadataPreservesGroupsDefaultSlotBooleanAndTrustListNodeAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            GdsCertificateTools tools = Tools(fixture);
            string directory = fixture.Remote.Id("directory").ToString();
            string application = fixture.Remote.Id("app").ToString();
            fixture.Remote.Reply(StatusCodes.Good,
                Variant.From([fixture.Remote.Id("group-a"), fixture.Remote.Id("group-b")]));
            CallToolResult groups = await tools.GetGroupsAsync(
                directory, application, offset: 1, maxResults: 1, sessionName: "selected").ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(false));
            CallToolResult status = await tools.GetStatusAsync(
                directory, application, ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(),
                sessionName: "selected").ConfigureAwait(false);
            fixture.Remote.Reply(StatusCodes.Good, Variant.From(fixture.Remote.Id("trust-list")));
            CallToolResult trustList = await tools.GetTrustListAsync(
                directory, application, sessionName: "selected").ConfigureAwait(false);

            Assert.That(
                RemoteModelTestSession.Payload(groups).GetProperty("groups").GetProperty("items")[0].GetString(),
                Is.EqualTo(fixture.Remote.Id("group-b").ToString()));
            Assert.That(RemoteModelTestSession.Payload(status).GetProperty("updateRequired").GetBoolean(), Is.False);
            Assert.That(RemoteModelTestSession.Payload(trustList).GetProperty("trustListId").GetString(),
                Is.EqualTo(fixture.Remote.Id("trust-list").ToString()));
            Assert.That(fixture.Remote.Calls[1].InputArguments[1], Is.EqualTo(Variant.From(NodeId.Null)));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task PullFinishPublishesOnlyPublicChainAndProtectsApplicationBoundPrivateKeyAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            ByteString certificate = CreateCertificate(fixture);
            string issuer = McpSecretReferences.GetResourceBinding(
                fixture.Manager.GetSessionInfoOrThrow("selected"), fixture.Remote.Id("directory"));
            SetPullReplies(fixture, certificate, "private-key-never-in-artifact");

            CallToolResult result = await Tools(fixture).FinishRequestAsync(
                fixture.Remote.Id("directory").ToString(), fixture.Remote.Id("app").ToString(),
                fixture.Remote.Id("request").ToString(), "issued-chain.json", "selected",
                certificatePath: "issued-leaf.der").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("resource").GetString(), Is.EqualTo("opcua-app:urn:test:gds-server"));
            Assert.That(json.GetProperty("requestId").GetString(), Is.EqualTo(fixture.Remote.Id("request").ToString()));
            string reference = json.GetProperty("privateKey").GetProperty("reference").GetString()!;
            Assert.That(json.GetProperty("privateKey").GetProperty("issuer").GetString(), Is.EqualTo(issuer));
            using ISecret secret = await fixture.Secrets.ResolveAsync(
                reference, "opcua-app:urn:test:gds-server", McpCapability.CertificateAdministration)
                .ConfigureAwait(false);
            Assert.That(Encoding.UTF8.GetString(secret.Bytes), Is.EqualTo("private-key-never-in-artifact"));
            string artifact = await File.ReadAllTextAsync(Path.Combine(fixture.Root!, "issued-chain.json"))
                .ConfigureAwait(false);
            using var bundle = JsonDocument.Parse(artifact);
            Assert.That(bundle.RootElement.GetProperty("certificates").GetArrayLength(), Is.EqualTo(1));
            Assert.That(bundle.RootElement.GetProperty("certificates")[0].GetBytesFromBase64(),
                Is.EqualTo(certificate.ToArray()));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root!, "issued-leaf.der"))
                .ConfigureAwait(false), Is.EqualTo(certificate.ToArray()));
            Assert.That(artifact, Does.Not.Contain("private-key-never-in-artifact"));
            Assert.That(json.GetRawText(), Does.Not.Contain("private-key-never-in-artifact"));
            Assert.That(fixture.Remote.Calls.Select(call => call.MethodId), Is.EqualTo(
            [
                fixture.Remote.Model(GdsMethods.DirectoryType_GetApplication),
                fixture.Remote.Model(GdsMethods.CertificateDirectoryType_FinishRequest)
            ]));
        }

        [Test]
        public async Task PullFinishReportsStorageFailureWithoutLosingRequestOrPublicCertificateAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            SetPullReplies(fixture, CreateCertificate(fixture), "private-key-sensitive");
            fixture.Store.SetFailure = new IOException("private-key-sensitive");

            CallToolResult result = await Tools(fixture).FinishRequestAsync(
                fixture.Remote.Id("directory").ToString(), fixture.Remote.Id("app").ToString(),
                fixture.Remote.Id("request").ToString(), "public.json", "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(), Is.EqualTo(StatusCodes.Good.Code));
            Assert.That(json.GetProperty("requestId").GetString(), Is.EqualTo(fixture.Remote.Id("request").ToString()));
            Assert.That(json.GetProperty("remoteIssuance").GetString(), Is.EqualTo("issued"));
            Assert.That(json.GetProperty("privateKeyPersistence").GetProperty("localPersistence").GetString(),
                Is.EqualTo("failed"));
            Assert.That(File.Exists(Path.Combine(fixture.Root!, "public.json")), Is.True);
            Assert.That(json.GetProperty("certificate").GetProperty("sha256").GetString(), Has.Length.EqualTo(64));
            Assert.That(json.GetRawText(), Does.Not.Contain("private-key-sensitive"));
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task CertificateReadsPreserveAlignedTypePairsAndOnlyExportTheSelectedPageAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            ByteString certificate = CreateCertificate(fixture);
            fixture.Remote.Reply(StatusCodes.Good,
                Variant.From([new NodeId(12560), new NodeId(12561)]),
                Variant.From([certificate, certificate]));

            CallToolResult result = await Tools(fixture).GetCertificatesAsync(
                fixture.Remote.Id("configuration").ToString(), GdsCertificateSource.Server,
                artifactPath: "page.json", offset: 1, maxResults: 1, sessionName: "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(json.GetProperty("certificates").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("certificates")[0].GetProperty("certificateTypeId").GetString(),
                Is.EqualTo("i=12561"));
            Assert.That(json.GetProperty("totalCount").GetInt32(), Is.EqualTo(2));
            Assert.That(json.GetProperty("nextOffset").ValueKind, Is.EqualTo(JsonValueKind.Null));
            using var bundle = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(fixture.Root!, "page.json")).ConfigureAwait(false));
            Assert.That(bundle.RootElement.GetProperty("certificates").GetArrayLength(), Is.EqualTo(1));
            fixture.Remote.AssertCall(fixture.Remote.Id("configuration"),
                MethodIds.ServerConfigurationType_GetCertificates, Variant.From(NodeId.Null));
        }

        [Test]
        public async Task CertificateReadRejectsMismatchedTypeAndCertificateArraysAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Reply(StatusCodes.Good,
                Variant.From([new NodeId(12560)]), Variant.From(ArrayOf<ByteString>.Empty));

            CallToolResult result = await Tools(fixture).GetCertificatesAsync(
                fixture.Remote.Id("configuration").ToString(), GdsCertificateSource.Server, sessionName: "selected")
                .ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadDecodingError);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PrivateKeyContainerCannotEnterThePublicCertificateUploadPathAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Private container", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddDays(1));
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "not-public.der"),
                certificate.Export(X509ContentType.Pfx)).ConfigureAwait(false);

            CallToolResult result = await Tools(fixture).CheckRevocationAsync(
                fixture.Remote.Id("directory").ToString(), "not-public.der", "selected").ConfigureAwait(false);

            Assert.That(result.IsError, Is.True);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(fixture.Store.SetTokens, Is.Empty);
        }

        [Test]
        public async Task PushStagingAndApplyAreCallerOwnedAndLateDisconnectDoesNotHideCommitAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            string root = fixture.Remote.Id("configuration").ToString();
            fixture.Remote.Child(fixture.Remote.Id("configuration"), new QualifiedName("DeleteCertificate"),
                fixture.Remote.Id("delete-method"));
            fixture.Remote.Reply(StatusCodes.Good);
            using (fixture.Enter())
            {
                CallToolResult staged = await Tools(fixture).StageDeleteAsync(
                    root, ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(), sessionName: "selected")
                    .ConfigureAwait(false);
                Assert.That(staged.IsError, Is.False);
                Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            }
            using (fixture.Enter(GdsTestContext.Other))
            {
                CallToolResult denied = await Tools(fixture).ApplyChangesAsync(root, "selected").ConfigureAwait(false);
                RemoteModelTestSession.Error(denied, StatusCodes.BadUserAccessDenied);
                Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(1));
            }
            using IDisposable caller = fixture.Enter();
            using var cancellation = new CancellationTokenSource();
            NodeId original = fixture.Remote.Session.Object.SessionId;
            fixture.Remote.OnCall = _ =>
            {
                fixture.Connection.Disconnect();
                cancellation.Cancel();
                return new CallMethodResult { StatusCode = StatusCodes.GoodResultsMayBeIncomplete };
            };

            CallToolResult applied = await Tools(fixture).ApplyChangesAsync(root, "selected", cancellation.Token)
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(applied);
            Assert.That(applied.IsError, Is.False);
            Assert.That(json.GetProperty("statusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.GoodResultsMayBeIncomplete.Code));
            Assert.That(json.GetProperty("sessionId").GetString(), Is.EqualTo(original.ToString()));
            Assert.That(json.GetProperty("reconnectInvoked").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(2));
            Assert.That(fixture.Remote.Calls[1].MethodId,
                Is.EqualTo(fixture.Remote.Model(MethodIds.ServerConfigurationType_ApplyChanges)));
        }

        [Test]
        public async Task OptionalSelfSignedMethodIsNotDispatchedWhenNotAdvertisedAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();

            CallToolResult result = await Tools(fixture).CreateSelfSignedAsync(
                fixture.Remote.Id("configuration").ToString(), new GdsSelfSignedInput
                {
                    CertificateTypeId = ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(),
                    SubjectName = "CN=Requested"
                }, "self.der", "selected").ConfigureAwait(false);

            RemoteModelTestSession.Error(result, StatusCodes.BadNotSupported);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(Directory.GetFiles(fixture.Root!), Is.Empty);
        }

        [Test]
        public async Task CancelChangesIsExplicitAndReleasesOnlyTheMatchingOwnedTransactionAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync().ConfigureAwait(false);
            string root = fixture.Remote.Id("configuration").ToString();
            fixture.Remote.Child(fixture.Remote.Id("configuration"), new QualifiedName("DeleteCertificate"),
                fixture.Remote.Id("delete-method"));
            fixture.Remote.Child(fixture.Remote.Id("configuration"), new QualifiedName("CancelChanges"),
                fixture.Remote.Id("cancel-method"));
            fixture.Remote.Reply(StatusCodes.Good);
            using (fixture.Enter())
            {
                await Tools(fixture).StageDeleteAsync(
                    root, ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(), sessionName: "selected")
                    .ConfigureAwait(false);
                CallToolResult cancelled = await Tools(fixture).CancelChangesAsync(root, "selected")
                    .ConfigureAwait(false);
                Assert.That(cancelled.IsError, Is.False);
            }
            using (fixture.Enter(GdsTestContext.Other))
            {
                CallToolResult next = await Tools(fixture).StageDeleteAsync(
                    root, ObjectTypeIds.RsaSha256ApplicationCertificateType.ToString(), sessionName: "selected")
                    .ConfigureAwait(false);
                Assert.That(next.IsError, Is.False);
            }
            Assert.That(fixture.Remote.Calls.Select(call => call.MethodId), Is.EqualTo(
            [
                fixture.Remote.Model(MethodIds.ServerConfigurationType_DeleteCertificate),
                fixture.Remote.Model(MethodIds.ServerConfigurationType_CancelChanges),
                fixture.Remote.Model(MethodIds.ServerConfigurationType_DeleteCertificate)
            ]));
        }

        [Test]
        public async Task TrustListDownloadConsumesShortReadsAndClosesTheOwnedHandleAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("trust-list"), ObjectTypeIds.TrustListType);
            byte[] encoded = EncodeTrustList(fixture);
            int reads = 0;
            fixture.Remote.OnCall = call =>
            {
                if (call.MethodId == fixture.Remote.Model(MethodIds.TrustListType_OpenWithMasks))
                {
                    return Reply(Variant.From(42u));
                }
                if (call.MethodId == fixture.Remote.Model(MethodIds.FileType_Read))
                {
                    Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(42u)));
                    ByteString bytes = reads++ switch
                    {
                        0 => ByteString.From(encoded.AsSpan(0, 3).ToArray()),
                        1 => ByteString.From(encoded.AsSpan(3).ToArray()),
                        _ => default
                    };
                    return Reply(Variant.From(bytes));
                }
                Assert.That(call.MethodId, Is.EqualTo(fixture.Remote.Model(MethodIds.FileType_Close)));
                return Reply();
            };

            CallToolResult result = await Tools(fixture).DownloadTrustListAsync(
                fixture.Remote.Id("trust-list").ToString(), "trust-list.bin", sessionName: "selected")
                .ConfigureAwait(false);

            Assert.That(result.IsError, Is.False);
            Assert.That(reads, Is.EqualTo(3), "A short Read is not EOF.");
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(fixture.Root!, "trust-list.bin"))
                .ConfigureAwait(false),
                Is.EqualTo(encoded));
            Assert.That(fixture.Remote.Calls[^1].MethodId,
                Is.EqualTo(fixture.Remote.Model(MethodIds.FileType_Close)));
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain("fileHandle"));
        }

        [Test]
        public async Task TrustListReadAndCloseFailuresAreBothVisibleWithoutPublishingPartialFileAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Type(fixture.Remote.Id("trust-list"), ObjectTypeIds.TrustListType);
            fixture.Remote.OnCall = call => call.MethodId == fixture.Remote.Model(MethodIds.TrustListType_OpenWithMasks)
                ? Reply(Variant.From(42u)) : new CallMethodResult
                {
                    StatusCode = call.MethodId == fixture.Remote.Model(MethodIds.FileType_Read)
                        ? StatusCodes.BadOutOfService : StatusCodes.BadSessionIdInvalid
                };

            CallToolResult result = await Tools(fixture).DownloadTrustListAsync(
                fixture.Remote.Id("trust-list").ToString(), "partial.bin", sessionName: "selected")
                .ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.True);
            Assert.That(json.GetProperty("readStatusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadOutOfService.Code));
            Assert.That(json.GetProperty("closeStatusCodeValue").GetUInt32(),
                Is.EqualTo(StatusCodes.BadSessionIdInvalid.Code));
            Assert.That(File.Exists(Path.Combine(fixture.Root!, "partial.bin")), Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task TrustListStagingUsesVerifiedAncestryAndCloseAndUpdateWithoutApplyingAsync()
        {
            await using GdsTestContext fixture = await GdsTestContext.CreateAsync(files: true).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            fixture.Remote.Browse(fixture.Remote.Id("configuration"), ReferenceTypeIds.HierarchicalReferences,
                [fixture.Remote.Reference(fixture.Remote.Id("trust-list"), ObjectTypeIds.TrustListType)]);
            byte[] encoded = EncodeTrustList(fixture);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Root!, "staging.bin"), encoded).ConfigureAwait(false);
            fixture.Remote.OnCall = call =>
            {
                if (call.MethodId == fixture.Remote.Model(MethodIds.FileType_Open))
                {
                    return Reply(Variant.From(17u));
                }
                if (call.MethodId == fixture.Remote.Model(MethodIds.FileType_Write))
                {
                    Assert.That(call.InputArguments[1].TryGetValue(out ByteString actual), Is.True);
                    Assert.That(actual.ToArray(), Is.EqualTo(encoded));
                    return Reply();
                }
                Assert.That(call.MethodId, Is.EqualTo(fixture.Remote.Model(MethodIds.TrustListType_CloseAndUpdate)));
                return Reply(Variant.From(true));
            };

            CallToolResult result = await Tools(fixture).StageTrustListAsync(
                fixture.Remote.Id("configuration").ToString(), fixture.Remote.Id("trust-list").ToString(),
                "staging.bin", "selected").ConfigureAwait(false);

            JsonElement json = RemoteModelTestSession.Payload(result);
            Assert.That(result.IsError, Is.False);
            Assert.That(json.GetProperty("applyChangesRequired").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("applyInvoked").GetBoolean(), Is.False);
            Assert.That(fixture.Remote.Calls, Has.Count.EqualTo(3));
            Assert.That(fixture.Remote.Calls.Select(call => call.ObjectId),
                Is.All.EqualTo(fixture.Remote.Id("trust-list")));
        }

        /// <summary>
        /// Gets the production certificate tools through DI.
        /// </summary>
        private static GdsCertificateTools Tools(GdsTestContext fixture)
        {
            return fixture.Services.GetRequiredService<GdsCertificateTools>();
        }

        /// <summary>
        /// Creates runtime-only certificate material; no certificate or private key is committed to the repository.
        /// </summary>
        private static ByteString CreateCertificate(GdsTestContext fixture)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=GDS MCP public test", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddDays(1));
            return ByteString.From(certificate.Export(X509ContentType.Cert));
        }

        /// <summary>
        /// Replies to the pre-issuance application read and the single explicit FinishRequest.
        /// </summary>
        private static void SetPullReplies(GdsTestContext fixture, ByteString certificate, string privateKey)
        {
            fixture.Remote.OnCall = call =>
                call.MethodId == fixture.Remote.Model(GdsMethods.DirectoryType_GetApplication)
                    ? Reply(Variant.FromStructure(new ApplicationRecordDataType
                    {
                        ApplicationId = fixture.Remote.Id("app"),
                        ApplicationUri = "urn:test:gds-server"
                    }))
                    : Reply(Variant.From(certificate),
                        Variant.From(ByteString.From(Encoding.UTF8.GetBytes(privateKey))),
                        Variant.From(ArrayOf<ByteString>.Empty));
        }

        /// <summary>
        /// Serializes a standard public empty trust list with deterministic mask and array encodings.
        /// </summary>
        private static byte[] EncodeTrustList(GdsTestContext fixture)
        {
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, fixture.Remote.Context, leaveOpen: true))
            {
                new TrustListDataType
                {
                    SpecifiedLists = 15,
                    TrustedCertificates = [],
                    IssuerCertificates = [],
                    TrustedCrls = [],
                    IssuerCrls = []
                }.Encode(encoder);
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Creates a standard Good method envelope.
        /// </summary>
        private static CallMethodResult Reply(params Variant[] outputs)
        {
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs.ToArrayOf() };
        }
    }
}
#endif
