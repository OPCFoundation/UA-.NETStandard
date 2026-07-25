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
using Opc.Ua.Client;
using Opc.Ua.Tests;

namespace Opc.Ua.XRegistry.Client.Tests
{
    /// <summary>
    /// Verifies the xRegistry client surface. Every wire interaction is driven through the
    /// source-generated ObjectType proxies, so the session is mocked at the service members the
    /// proxies use (<c>CallAsync</c> and <c>ReadAsync</c>) rather than at the extension methods.
    /// </summary>
    [TestFixture]
    [Category("XRegistry")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class XRegistryClientTests
    {
        [Test]
        public void ConstructorNullSessionThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new GenericXRegistryClient(null!, TestNamespaceUri, CreateTelemetry()));

            Assert.That(ex.ParamName, Is.EqualTo("session"));
        }

        [Test]
        public void ConstructorNullTelemetryThrowsArgumentNullException()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => new GenericXRegistryClient(CreateSession().Object, TestNamespaceUri, null!));

            Assert.That(ex.ParamName, Is.EqualTo("telemetry"));
        }

        [Test]
        public void ConstructorEmptyNamespaceUriThrowsArgumentException()
        {
            ArgumentException ex = Assert.Throws<ArgumentException>(
                () => new GenericXRegistryClient(CreateSession().Object, string.Empty, CreateTelemetry()));

            Assert.That(ex.ParamName, Is.EqualTo("registryNamespaceUri"));
        }

        [Test]
        public void ConstructorUnknownNamespaceThrowsBadNodeIdUnknown()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => new GenericXRegistryClient(
                    CreateSession().Object, "http://example.org/UA/Absent/", CreateTelemetry()));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void ConstructorResolvesTheRegistryNamespaceIndex()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());

            Assert.Multiple(() =>
            {
                Assert.That(client.NamespaceIndex, Is.EqualTo(1));
                Assert.That(client.RegistryNamespaceUri, Is.EqualTo(TestNamespaceUri));
                Assert.That(client.Session, Is.Not.Null);
            });
        }

        [Test]
        public void DefaultConstructorBindsTheBaseRegistryNamespace()
        {
            var client = new GenericXRegistryClient(CreateSession().Object, CreateTelemetry());

            Assert.That(client.RegistryNamespaceUri, Is.EqualTo(XRegistryWellKnown.XRegistryNamespaceUri));
        }

        [Test]
        public void ProxyAccessorsRejectNullNodeIds()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());

            Assert.Multiple(() =>
            {
                Assert.That(() => client.GetRegistry(NodeId.Null), Throws.ArgumentException);
                Assert.That(() => client.GetGroup(NodeId.Null), Throws.ArgumentException);
                Assert.That(() => client.GetResource(NodeId.Null), Throws.ArgumentException);
            });
        }

        /// <summary>
        /// ResourceType is a FileType and the group/registry types are Folders, so the generated
        /// proxy chain must expose the inherited surface the lifecycle relies on.
        /// </summary>
        [Test]
        public void GeneratedProxiesInheritTheirBaseTypeProxies()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());

            Assert.Multiple(() =>
            {
                Assert.That(client.GetResource(new NodeId(42u, 1)), Is.InstanceOf<FileTypeClient>());
                Assert.That(client.GetGroup(new NodeId(43u, 1)), Is.InstanceOf<FolderTypeClient>());
                Assert.That(client.GetRegistry(new NodeId(44u, 1)), Is.InstanceOf<FolderTypeClient>());
            });
        }

        [Test]
        public void ResolveResourceEmptyIdThrowsArgumentException()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => client.ResolveResourceAsync(default),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.ResolveResourceAsync(ByteString.From([])),
                    Throws.ArgumentException);
            });
        }

        [Test]
        public async Task ResolveResourceReturnsTheDocumentFromTheFastPathNodeAsync()
        {
            byte[] document = [1, 2, 3, 4];
            ByteString contentId = ByteString.From([0xAA, 0xBB]);
            Mock<ISession> session = CreateSession();
            ReadValueId? requested = null;
            SetupRead(session, new DataValue(new Variant(ByteString.From(document))), r => requested = r);

            GenericXRegistryClient client = CreateClient(session);
            ByteString resolved = await client.ResolveResourceAsync(contentId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.IsNull, Is.False);
                Assert.That(resolved.Span.ToArray(), Is.EqualTo(document));
                Assert.That(requested, Is.Not.Null);
                Assert.That(requested!.NodeId, Is.EqualTo(new NodeId(contentId, 1)),
                    "The fast path addresses the resource by an Opaque NodeId built from the content id.");
            });
        }

        [Test]
        public async Task ResolveResourceReturnsNullWhenTheNodeIdIsUnknownAsync()
        {
            ByteString resolved = await ResolveWithReadFaultAsync(StatusCodes.BadNodeIdUnknown)
                .ConfigureAwait(false);

            Assert.That(resolved.IsNull, Is.True);
        }

        [Test]
        public async Task ResolveResourceReturnsNullWhenTheNodeIdIsInvalidAsync()
        {
            ByteString resolved = await ResolveWithReadFaultAsync(StatusCodes.BadNodeIdInvalid)
                .ConfigureAwait(false);

            Assert.That(resolved.IsNull, Is.True);
        }

        [Test]
        public void ResolveResourcePropagatesOtherServiceFaults()
        {
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                () => ResolveWithReadFaultAsync(StatusCodes.BadUserAccessDenied));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void RegisterResourceValidatesItsArguments()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());
            var group = new NodeId(1u, 1);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => client.RegisterResourceAsync(NodeId.Null, "id", new byte[1]),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.RegisterResourceAsync(group, string.Empty, new byte[1]),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.RegisterResourceAsync(group, "id", new byte[1], chunkSize: 0),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [TestCase(4, 4, 1)]
        [TestCase(8, 4, 2)]
        [TestCase(9, 4, 3)]
        [TestCase(0, 4, 0)]
        public async Task RegisterResourceStreamsTheDocumentInChunksAsync(
            int documentLength, int chunkSize, int expectedWrites)
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            (NodeId resourceNodeId, string assignedVersionId) = await client
                .RegisterResourceAsync(
                    new NodeId(1u, 1), "urn:resource", new byte[documentLength], chunkSize: chunkSize)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resourceNodeId, Is.EqualTo(s_createdResourceNodeId));
                Assert.That(assignedVersionId, Is.EqualTo("7"));
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Write), Is.EqualTo(expectedWrites),
                    "One Write per chunk.");
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Close), Is.EqualTo(1),
                    "The write handle is always closed.");
            });
        }

        /// <summary>
        /// A domain registry client extends the abstract base and inherits the whole lifecycle,
        /// which is the extension point domain registries such as the PubSub Schema Registry use.
        /// </summary>
        [Test]
        public async Task DomainClientInheritsTheBaseLifecycleAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            var domain = new TestDomainRegistryClient(session.Object, CreateTelemetry());
            (NodeId resourceNodeId, string assignedVersionId) = await domain
                .RegisterDomainResourceAsync(new NodeId(1u, 1), new byte[4]).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(domain, Is.InstanceOf<XRegistryClient>());
                Assert.That(resourceNodeId, Is.EqualTo(s_createdResourceNodeId));
                Assert.That(assignedVersionId, Is.EqualTo("7"));
                Assert.That(domain.DomainPrefixApplied, Is.True);
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Write), Is.EqualTo(1));
            });
        }

        /// <summary>
        /// ResourceType is a FileType, so a document is read back through the inherited
        /// Open/Read/Close methods, chunked until the server returns a short read.
        /// </summary>
        [Test]
        public async Task ReadDocumentStreamsTheResourceUntilAShortReadAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            ResourceTypeClient resource = client.GetResource(new NodeId(7u, 1));

            ByteString document = await resource.ReadDocumentAsync(chunkSize: 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(document.IsNull, Is.False);
                Assert.That(document.Span.ToArray(), Is.EqualTo(s_readChunk),
                    "A short read terminates the loop.");
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Open), Is.EqualTo(1));
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Close), Is.EqualTo(1),
                    "The read handle is always closed.");
            });
        }

        [Test]
        public void ResourceExtensionsValidateTheirArguments()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());
            ResourceTypeClient resource = client.GetResource(new NodeId(7u, 1));

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => resource.WriteDocumentAsync(1, new byte[1], chunkSize: 0).AsTask(),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => resource.ReadDocumentAsync(chunkSize: 0).AsTask(),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => ((ResourceTypeClient)null!).WriteDocumentAsync(1, new byte[1]).AsTask(),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => ((ResourceTypeClient)null!).ReadDocumentAsync().AsTask(),
                    Throws.ArgumentNullException);
            });
        }

        private static Task<ByteString> ResolveWithReadFaultAsync(StatusCode statusCode)
        {
            Mock<ISession> session = CreateSession();
            SetupReadFault(session, statusCode);

            GenericXRegistryClient client = CreateClient(session);
            return client.ResolveResourceAsync(ByteString.From([0x01]));
        }

        private static GenericXRegistryClient CreateClient(Mock<ISession> session)
        {
            return new GenericXRegistryClient(session.Object, TestNamespaceUri, CreateTelemetry());
        }

        private static ITelemetryContext CreateTelemetry()
        {
            return NUnitTelemetryContext.Create();
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(TestNamespaceUri);
            return namespaceUris;
        }

        private static Mock<ISession> CreateSession()
        {
            NamespaceTable namespaceUris = CreateNamespaceTable();

            var session = new Mock<ISession>();
            session.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);

            var messageContext = new Mock<IServiceMessageContext>();
            messageContext.SetupGet(c => c.NamespaceUris).Returns(namespaceUris);
            messageContext.SetupGet(c => c.Factory).Returns(EncodeableFactory.Create());
            session.SetupGet(s => s.MessageContext).Returns(messageContext.Object);
            return session;
        }

        private static void SetupRead(
            Mock<ISession> session,
            DataValue value,
            Action<ReadValueId>? capture = null)
        {
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, items, _) =>
                    {
                        if (capture != null && items.Count > 0)
                        {
                            capture(items[0]);
                        }
                    })
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValue[] { value },
                    DiagnosticInfos = [],
                    ResponseHeader = new ResponseHeader()
                });
        }

        private static void SetupReadFault(Mock<ISession> session, StatusCode statusCode)
        {
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results = new DataValue[] { DataValue.FromStatusCode(statusCode) },
                    DiagnosticInfos = [],
                    ResponseHeader = new ResponseHeader()
                });
        }

        /// <summary>
        /// Answers every Call with outputs shaped for the method being invoked, and records the
        /// requests so a test can assert the lifecycle the client drove.
        /// </summary>
        private static void SetupCall(Mock<ISession> session, List<CallMethodRequest> calls)
        {
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        CallMethodRequest request = requests[0];
                        calls.Add(request);
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            Results = new CallMethodResult[]
                            {
                                new()
                                {
                                    StatusCode = StatusCodes.Good,
                                    OutputArguments = OutputsFor(request)
                                }
                            },
                            DiagnosticInfos = [],
                            ResponseHeader = new ResponseHeader()
                        });
                    });
        }

        private static ArrayOf<Variant> OutputsFor(CallMethodRequest request)
        {
            // CreateResource(ResourceId, VersionId, RequestFileOpen) returns
            // (ResourceNodeId, AssignedVersionId, FileHandle).
            if (request.InputArguments.Count == 3)
            {
                return new Variant[]
                {
                    new(s_createdResourceNodeId),
                    new("7"),
                    new(99u)
                };
            }

            // FileType Open(mode) returns a handle; Read(handle, length) returns the chunk.
            if (request.InputArguments.Count == 1)
            {
                return new Variant[] { new(99u) };
            }
            if (request.InputArguments.Count == 2 &&
                request.InputArguments[1].TryGetValue(out int _))
            {
                return new Variant[] { new(ByteString.From(s_readChunk)) };
            }
            return [];
        }

        private static int CountCalls(List<CallMethodRequest> calls, ExpandedNodeId methodId)
        {
            NodeId expected = ExpandedNodeId.ToNodeId(methodId, CreateNamespaceTable());

            int count = 0;
            foreach (CallMethodRequest call in calls)
            {
                if (call.MethodId == expected)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Stands in for a domain registry client (Schema Registry, WoT registry): it extends the
        /// abstract base and adds domain naming without re-implementing the lifecycle.
        /// </summary>
        private sealed class TestDomainRegistryClient : XRegistryClient
        {
            public TestDomainRegistryClient(ISession session, ITelemetryContext telemetry)
                : base(session, TestNamespaceUri, telemetry)
            {
            }

            public bool DomainPrefixApplied { get; private set; }

            public Task<(NodeId ResourceNodeId, string AssignedVersionId)> RegisterDomainResourceAsync(
                NodeId groupNodeId,
                ReadOnlyMemory<byte> document)
            {
                DomainPrefixApplied = true;
                return RegisterResourceAsync(groupNodeId, "urn:domain:resource", document);
            }
        }

        private const string TestNamespaceUri = XRegistryWellKnown.XRegistryNamespaceUri;
        private static readonly NodeId s_createdResourceNodeId = new(4711u, 1);
        private static readonly byte[] s_readChunk = [0x0A, 0x0B];
    }
}
