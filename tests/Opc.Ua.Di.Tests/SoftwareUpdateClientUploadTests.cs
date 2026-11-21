// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Client;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Argument-validation tests for
    /// <see cref="SoftwareUpdateClient.UploadPackageAsync(Stream, string, int?, CancellationToken)"/>
    /// and its byte-array overload. The happy-path call sequence is
    /// covered end-to-end by
    /// <c>SoftwareUpdateClientUploadIntegrationTests</c>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("ClientHelpers")]
    [Category("FileTransfer")]
    public sealed class SoftwareUpdateClientUploadTests
    {
        [Test]
        public void UploadPackageAsyncThrowsOnNullStream()
        {
            SoftwareUpdateClient client = CreateClient();
            Assert.That(
                () => client.UploadPackageAsync((Stream)null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void UploadPackageAsyncThrowsOnNullByteArray()
        {
            SoftwareUpdateClient client = CreateClient();
            Assert.That(
                () => client.UploadPackageAsync((byte[])null!),
                Throws.InstanceOf<ArgumentNullException>());
        }

        [Test]
        public void UploadPackageAsyncThrowsOnNonReadableStream()
        {
            SoftwareUpdateClient client = CreateClient();
            using var stream = new MemoryStream();
            stream.Close();
            Assert.That(
                () => client.UploadPackageAsync(stream),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void UploadPackageAsyncThrowsOnZeroChunkSize()
        {
            SoftwareUpdateClient client = CreateClient();
            Assert.That(
                () => client.UploadPackageAsync(
                    [], chunkSizeBytes: 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void UploadPackageAsyncThrowsOnNegativeChunkSize()
        {
            SoftwareUpdateClient client = CreateClient();
            Assert.That(
                () => client.UploadPackageAsync(
                    [], chunkSizeBytes: -1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DefaultUploadChunkSizeIs8KiB()
        {
            Assert.That(SoftwareUpdateClient.DefaultUploadChunkSizeBytes,
                Is.EqualTo(8 * 1024));
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------

        private static SoftwareUpdateClient CreateClient()
        {
            var sessionMock = new Mock<ISession>();
            var nsTable = new NamespaceTable();
            nsTable.GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);
            sessionMock.SetupGet(s => s.NamespaceUris).Returns(nsTable);
            return new SoftwareUpdateClient(
                sessionMock.Object,
                new NodeId("su-1", 2),
                new Mock<ITelemetryContext>().Object);
        }
    }
}
