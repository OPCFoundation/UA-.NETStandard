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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Drives <see cref="SoftwareUpdateClient.UploadPackageAsync(Stream, string, int?, CancellationToken)"/>
    /// against the in-process <see cref="DiServerFixture"/> via the
    /// <see cref="DiInProcessSessionBridge"/> Moq-backed
    /// <see cref="ISession"/> so the client + server wiring are exercised
    /// together without standing up a real TCP endpoint.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("SoftwareUpdate")]
    [Category("FileTransfer")]
    [Category("Integration")]
    public sealed class SoftwareUpdateClientUploadIntegrationTests
    {
        private DiServerFixture m_fixture = null!;
        private MemoryPackageStore m_store = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_store = new MemoryPackageStore();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task UploadPackageAsyncCommitsPayloadToPackageStore()
        {
            (NodeState su, _) = await CreateDeviceWithSuAsync("UploadE2E")
                .ConfigureAwait(false);

            Mock<ISession> sessionMock = DiInProcessSessionBridge.Build(m_fixture);
            var client = new SoftwareUpdateClient(
                sessionMock.Object, su.NodeId, NullTelemetry());

            byte[] payload = MakeSequentialPayload(12_288);
            const string packageId = "upload-integration-1";

            long uploaded = await client
                .UploadPackageAsync(payload, packageId)
                .ConfigureAwait(false);

            Assert.That(uploaded, Is.EqualTo(payload.LongLength));
            byte[] stored = await ReadAllAsync(m_store, packageId)
                .ConfigureAwait(false);
            Assert.That(stored, Is.EqualTo(payload),
                "Stored package bytes must match the uploaded payload.");
        }

        [Test]
        public async Task UploadPackageAsyncStreamOverloadStreamsViaCustomChunkSize()
        {
            (NodeState su, _) = await CreateDeviceWithSuAsync("UploadStream")
                .ConfigureAwait(false);

            Mock<ISession> sessionMock = DiInProcessSessionBridge.Build(m_fixture);
            var client = new SoftwareUpdateClient(
                sessionMock.Object, su.NodeId, NullTelemetry());

            byte[] payload = MakeSequentialPayload(5000);
            using var ms = new MemoryStream(payload, writable: false);

            long uploaded = await client.UploadPackageAsync(
                ms, suggestedPackageId: "stream-upload", chunkSizeBytes: 1024)
                .ConfigureAwait(false);

            Assert.That(uploaded, Is.EqualTo(payload.LongLength));
            byte[] stored = await ReadAllAsync(m_store, "stream-upload")
                .ConfigureAwait(false);
            Assert.That(stored, Is.EqualTo(payload));
        }

        [Test]
        public async Task UploadPackageAsyncEmptyPayloadStillCommitsEmptyPackage()
        {
            (NodeState su, _) = await CreateDeviceWithSuAsync("UploadEmpty")
                .ConfigureAwait(false);

            Mock<ISession> sessionMock = DiInProcessSessionBridge.Build(m_fixture);
            var client = new SoftwareUpdateClient(
                sessionMock.Object, su.NodeId, NullTelemetry());

            long uploaded = await client
                .UploadPackageAsync([], "empty-package")
                .ConfigureAwait(false);

            Assert.That(uploaded, Is.Zero);
            byte[] stored = await ReadAllAsync(m_store, "empty-package")
                .ConfigureAwait(false);
            Assert.That(stored, Has.Length.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private async Task<(NodeState Su, PackageLoadingState Loading)>
            CreateDeviceWithSuAsync(string deviceName)
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    deviceName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);
            builder.WithSoftwareUpdate(m_store);

            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var loading = (PackageLoadingState)su.FindChild(
                ctx, new QualifiedName("Loading", diNs))!;
            return (su, loading);
        }

        private static async Task<byte[]> ReadAllAsync(
            MemoryPackageStore store, string packageId)
        {
            // 'using' (sync dispose) instead of 'await using' so the
            // assertion helper compiles on net48 where System.IO.Stream
            // does not implement IAsyncDisposable.
            using Stream stream = await store
                .OpenReadAsync(packageId, CancellationToken.None)
                .ConfigureAwait(false);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }

        private static ITelemetryContext NullTelemetry()
        {
            return new Mock<ITelemetryContext>().Object;
        }

        private static byte[] MakeSequentialPayload(int length)
        {
            var bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i & 0xFF);
            }
            return bytes;
        }
    }
}
