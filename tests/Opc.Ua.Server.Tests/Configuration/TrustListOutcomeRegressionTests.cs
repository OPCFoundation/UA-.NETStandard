/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Tests.FileSystem;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("TrustList")]
    public sealed class TrustListOutcomeRegressionTests
    {
        [TestCase("read", true)]
        [TestCase("write", true)]
        [TestCase("close", true)]
        [TestCase("update", true)]
        [TestCase("read", false)]
        [TestCase("write", false)]
        [TestCase("close", false)]
        [TestCase("update", false)]
        public async Task ClosedHandleIsInvalidWhileAnotherSessionsLiveHandleRemainsProtectedAsync(
            string operation, bool closed)
        {
            using var harness = new TrustHarness();
            uint handle = await harness.OpenAsync(operation is "write" or "update").ConfigureAwait(false);
            if (closed)
            {
                CloseMethodStateResult close = await harness.Node.Close!.OnCallAsync!(
                    harness.Context, harness.Node.Close, harness.Node.NodeId, handle, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(close.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            SessionSystemContext context = closed ? harness.Context : harness.CreateContext(new NodeId(200, 1));
            MethodState method = operation switch
            {
                "read" => harness.Node.Read!,
                "write" => harness.Node.Write!,
                "close" => harness.Node.Close!,
                _ => harness.Node.CloseAndUpdate!
            };
            ArrayOf<Variant> arguments = operation switch
            {
                "read" => [handle, 1],
                "write" => [handle, ByteString.From([1])],
                _ => [handle]
            };
            (ServiceResult result, List<Variant> output) = await FileReadRegressionTests.CallAsync(
                method, context, harness.Node.NodeId, arguments).ConfigureAwait(false);
            Assert.That(result.StatusCode,
                Is.EqualTo(closed ? StatusCodes.BadInvalidArgument : StatusCodes.BadUserAccessDenied));
            Assert.That(output, Is.Empty);
            Assert.That(harness.Node.OpenCount!.Value, Is.EqualTo(closed ? 0 : 1));
        }

        [TestCase("decode")]
        [TestCase("store")]
        [TestCase("cancel")]
        [TestCase("success")]
        public async Task FailedCloseAndUpdateDoesNotAdvanceLastUpdateTimeAsync(string outcome)
        {
            using var harness = new TrustHarness();
            DateTimeUtc original = harness.Node.LastUpdateTime!.Value;
            uint handle = await harness.OpenAsync(write: true).ConfigureAwait(false);
            using Certificate certificate = DefaultCertificateFactory.Instance
                .CreateCertificate("CN=TrustList Outcome Regression").CreateForRSA();
            ByteString payload = outcome == "decode"
                ? ByteString.From([1, 2, 3])
                : harness.Encode(new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = outcome == "store" ? [certificate.RawData.ToByteString()] : []
                });
            WriteMethodStateResult written = await harness.Node.Write!.OnCallAsync!(
                harness.Context, harness.Node.Write, harness.Node.NodeId, handle, payload, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(written.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            if (outcome == "store")
            {
                using var writer = new StreamWriter(harness.TrustedPath);
                await writer.WriteAsync("not a directory").ConfigureAwait(false);
            }
            using var cancellation = new CancellationTokenSource();
            if (outcome == "cancel")
            {
                cancellation.Cancel();
            }
            CloseAndUpdateMethodStateResult result = await harness.Node.CloseAndUpdate!.OnCallAsync!(
                harness.Context, harness.Node.CloseAndUpdate, harness.Node.NodeId, handle, cancellation.Token)
                .ConfigureAwait(false);
            if (outcome == "success")
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(harness.Node.LastUpdateTime.Value, Is.GreaterThan(original));
            }
            else
            {
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
                Assert.That(harness.Node.LastUpdateTime.Value, Is.EqualTo(original));
            }
            Assert.That(harness.Node.OpenCount!.Value, Is.Zero);
            Assert.That(result.ApplyChangesRequired, Is.False);
        }

        [TestCase("replace")]
        [TestCase("add")]
        [TestCase("remove")]
        public async Task RolledBackTrustListCommitRestoresItsLastSuccessfulUpdateTimeAsync(string operation)
        {
            using var harness = new TrustHarness(transactional: true);
            DateTimeUtc original = harness.Node.LastUpdateTime!.Value;
            using Certificate originalCertificate = DefaultCertificateFactory.Instance
                .CreateCertificate("CN=Original TrustList Certificate").CreateForRSA();
            using Certificate replacement = DefaultCertificateFactory.Instance
                .CreateCertificate("CN=Replacement TrustList Certificate").CreateForRSA();
            using ICertificateStore store = new CertificateStoreIdentifier(harness.TrustedPath)
                .OpenStore(harness.Context.Telemetry);
            if (operation != "add")
            {
                await store.AddAsync(originalCertificate).ConfigureAwait(false);
            }
            if (operation == "replace")
            {
                uint handle = await harness.OpenAsync(write: true).ConfigureAwait(false);
                ByteString payload = harness.Encode(new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.TrustedCertificates,
                    TrustedCertificates = [replacement.RawData.ToByteString()]
                });
                await harness.Node.Write!.OnCallAsync!(
                    harness.Context, harness.Node.Write, harness.Node.NodeId, handle, payload, CancellationToken.None)
                    .ConfigureAwait(false);
                CloseAndUpdateMethodStateResult staged = await harness.Node.CloseAndUpdate!.OnCallAsync!(
                    harness.Context, harness.Node.CloseAndUpdate, harness.Node.NodeId, handle, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(staged.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(staged.ApplyChangesRequired, Is.True);
            }
            else if (operation == "add")
            {
                AddCertificateMethodStateResult staged = await harness.Node.AddCertificate!.OnCallAsync!(
                    harness.Context, harness.Node.AddCertificate, harness.Node.NodeId,
                    replacement.RawData.ToByteString(), true, CancellationToken.None).ConfigureAwait(false);
                Assert.That(staged.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                RemoveCertificateMethodStateResult staged = await harness.Node.RemoveCertificate!.OnCallAsync!(
                    harness.Context, harness.Node.RemoveCertificate, harness.Node.NodeId,
                    originalCertificate.Thumbprint, true, CancellationToken.None).ConfigureAwait(false);
                Assert.That(staged.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(harness.Node.LastUpdateTime.Value, Is.EqualTo(original));
            harness.Coordinator!.Stage(s_sessionId, new PushConfigurationOperation
            {
                AffectedTrustList = new NodeId("later-operation", 1),
                CommitAsync = _ => throw new ServiceResultException(StatusCodes.BadInvalidState)
            });
            ServiceResult result = await harness.Coordinator.ApplyChangesAsync(s_sessionId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(harness.Node.LastUpdateTime.Value, Is.EqualTo(original));
            using CertificateCollection originalRetained = await store.FindByThumbprintAsync(originalCertificate.Thumbprint)
                .ConfigureAwait(false);
            using CertificateCollection replacementAbsent = await store.FindByThumbprintAsync(replacement.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(originalRetained, Has.Count.EqualTo(operation == "add" ? 0 : 1));
            Assert.That(replacementAbsent, Is.Empty);
        }

        [Test]
        public async Task CancellationDuringPartialTrustListCommitStillRestoresAllStoresAsync()
        {
            using var harness = new TrustHarness(transactional: true);
            using var request = new CancellationTokenSource();
            using Certificate original = DefaultCertificateFactory.Instance
                .CreateCertificate("CN=Original Partial TrustList").CreateForRSA();
            using Certificate replacement = DefaultCertificateFactory.Instance
                .CreateCertificate("CN=Replacement Partial TrustList").CreateForRSA();
            using ICertificateStore issuer = new CertificateStoreIdentifier(harness.IssuerPath)
                .OpenStore(harness.Context.Telemetry);
            using ICertificateStore trusted = new CertificateStoreIdentifier(harness.TrustedPath)
                .OpenStore(harness.Context.Telemetry);
            await issuer.AddAsync(original).ConfigureAwait(false);
            await trusted.AddAsync(original).ConfigureAwait(false);
            var failing = new Mock<ICertificateStore>();
            failing.Setup(store => store.EnumerateAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) => trusted.EnumerateAsync(ct));
            failing.Setup(store => store.EnumerateCRLsAsync(It.IsAny<CancellationToken>()))
                .Returns((CancellationToken ct) => trusted.EnumerateCRLsAsync(ct));
            failing.Setup(store => store.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string thumbprint, CancellationToken ct) => trusted.DeleteAsync(thumbprint, ct));
            failing.Setup(store => store.AddAsync(
                    It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()))
                .Returns((Certificate certificate, char[]? password, CancellationToken ct) =>
                {
                    if (certificate.Thumbprint == replacement.Thumbprint)
                    {
                        request.Cancel();
                        ct.ThrowIfCancellationRequested();
                    }
                    return trusted.AddAsync(certificate, password, ct);
                });
            harness.UseTrustedStore(failing.Object);
            DateTimeUtc lastUpdate = harness.Node.LastUpdateTime!.Value;
            uint handle = await harness.OpenAsync(write: true).ConfigureAwait(false);
            ByteString payload = harness.Encode(new TrustListDataType
            {
                SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.IssuerCertificates),
                TrustedCertificates = [replacement.RawData.ToByteString()],
                IssuerCertificates = [replacement.RawData.ToByteString()]
            });
            await harness.Node.Write!.OnCallAsync!(
                harness.Context, harness.Node.Write, harness.Node.NodeId, handle, payload, CancellationToken.None)
                .ConfigureAwait(false);
            CloseAndUpdateMethodStateResult staged = await harness.Node.CloseAndUpdate!.OnCallAsync!(
                harness.Context, harness.Node.CloseAndUpdate, harness.Node.NodeId, handle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(staged.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
            ServiceResult result = await harness.Coordinator!.ApplyChangesAsync(s_sessionId, request.Token)
                .ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
            using CertificateCollection issuerOriginal = await issuer.FindByThumbprintAsync(original.Thumbprint)
                .ConfigureAwait(false);
            using CertificateCollection issuerReplacement = await issuer.FindByThumbprintAsync(replacement.Thumbprint)
                .ConfigureAwait(false);
            using CertificateCollection trustedOriginal = await trusted.FindByThumbprintAsync(original.Thumbprint)
                .ConfigureAwait(false);
            using CertificateCollection trustedReplacement = await trusted.FindByThumbprintAsync(replacement.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(issuerOriginal, Has.Count.EqualTo(1));
            Assert.That(trustedOriginal, Has.Count.EqualTo(1));
            Assert.That(issuerReplacement, Is.Empty);
            Assert.That(trustedReplacement, Is.Empty);
            Assert.That(harness.Node.LastUpdateTime.Value, Is.EqualTo(lastUpdate));
        }

        private sealed class TrustHarness : IDisposable
        {
            public TrustHarness(bool transactional = false)
            {
                m_path = Path.Combine(Path.GetTempPath(), "TrustListOutcome-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(m_path);
                m_telemetry = NUnitTelemetryContext.Create();
                Context = CreateContext(s_sessionId);
                Node = new TrustListState(null) { NodeId = new NodeId("trust-list-outcome", 1) };
                Node.CreateOrReplaceOpen(Context, Node);
                Node.CreateOrReplaceClose(Context, Node);
                Node.CreateOrReplaceRead(Context, Node);
                Node.CreateOrReplaceWrite(Context, Node);
                Node.CreateOrReplaceOpenCount(Context, Node);
                Node.CreateOrReplaceOpenWithMasks(Context, Node);
                Node.CreateOrReplaceCloseAndUpdate(Context, Node);
                Node.CreateOrReplaceAddCertificate(Context, Node);
                Node.CreateOrReplaceRemoveCertificate(Context, Node);
                Node.CreateOrReplaceLastUpdateTime(Context, Node);
                InitializeMethod(Node.Read!, MethodIds.FileType_Read, BrowseNames.Read);
                InitializeMethod(Node.Write!, MethodIds.FileType_Write, BrowseNames.Write);
                InitializeMethod(Node.Close!, MethodIds.FileType_Close, BrowseNames.Close);
                InitializeMethod(
                    Node.CloseAndUpdate!, MethodIds.TrustListType_CloseAndUpdate, BrowseNames.CloseAndUpdate);
                Node.LastUpdateTime!.Value = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                Coordinator = transactional ? new PushConfigurationTransactionCoordinator(m_telemetry) : null;
                m_trustList = new TrustList(
                    Node,
                    new CertificateStoreIdentifier(TrustedPath),
                    new CertificateStoreIdentifier(IssuerPath),
                    static (_, _) => { },
                    static (_, _) => { },
                    m_telemetry,
                    Coordinator,
                    maxTrustListSize: 8192);
            }

            public TrustListState Node { get; }
            public SessionSystemContext Context { get; }
            public PushConfigurationTransactionCoordinator? Coordinator { get; }
            public string TrustedPath => Path.Combine(m_path, "trusted");
            public string IssuerPath => Path.Combine(m_path, "issuers");

            public void UseTrustedStore(ICertificateStore store)
            {
                typeof(TrustList).GetField("m_trustedStoreInstance", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(m_trustList, store);
            }

            public SessionSystemContext CreateContext(NodeId sessionId)
            {
                var namespaceUris = new NamespaceTable();
                return new SessionSystemContext(m_telemetry)
                {
                    SessionId = sessionId,
                    NamespaceUris = namespaceUris,
                    ServerUris = new StringTable(),
                    TypeTable = new TypeTable(namespaceUris),
                    EncodeableFactory = EncodeableFactory.Create()
                };
            }

            public async ValueTask<uint> OpenAsync(bool write)
            {
                OpenMethodStateResult result = await Node.Open!.OnCallAsync!(
                    Context, Node.Open, Node.NodeId, write ? (byte)6 : (byte)1, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.Good));
                return result.FileHandle;
            }

            public ByteString Encode(TrustListDataType payload)
            {
                var messageContext = new ServiceMessageContext(Context.Telemetry, Context.EncodeableFactory)
                {
                    NamespaceUris = Context.NamespaceUris,
                    ServerUris = Context.ServerUris
                };
                using var encoder = new BinaryEncoder(messageContext);
                encoder.WriteEncodeable(null, payload);
                return encoder.CloseAndReturnBuffer().ToByteString();
            }

            public void Dispose()
            {
                Coordinator?.CancelChanges(s_sessionId);
                m_trustList.Dispose();
                Directory.Delete(m_path, recursive: true);
            }

            private void InitializeMethod(MethodState method, NodeId declarationId, string name)
            {
                method.Create(Context, declarationId, new QualifiedName(name), new LocalizedText(name), false);
                method.MethodDeclarationId = declarationId;
            }

            private readonly string m_path;
            private readonly ITelemetryContext m_telemetry;
            private readonly TrustList m_trustList;
        }

        private static readonly NodeId s_sessionId = new(100, 1);
    }
}
