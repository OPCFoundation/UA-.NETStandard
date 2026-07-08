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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
            if (Directory.Exists(m_basePath))
            {
                Directory.Delete(m_basePath, true);
            }
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
        public void OpenWriteWithoutEraseFlagReturnsBadNotWritable()
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
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
                    (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                    ref fileHandle),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void OpenTwiceReplacesPreviousSessionAndInvalidatesOldHandle()
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

            Assert.That(ServiceResult.IsGood(secondOpenResult), Is.True);
            Assert.That(handleB, Is.Not.EqualTo(handleA));
            // The last open always wins: the open count stays at 1, and the
            // handle from the first (discarded) session is no longer valid.
            Assert.That(node.OpenCount.Value, Is.EqualTo((ushort)1));

            ByteString data = default;
            ServiceResult readWithStaleHandleResult = node.Read.OnCall(
                contextB,
                node.Read,
                node.NodeId,
                handleA,
                1024,
                ref data);
            Assert.That(
                readWithStaleHandleResult.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
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
        public void ReadExceedingMaxTrustListSizeReturnsBadEncodingLimitsExceeded()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 10);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(context, node.Open, node.NodeId, (byte)OpenFileMode.Read, ref fileHandle);

            ByteString data = default;
            ServiceResult result = node.Read.OnCall(
                context,
                node.Read,
                node.NodeId,
                fileHandle,
                20,
                ref data);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ByteString payload = ByteString.From(new byte[] { 1, 2, 3, 4 });
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                CancellationToken.None).ConfigureAwait(false);

            ByteString payload = ByteString.From(new byte[] { 1, 2, 3, 4 });
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ByteString payload = ByteString.From(new byte[] { 1, 2, 3 });
            ServiceResult result = node.Write.OnCall(
                context,
                node.Write,
                node.NodeId,
                fileHandle + 1,
                payload);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ByteString payload = ByteString.From(new byte[] { 1, 2, 3 });
            ServiceResult result = node.Write.OnCall(
                otherContext,
                node.Write,
                node.NodeId,
                fileHandle,
                payload);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void WriteWithoutWriteAccessThrowsBadUserAccessDenied()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, allowWrite: false);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            ByteString payload = ByteString.From(new byte[] { 1 });
            Assert.That(
                () => node.Write.OnCall(context, node.Write, node.NodeId, 1, payload),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void WriteExceedingMaxTrustListSizeReturnsBadEncodingLimitsExceeded()
        {
            TrustListState node = CreateNode();
            CreateTrustList(node, maxTrustListSize: 5);
            ISystemContext context = CreateContext(new NodeId(Guid.NewGuid(), 1));

            uint fileHandle = 0;
            node.Open.OnCall(
                context,
                node.Open,
                node.NodeId,
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ByteString payload = ByteString.From(new byte[10]);
            ServiceResult result = node.Write.OnCall(
                context,
                node.Write,
                node.NodeId,
                fileHandle,
                payload);

            Assert.That(
                result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            bool restartRequired = false;
            ServiceResult result = node.CloseAndUpdate.OnCall(
                context,
                node.CloseAndUpdate,
                node.NodeId,
                fileHandle + 1,
                ref restartRequired);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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
                (byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting),
                ref fileHandle);

            ByteString payload = ByteString.From([0x01, 0x02, 0x03, 0x04, 0x05]);
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
                Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
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
        public async Task AddCertificateAsyncAddsToIssuerStoreAsync()
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

            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            using ICertificateStore issuerStore = m_issuerStore.OpenStore(m_telemetry);
            using CertificateCollection found = await issuerStore
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(1));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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
                Is.EqualTo((StatusCode)StatusCodes.BadCertificateInvalid));
        }

        [Test]
        public void AddCertificateWhileSessionOpenReturnsBadInvalidState()
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void RemoveCertificateWhileSessionOpenReturnsBadInvalidState()
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

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
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
            int maxTrustListSize = 0)
        {
            TrustList.SecureAccess readAccess = allowRead ? AllowAccess : DenyAccess;
            TrustList.SecureAccess writeAccess = allowWrite ? AllowAccess : DenyAccess;

            return new TrustList(
                node,
                m_trustedStore,
                m_issuerStore,
                readAccess,
                writeAccess,
                m_telemetry,
                maxTrustListSize);
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
                EncodeableFactory = Opc.Ua.EncodeableFactory.Create()
            };
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
