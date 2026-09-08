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
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.XRegistry.Tests
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
        public void RegistryNodeIdAddressesTheWellKnownRoot()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());

            Assert.Multiple(() =>
            {
                Assert.That(client.RegistryNodeId.IsNull, Is.False);
                Assert.That(client.RegistryNodeId,
                    Is.EqualTo(new NodeId(XRegistryWellKnown.RegistryObject, 1)),
                    "The root sits at a well-known identifier in the registry namespace, so a " +
                    "caller reaches the group lifecycle without Browsing for it.");
            });
        }

        [Test]
        public void RegistryNodeIdUsesAnExplicitRootWhenSupplied()
        {
            var domainRoot = new NodeId(64100u, 1);

            var client = new GenericXRegistryClient(
                CreateSession().Object,
                TestNamespaceUri,
                domainRoot,
                CreateTelemetry());

            Assert.That(client.RegistryNodeId, Is.EqualTo(domainRoot),
                "A domain registry declares its own root, so an explicitly supplied NodeId must " +
                "win over the provisional well-known identifier.");
        }

        [Test]
        public void RegistryNodeIdFallsBackToTheWellKnownRootForANullExplicitRoot()
        {
            var client = new GenericXRegistryClient(
                CreateSession().Object,
                TestNamespaceUri,
                default,
                CreateTelemetry());

            Assert.That(client.RegistryNodeId,
                Is.EqualTo(new NodeId(XRegistryWellKnown.RegistryObject, 1)),
                "A null NodeId selects the well-known root, so the explicit-root overload stays " +
                "equivalent to the namespace-only constructor.");
        }

        [Test]
        public async Task AnExplicitRootDrivesTheGroupLifecycleAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);
            var domainRoot = new NodeId(64100u, 1);

            var client = new GenericXRegistryClient(
                session.Object,
                TestNamespaceUri,
                domainRoot,
                CreateTelemetry());
            await client.CreateGroupAsync(client.RegistryNodeId, "things")
                .ConfigureAwait(false);

            Assert.That(calls, Has.Count.EqualTo(1));
            Assert.That(calls[0].ObjectId, Is.EqualTo(domainRoot),
                "The lifecycle Methods must be invoked on the explicit root, not the well-known one.");
        }

        [Test]
        public async Task RegistryNodeIdDrivesTheGroupLifecycleAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            NodeId group = await client.CreateGroupAsync(client.RegistryNodeId, "schemas")
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(group, Is.EqualTo(s_createdGroupNodeId));
                Assert.That(calls, Has.Count.EqualTo(1));
                Assert.That(calls[0].ObjectId, Is.EqualTo(client.RegistryNodeId),
                    "CreateGroup is invoked on the registry root itself.");
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
                Assert.That(requested.IndexRange, Is.Not.Null.And.Not.Empty,
                    "The read is range-based, so a document larger than MaxByteStringLength is " +
                    "fetched in slices instead of failing.");
            });
        }

        [Test]
        public async Task ResolveResourceReadsALargeDocumentInSlicesAsync()
        {
            ByteString contentId = ByteString.From([0xAA, 0xBB]);
            Mock<ISession> session = CreateSession();
            var ranges = new List<string?>();
            var chunks = new Queue<byte[]>();
            chunks.Enqueue([1, 2, 3, 4]);
            chunks.Enqueue([5, 6]);

            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader?, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, items, _) =>
                    {
                        ranges.Add(items[0].IndexRange);
                        byte[] chunk = chunks.Count > 0 ? chunks.Dequeue() : [];
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            Results = new DataValue[]
                            {
                                new(new Variant(ByteString.From(chunk)))
                            },
                            DiagnosticInfos = [],
                            ResponseHeader = new ResponseHeader()
                        });
                    });

            // A tiny chunk size forces the sliced path: the first full-size chunk cannot be the last.
            GenericXRegistryClient client = CreateClient(session);
            ByteString resolved = await client.ResolveResourceAsync(contentId, 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resolved.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }),
                    "The slices are stitched back into the whole document.");
                Assert.That(ranges, Has.Count.GreaterThan(1),
                    "More than one ranged Read is issued when the document exceeds the chunk size.");
                Assert.That(ranges[0], Is.EqualTo("0:3"));
                Assert.That(ranges[1], Is.EqualTo("4:7"));
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
                    () => client.RegisterResourceAsync(NodeId.Null, "id", ByteString.From(new byte[1])),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.RegisterResourceAsync(group, string.Empty, ByteString.From(new byte[1])),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.RegisterResourceAsync(group, "id", ByteString.From(new byte[1]), chunkSize: 0),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        [Test]
        public async Task RegisterResourceStreamsTheCorrectBytesPerChunkAsync()
        {
            byte[] document = [1, 2, 3, 4, 5, 6, 7, 8, 9];
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            await client
                .RegisterResourceAsync(new NodeId(1u, 1), "urn:resource", ByteString.From(document), chunkSize: 4)
                .ConfigureAwait(false);

            // Each chunk wraps a slice of the caller's buffer rather than copying it, so verify the
            // slices reassemble into exactly the original document.
            var streamed = new List<byte>();
            foreach (CallMethodRequest call in calls)
            {
                if (call.MethodId != ExpandedNodeId.ToNodeId(
                        Opc.Ua.MethodIds.FileType_Write, session.Object.NamespaceUris))
                {
                    continue;
                }
                Assert.That(call.InputArguments[1].TryGetValue(out ByteString chunk), Is.True);
                streamed.AddRange(chunk.Span.ToArray());
            }

            Assert.That(streamed, Is.EqualTo(document));
        }

        [Test]
        public async Task RegistrationResultsExposeNamedMembersAndValueEqualityAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            ResourceRegistrationResult resource = await client
                .GetOrRegisterResourceAsync(new NodeId(1u, 1), "urn:doc", ByteString.From(new byte[4]))
                .ConfigureAwait(false);
            GroupRegistrationResult group = await client
                .GetOrCreateGroupAsync(client.RegistryNodeId, "schemas").ConfigureAwait(false);

            (NodeId nodeId, string versionId, bool created) = resource;
            Assert.Multiple(() =>
            {
                Assert.That(resource.ResourceNodeId, Is.EqualTo(s_createdResourceNodeId));
                Assert.That(resource.AssignedVersionId, Is.EqualTo("7"));
                Assert.That(resource.Created, Is.True);
                Assert.That(group.GroupNodeId, Is.EqualTo(s_createdGroupNodeId));
                Assert.That(group.Created, Is.True);

                // The results still deconstruct where that reads better than named members.
                Assert.That(nodeId, Is.EqualTo(resource.ResourceNodeId));
                Assert.That(versionId, Is.EqualTo(resource.AssignedVersionId));
                Assert.That(created, Is.EqualTo(resource.Created));

                // A record struct gives value equality, which a caller can rely on.
                Assert.That(
                    resource,
                    Is.EqualTo(new ResourceRegistrationResult(s_createdResourceNodeId, "7", true)));
                Assert.That(group, Is.EqualTo(new GroupRegistrationResult(s_createdGroupNodeId, true)));
            });
        }

        [Test]
        public async Task RegisterResourceReportsTheVersionAsCreatedAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            ResourceRegistrationResult result = await client
                .RegisterResourceAsync(new NodeId(1u, 1), "urn:doc", ByteString.From(new byte[4]))
                .ConfigureAwait(false);

            Assert.That(result.Created, Is.True,
                "A strict registration only returns when it created the version.");
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
            (NodeId resourceNodeId, string assignedVersionId, _) = await client
                .RegisterResourceAsync(
                    new NodeId(1u, 1), "urn:resource", ByteString.From(new byte[documentLength]), chunkSize: chunkSize)
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
            (NodeId resourceNodeId, string assignedVersionId, _) = await domain
                .RegisterDomainResourceAsync(new NodeId(1u, 1), ByteString.From(new byte[4])).ConfigureAwait(false);

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
                    () => resource.WriteDocumentAsync(1, ByteString.From(new byte[1]), chunkSize: 0).AsTask(),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => resource.ReadDocumentAsync(chunkSize: 0).AsTask(),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
                Assert.That(
                    () => ((ResourceTypeClient)null!).WriteDocumentAsync(1, ByteString.From(new byte[1])).AsTask(),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => ((ResourceTypeClient)null!).ReadDocumentAsync().AsTask(),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// The group lifecycle the model declares on RegistryType is reachable from the client
        /// through the generated registry proxy.
        /// </summary>
        [Test]
        public async Task CreateGroupDrivesTheRegistryProxyAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            NodeId groupNodeId = await client
                .CreateGroupAsync(new NodeId(5u, 1), "schemas").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(groupNodeId, Is.EqualTo(s_createdGroupNodeId));
                Assert.That(calls, Has.Count.EqualTo(1));
                Assert.That(calls[0].InputArguments[0].TryGetValue(out string? groupId), Is.True);
                Assert.That(groupId, Is.EqualTo("schemas"));
            });
        }

        [Test]
        public async Task GetOrCreateGroupReportsWhetherItCreatedTheGroupAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            (NodeId groupNodeId, bool created) = await client
                .GetOrCreateGroupAsync(new NodeId(5u, 1), "schemas").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(groupNodeId, Is.EqualTo(s_createdGroupNodeId));
                Assert.That(created, Is.True);
            });
        }

        [Test]
        public void GroupHelpersValidateTheirArguments()
        {
            GenericXRegistryClient client = CreateClient(CreateSession());
            var registry = new NodeId(5u, 1);

            Assert.Multiple(() =>
            {
                Assert.That(() => client.CreateGroupAsync(registry, string.Empty), Throws.ArgumentException);
                Assert.That(
                    () => client.GetOrCreateGroupAsync(registry, string.Empty), Throws.ArgumentException);
                Assert.That(
                    () => client.GetOrRegisterResourceAsync(NodeId.Null, "id", ByteString.From(new byte[1])),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.GetOrRegisterResourceAsync(new NodeId(1u, 1), string.Empty, ByteString.From(new byte[1])),
                    Throws.ArgumentException);
                Assert.That(
                    () => client.GetOrRegisterResourceAsync(
                        new NodeId(1u, 1), "id", ByteString.From(new byte[1]), chunkSize: 0),
                    Throws.InstanceOf<ArgumentOutOfRangeException>());
            });
        }

        /// <summary>
        /// The idempotent registration only streams the document when it actually created the
        /// version, so re-registering an existing version is cheap.
        /// </summary>
        [Test]
        public async Task GetOrRegisterResourceSkipsTheUploadWhenTheVersionExistsAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls, resourceCreated: false);

            GenericXRegistryClient client = CreateClient(session);
            (NodeId resourceNodeId, string assignedVersionId, bool created) = await client
                .GetOrRegisterResourceAsync(new NodeId(1u, 1), "urn:doc", ByteString.From(new byte[8]))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.False);
                Assert.That(resourceNodeId, Is.EqualTo(s_createdResourceNodeId));
                Assert.That(assignedVersionId, Is.EqualTo("7"));
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Write), Is.Zero,
                    "An existing version is not re-uploaded.");
            });
        }

        [Test]
        public async Task GetOrRegisterResourceUploadsWhenItCreatedTheVersionAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls, resourceCreated: true);

            GenericXRegistryClient client = CreateClient(session);
            (_, _, bool created) = await client
                .GetOrRegisterResourceAsync(new NodeId(1u, 1), "urn:doc", ByteString.From(new byte[8]), chunkSize: 4)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(created, Is.True);
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Write), Is.EqualTo(2));
                Assert.That(CountCalls(calls, Opc.Ua.MethodIds.FileType_Close), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DeleteHelpersPassTheExpectedEpochAsync()
        {
            Mock<ISession> session = CreateSession();
            var calls = new List<CallMethodRequest>();
            SetupCall(session, calls);

            GenericXRegistryClient client = CreateClient(session);
            await client.DeleteResourceAsync(new NodeId(9u, 1), 3).ConfigureAwait(false);
            await client.DeleteGroupAsync(new NodeId(8u, 1), 4).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(calls, Has.Count.EqualTo(2));
                Assert.That(calls[0].InputArguments[0].TryGetValue(out uint resourceEpoch), Is.True);
                Assert.That(resourceEpoch, Is.EqualTo(3u));
                Assert.That(calls[1].InputArguments[0].TryGetValue(out uint groupEpoch), Is.True);
                Assert.That(groupEpoch, Is.EqualTo(4u));
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
            // ResolveResourceAsync reads through ReadBytesAsync, which chunks on this limit.
            messageContext.SetupGet(c => c.MaxByteStringLength).Returns(65536);
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
        private static void SetupCall(
            Mock<ISession> session,
            List<CallMethodRequest> calls,
            bool resourceCreated = true)
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
                                    OutputArguments = OutputsFor(request, resourceCreated)
                                }
                            },
                            DiagnosticInfos = [],
                            ResponseHeader = new ResponseHeader()
                        });
                    });
        }

        private static ArrayOf<Variant> OutputsFor(CallMethodRequest request, bool resourceCreated)
        {
            // CreateResource/GetOrCreateResource(ResourceId, VersionId, RequestFileOpen) return
            // (ResourceNodeId, AssignedVersionId, FileHandle[, Created]).
            if (request.InputArguments.Count == 3)
            {
                return new Variant[]
                {
                    new(s_createdResourceNodeId),
                    new("7"),
                    new(99u),
                    new(resourceCreated)
                };
            }

            // CreateGroup/GetOrCreateGroup(GroupId) return (GroupNodeId[, Created]); the file
            // methods and Delete(ExpectedEpoch) take one argument and return nothing meaningful.
            if (request.InputArguments.Count == 1 &&
                request.InputArguments[0].TryGetValue(out string? _))
            {
                return new Variant[] { new(s_createdGroupNodeId), new(true) };
            }
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
        /// Stands in for a domain registry client: it extends the abstract base and adds domain
        /// naming without re-implementing the lifecycle.
        /// </summary>
        private sealed class TestDomainRegistryClient : XRegistryClient
        {
            public TestDomainRegistryClient(ISession session, ITelemetryContext telemetry)
                : base(session, TestNamespaceUri, telemetry)
            {
            }

            public bool DomainPrefixApplied { get; private set; }

            public Task<ResourceRegistrationResult> RegisterDomainResourceAsync(
                NodeId groupNodeId,
                ByteString document)
            {
                DomainPrefixApplied = true;
                return RegisterResourceAsync(groupNodeId, "urn:domain:resource", document);
            }
        }

        private const string TestNamespaceUri = XRegistryWellKnown.XRegistryNamespaceUri;
        private static readonly NodeId s_createdResourceNodeId = new(4711u, 1);
        private static readonly NodeId s_createdGroupNodeId = new(4712u, 1);
        private static readonly byte[] s_readChunk = [0x0A, 0x0B];

        [TestCase("0.6.0", 0, 6, 0, true)]
        [TestCase("0.5.9", 0, 6, 0, false)]
        [TestCase("0.7.0", 0, 6, 0, true)]
        [TestCase("1.0.0", 0, 6, 0, true)]
        [TestCase("0.6.0-preview", 0, 6, 0, true)]
        [TestCase("0.6.1", 0, 6, 0, true)]
        [TestCase("0.5.99", 0, 6, 0, false)]
        [TestCase("", 0, 6, 0, false)]
        [TestCase("abc", 0, 6, 0, false)]
        [TestCase("0.6", 0, 6, 0, true)]
        [TestCase("1", 0, 6, 0, true)]
        [TestCase("0", 0, 6, 0, false)]
        // Regression: a single-component version equal to an all-zero
        // minor/patch threshold with the same major must compare equal
        // ("1" == "1.0.0"), not strictly less (the omitted components are 0,
        // not a parse failure).
        [TestCase("1", 1, 0, 0, true)]
        [TestCase("2", 1, 9, 9, true)]
        [TestCase("1", 2, 0, 0, false)]
        [TestCase("1.5", 1, 5, 0, true)]
        [TestCase("1.5", 1, 5, 1, false)]
        [TestCase("1.x", 1, 0, 0, false)]
        [TestCase("1.0.x", 1, 0, 0, false)]
        public void IsVersionAtLeastParsesCorrectly(
            string version, int major, int minor, int patch, bool expected)
        {
            bool result = XRegistryClient.IsVersionAtLeast(version ?? string.Empty, major, minor, patch);
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
