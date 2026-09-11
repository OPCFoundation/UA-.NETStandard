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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Bridge.Native;
using Opc.Ua.XRegistry.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Native
{
    [TestFixture]
    [Category("XRegistryNative")]
    public sealed class XRegistryNativeContractTests
    {
        [Test]
        public void ConstructionRequiresExplicitEndpointAndValidOptions()
        {
            var endpoint = new Mock<IXRegistryEndpoint>();
            var options = new XRegistryBridgeNativeOptions();
            Assert.Multiple(() =>
            {
                Assert.That(() => new XRegistryBridgeNodeManagerFactory(null!, options), Throws.ArgumentNullException);
                Assert.That(() => new XRegistryBridgeNodeManagerFactory(endpoint.Object, null!),
                    Throws.ArgumentNullException);
                Assert.That(() => new XRegistryBridgeNodeManagerFactory(endpoint.Object,
                    options with { NamespaceUri = XRegistryWellKnown.XRegistryNamespaceUri }),
                    Throws.ArgumentException);
                Assert.That(() => new XRegistryBridgeNodeManagerFactory(endpoint.Object,
                    options with { ChunkSize = 0 }), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new XRegistryBridgeNodeManagerFactory(endpoint.Object,
                    options with { MaxDocumentBytes = options.MaxMessageBytes + 1 }),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(() => new XRegistryBridgeNodeManagerFactory(endpoint.Object, options with
                {
                    MaxMessageBytes = int.MaxValue,
                    MaxBufferedBytes = int.MaxValue
                }), Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(options.RequireEncryptedWrites, Is.True);
                Assert.That(() => options with { RequireEncryptedWrites = false }, Throws.ArgumentException);
                Assert.That(options.AuthorizeCallerAsync, Is.Null);
                Assert.That(options.RootAddress.NamespaceUri, Is.EqualTo(options.NamespaceUri));
            });
        }

        [Test]
        public void AdapterRejectsNullSessionRootOptionsAndTelemetry()
        {
            var namespaces = new NamespaceTable();
            ushort ns = namespaces.GetIndexOrAppend("urn:contract:test");
            var session = new Mock<ISession>();
            session.Setup(value => value.NamespaceUris).Returns(namespaces);
            var root = new NodeId("root", ns);
            var options = new XRegistryBridgeNativeOptions();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Assert.Multiple(() =>
            {
                Assert.That(() => new XRegistryOpcUaEndpoint((ISession)null!, root, options, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new XRegistryOpcUaEndpoint(session.Object, NodeId.Null, options, telemetry),
                    Throws.ArgumentException);
                Assert.That(() => new XRegistryOpcUaEndpoint(session.Object, root, null!, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new XRegistryOpcUaEndpoint(session.Object, root, options, null!),
                    Throws.ArgumentNullException);
            });
        }

        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void ProjectionPreservesBoundaryEpochs(uint epoch)
        {
            using var document = JsonDocument.Parse(
                "{\"epoch\":" + epoch.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            Assert.That(XRegistryNativeJson.Epoch(document.RootElement), Is.EqualTo(epoch));
        }

        [TestCase(/*lang=json,strict*/ """{"epoch":4294967296}""")]
        [TestCase(/*lang=json,strict*/ """{"epoch":-1}""")]
        [TestCase(/*lang=json,strict*/ """{"epoch":null}""")]
        [TestCase(/*lang=json,strict*/ """{"epoch":"1"}""")]
        [TestCase("{}")]
        public void ProjectionRejectsWideOrMissingEpochInsteadOfTruncating(string json)
        {
            using var document = JsonDocument.Parse(json);
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => XRegistryNativeJson.Epoch(document.RootElement));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void LabelsNeverCoerceTypedValuesToStrings()
        {
            using var valid = JsonDocument.Parse("""{"labels":{"key":"value"}}""");
            using var invalid = JsonDocument.Parse("""{"labels":{"key":{"nested":42}}}""");
            Assert.That(XRegistryNativeJson.Labels(valid.RootElement)["key"], Is.EqualTo("value"));
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => XRegistryNativeJson.Labels(invalid.RootElement));
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void CollectionKeysKeepIdenticalIdsDistinctWithoutRenaming()
        {
            using var definition = JsonDocument.Parse("""{"singular":"item","resources":{}}""");
            using var metadata = JsonDocument.Parse("""{"epoch":0}""");
            var left = new XRegistryNativeGroup("/left/same", "left", "same", definition.RootElement,
                metadata.RootElement, []);
            var right = new XRegistryNativeGroup("/right/same", "right", "same", definition.RootElement,
                metadata.RootElement, []);
            Assert.Multiple(() =>
            {
                Assert.That(left.GroupId, Is.Not.EqualTo(right.GroupId));
                Assert.That(Uri.UnescapeDataString(left.GroupId), Is.EqualTo(left.Path));
                Assert.That(Uri.UnescapeDataString(right.GroupId), Is.EqualTo(right.Path));
                Assert.That(left.Id, Is.EqualTo(right.Id));
                Assert.That(left.Xid, Is.EqualTo("/left/same"));
            });
        }

        [Test]
        public void FileBudgetRejectsThenRecoversAtExactLimits()
        {
            var options = new XRegistryBridgeNativeOptions { MaxOpenFiles = 1 };
            var budget = new XRegistryFileBudget(options);
            budget.ReserveHandle();
            ServiceResultException handles = Assert.Throws<ServiceResultException>(budget.ReserveHandle);
            budget.ReleaseHandle();
            Assert.That(budget.ReserveHandle, Throws.Nothing);
            budget.ReleaseHandle();
            budget.ReserveBytes(options.MaxBufferedBytes);
            ServiceResultException bytes = Assert.Throws<ServiceResultException>(() => budget.ReserveBytes(1));
            budget.ReleaseBytes(options.MaxBufferedBytes);
            Assert.That(() => budget.ReserveBytes(options.MaxBufferedBytes), Throws.Nothing);
            budget.ReleaseBytes(options.MaxBufferedBytes);
            Assert.Multiple(() =>
            {
                Assert.That(handles.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(bytes.StatusCode, Is.EqualTo(StatusCodes.BadOutOfMemory));
            });
        }

        [Test]
        public async Task LocalNamespaceUriReferencesAreResolvedWithoutFixedIndicesAsync()
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:unrelated:padding");
            ushort index = namespaces.GetIndexOrAppend("urn:actual:registry");
            var session = new Mock<ISession>();
            session.Setup(value => value.NamespaceUris).Returns(namespaces);
            session.Setup(value => value.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            References =
                            [
                                new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId("child", "urn:actual:registry"),
                                    BrowseName = new QualifiedName("child", index),
                                    NodeClass = NodeClass.Object,
                                    TypeDefinition = Ua.ObjectTypeIds.FolderType
                                }
                            ]
                        }
                    ]
                });
            ArrayOf<ReferenceDescription> references = await XRegistryOpcUaEndpoint.BrowseAsync(
                session.Object, new NodeId("root", index), new XRegistryBridgeNativeOptions(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(references.Count, Is.EqualTo(1));
                Assert.That(ExpandedNodeId.ToNodeId(references[0].NodeId, namespaces),
                    Is.EqualTo(new NodeId("child", index)));
            });
        }
    }
}
