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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that exercise the method handlers of <see cref="TrustList"/> (Open,
    /// OpenWithMasks, Read, Write, Close, CloseAndUpdate, AddCertificate,
    /// RemoveCertificate) for both the sync <c>OnCall</c> and async
    /// <c>OnCallAsync</c> dispatch paths, including access-validation failures,
    /// session/file-handle validation and the trust-list size/masking logic.
    /// Certificate stores are created in a unique temporary directory per test
    /// so that runs are deterministic, offline and do not depend on any
    /// committed certificate material.
    /// </summary>
    [TestFixture]
    [Category("TrustList")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class TrustListTests
    {
        private ITelemetryContext m_telemetry;
        private string m_basePath;
        private CertificateStoreIdentifier m_trustedStore;
        private CertificateStoreIdentifier m_issuerStore;
        private readonly List<TrustList> m_createdTrustLists = [];

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_basePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "tl",
                Guid.NewGuid().ToString("N")[..8]);
            m_trustedStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "trusted"));
            m_issuerStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "issuer"));
        }

        [TearDown]
        public void TearDown()
        {
            // Dispose the store instances the created TrustLists hold open.
            foreach (TrustList trustList in m_createdTrustLists)
            {
                trustList.Dispose();
            }
            m_createdTrustLists.Clear();

            if (Directory.Exists(m_basePath))
            {
                Directory.Delete(m_basePath, true);
            }
        }

        [Test]
        public async Task ScopedResolverReusesStoresAndDisposesThemAsync()
        {
            TrustListState node = CreateNode();
            m_trustedStore.StoreType = "ScopedTrusted";
            m_issuerStore.StoreType = "ScopedIssuers";
            var trusted = new Mock<ICertificateStore>();
            var issuers = new Mock<ICertificateStore>();
            trusted.Setup(store => store.EnumerateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new CertificateCollection());
            issuers.Setup(store => store.EnumerateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new CertificateCollection());
            var resolver = new Mock<ICertificateStoreResolver>(MockBehavior.Strict);
            resolver.Setup(instance => instance.OpenCertificateStore(m_trustedStore.StorePath, "ScopedTrusted", true))
                .Returns(trusted.Object);
            resolver.Setup(instance => instance.OpenCertificateStore(m_issuerStore.StorePath, "ScopedIssuers", true))
                .Returns(issuers.Object);
            using (var trustList = new TrustList(node, m_trustedStore, m_issuerStore,
                AllowAccess, AllowAccess, m_telemetry, null, 0, TrustList.DefaultMaxTrustListSizeSafetyCeiling,
                resolver.Object))
            {
                ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    OpenWithMasksMethodStateResult result = await node.OpenWithMasks.OnCallAsync(
                        context, node.OpenWithMasks, node.NodeId,
                        (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.IssuerCertificates),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
                }
                trusted.Verify(store => store.Dispose(), Times.Never);
                issuers.Verify(store => store.Dispose(), Times.Never);
            }
            resolver.Verify(instance => instance.OpenCertificateStore(
                m_trustedStore.StorePath, "ScopedTrusted", true), Times.Once);
            resolver.Verify(instance => instance.OpenCertificateStore(
                m_issuerStore.StorePath, "ScopedIssuers", true), Times.Once);
            trusted.Verify(store => store.Dispose(), Times.Once);
            issuers.Verify(store => store.Dispose(), Times.Once);
            trusted.Verify(store => store.EnumerateAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            issuers.Verify(store => store.EnumerateAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            resolver.VerifyNoOtherCalls();
            Assert.That(Directory.Exists(m_basePath), Is.False);
        }

        [TestCase(TrustListMasks.TrustedCertificates)]
        [TestCase(TrustListMasks.IssuerCertificates)]
        public void ScopedResolverFailureDoesNotFallBackToDirectory(TrustListMasks mask)
        {
            TrustListState node = CreateNode();
            CertificateStoreIdentifier identifier = mask == TrustListMasks.TrustedCertificates
                ? m_trustedStore : m_issuerStore;
            var resolver = new Mock<ICertificateStoreResolver>(MockBehavior.Strict);
            var failure = new IOException("Scoped provider unavailable");
            var trusted = new Mock<ICertificateStore>();
            if (mask == TrustListMasks.IssuerCertificates)
            {
                resolver.Setup(instance => instance.OpenCertificateStore(
                    m_trustedStore.StorePath, m_trustedStore.StoreType, true)).Returns(trusted.Object);
            }
            resolver.Setup(instance => instance.OpenCertificateStore(identifier.StorePath, identifier.StoreType, true))
                .Throws(failure);
            using var trustList = new TrustList(node, m_trustedStore, m_issuerStore,
                AllowAccess, AllowAccess, m_telemetry, null, 0, TrustList.DefaultMaxTrustListSizeSafetyCeiling,
                resolver.Object);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            IOException actual = Assert.ThrowsAsync<IOException>(async () =>
                await node.OpenWithMasks.OnCallAsync(context, node.OpenWithMasks, node.NodeId,
                    (uint)mask, CancellationToken.None).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(failure));
            resolver.Verify(instance => instance.OpenCertificateStore(
                identifier.StorePath, identifier.StoreType, true), Times.Once);
            if (mask == TrustListMasks.IssuerCertificates)
            {
                resolver.Verify(instance => instance.OpenCertificateStore(
                    m_trustedStore.StorePath, m_trustedStore.StoreType, true), Times.Once);
            }
            resolver.VerifyNoOtherCalls();
            Assert.That(Directory.Exists(m_basePath), Is.False);
        }

        [Test]
        public void OpenReadReturnsGoodAndSetsOpenCount()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            ServiceResult result = node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                ref fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fileHandle, Is.Not.Zero);
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));
        }

        [Test]
        public async Task OpenAsyncReadReturnsGoodAndSetsOpenCountAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            OpenMethodStateResult result = await node.Open.OnCallAsync(
                context,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.FileHandle, Is.Not.Zero);
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));
        }

        [Test]
        public void OpenReadWithoutReadAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowRead: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            Assert.That(
                () => node.Open.OnCall(
                    context,
                    node.Open,
                    node.NodeId,
                    (byte)OpenFileMode.Read,
                    ref fileHandle),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void OpenWriteWithoutEraseFlagReturnsBadNotSupported()
        {
            TrustListState node = CreateNode();
            // Neither read nor write access is granted: this mode is rejected
            // before any access check is performed.
            CreateTrustList(node, allowRead: false, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            ServiceResult result = node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Write,
                ref fileHandle);

            // OPC 10000-12 §7.8.2.2: modes other than Read and
            // Write|EraseExisting return Bad_NotSupported.
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(fileHandle, Is.Zero);
        }

        [Test]
        public void OpenWriteEraseExistingReturnsGoodAndPreparesEmptyStream()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            ServiceResult result = node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fileHandle, Is.Not.Zero);
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));
        }

        [Test]
        public void OpenWriteWithoutWriteAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            Assert.That(
                () => node.Open.OnCall(
                    context,
                    node.Open,
                    node.NodeId,
                    (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                    ref fileHandle),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void OpenForReadSeveralTimesKeepsEveryReadHandle()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext contextA = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext contextB = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint handleA = 0;
            node.Open.OnCall(contextA, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref handleA);

            uint handleB = 0;
            ServiceResult secondOpenResult = node.Open.OnCall(
                contextB,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                ref handleB);
            uint handleA2 = 0;
            ServiceResult sameSessionOpenResult = node.Open.OnCall(
                contextA,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                ref handleA2);

            // OPC 10000-20 §4.2.2: Clients can open the same file several
            // times for read; no read handle is evicted by another one.
            Assert.That(ServiceResult.IsGood(secondOpenResult), Is.True);
            Assert.That(ServiceResult.IsGood(sameSessionOpenResult), Is.True);
            Assert.That(new[] { handleA, handleB, handleA2 }, Is.Unique);
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)3));

            ByteString dataA = default;
            ServiceResult readA = node.Read.OnCall(
                contextA, node.Read, node.NodeId, handleA, 1024 * 1024, ref dataA);
            ByteString dataB = default;
            ServiceResult readB = node.Read.OnCall(
                contextB, node.Read, node.NodeId, handleB, 1024 * 1024, ref dataB);
            Assert.That(ServiceResult.IsGood(readA), Is.True, readA.ToString());
            Assert.That(ServiceResult.IsGood(readB), Is.True, readB.ToString());
            Assert.That(dataA.Length, Is.GreaterThan(0));
            Assert.That(dataB.ToArray(), Is.EqualTo(dataA.ToArray()));

            // A handle is only usable by the Session that opened it.
            ByteString data = default;
            ServiceResult readWithOtherSessionsHandle = node.Read.OnCall(
                contextB, node.Read, node.NodeId, handleA, 1024, ref data);
            Assert.That(
                readWithOtherSessionsHandle.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));

            ServiceResult close = node.Close.OnCall(contextA, node.Close, node.NodeId, handleA);
            Assert.That(ServiceResult.IsGood(close), Is.True);
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)2));

            // A write open is refused while any read handle is open.
            uint writeHandle = 0;
            ServiceResult writeOpen = node.Open.OnCall(
                contextA,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            Assert.That(writeOpen.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void SameSessionOpenWhileItsWriteHandleIsOpenIsRejected()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint writeHandle = 0;
            ServiceResult firstOpen = node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            Assert.That(ServiceResult.IsGood(firstOpen), Is.True);

            // OPC 10000-20 §4.2.2: a file that is already open cannot be
            // opened for writing, and a file open for writing cannot be
            // opened for reading - also by the Session holding the handle.
            uint secondHandle = 0;
            ServiceResult reopenWrite = node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref secondHandle);
            Assert.That(reopenWrite.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            ServiceResult reopenRead = node.Open.OnCall(
                context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref secondHandle);
            Assert.That(reopenRead.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));

            // The original write handle survives.
            ServiceResult write = node.Write.OnCall(
                context, node.Write, node.NodeId, writeHandle, ByteString.From(new byte[] { 1 }));
            Assert.That(ServiceResult.IsGood(write), Is.True, write.ToString());
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));
        }

        /// <summary>
        /// OPC 10000-12 §7.8.2.1: if no Method is called on an open handle for
        /// ActivityTimeout (default 60 000 ms) the Server closes the TrustList
        /// and discards the changes, so an idle writer of a live Session does
        /// not block other Sessions forever.
        /// </summary>
        [Test]
        public void IdleWriteHandleIsClosedAfterActivityTimeout()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var liveSessions = new List<ISession>();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(manager => manager.GetSessions()).Returns(() => [.. liveSessions]);
            var writerId = new NodeId(Guid.NewGuid(), 1);
            var otherId = new NodeId(Guid.NewGuid(), 1);
            liveSessions.Add(CreateSession(writerId));
            liveSessions.Add(CreateSession(otherId));
            ServerSystemContext writer = CreateServerContext(sessionManager.Object, writerId, timeProvider);
            ServerSystemContext other = CreateServerContext(sessionManager.Object, otherId, timeProvider);
            Assert.That(trustList.ActivityTimeout, Is.EqualTo(60000));

            uint writeHandle = 0;
            ServiceResult writeOpen = node.Open.OnCall(
                writer,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            Assert.That(ServiceResult.IsGood(writeOpen), Is.True);

            // Every Method call on the handle restarts the timeout.
            timeProvider.Advance(TimeSpan.FromSeconds(50));
            ServiceResult write = node.Write.OnCall(
                writer, node.Write, node.NodeId, writeHandle, ByteString.From(new byte[] { 1 }));
            Assert.That(ServiceResult.IsGood(write), Is.True, write.ToString());
            timeProvider.Advance(TimeSpan.FromSeconds(50));
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));

            uint otherHandle = 0;
            ServiceResult blocked = node.Open.OnCall(
                other,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref otherHandle);
            Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));

            timeProvider.Advance(TimeSpan.FromSeconds(11));
            Assert.That(node.OpenCount.Value, Is.Zero);

            ServiceResult staleWrite = node.Write.OnCall(
                writer, node.Write, node.NodeId, writeHandle, ByteString.From(new byte[] { 1 }));
            Assert.That(staleWrite.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));

            ServiceResult reopen = node.Open.OnCall(
                other,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref otherHandle);
            Assert.That(ServiceResult.IsGood(reopen), Is.True, reopen.ToString());
        }

        [Test]
        public void ActivityTimeoutPropertyValueIsHonored()
        {
            TrustListState node = CreateNode();
            var systemContext = new SystemContext(m_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            node.CreateOrReplaceActivityTimeout(systemContext, node);
            node.ActivityTimeout.Value = 5000;
            TrustList trustList = CreateTrustList(node);
            SessionSystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            Assert.That(trustList.ActivityTimeout, Is.EqualTo(5000));

            uint readHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref readHandle);
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));

            trustList.ExpireForInactivity(readHandle);
            Assert.That(node.OpenCount.Value, Is.Zero);
            ByteString data = default;
            ServiceResult read = node.Read.OnCall(
                context, node.Read, node.NodeId, readHandle, 16, ref data);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void OpenWithMasksSyncAllReturnsGood()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            ServiceResult result = node.OpenWithMasks.OnCall(
                context,
                node.OpenWithMasks,
                node.NodeId,
                (uint)TrustListMasks.All,
                ref fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(fileHandle, Is.Not.Zero);
        }

        [Test]
        public async Task OpenWithMasksTrustedCertificatesOnlyEncodesOnlyRequestedMaskAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate trustedCert = CreateTestCertificate("CN=TrustList Trusted Cert");
            using Certificate issuerCert = CreateTestCertificate("CN=TrustList Issuer Cert");
            using (ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                await trustedStore.AddAsync(trustedCert).ConfigureAwait(false);
            }
            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerStore.AddAsync(issuerCert).ConfigureAwait(false);
            }

            OpenWithMasksMethodStateResult openResult = await node.OpenWithMasks.OnCallAsync(
                context,
                node.OpenWithMasks,
                node.NodeId,
                (uint)TrustListMasks.TrustedCertificates,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(openResult.ServiceResult), Is.True);

            ReadMethodStateResult readResult = await node.Read.OnCallAsync(
                context,
                node.Read,
                node.NodeId,
                openResult.FileHandle,
                65536,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(readResult.ServiceResult), Is.True);

            TrustListDataType decoded = DecodeTrustListPayload(context, readResult.Data);
            Assert.That(decoded.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.TrustedCertificates));
            Assert.That(decoded.TrustedCertificates, Has.Count.EqualTo(1));
            Assert.That(decoded.IssuerCertificates, Is.Empty);
        }

        [Test]
        public void ReadWithInvalidFileHandleReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ByteString data = default;
            ServiceResult result = node.Read.OnCall(
                context,
                node.Read,
                node.NodeId,
                fileHandle + 1,
                1024,
                ref data);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void ReadFromDifferentSessionReturnsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext openingContext = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext otherContext = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                openingContext,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                ref fileHandle);

            ByteString data = default;
            ServiceResult result = node.Read.OnCall(
                otherContext,
                node.Read,
                node.NodeId,
                fileHandle,
                1024,
                ref data);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void ReadWithoutReadAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            // Read requires read access even when the file was opened for
            // writing with write access only.
            CreateTrustList(node, allowRead: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            ByteString data = default;
            Assert.That(
                () => node.Read.OnCall(context, node.Read, node.NodeId, 1, 1024, ref data),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void ReadLongerThanRemainingReturnsRemainingBytesThenEndOfFile()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 10);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            // OPC 10000-20 §4.2.4: the length is an upper bound; the rest of
            // the file is returned even when the requested length exceeds the
            // size limit, and an empty ByteString marks the end of the file.
            ByteString data = default;
            ServiceResult result = node.Read.OnCall(
                context,
                node.Read,
                node.NodeId,
                fileHandle,
                1024 * 1024,
                ref data);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(data.Length, Is.GreaterThan(0));
            Assert.That(DecodeTrustListPayload(context, data), Is.Not.Null);

            ByteString endOfFile = default;
            result = node.Read.OnCall(
                context,
                node.Read,
                node.NodeId,
                fileHandle,
                1024 * 1024,
                ref endOfFile);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(endOfFile.Length, Is.Zero);
        }

        [Test]
        public async Task ReadAsyncReturnsRequestedDataAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            OpenMethodStateResult openResult = await node.Open.OnCallAsync(
                context,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                CancellationToken.None).ConfigureAwait(false);

            ReadMethodStateResult readResult = await node.Read.OnCallAsync(
                context,
                node.Read,
                node.NodeId,
                openResult.FileHandle,
                65536,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(readResult.ServiceResult), Is.True);
            Assert.That(readResult.Data.Length, Is.GreaterThan(0));
        }

        [Test]
        public void WriteAfterOpenWriteSucceeds()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var payload = ByteString.From(new byte[] { 1, 2, 3, 4 });
            ServiceResult result = node.Write.OnCall(
                context,
                node.Write,
                node.NodeId,
                fileHandle,
                payload);

            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public async Task WriteAsyncAfterOpenWriteSucceedsAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            OpenMethodStateResult openResult = await node.Open.OnCallAsync(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                CancellationToken.None).ConfigureAwait(false);

            var payload = ByteString.From(new byte[] { 1, 2, 3, 4 });
            WriteMethodStateResult writeResult = await node.Write.OnCallAsync(
                context,
                node.Write,
                node.NodeId,
                openResult.FileHandle,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(writeResult.ServiceResult), Is.True);
        }

        [Test]
        public void WriteWithInvalidFileHandleReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var payload = ByteString.From(new byte[] { 1, 2, 3 });
            ServiceResult result = node.Write.OnCall(
                context,
                node.Write,
                node.NodeId,
                fileHandle + 1,
                payload);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void WriteFromDifferentSessionReturnsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext openingContext = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext otherContext = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                openingContext,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var payload = ByteString.From(new byte[] { 1, 2, 3 });
            ServiceResult result = node.Write.OnCall(
                otherContext,
                node.Write,
                node.NodeId,
                fileHandle,
                payload);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void WriteWithoutWriteAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            var payload = ByteString.From(new byte[] { 1 });
            Assert.That(
                () => node.Write.OnCall(context, node.Write, node.NodeId, 1, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void WriteExceedingMaxTrustListSizeReturnsBadRequestTooLarge()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 5);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var payload = ByteString.From(new byte[10]);
            ServiceResult result = node.Write.OnCall(
                context,
                node.Write,
                node.NodeId,
                fileHandle,
                payload);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public void CloseValidHandleResetsOpenCountToZero()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ServiceResult result = node.Close.OnCall(context, node.Close, node.NodeId, fileHandle);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(node.OpenCount.Value, Is.Zero);
        }

        [Test]
        public async Task CloseAsyncValidHandleResetsOpenCountToZeroAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            OpenMethodStateResult openResult = await node.Open.OnCallAsync(
                context,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                CancellationToken.None).ConfigureAwait(false);

            CloseMethodStateResult closeResult = await node.Close.OnCallAsync(
                context,
                node.Close,
                node.NodeId,
                openResult.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(closeResult.ServiceResult), Is.True);
            Assert.That(node.OpenCount.Value, Is.Zero);
        }

        [Test]
        public void CloseWithInvalidFileHandleReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ServiceResult result = node.Close.OnCall(context, node.Close, node.NodeId, fileHandle + 1);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void CloseFromDifferentSessionReturnsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext openingContext = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext otherContext = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                openingContext,
                node.Open,
                node.NodeId,
                (byte)OpenFileMode.Read,
                ref fileHandle);

            ServiceResult result = node.Close.OnCall(otherContext, node.Close, node.NodeId, fileHandle);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CloseWithoutReadAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowRead: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            Assert.That(
                () => node.Close.OnCall(context, node.Close, node.NodeId, 1),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task CloseAndUpdateWithValidDataAppliesCertificatesAndReturnsGoodAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate trustedCert = CreateTestCertificate("CN=TrustList CloseAndUpdate Cert");

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates
            };
            ArrayOf<ByteString> trustedCertificates = new ByteString[] { trustedCert.RawData.ToByteString() };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates.AddItems(trustedCertificates);
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            node.Write.OnCall(context, node.Write, node.NodeId, fileHandle, payload);

            bool restartRequired = true;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                ref restartRequired);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(restartRequired, Is.False);
            Assert.That(node.OpenCount.Value, Is.Zero);

            using ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection found = await trustedStore
                .FindByThumbprintAsync(trustedCert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task CloseAndUpdateAsyncWithValidDataReturnsGoodAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            OpenMethodStateResult openResult = await node.Open.OnCallAsync(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                CancellationToken.None).ConfigureAwait(false);

            var trustListData = new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.None };
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            await node.Write.OnCallAsync(
                context,
                node.Write,
                node.NodeId,
                openResult.FileHandle,
                payload,
                CancellationToken.None).ConfigureAwait(false);

            CloseAndUpdateMethodStateResult result = await node.CloseAndUpdate.OnCallAsync(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                openResult.FileHandle,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);
            Assert.That(result.ApplyChangesRequired, Is.False);
        }

        [Test]
        public void CloseAndUpdateWithInvalidFileHandleReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            bool restartRequired = false;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle + 1,
                ref restartRequired);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void CloseAndUpdateWithCorruptDataReturnsBadCertificateInvalid()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var payload = ByteString.From([0x01, 0x02, 0x03, 0x04, 0x05]);
            node.Write.OnCall(context, node.Write, node.NodeId, fileHandle, payload);

            bool restartRequired = false;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                ref restartRequired);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void CloseAndUpdateWithoutWriteAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            bool restartRequired = false;
            Assert.That(
                () => node.CloseAndUpdate.OnCall(
                    context,
                    node.CloseAndUpdate,
                    node.NodeId,
                    1,
                    ref restartRequired),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AddCertificateAddsToTrustedStoreAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList AddCertificate Trusted");

            ServiceResult result = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                true);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            using ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection found = await trustedStore
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AddCertificateAsyncNotTrustedReturnsBadCertificateInvalidAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList AddCertificate Issuer");

            AddCertificateMethodStateResult result = await node.AddCertificate.OnCallAsync(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                false,
                CancellationToken.None).ConfigureAwait(false);

            // OPC 10000-12 §7.8.2.6: IsTrustedCertificate FALSE returns
            // Bad_CertificateInvalid; issuers cannot be added without a CRL.
            Assert.That(result.ServiceResult.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));

            using ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry);
            using CertificateCollection found = await issuerStore
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Is.Empty);
        }

        [Test]
        public void AddCertificateWithEmptyCertificateReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            ServiceResult result = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                ByteString.Empty,
                true);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddCertificateWithInvalidDataReturnsBadCertificateInvalid()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            ServiceResult result = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                ByteString.From([0x01, 0x02, 0x03]),
                true);

            Assert.That(
                result.StatusCode,
                Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void AddCertificateWhileOpenForReadReturnsBadNotWritable()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList AddCertificate SessionOpen");

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ServiceResult result = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                true);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void AddCertificateWhileOpenForWriteReturnsBadInvalidState()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList AddCertificate WriteOpen");

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            ServiceResult result = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                true);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void AddCertificateWithoutWriteAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList AddCertificate NoAccess");

            Assert.That(
                () => node.AddCertificate.OnCall(
                    context,
                    node.AddCertificate,
                    node.NodeId,
                    cert.RawData.ToByteString(),
                    true),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task RemoveCertificateRemovesFromStoreAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList RemoveCertificate");

            using (ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                await trustedStore.AddAsync(cert).ConfigureAwait(false);
            }

            ServiceResult result = node.RemoveCertificate.OnCall(
                context,
                node.RemoveCertificate,
                node.NodeId,
                cert.Thumbprint,
                true);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            using ICertificateStore verifyStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection found = await verifyStore
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Is.Empty);
        }

        [Test]
        public async Task RemoveCertificateAsyncRemovesFromIssuerStoreAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));
            using Certificate cert = CreateTestCertificate("CN=TrustList RemoveCertificate Issuer");

            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerStore.AddAsync(cert).ConfigureAwait(false);
            }

            RemoveCertificateMethodStateResult result = await node.RemoveCertificate.OnCallAsync(
                context,
                node.RemoveCertificate,
                node.NodeId,
                cert.Thumbprint,
                false,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            using ICertificateStore verifyStore = m_issuerStore.OpenStore(m_telemetry);
            using CertificateCollection found = await verifyStore
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Is.Empty);
        }

        [Test]
        public void RemoveCertificateWithEmptyThumbprintReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            ServiceResult result = node.RemoveCertificate.OnCall(
                context,
                node.RemoveCertificate,
                node.NodeId,
                string.Empty,
                true);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RemoveCertificateNotFoundReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            ServiceResult result = node.RemoveCertificate.OnCall(
                context,
                node.RemoveCertificate,
                node.NodeId,
                "0000000000000000000000000000000000000000",
                true);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RemoveCertificateWhileOpenForReadReturnsBadNotWritable()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ServiceResult result = node.RemoveCertificate.OnCall(
                context,
                node.RemoveCertificate,
                node.NodeId,
                "AABBCCDDEEFF00112233445566778899AABBCCDD",
                true);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public void RemoveCertificateWithoutWriteAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            Assert.That(
                () => node.RemoveCertificate.OnCall(
                    context,
                    node.RemoveCertificate,
                    node.NodeId,
                    "AABBCCDDEEFF00112233445566778899AABBCCDD",
                    true),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void ComputeEffectiveMaxTrustListSizeUnlimitedReturnsSafetyCeiling()
        {
            Assert.That(
                TrustList.ComputeEffectiveMaxTrustListSize(0, 4096),
                Is.EqualTo(4096));
        }

        [Test]
        public void ComputeEffectiveMaxTrustListSizeFiniteBelowCeilingReturnsAdvertised()
        {
            Assert.That(
                TrustList.ComputeEffectiveMaxTrustListSize(500, 4096),
                Is.EqualTo(500));
        }

        [Test]
        public void ComputeEffectiveMaxTrustListSizeFiniteAboveCeilingReturnsCeiling()
        {
            Assert.That(
                TrustList.ComputeEffectiveMaxTrustListSize(8192, 4096),
                Is.EqualTo(4096));
        }

        [Test]
        public void ComputeEffectiveMaxTrustListSizeNonPositiveCeilingFallsBackToDefault()
        {
            Assert.That(
                TrustList.ComputeEffectiveMaxTrustListSize(0, 0),
                Is.EqualTo(TrustList.DefaultMaxTrustListSizeSafetyCeiling));
            Assert.That(
                TrustList.ComputeEffectiveMaxTrustListSize(0, -1),
                Is.EqualTo(TrustList.DefaultMaxTrustListSizeSafetyCeiling));
        }

        [Test]
        public void EffectiveMaxTrustListSizeForUnlimitedAdvertisedIsSafetyCeiling()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(
                node,
                maxTrustListSize: 0,
                maxTrustListSizeSafetyCeiling: 2048);

            Assert.That(trustList.EffectiveMaxTrustListSize, Is.EqualTo(2048));
        }

        [Test]
        public void EffectiveMaxTrustListSizeClampsAdvertisedAboveCeiling()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(
                node,
                maxTrustListSize: 1_000_000,
                maxTrustListSizeSafetyCeiling: 2048);

            Assert.That(trustList.EffectiveMaxTrustListSize, Is.EqualTo(2048));
        }

        [Test]
        public void LegacyConstructorHonorsFiniteSizeAboveDefaultCeiling()
        {
            // Backward compatibility: the legacy (single-size) overload must
            // honor a configured finite size exactly, even when it exceeds the
            // default safety ceiling, and must never clamp it.
            const int large = 4 * 1024 * 1024;
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node, maxTrustListSize: large);

            Assert.That(trustList.EffectiveMaxTrustListSize, Is.EqualTo(large));
        }

        [Test]
        public void LegacyConstructorUnlimitedUsesDefaultSafetyCeiling()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node, maxTrustListSize: 0);

            Assert.That(
                trustList.EffectiveMaxTrustListSize,
                Is.EqualTo(TrustList.DefaultMaxTrustListSizeSafetyCeiling));
        }

        [Test]
        public void WriteAtExactEffectiveLimitSucceedsAndOneMoreByteFails()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 0, maxTrustListSizeSafetyCeiling: 16);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ServiceResult atLimit = node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, ByteString.From(new byte[16]));
            Assert.That(ServiceResult.IsGood(atLimit), Is.True);

            ServiceResult overLimit = node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, ByteString.From(new byte[1]));
            Assert.That(
                overLimit.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public void WriteCumulativeAcrossChunksEnforcesEffectiveLimit()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 0, maxTrustListSizeSafetyCeiling: 10);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            for (int i = 0; i < 5; i++)
            {
                ServiceResult chunk = node.Write.OnCall(
                    context, node.Write, node.NodeId, fileHandle, ByteString.From(new byte[2]));
                Assert.That(ServiceResult.IsGood(chunk), Is.True);
            }

            ServiceResult overLimit = node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, ByteString.From(new byte[1]));
            Assert.That(
                overLimit.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public void ReadInChunksReturnsWholeStreamLargerThanEffectiveLimit()
        {
            TrustListState node = CreateNode();
            // The encoded read stream of the (empty) stores is larger than the
            // 8-byte ceiling. The limit bounds what a Client may write; content
            // the server encoded itself must stay readable to the end.
            CreateTrustList(node, maxTrustListSize: 0, maxTrustListSizeSafetyCeiling: 8);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            var received = new List<byte>();
            while (true)
            {
                ByteString data = default;
                ServiceResult chunk = node.Read.OnCall(
                    context, node.Read, node.NodeId, fileHandle, 4, ref data);
                Assert.That(ServiceResult.IsGood(chunk), Is.True);
                if (data.Length == 0)
                {
                    break;
                }
                received.AddRange(data.ToArray());
            }

            Assert.That(received, Has.Count.GreaterThan(8));
            Assert.That(DecodeTrustListPayload(context, ByteString.From(received.ToArray())), Is.Not.Null);
        }

        [Test]
        public void ReadWithNegativeLengthReturnsBadInvalidArgument()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ByteString data = default;
            ServiceResult result = node.Read.OnCall(
                context, node.Read, node.NodeId, fileHandle, -1, ref data);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void WriteEncodedPayloadExceedingEffectiveLimitReturnsBadRequestTooLarge()
        {
            TrustListState node = CreateNode();
            // A real certificate payload is well over the 100-byte ceiling.
            CreateTrustList(node, maxTrustListSize: 0, maxTrustListSizeSafetyCeiling: 100);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate cert = CreateTestCertificate("CN=TrustList Oversized Payload");
            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates
            };
            ArrayOf<ByteString> trustedCertificates = new ByteString[] { cert.RawData.ToByteString() };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates.AddItems(
                trustedCertificates);
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            Assert.That(payload.Length, Is.GreaterThan(100));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ServiceResult result = node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, payload);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public async Task CloseAndUpdateWithEncodedPayloadUnderCeilingSucceedsAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 0, maxTrustListSizeSafetyCeiling: 64 * 1024);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate trustedCert = CreateTestCertificate("CN=TrustList Ceiling Payload");

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates
            };
            ArrayOf<ByteString> trustedCertificates
                = new ByteString[] { trustedCert.RawData.ToByteString() };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates.AddItems(
                trustedCertificates);
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            node.Write.OnCall(context, node.Write, node.NodeId, fileHandle, payload);

            bool restartRequired = true;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context, node.CloseAndUpdate, node.NodeId, fileHandle, ref restartRequired);

            Assert.That(ServiceResult.IsGood(result), Is.True);

            using ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection found = await trustedStore
                .FindByThumbprintAsync(trustedCert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(1));
        }

        [Test]
        public void AddCertificateExceedingEffectiveLimitReturnsBadRequestTooLarge()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 0, maxTrustListSizeSafetyCeiling: 100);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate cert = CreateTestCertificate("CN=TrustList Add Oversized");
            ByteString rawCertificate = cert.RawData.ToByteString();
            Assert.That(rawCertificate.Length, Is.GreaterThan(100));

            ServiceResult result = node.AddCertificate.OnCall(
                context, node.AddCertificate, node.NodeId, rawCertificate, true);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadRequestTooLarge));
        }

        [Test]
        public void CloseAndUpdateLegacyNotifiesCertificateChangeForCertificates()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            var notifier = new RecordingTrustListChangeNotifier();
            trustList.SetTrustListChangeNotifier(notifier, TrustListIdentifier.Peers);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate trustedCert = CreateTestCertificate("CN=TrustList Notify Cert");

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.TrustedCertificates
            };
            ArrayOf<ByteString> trustedCertificates = new ByteString[] { trustedCert.RawData.ToByteString() };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates.AddItems(trustedCertificates);
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            node.Write.OnCall(context, node.Write, node.NodeId, fileHandle, payload);

            bool restartRequired = true;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                ref restartRequired);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(notifier.Notifications, Has.Count.EqualTo(1));
            Assert.That(notifier.Notifications[0].Scope, Is.EqualTo(TrustListIdentifier.Peers));
            Assert.That(notifier.Notifications[0].TrustChanged, Is.True);
            Assert.That(notifier.Notifications[0].CrlChanged, Is.False);
        }

        [Test]
        public void CloseAndUpdateLegacyNotifiesCertificateChangeForCrls()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            var notifier = new RecordingTrustListChangeNotifier();
            trustList.SetTrustListChangeNotifier(notifier, TrustListIdentifier.Peers);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate caCert = CertificateBuilder
                .Create("CN=TrustList Notify CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            X509CRL crl = DefaultCertificateIssuer.Instance.RevokeCertificates(
                caCert,
                null,
                null);

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);

            // The CA certificate must accompany its CRL: the directory store
            // resolves a CRL's issuer from its own certificates on AddCRL.
            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.TrustedCrls)
            };
            ArrayOf<ByteString> trustedCertificates = new ByteString[] { caCert.RawData.ToByteString() };
            trustListData.TrustedCertificates = trustListData.TrustedCertificates.AddItems(trustedCertificates);
            ArrayOf<ByteString> trustedCrls = new ByteString[] { crl.RawData.ToByteString() };
            trustListData.TrustedCrls = trustListData.TrustedCrls.AddItems(trustedCrls);
            ByteString payload = EncodeTrustListPayload(context, trustListData);
            node.Write.OnCall(context, node.Write, node.NodeId, fileHandle, payload);

            bool restartRequired = true;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle,
                ref restartRequired);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(notifier.Notifications, Has.Count.EqualTo(1));
            Assert.That(notifier.Notifications[0].TrustChanged, Is.True);
            Assert.That(notifier.Notifications[0].CrlChanged, Is.True);
        }

        [Test]
        public void AddCertificateLegacyNotifiesTrustChangeOnly()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            var notifier = new RecordingTrustListChangeNotifier();
            trustList.SetTrustListChangeNotifier(notifier, TrustListIdentifier.Users);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate cert = CreateTestCertificate("CN=TrustList Notify Add Cert");

            ServiceResult result = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                true);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(notifier.Notifications, Has.Count.EqualTo(1));
            Assert.That(notifier.Notifications[0].Scope, Is.EqualTo(TrustListIdentifier.Users));
            Assert.That(notifier.Notifications[0].TrustChanged, Is.True);
            Assert.That(notifier.Notifications[0].CrlChanged, Is.False);
        }

        [Test]
        public void RemoveCertificateLegacyNotifiesTrustChange()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            var notifier = new RecordingTrustListChangeNotifier();
            trustList.SetTrustListChangeNotifier(notifier, TrustListIdentifier.Peers);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate cert = CreateTestCertificate("CN=TrustList Notify Remove Cert");
            ServiceResult addResult = node.AddCertificate.OnCall(
                context,
                node.AddCertificate,
                node.NodeId,
                cert.RawData.ToByteString(),
                true);
            Assert.That(ServiceResult.IsGood(addResult), Is.True);
            notifier.Notifications.Clear();

            ServiceResult result = node.RemoveCertificate.OnCall(
                context,
                node.RemoveCertificate,
                node.NodeId,
                cert.Thumbprint,
                true);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(notifier.Notifications, Has.Count.EqualTo(1));
            Assert.That(notifier.Notifications[0].TrustChanged, Is.True);
            Assert.That(notifier.Notifications[0].CrlChanged, Is.False);
        }

        private sealed class RecordingTrustListChangeNotifier : ICertificateTrustListManager
        {
            public List<(TrustListIdentifier Scope, bool TrustChanged, bool CrlChanged)> Notifications { get; }
                = [];

            public IReadOnlyCollection<TrustListIdentifier> TrustLists => [];

            public void RegisterTrustList(
                TrustListIdentifier trustList,
                string trustedStorePath,
                string issuerStorePath = null)
            {
                throw new NotSupportedException();
            }

            public ICertificateStore OpenTrustedStore(TrustListIdentifier trustList)
            {
                throw new NotSupportedException();
            }

            public ICertificateStore OpenIssuerStore(TrustListIdentifier trustList)
            {
                throw new NotSupportedException();
            }

            public Task<ITrustListTransaction> BeginUpdateAsync(
                TrustListIdentifier trustList,
                CancellationToken ct = default)
            {
                throw new NotSupportedException();
            }

            public void NotifyTrustListChanged(
                TrustListIdentifier trustList,
                bool trustChanged,
                bool crlChanged)
            {
                Notifications.Add((trustList, trustChanged, crlChanged));
            }
        }

        [Test]
        public void ReadOpenWhileAnotherSessionWritesReturnsBadNotReadableAndKeepsWriter()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext writer = CreateContext(new NodeId(Guid.NewGuid(), 1));
            ISystemContext reader = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint writeHandle = 0;
            node.Open.OnCall(
                writer,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);

            // OPC 10000-20 §4.2.2: a file open for writing cannot be opened
            // for reading; the writer's upload must survive.
            uint readHandle = 0;
            ServiceResult readOpen = node.Open.OnCall(
                reader, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref readHandle);
            Assert.That(readOpen.StatusCode, Is.EqualTo(StatusCodes.BadNotReadable));

            uint secondWriteHandle = 0;
            ServiceResult writeOpen = node.Open.OnCall(
                reader,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref secondWriteHandle);
            Assert.That(writeOpen.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));

            ServiceResult write = node.Write.OnCall(
                writer, node.Write, node.NodeId, writeHandle, ByteString.From(new byte[] { 1 }));
            Assert.That(ServiceResult.IsGood(write), Is.True);
        }

        [Test]
        public void WriteOpenWhileAnotherSessionReadsReturnsBadNotWritableAndKeepsReader()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            var liveSessions = new List<ISession>();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(manager => manager.GetSessions()).Returns(() => [.. liveSessions]);
            var readerId = new NodeId(Guid.NewGuid(), 1);
            var writerId = new NodeId(Guid.NewGuid(), 1);
            liveSessions.Add(CreateSession(readerId));
            liveSessions.Add(CreateSession(writerId));
            ServerSystemContext reader = CreateServerContext(sessionManager.Object, readerId);
            ServerSystemContext writer = CreateServerContext(sessionManager.Object, writerId);

            uint readHandle = 0;
            ServiceResult readOpen = node.Open.OnCall(
                reader, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref readHandle);
            Assert.That(ServiceResult.IsGood(readOpen), Is.True);

            // OPC 10000-20 §4.2.2: a file that is already open cannot be
            // opened for writing; the reader's download must survive.
            uint writeHandle = 0;
            ServiceResult blocked = node.Open.OnCall(
                writer,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));

            ByteString data = default;
            ServiceResult read = node.Read.OnCall(
                reader, node.Read, node.NodeId, readHandle, 16, ref data);
            Assert.That(ServiceResult.IsGood(read), Is.True, read.ToString());
            Assert.That(data.Length, Is.GreaterThan(0));

            // Once the reading Session is gone its handle no longer blocks.
            liveSessions.RemoveAt(0);
            ServiceResult writeOpen = node.Open.OnCall(
                writer,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            Assert.That(ServiceResult.IsGood(writeOpen), Is.True, writeOpen.ToString());
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));
        }

        [Test]
        public void WriteOpenAbandonedByClosedSessionIsReleasedForAnotherSession()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            var liveSessions = new List<ISession>();
            var sessionManager = new Mock<ISessionManager>();
            sessionManager.Setup(manager => manager.GetSessions()).Returns(() => [.. liveSessions]);
            var writerId = new NodeId(Guid.NewGuid(), 1);
            var otherId = new NodeId(Guid.NewGuid(), 1);
            liveSessions.Add(CreateSession(writerId));
            liveSessions.Add(CreateSession(otherId));
            ServerSystemContext writer = CreateServerContext(sessionManager.Object, writerId);
            ServerSystemContext other = CreateServerContext(sessionManager.Object, otherId);

            uint writeHandle = 0;
            ServiceResult writeOpen = node.Open.OnCall(
                writer,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            Assert.That(ServiceResult.IsGood(writeOpen), Is.True);

            // While the writing Session is alive its upload is protected.
            uint otherHandle = 0;
            ServiceResult blocked = node.Open.OnCall(
                other,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref otherHandle);
            Assert.That(blocked.StatusCode, Is.EqualTo(StatusCodes.BadNotWritable));
            using Certificate cert = CreateTestCertificate("CN=TrustList Abandoned Handle");
            ServiceResult blockedAdd = node.AddCertificate.OnCall(
                other, node.AddCertificate, node.NodeId, cert.RawData.ToByteString(), true);
            Assert.That(blockedAdd.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            // The writing Session goes away without Close/CloseAndUpdate and
            // without the host being told (e.g. a GDS certificate group
            // TrustList): the abandoned handle must not lock the TrustList.
            liveSessions.RemoveAt(0);

            ServiceResult add = node.AddCertificate.OnCall(
                other, node.AddCertificate, node.NodeId, cert.RawData.ToByteString(), true);
            Assert.That(ServiceResult.IsGood(add), Is.True, add.ToString());
            Assert.That(node.OpenCount.Value, Is.Zero);

            node.Open.OnCall(
                writer,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref writeHandle);
            ServiceResult reopen = node.Open.OnCall(
                other,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref otherHandle);
            Assert.That(ServiceResult.IsGood(reopen), Is.True, reopen.ToString());
            Assert.That(otherHandle, Is.Not.Zero);

            // The stale handle of the closed Session is gone.
            ServiceResult staleWrite = node.Write.OnCall(
                writer, node.Write, node.NodeId, writeHandle, ByteString.From(new byte[] { 1 }));
            Assert.That(ServiceResult.IsBad(staleWrite), Is.True);
        }

        [Test]
        public void WriteAndCloseAndUpdateOnReadHandleReturnBadInvalidState()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ServiceResult write = node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, ByteString.From(new byte[] { 1 }));
            Assert.That(write.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            // OPC 10000-12 §7.8.2.5: CloseAndUpdate only for a write open.
            bool restartRequired = false;
            ServiceResult closeAndUpdate = node.CloseAndUpdate.OnCall(
                context, node.CloseAndUpdate, node.NodeId, fileHandle, ref restartRequired);
            Assert.That(closeAndUpdate.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task RemoveIssuerStillNeededReturnsBadCertificateChainIncompleteAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate caCert = CertificateBuilder
                .Create("CN=TrustList Chain CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=TrustList Chain Leaf")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerStore.AddAsync(caCert).ConfigureAwait(false);
            }
            using (ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                await trustedStore.AddAsync(leaf).ConfigureAwait(false);
            }

            // OPC 10000-12 §7.8.2.7: a CA needed to validate another
            // certificate of the TrustList cannot be removed.
            ServiceResult result = node.RemoveCertificate.OnCall(
                context, node.RemoveCertificate, node.NodeId, caCert.Thumbprint, false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateChainIncomplete));

            ServiceResult removeLeaf = node.RemoveCertificate.OnCall(
                context, node.RemoveCertificate, node.NodeId, leaf.Thumbprint, true);
            Assert.That(ServiceResult.IsGood(removeLeaf), Is.True);

            ServiceResult removeCa = node.RemoveCertificate.OnCall(
                context, node.RemoveCertificate, node.NodeId, caCert.Thumbprint, false);
            Assert.That(ServiceResult.IsGood(removeCa), Is.True);
        }

        [Test]
        public async Task RemoveTrustedCaStillNeededReturnsBadCertificateChainIncompleteAsync()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate rootCa = CertificateBuilder
                .Create("CN=TrustList Trusted Root CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate intermediate = CertificateBuilder
                .Create("CN=TrustList Issuer Intermediate CA")
                .SetCAConstraint()
                .SetIssuer(rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using (ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                await trustedStore.AddAsync(rootCa).ConfigureAwait(false);
            }
            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerStore.AddAsync(intermediate).ConfigureAwait(false);
            }

            // OPC 10000-12 §7.8.2.7: the trusted root is needed to validate
            // the intermediate in the issuer list.
            ServiceResult result = node.RemoveCertificate.OnCall(
                context, node.RemoveCertificate, node.NodeId, rootCa.Thumbprint, true);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateChainIncomplete));
            using (ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry))
            {
                using CertificateCollection found = await trustedStore
                    .FindByThumbprintAsync(rootCa.Thumbprint)
                    .ConfigureAwait(false);
                Assert.That(found, Has.Count.EqualTo(1));
            }

            // A copy of the root in the issuer list keeps the chain complete.
            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerStore.AddAsync(rootCa).ConfigureAwait(false);
            }
            ServiceResult removeWithCopy = node.RemoveCertificate.OnCall(
                context, node.RemoveCertificate, node.NodeId, rootCa.Thumbprint, true);
            Assert.That(ServiceResult.IsGood(removeWithCopy), Is.True, removeWithCopy.ToString());
        }

        [Test]
        public async Task AddCertificateWithValidationRejectsCertificateWhoseIssuerIsMissingAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate caCert = CertificateBuilder
                .Create("CN=TrustList Missing CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=TrustList Orphan Leaf")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // OPC 10000-12 §7.8.2.6: a CA-issued certificate whose issuer is
            // not in the TrustList is rejected with a validation error.
            ServiceResult result = node.AddCertificate.OnCall(
                context, node.AddCertificate, node.NodeId, leaf.RawData.ToByteString(), true);
            Assert.That(ServiceResult.IsBad(result), Is.True);

            using ICertificateStore trustedStore = m_trustedStore.OpenStore(m_telemetry);
            using CertificateCollection found = await trustedStore
                .FindByThumbprintAsync(leaf.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Is.Empty);

            // A self-signed certificate passes: untrusted is suppressible.
            using Certificate selfSigned = CreateTestCertificate("CN=TrustList Validated SelfSigned");
            ServiceResult accepted = node.AddCertificate.OnCall(
                context, node.AddCertificate, node.NodeId, selfSigned.RawData.ToByteString(), true);
            Assert.That(ServiceResult.IsGood(accepted), Is.True, accepted.ToString());
        }

        [Test]
        public async Task RejectedTrustListCertificateIsNotRecordedInRejectedStoreAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            var rejectedStore = new CertificateStoreIdentifier(Path.Combine(m_basePath, "rejected"));
            using Certificate rejectedPeer = CreateTestCertificate("CN=TrustList Genuinely Rejected Peer");
            using (ICertificateStore store = rejectedStore.OpenStore(m_telemetry))
            {
                await store.AddAsync(rejectedPeer).ConfigureAwait(false);
            }
            trustList.SetCertificateValidation(new SecurityConfiguration
            {
                RejectedCertificateStore = rejectedStore
            });
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate caCert = CertificateBuilder
                .Create("CN=TrustList Rejected Store CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=TrustList Rejected Store Leaf")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ServiceResult result = node.AddCertificate.OnCall(
                context, node.AddCertificate, node.NodeId, leaf.RawData.ToByteString(), true);
            Assert.That(ServiceResult.IsBad(result), Is.True);

            // The refused certificate is not a rejected peer: the server's
            // Rejected store keeps exactly what it held before.
            using (ICertificateStore store = rejectedStore.OpenStore(m_telemetry))
            {
                using CertificateCollection rejected = await store.EnumerateAsync().ConfigureAwait(false);
                Assert.That(rejected, Has.Count.EqualTo(1));
                Assert.That(rejected[0].Thumbprint, Is.EqualTo(rejectedPeer.Thumbprint));
            }
        }

        [Test]
        public async Task CloseAndUpdateWithValidationDoesNotResolveIssuersFromReplacedStoresAsync()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            // As for the server's DefaultApplicationGroup, the validating
            // SecurityConfiguration's Peers stores are the stores the upload
            // replaces.
            trustList.SetCertificateValidation(new SecurityConfiguration
            {
                TrustedPeerCertificates = new CertificateTrustList { StorePath = m_trustedStore.StorePath },
                TrustedIssuerCertificates = new CertificateTrustList { StorePath = m_issuerStore.StorePath }
            });
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate caCert = CertificateBuilder
                .Create("CN=TrustList Replaced CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=TrustList Replaced Leaf")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                await issuerStore.AddAsync(caCert).ConfigureAwait(false);
            }

            // The new TrustList drops the CA but keeps the certificate it
            // issued: the CA still in the current issuer store must not make
            // the orphaned certificate validate (OPC 10000-12 §7.8.2.5).
            ServiceResult result = CloseAndUpdateWith(node, context, new TrustListDataType
            {
                SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.IssuerCertificates),
                TrustedCertificates = [leaf.RawData.ToByteString()]
            });
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));

            using (ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry))
            {
                using CertificateCollection issuers = await issuerStore.EnumerateAsync().ConfigureAwait(false);
                Assert.That(issuers, Has.Count.EqualTo(1), "the rejected update must not be applied");
            }

            // The same certificate with its CA in the uploaded list is valid.
            result = CloseAndUpdateWith(node, context, new TrustListDataType
            {
                SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates | TrustListMasks.IssuerCertificates),
                TrustedCertificates = [leaf.RawData.ToByteString()],
                IssuerCertificates = [caCert.RawData.ToByteString()]
            });
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        [Test]
        public void CloseAndUpdateWithValidationRejectsCertificateRevokedByUploadedCrl()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate caCert = CertificateBuilder
                .Create("CN=TrustList Revoking CA")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=TrustList Revoked Leaf")
                .SetIssuer(caCert)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            IX509CRL crl = CrlBuilder
                .Create(caCert.SubjectName)
                .SetNextUpdate(DateTime.UtcNow.AddDays(30))
                .AddRevokedCertificate(leaf)
                .CreateForRSA(caCert);

            // The uploaded TrustList revokes one of its own certificates.
            ServiceResult result = CloseAndUpdateWith(node, context, new TrustListDataType
            {
                SpecifiedLists = (uint)(TrustListMasks.TrustedCertificates |
                    TrustListMasks.IssuerCertificates | TrustListMasks.IssuerCrls),
                TrustedCertificates = [leaf.RawData.ToByteString()],
                IssuerCertificates = [caCert.RawData.ToByteString()],
                IssuerCrls = [crl.RawData.ToByteString()]
            });
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        private static ServiceResult CloseAndUpdateWith(
            TrustListState node,
            ISystemContext context,
            TrustListDataType trustListData)
        {
            uint fileHandle = 0;
            ServiceResult open = node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);
            Assert.That(ServiceResult.IsGood(open), Is.True, open.ToString());
            ServiceResult write = node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, EncodeTrustListPayload(context, trustListData));
            Assert.That(ServiceResult.IsGood(write), Is.True, write.ToString());

            bool restartRequired = false;
            return node.CloseAndUpdate.OnCall(
                context, node.CloseAndUpdate, node.NodeId, fileHandle, ref restartRequired);
        }

        [Test]
        public void CloseAndUpdateWithValidationRejectsNonCaIssuerCertificate()
        {
            TrustListState node = CreateNode();
            TrustList trustList = CreateTrustList(node);
            trustList.SetCertificateValidation(new SecurityConfiguration());
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            using Certificate notACa = CreateTestCertificate("CN=TrustList Not A CA Issuer");
            var trustListData = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.IssuerCertificates,
                IssuerCertificates = [notACa.RawData.ToByteString()]
            };

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting,
                ref fileHandle);
            node.Write.OnCall(
                context, node.Write, node.NodeId, fileHandle, EncodeTrustListPayload(context, trustListData));

            // OPC 10000-12 §7.8.2.5: the new TrustList is validated before it
            // is applied; a leaf certificate is not a valid issuer.
            bool restartRequired = false;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context, node.CloseAndUpdate, node.NodeId, fileHandle, ref restartRequired);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateInvalid));
        }

        private static Certificate CreateTestCertificate(string subject)
        {
            return CertificateBuilder
                .Create("CN=TL" + subject.Length.ToString(CultureInfo.InvariantCulture))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private TrustListState CreateNode()
        {
            var context = new SystemContext(m_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
            var node = new TrustListState(null);
            node.CreateOrReplaceOpen(context, node);
            node.CreateOrReplaceClose(context, node);
            node.CreateOrReplaceRead(context, node);
            node.CreateOrReplaceWrite(context, node);
            node.CreateOrReplaceOpenCount(context, node);
            node.CreateOrReplaceOpenWithMasks(context, node);
            node.CreateOrReplaceCloseAndUpdate(context, node);
            node.CreateOrReplaceAddCertificate(context, node);
            node.CreateOrReplaceRemoveCertificate(context, node);
            node.CreateOrReplaceLastUpdateTime(context, node);
            return node;
        }

        private TrustList CreateTrustList(
            TrustListState node,
            bool allowRead = true,
            bool allowWrite = true,
            int maxTrustListSize = 0,
            int? maxTrustListSizeSafetyCeiling = null)
        {
            TrustList.SecureAccess readAccess = allowRead ? AllowAccess : DenyAccess;
            TrustList.SecureAccess writeAccess = allowWrite ? AllowAccess : DenyAccess;

            if (maxTrustListSizeSafetyCeiling.HasValue)
            {
                return Track(new TrustList(
                    node,
                    m_trustedStore,
                    m_issuerStore,
                    readAccess,
                    writeAccess,
                    m_telemetry,
                    coordinator: null,
                    maxTrustListSize,
                    maxTrustListSizeSafetyCeiling.Value));
            }

            return Track(new TrustList(
                node,
                m_trustedStore,
                m_issuerStore,
                readAccess,
                writeAccess,
                m_telemetry,
                maxTrustListSize));
        }

        private TrustList Track(TrustList trustList)
        {
            m_createdTrustLists.Add(trustList);
            return trustList;
        }

        private static void AllowAccess(ISystemContext context, CertificateStoreIdentifier store)
        {
            // Access granted: no-op.
        }

        private static void DenyAccess(ISystemContext context, CertificateStoreIdentifier store)
        {
            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
        }

        private SessionSystemContext CreateContext(NodeId sessionId)
        {
            return new SessionSystemContext(m_telemetry)
            {
                SessionId = sessionId,
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable(),
                EncodeableFactory = EncodeableFactory.Create()
            };
        }

        private ServerSystemContext CreateServerContext(
            ISessionManager sessionManager,
            NodeId sessionId,
            TimeProvider timeProvider = null)
        {
            var server = new Mock<IServerInternal>();
            if (timeProvider != null)
            {
                server.As<ITimeProviderProvider>().Setup(s => s.TimeProvider).Returns(timeProvider);
            }
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(m_telemetry);
            server.Setup(s => s.SessionManager).Returns(sessionManager);
            return new ServerSystemContext(server.Object) { SessionId = sessionId };
        }

        private static ISession CreateSession(NodeId sessionId)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            return session.Object;
        }

        private static ByteString EncodeTrustListPayload(ISystemContext context, TrustListDataType trustList)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext(
                context.Telemetry,
                context.EncodeableFactory)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris
            };
            using var strm = new MemoryStream();
            using (var encoder = new BinaryEncoder(strm, messageContext, true))
            {
                encoder.WriteEncodeable(null, trustList);
            }
            return ByteString.From(strm.ToArray());
        }

        private static TrustListDataType DecodeTrustListPayload(ISystemContext context, ByteString data)
        {
            IServiceMessageContext messageContext = new ServiceMessageContext(
                context.Telemetry,
                context.EncodeableFactory)
            {
                NamespaceUris = context.NamespaceUris,
                ServerUris = context.ServerUris
            };
            var trustList = new TrustListDataType();
            using var strm = new MemoryStream(data.ToArray());
            using (var decoder = new BinaryDecoder(strm, messageContext))
            {
                trustList.Decode(decoder);
            }
            return trustList;
        }
    }
}
