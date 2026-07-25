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
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.XRegistry.Server.Tests
{
    /// <summary>
    /// Verifies the denial-of-service safety valves on the xRegistry registration Methods: the
    /// concurrent-upload-handle cap, the per-resource byte cap, and the registered-resource-node cap.
    /// The Method handlers are invoked directly against a node manager built over a mocked server.
    /// </summary>
    [TestFixture]
    public sealed class XRegistryRegistrationNodeManagerTests
    {
        /// <summary>
        /// CreateResource is rejected with BadTooManyOperations once the open-handle cap is reached.
        /// </summary>
        [Test]
        public void CreateResourceBeyondConcurrentUploadLimitReturnsBadTooManyOperations()
        {
            using XRegistryRegistrationNodeManager nm = CreateNodeManager(
                new XRegistryServerOptions { MaxConcurrentUploads = 2 });

            Assert.Multiple(() =>
            {
                Assert.That(CreateResource(nm, out _).Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(CreateResource(nm, out _).Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(CreateResource(nm, out _).Code, Is.EqualTo(StatusCodes.BadTooManyOperations));
            });
        }

        /// <summary>
        /// Write is rejected with BadRequestTooLarge once the cumulative per-handle byte cap is exceeded.
        /// </summary>
        [Test]
        public void WriteBeyondResourceByteLimitReturnsBadRequestTooLarge()
        {
            using XRegistryRegistrationNodeManager nm = CreateNodeManager(
                new XRegistryServerOptions { MaxResourceBytes = 4 });
            uint handle = CreateResourceHandle(nm);

            Assert.Multiple(() =>
            {
                Assert.That(Write(nm, handle, new byte[4]).Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(Write(nm, handle, new byte[1]).Code, Is.EqualTo(StatusCodes.BadRequestTooLarge));
            });
        }

        /// <summary>
        /// Close is rejected with BadTooManyOperations once the registered-resource-node cap is reached.
        /// </summary>
        [Test]
        public void CloseBeyondRegisteredResourceLimitReturnsBadTooManyOperations()
        {
            using XRegistryRegistrationNodeManager nm = CreateNodeManager(
                new XRegistryServerOptions
                {
                    MaxRegisteredResources = 2,
                    ContentIdProvider = new FakeContentIdProvider()
                });

            Assert.Multiple(() =>
            {
                Assert.That(Register(nm, 1, out _).Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(Register(nm, 2, out _).Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(Register(nm, 3, out _).Code, Is.EqualTo(StatusCodes.BadTooManyOperations));
            });
        }

        /// <summary>
        /// Deleting a registered resource frees a slot so a subsequent Close succeeds again.
        /// </summary>
        [Test]
        public void DeleteReleasesRegisteredResourceSlot()
        {
            using XRegistryRegistrationNodeManager nm = CreateNodeManager(
                new XRegistryServerOptions
                {
                    MaxRegisteredResources = 1,
                    ContentIdProvider = new FakeContentIdProvider()
                });

            StatusCode first = Register(nm, 1, out ByteString firstContentId);
            StatusCode full = Register(nm, 2, out _);
            StatusCode deleted = Delete(nm, firstContentId);
            StatusCode afterDelete = Register(nm, 3, out _);

            Assert.Multiple(() =>
            {
                Assert.That(first.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(full.Code, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(deleted.Code, Is.EqualTo(StatusCodes.Good));
                Assert.That(afterDelete.Code, Is.EqualTo(StatusCodes.Good));
            });
        }

        private static XRegistryRegistrationNodeManager CreateNodeManager(XRegistryServerOptions options)
        {
            Mock<IServerInternal> server = CreateServer(options.RegistryNamespaceUri);
            return new XRegistryRegistrationNodeManager(server.Object, null!, options);
        }

        private static Mock<IServerInternal> CreateServer(string namespaceUri)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(namespaceUri);
            var serverUris = new StringTable();
            var server = new Mock<IServerInternal>();
            var masterNodeManager = new Mock<IMasterNodeManager>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(new TypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private static StatusCode CreateResource(XRegistryRegistrationNodeManager nm, out uint handle)
        {
            var outputs = new List<Variant>();
            ServiceResult result = nm.OnCreateResource(
                nm.SystemContext,
                null!,
                NodeId.Null,
                new Variant[] { new Variant(string.Empty), new Variant(string.Empty) },
                outputs);
            handle = 0;
            if (outputs.Count > 0)
            {
                _ = outputs[0].TryGetValue(out handle);
            }
            return result.StatusCode;
        }

        private static uint CreateResourceHandle(XRegistryRegistrationNodeManager nm)
        {
            StatusCode status = CreateResource(nm, out uint handle);
            Assert.That(status.Code, Is.EqualTo(StatusCodes.Good));
            return handle;
        }

        private static StatusCode Write(XRegistryRegistrationNodeManager nm, uint handle, byte[] data)
        {
            var outputs = new List<Variant>();
            ServiceResult result = nm.OnWrite(
                nm.SystemContext,
                null!,
                NodeId.Null,
                new Variant[] { new Variant(handle), new Variant(ByteString.From(data)) },
                outputs);
            return result.StatusCode;
        }

        private static StatusCode Register(XRegistryRegistrationNodeManager nm, int seed, out ByteString contentId)
        {
            uint handle = CreateResourceHandle(nm);
            // Distinct document bytes per seed so the fake provider yields a distinct content id.
            byte[] document = [(byte)seed, (byte)(seed >> 8), 0xAB, 0xCD];
            Assert.That(Write(nm, handle, document).Code, Is.EqualTo(StatusCodes.Good));

            var outputs = new List<Variant>();
            ServiceResult result = nm.OnClose(
                nm.SystemContext,
                null!,
                NodeId.Null,
                new Variant[] { new Variant(handle), new Variant("avro") },
                outputs);
            contentId = default;
            if (outputs.Count > 0)
            {
                _ = outputs[0].TryGetValue(out contentId);
            }
            return result.StatusCode;
        }

        private static StatusCode Delete(XRegistryRegistrationNodeManager nm, ByteString contentId)
        {
            var outputs = new List<Variant>();
            ServiceResult result = nm.OnDelete(
                nm.SystemContext,
                null!,
                NodeId.Null,
                new Variant[] { new Variant(contentId) },
                outputs);
            return result.StatusCode;
        }

        private sealed class FakeContentIdProvider : IResourceContentIdProvider
        {
            public ByteString ComputeContentId(string format, ReadOnlySpan<byte> document)
            {
                return ByteString.From(document.ToArray());
            }

            public string? GetAlgorithm(string format)
            {
                return "test";
            }
        }
    }
}
