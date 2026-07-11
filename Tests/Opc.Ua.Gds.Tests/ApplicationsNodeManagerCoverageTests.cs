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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Deterministic request/response coverage tests for the GDS
    /// ApplicationsNodeManager validation and error branches. These exercise
    /// the concrete <see cref="StatusCode"/> results produced by the node
    /// manager for unknown applications, unsupported certificate groups and
    /// invalid application records, using only the shared in-process GDS
    /// fixture without any restart, reconnect or timing dependency.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Category("ApplicationsNodeManagerCoverage")]
    public sealed class ApplicationsNodeManagerCoverageTests : GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task ApplicationsNodeManagerCoverageSetUp()
        {
            m_directoryNodeId = ToNodeId(ObjectIds.Directory);

            // Register a single valid application shared by the read-only tests.
            ApplicationRecordDataType appRecord = CreateTestApplicationRecord("AnmCoverage");
            m_registeredAppId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task ApplicationsNodeManagerCoverageTearDown()
        {
            if (!m_registeredAppId.IsNull)
            {
                try
                {
                    await UnregisterApplicationAsync(m_registeredAppId).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        [Test]
        public async Task UpdateApplicationWithUnknownIdReturnsBadNotFoundAsync()
        {
            ApplicationRecordDataType record = CreateTestApplicationRecord("UpdateUnknown");
            record.ApplicationId = UnknownApplicationId();

            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_UpdateApplication),
                [new(new ExtensionObject(record))]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task UpdateApplicationWithInvalidUriReturnsBadInvalidArgumentAsync()
        {
            ApplicationRecordDataType record = CreateTestApplicationRecord("UpdateInvalidUri");
            NodeId applicationId = await RegisterApplicationAsync(record).ConfigureAwait(false);

            try
            {
                record.ApplicationId = applicationId;
                record.ApplicationUri = "not a valid application uri";

                CallMethodResult result = await CallDirectoryMethodAsync(
                    ToNodeId(MethodIds.Directory_UpdateApplication),
                    [new(new ExtensionObject(record))]).ConfigureAwait(false);

                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            }
            finally
            {
                await UnregisterApplicationAsync(applicationId).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task GetCertificateGroupsWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetCertificateGroups),
                [new(UnknownApplicationId())]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetCertificateGroupsForRegisteredApplicationReturnsEmptyGroupsAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetCertificateGroups),
                [new(m_registeredAppId)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.OutputArguments.Count, Is.EqualTo(1));

            var certificateGroupIds = (ArrayOf<NodeId>)result.OutputArguments[0];
            Assert.That(certificateGroupIds.IsEmpty, Is.True);
        }

        [Test]
        public async Task GetTrustListWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetTrustList),
                [new(UnknownApplicationId()), new(NodeId.Null)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetTrustListWithoutConfiguredGroupReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetTrustList),
                [new(m_registeredAppId), new(NodeId.Null)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetCertificateStatusWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetCertificateStatus),
                [new(UnknownApplicationId()), new(NodeId.Null), new(NodeId.Null)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetCertificateStatusWithoutConfiguredGroupReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetCertificateStatus),
                [new(m_registeredAppId), new(NodeId.Null), new(NodeId.Null)])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetCertificatesWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetCertificates),
                [new(UnknownApplicationId()), new(NodeId.Null)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task GetCertificatesWithUnknownGroupReturnsBadInvalidArgumentAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_GetCertificates),
                [new(m_registeredAppId), new(UnknownCertificateGroupId())])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task StartNewKeyPairRequestWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_StartNewKeyPairRequest),
                NewKeyPairArguments(UnknownApplicationId())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task StartNewKeyPairRequestWithoutConfiguredGroupReturnsBadInvalidArgumentAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_StartNewKeyPairRequest),
                NewKeyPairArguments(m_registeredAppId)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task StartSigningRequestWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_StartSigningRequest),
                SigningRequestArguments(UnknownApplicationId())).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task StartSigningRequestWithoutConfiguredGroupReturnsBadInvalidArgumentAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_StartSigningRequest),
                SigningRequestArguments(m_registeredAppId)).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task FinishRequestWithUnknownApplicationReturnsBadNotFoundAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_FinishRequest),
                [new(UnknownApplicationId()), new(NodeId.Null)]).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task FinishRequestWithUnknownRequestReturnsBadInvalidArgumentAsync()
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_FinishRequest),
                [new(m_registeredAppId), new(UnknownApplicationId())])
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        private static Variant[] NewKeyPairArguments(NodeId applicationId)
        {
            return
            [
                new(applicationId),
                new(NodeId.Null),
                new(NodeId.Null),
                new("CN=ApplicationsNodeManagerCoverage"),
                new(Array.Empty<string>()),
                new("PFX"),
                new(string.Empty)
            ];
        }

        private static Variant[] SigningRequestArguments(NodeId applicationId)
        {
            // The certificate request must be a ByteString Variant. A plain
            // byte[] would bind to Variant(object) and be typed as a Byte array,
            // which the method rejects with BadInvalidArgument before the
            // application check. The bytes are never parsed because the
            // application and certificate group checks return first.
            var certificateRequest = (ByteString)new byte[] { 0x30, 0x03, 0x02, 0x01, 0x00 };
            return
            [
                new(applicationId),
                new(NodeId.Null),
                new(NodeId.Null),
                new(certificateRequest)
            ];
        }

        private static NodeId UnknownCertificateGroupId()
        {
            return new NodeId(Guid.NewGuid());
        }

        private NodeId UnknownApplicationId()
        {
            // A well-formed Guid NodeId in the GDS applications namespace makes
            // GetApplication return null (reaching the application==null branch)
            // rather than throwing early from GetNodeIdGuid.
            return new NodeId(Guid.NewGuid(), m_registeredAppId.NamespaceIndex);
        }

        private async Task<CallMethodResult> CallDirectoryMethodAsync(
            NodeId methodId,
            Variant[] inputArguments,
            CancellationToken ct = default)
        {
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = inputArguments.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<NodeId> RegisterApplicationAsync(
            ApplicationRecordDataType appRecord,
            CancellationToken ct = default)
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_RegisterApplication),
                [new(new ExtensionObject(appRecord))],
                ct).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                $"RegisterApplication failed: {result.StatusCode}");
            return (NodeId)result.OutputArguments[0];
        }

        private async Task UnregisterApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            CallMethodResult result = await CallDirectoryMethodAsync(
                ToNodeId(MethodIds.Directory_UnregisterApplication),
                [new(applicationId)],
                ct).ConfigureAwait(false);

            if (!StatusCode.IsGood(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
        }

        private NodeId m_directoryNodeId;
        private NodeId m_registeredAppId;
    }
}
