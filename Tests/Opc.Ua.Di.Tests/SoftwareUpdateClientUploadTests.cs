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
                    Array.Empty<byte>(), chunkSizeBytes: 0),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void UploadPackageAsyncThrowsOnNegativeChunkSize()
        {
            SoftwareUpdateClient client = CreateClient();
            Assert.That(
                () => client.UploadPackageAsync(
                    Array.Empty<byte>(), chunkSizeBytes: -1),
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
