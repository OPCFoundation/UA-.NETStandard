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
using Opc.Ua.Aas.Client.Registry;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Tests.Client
{
    /// <summary>
    /// Behaviour of the registry browse and version listing surface.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasRegistryClientListingTests
    {
        private const string XRegistryNamespaceUri = Opc.Ua.XRegistry.Namespaces.xRegistry;

        [Test]
        public async Task ListEnvironmentDocumentsProjectsOrganizedObjectsOntoFileClientsAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registry = new("AASRegistry", 2);
            NodeId first = new("env-1", 2);
            NodeId second = new("env-2", 2);
            SetupBrowse(session, registry, default, BrowseReference(first), BrowseReference(second));
            SetupStringProperty(session, first, "EnvironmentIdentifier", "urn:env:1");
            SetupStringProperty(session, second, "EnvironmentIdentifier", "urn:env:2");

            var client = new AasRegistryClient(session.Object, registry, Mock.Of<ITelemetryContext>());
            ArrayOf<AasEnvironmentFileClient> documents = await client.ListEnvironmentDocumentsAsync();

            Assert.That(documents.Count, Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(documents[0].ResourceNodeId, Is.EqualTo(first));
                Assert.That(documents[1].ResourceNodeId, Is.EqualTo(second));
                Assert.That(documents[0].GroupNodeId, Is.EqualTo(registry));
                Assert.That(documents[1].GroupNodeId, Is.EqualTo(registry));
            });
            Assert.That(await documents[0].ReadSourceIdentityAsync(), Is.EqualTo("urn:env:1"));
            Assert.That(await documents[1].ReadSourceIdentityAsync(), Is.EqualTo("urn:env:2"));
        }

        [Test]
        public async Task ListEnvironmentDocumentsReturnsEmptyWhenNothingIsOrganizedAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registry = new("AASRegistry", 2);
            SetupBrowse(session, registry, default);

            var client = new AasRegistryClient(session.Object, registry, Mock.Of<ITelemetryContext>());
            ArrayOf<AasEnvironmentFileClient> documents = await client.ListEnvironmentDocumentsAsync();

            Assert.That(documents.Count, Is.Zero);
            session.Verify(
                s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ListEnvironmentDocumentsFollowsContinuationPointsAndSkipsUnresolvableNodeIdsAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registry = new("AASRegistry", 2);
            NodeId first = new("env-1", 2);
            NodeId third = new("env-3", 2);
            ByteString continuationPoint = new(new byte[] { 7, 8, 9 });

            // The unresolvable reference points at an unknown namespace URI, so ExpandedNodeId.ToNodeId
            // yields a null NodeId which the reader must drop instead of materializing a bad client.
            var unresolvable = new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("env-2", 0, "urn:not:in:the:namespace:table", 0),
                BrowseName = new QualifiedName("env-2", 2),
                DisplayName = new LocalizedText("env-2"),
                NodeClass = NodeClass.Object
            };
            SetupBrowse(session, registry, continuationPoint, BrowseReference(first), unresolvable);
            SetupBrowseNext(session, continuationPoint, BrowseReference(third));

            var client = new AasRegistryClient(session.Object, registry, Mock.Of<ITelemetryContext>());
            ArrayOf<AasEnvironmentFileClient> documents = await client.ListEnvironmentDocumentsAsync();

            Assert.That(documents.Count, Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(documents[0].ResourceNodeId, Is.EqualTo(first));
                Assert.That(documents[1].ResourceNodeId, Is.EqualTo(third));
            });
            session.Verify(
                s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    false,
                    It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ListSubmodelsBindsEachDocumentToTheOwningShellGroupAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("shell-group", 2);
            NodeId submodel = new("submodel-1", 2);
            SetupBrowse(session, group, default, BrowseReference(submodel));
            SetupStringProperty(session, submodel, "SubmodelIdentifier", "urn:submodel:1");

            var client = new AasShellGroupClient(session.Object, group, Mock.Of<ITelemetryContext>());
            ArrayOf<AasSubmodelFileClient> submodels = await client.ListSubmodelsAsync();

            Assert.That(submodels.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(submodels[0].GroupNodeId, Is.EqualTo(group));
                Assert.That(submodels[0].ResourceNodeId, Is.EqualTo(submodel));
                Assert.That(submodels[0].Session, Is.SameAs(session.Object));
            });
            Assert.That(await submodels[0].ReadSourceIdentityAsync(), Is.EqualTo("urn:submodel:1"));
        }

        [Test]
        public async Task ListSubmodelTemplatesBindsEachDocumentToTheOwningTemplateGroupAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("template-group", 2);
            NodeId template = new("template-1", 2);
            SetupBrowse(session, group, default, BrowseReference(template));
            SetupStringProperty(session, template, "SubmodelIdentifier", "urn:template:1");

            var client = new AasSubmodelTemplateGroupClient(session.Object, group, Mock.Of<ITelemetryContext>());
            ArrayOf<AasSubmodelFileClient> templates = await client.ListSubmodelTemplatesAsync();

            Assert.That(templates.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(templates[0].GroupNodeId, Is.EqualTo(group));
                Assert.That(templates[0].ResourceNodeId, Is.EqualTo(template));
            });
            Assert.That(await templates[0].ReadSourceIdentityAsync(), Is.EqualTo("urn:template:1"));
        }

        [Test]
        public async Task ListConceptDescriptionsBindsEachDocumentToTheOwningDictionaryAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("dictionary-group", 2);
            NodeId concept = new("concept-1", 2);
            NodeId other = new("concept-2", 2);
            SetupBrowse(session, group, default, BrowseReference(concept), BrowseReference(other));
            SetupStringProperty(session, concept, "ConceptIdentifier", "urn:concept:1");

            var client = new AasConceptDictionaryGroupClient(session.Object, group, Mock.Of<ITelemetryContext>());
            ArrayOf<AasConceptDescriptionFileClient> concepts = await client.ListConceptDescriptionsAsync();

            Assert.That(concepts.Count, Is.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(concepts[0].ResourceNodeId, Is.EqualTo(concept));
                Assert.That(concepts[1].ResourceNodeId, Is.EqualTo(other));
                Assert.That(concepts[0].GroupNodeId, Is.EqualTo(group));
            });
            Assert.That(await concepts[0].ReadSourceIdentityAsync(), Is.EqualTo("urn:concept:1"));
        }

        [Test]
        public async Task ListPackagesBindsEachDocumentToTheOwningPackageStoreAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("store-group", 2);
            NodeId package = new("package-1", 2);
            SetupBrowse(session, group, default, BrowseReference(package));
            SetupStringProperty(session, package, "PackageIdentifier", "urn:package:1");

            var client = new AasPackageStoreGroupClient(session.Object, group, Mock.Of<ITelemetryContext>());
            ArrayOf<AasPackageFileClient> packages = await client.ListPackagesAsync();

            Assert.That(packages.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(packages[0].GroupNodeId, Is.EqualTo(group));
                Assert.That(packages[0].ResourceNodeId, Is.EqualTo(package));
            });
            Assert.That(await packages[0].ReadSourceIdentityAsync(), Is.EqualTo("urn:package:1"));
        }

        [Test]
        public void ReadSourceIdentityFailsWhenTheMandatoryPropertyIsMissing()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("shell-group", 2);
            SetupMissingChild(session, group, "AasIdentifier");

            var client = new AasShellGroupClient(session.Object, group, Mock.Of<ITelemetryContext>());

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => client.ReadSourceIdentityAsync().AsTask())!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNoMatch));
                Assert.That(exception.Message, Does.Contain("AasIdentifier"));
            });
        }

        [Test]
        public void ReadSourceIdentityFailsWhenThePropertyDoesNotHoldAString()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("store-group", 2);
            NodeId property = new("store-group.StoreIdentifier", 2);
            SetupBrowsePath(session, group, "StoreIdentifier", property, StatusCodes.Good);
            SetupReadValue(session, property, new DataValue(new Variant(42)));

            var client = new AasPackageStoreGroupClient(session.Object, group, Mock.Of<ITelemetryContext>());

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                () => client.ReadSourceIdentityAsync().AsTask())!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUnexpectedError));
                Assert.That(exception.Message, Does.Contain("did not return a string value"));
            });
        }

        [Test]
        public async Task ListVersionsKeepsOnlySiblingsSharingTheResourceIdAndOrdersThemByCreationAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("shell-group", 2);
            NodeId newer = new("submodel-v2", 2);
            NodeId older = new("submodel-v1", 2);
            NodeId foreign = new("other-resource", 2);
            SetupBrowse(session, group, default, BrowseReference(newer), BrowseReference(older),
                BrowseReference(foreign));

            SetupXRegistryString(session, newer, "ResourceId", "urn:submodel");
            SetupXRegistryString(session, newer, "VersionId", "2");
            SetupXRegistryDateTime(session, newer, "CreatedAt", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            SetupXRegistryDateTime(session, newer, "ModifiedAt", new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

            SetupXRegistryString(session, older, "ResourceId", "urn:submodel");
            SetupXRegistryString(session, older, "VersionId", "1");
            SetupXRegistryDateTime(session, older, "CreatedAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetupXRegistryDateTime(session, older, "ModifiedAt", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            SetupXRegistryString(session, foreign, "ResourceId", "urn:other");

            var client = new AasSubmodelFileClient(session.Object, group, newer, Mock.Of<ITelemetryContext>());
            ArrayOf<AasRegistryResourceVersionInfo> versions = await client.ListVersionsAsync();

            Assert.That(VersionIds(versions), Is.EqualTo(s_expectedVersionOrder));
            Assert.Multiple(() =>
            {
                Assert.That(versions[0].ResourceNodeId, Is.EqualTo(older));
                Assert.That(versions[1].ResourceNodeId, Is.EqualTo(newer));
                Assert.That(versions[0].ResourceId, Is.EqualTo("urn:submodel"));
                Assert.That(
                    versions[1].CreatedAt,
                    Is.EqualTo(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
                Assert.That(
                    versions[1].ModifiedAt,
                    Is.EqualTo(new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc)));
            });
        }

        [Test]
        public async Task ListVersionsTreatsAbsentOptionalPropertiesAsEmptyAndEpochMinimumAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("shell-group", 2);
            NodeId resource = new("submodel-v1", 2);
            SetupBrowse(session, group, default, BrowseReference(resource));

            SetupXRegistryString(session, resource, "ResourceId", "urn:submodel");
            SetupMissingXRegistryChild(session, resource, "VersionId");
            SetupMissingXRegistryChild(session, resource, "CreatedAt");
            SetupMissingXRegistryChild(session, resource, "ModifiedAt");

            var client = new AasSubmodelFileClient(session.Object, group, resource, Mock.Of<ITelemetryContext>());
            ArrayOf<AasRegistryResourceVersionInfo> versions = await client.ListVersionsAsync();

            Assert.That(versions.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(versions[0].VersionId, Is.Empty);
                Assert.That(versions[0].CreatedAt, Is.EqualTo(DateTime.MinValue));
                Assert.That(versions[0].ModifiedAt, Is.EqualTo(DateTime.MinValue));
            });
        }

        [Test]
        public async Task ListVersionsIgnoresSiblingsWhoseOptionalPropertiesReadBackBadAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("shell-group", 2);
            NodeId resource = new("submodel-v1", 2);
            NodeId broken = new("submodel-broken", 2);
            SetupBrowse(session, group, default, BrowseReference(resource), BrowseReference(broken));

            SetupXRegistryString(session, resource, "ResourceId", "urn:submodel");
            SetupXRegistryString(session, resource, "VersionId", "1");
            SetupXRegistryDateTime(session, resource, "CreatedAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetupXRegistryDateTime(
                session,
                resource,
                "ModifiedAt",
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            NodeId brokenProperty = new("submodel-broken.ResourceId", 3);
            SetupBrowsePath(session, broken, "ResourceId", brokenProperty, StatusCodes.Good, XRegistryNamespaceUri);
            SetupReadValue(session, brokenProperty, DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));

            var client = new AasSubmodelFileClient(session.Object, group, resource, Mock.Of<ITelemetryContext>());
            ArrayOf<AasRegistryResourceVersionInfo> versions = await client.ListVersionsAsync();

            Assert.That(versions.Count, Is.EqualTo(1));
            Assert.That(versions[0].ResourceNodeId, Is.EqualTo(resource));
        }

        [Test]
        public async Task ResolveVersionAsOfSkipsVersionsCreatedAfterTheRequestedMomentAsync()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("shell-group", 2);
            NodeId older = new("submodel-v1", 2);
            NodeId newer = new("submodel-v2", 2);
            SetupBrowse(session, group, default, BrowseReference(older), BrowseReference(newer));

            SetupXRegistryString(session, older, "ResourceId", "urn:submodel");
            SetupXRegistryString(session, older, "VersionId", "1");
            SetupXRegistryDateTime(session, older, "CreatedAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetupXRegistryDateTime(session, older, "ModifiedAt", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            SetupXRegistryString(session, newer, "ResourceId", "urn:submodel");
            SetupXRegistryString(session, newer, "VersionId", "2");
            SetupXRegistryDateTime(session, newer, "CreatedAt", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
            SetupXRegistryDateTime(session, newer, "ModifiedAt", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

            var client = new AasSubmodelFileClient(session.Object, group, older, Mock.Of<ITelemetryContext>());

            AasRegistryResourceVersionInfo? atCutoff = await client.ResolveVersionAsOfAsync(
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            AasRegistryResourceVersionInfo? afterCutoff = await client.ResolveVersionAsOfAsync(
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));
            AasRegistryResourceVersionInfo? beforeAnything = await client.ResolveVersionAsOfAsync(
                new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.Multiple(() =>
            {
                Assert.That(atCutoff?.VersionId, Is.EqualTo("1"));
                Assert.That(afterCutoff?.VersionId, Is.EqualTo("2"));
                Assert.That(beforeAnything, Is.Null);
            });
        }

        private static readonly string[] s_expectedVersionOrder = ["1", "2"];

        private static List<string> VersionIds(ArrayOf<AasRegistryResourceVersionInfo> versions)
        {
            var ids = new List<string>(versions.Count);
            for (int i = 0; i < versions.Count; i++)
            {
                ids.Add(versions[i].VersionId);
            }
            return ids;
        }

        private static Mock<ISession> CreateSessionMock()
        {
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            namespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            namespaceUris.GetIndexOrAppend(Opc.Ua.XRegistry.Namespaces.xRegistry);
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(Mock.Of<ITelemetryContext>());
            messageContext.NamespaceUris = namespaceUris;
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.Setup(s => s.Dispose());
            return session;
        }

        private static void SetupBrowse(
            Mock<ISession> session,
            NodeId nodeId,
            ByteString continuationPoint,
            params ReferenceDescription[] references)
        {
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(b =>
                        b.Count == 1 &&
                        b[0].NodeId == nodeId &&
                        b[0].ReferenceTypeId == Opc.Ua.ReferenceTypeIds.Organizes &&
                        b[0].NodeClassMask == (uint)NodeClass.Object),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = continuationPoint,
                            References = references.ToArrayOf()
                        }
                    ]
                });
        }

        private static void SetupBrowseNext(
            Mock<ISession> session,
            ByteString continuationPoint,
            params ReferenceDescription[] references)
        {
            session
                .Setup(s => s.BrowseNextAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<bool>(),
                    It.Is<ArrayOf<ByteString>>(c => c.Count == 1 && c[0].Equals(continuationPoint)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    Results =
                    [
                        new BrowseResult
                        {
                            StatusCode = StatusCodes.Good,
                            ContinuationPoint = default,
                            References = references.ToArrayOf()
                        }
                    ]
                });
        }

        private static ReferenceDescription BrowseReference(NodeId nodeId)
        {
            return new ReferenceDescription
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(nodeId.ToString(), 2),
                DisplayName = new LocalizedText(nodeId.ToString()),
                NodeClass = NodeClass.Object
            };
        }

        private static void SetupStringProperty(
            Mock<ISession> session,
            NodeId owner,
            string propertyName,
            string value)
        {
            NodeId propertyNodeId = new(owner + "." + propertyName, 2);
            SetupBrowsePath(session, owner, propertyName, propertyNodeId, StatusCodes.Good);
            SetupReadValue(session, propertyNodeId, new DataValue(new Variant(value)));
        }

        private static void SetupXRegistryString(
            Mock<ISession> session,
            NodeId owner,
            string propertyName,
            string value)
        {
            NodeId propertyNodeId = new(owner + "." + propertyName, 3);
            SetupBrowsePath(session, owner, propertyName, propertyNodeId, StatusCodes.Good, XRegistryNamespaceUri);
            SetupReadValue(session, propertyNodeId, new DataValue(new Variant(value)));
        }

        private static void SetupXRegistryDateTime(
            Mock<ISession> session,
            NodeId owner,
            string propertyName,
            DateTime value)
        {
            NodeId propertyNodeId = new(owner + "." + propertyName, 3);
            SetupBrowsePath(session, owner, propertyName, propertyNodeId, StatusCodes.Good, XRegistryNamespaceUri);
            SetupReadValue(session, propertyNodeId, new DataValue(new Variant(new DateTimeUtc(value))));
        }

        private static void SetupMissingChild(Mock<ISession> session, NodeId owner, string propertyName)
        {
            SetupBrowsePath(session, owner, propertyName, NodeId.Null, StatusCodes.BadNoMatch);
        }

        private static void SetupMissingXRegistryChild(Mock<ISession> session, NodeId owner, string propertyName)
        {
            SetupBrowsePath(session, owner, propertyName, NodeId.Null, StatusCodes.BadNoMatch, XRegistryNamespaceUri);
        }

        private static void SetupBrowsePath(
            Mock<ISession> session,
            NodeId startingNode,
            string browseName,
            NodeId target,
            StatusCode statusCode,
            string namespaceUri = Opc.Ua.Aas.V3.Namespaces.AasV3)
        {
            ushort namespaceIndex = session.Object.NamespaceUris.GetIndexOrAppend(namespaceUri);
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<BrowsePath>>(p =>
                        p.Count == 1 &&
                        p[0].StartingNode == startingNode &&
                        p[0].RelativePath.Elements[0].TargetName.Name == browseName &&
                        p[0].RelativePath.Elements[0].TargetName.NamespaceIndex == namespaceIndex),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TranslateBrowsePathsToNodeIdsResponse
                {
                    Results =
                    [
                        new BrowsePathResult
                        {
                            StatusCode = statusCode,
                            Targets = StatusCode.IsGood(statusCode)
                                ? [new BrowsePathTarget { TargetId = target }]
                                : []
                        }
                    ]
                });
        }

        private static void SetupReadValue(Mock<ISession> session, NodeId nodeId, DataValue value)
        {
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(nodes => nodes.Count == 1 && nodes[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse { Results = [value] });
        }
    }
}
