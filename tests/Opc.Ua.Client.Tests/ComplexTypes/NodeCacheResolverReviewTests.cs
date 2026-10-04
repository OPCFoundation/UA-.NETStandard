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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.ComplexTypes
{
    [TestFixture]
    [Category("Client")]
    public sealed class NodeCacheResolverReviewTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task DictionaryNamespaceLookupIsolatesNodeErrorsButNotCancellationAsync(bool cancel)
        {
            using var session = SessionMock.Create();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ushort ns = session.NamespaceUris.GetIndexOrAppend("urn:test:good-dictionary");
            var stale = new NodeId(1u, ns);
            var good = new NodeId(2u, ns);
            var namespaceProperty = new NodeId(3u, ns);
            var description = new NodeId(4u, ns);
            var cache = new Mock<INodeCache>();
            cache.SetupGet(value => value.NamespaceUris).Returns(session.NamespaceUris);
            cache.Setup(value => value.GetReferencesAsync(
                    It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<bool>(),
                    false, It.IsAny<CancellationToken>()))
                .Returns((NodeId id, NodeId reference, bool inverse, bool _, CancellationToken ct) =>
                {
                    ArrayOf<INode> result = [];
                    if (id == ObjectIds.OPCBinarySchema_TypeSystem)
                    {
                        result = [new Node { NodeId = stale }, new Node { NodeId = good }];
                    }
                    else if (reference == ReferenceTypeIds.HasProperty)
                    {
                        if (id == stale)
                        {
                            if (cancel)
                            {
                                timeout.Cancel();
                                throw new OperationCanceledException(ct);
                            }
                            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                        }
                        result =
                        [
                            new Node { NodeId = namespaceProperty, BrowseName = new QualifiedName(BrowseNames.NamespaceUri) }
                        ];
                    }
                    else if (inverse)
                    {
                        result = [new Node { NodeId = ObjectIds.OPCBinarySchema_TypeSystem }];
                    }
                    else if (id == good)
                    {
                        result = [new Node { NodeId = description }];
                    }
                    return new ValueTask<ArrayOf<INode>>(result);
                });
            cache.Setup(value => value.GetReferencesAsync(
                    It.IsAny<ArrayOf<NodeId>>(), It.IsAny<ArrayOf<NodeId>>(),
                    false, false, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<INode>>([]));
            cache.Setup(value => value.GetValuesAsync(
                    It.IsAny<ArrayOf<NodeId>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<NodeId> ids, CancellationToken _) =>
                    new ValueTask<ArrayOf<DataValue>>(ids.ConvertAll(id =>
                        new DataValue(id == namespaceProperty ? "urn:test:good-dictionary" : "GoodType"))));
            var schema = ByteString.From("""
                <opc:TypeDictionary xmlns:opc="http://opcfoundation.org/BinarySchema/"
                    TargetNamespace="urn:test:good-dictionary" DefaultByteOrder="LittleEndian">
                    <opc:StructuredType Name="GoodType">
                        <opc:Field Name="Value" TypeName="opc:Int32" />
                    </opc:StructuredType>
                </opc:TypeDictionary>
                """u8);
            session.Channel.Setup(channel => channel.SendRequestAsync(
                    It.IsAny<ReadRequest>(), It.IsAny<CancellationToken>()))
                .Returns((ReadRequest request, CancellationToken _) =>
                    new ValueTask<IServiceResponse>(new ReadResponse
                    {
                        Results = request.NodesToRead.ConvertAll(item => item.NodeId == good
                            ? new DataValue(schema)
                            : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown))
                    }));
            using var resolver = new NodeCacheResolver(session, cache.Object, NUnitTelemetryContext.Create());

            if (cancel)
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await resolver.LoadDataTypeSystem(ct: timeout.Token).ConfigureAwait(false));
                return;
            }

            IReadOnlyDictionary<NodeId, DataDictionary> dictionaries = await resolver
                .LoadDataTypeSystem(ct: timeout.Token).ConfigureAwait(false);

            Assert.That(dictionaries.Keys, Is.EquivalentTo([good]));
            Assert.That(dictionaries[good].TypeDictionary.TargetNamespace, Is.EqualTo("urn:test:good-dictionary"));
            Assert.That(dictionaries[good].DataTypes[description].Name, Is.EqualTo("GoodType"));
            Assert.That(dictionaries[good].GetSchema(description), Does.Contain("GoodType"));
            session.Channel.Verify(channel => channel.SendRequestAsync(
                It.Is<ReadRequest>(request => request.NodesToRead.Contains(item => item.NodeId == stale)),
                It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
