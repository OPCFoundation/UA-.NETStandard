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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Aas.Client.Hosting;
using Opc.Ua.Aas.Client.Registry;
using Opc.Ua.Client;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.Aas.Tests.Client
{
    [TestFixture]
    [Category("Aas")]
    public sealed class AasRegistryClientTests
    {
        [Test]
        public async Task ForServerAsyncResolvesWellKnownRegistryRoot()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registryNodeId = new("AASRegistry", 2);
            SetupBrowsePath(session, global::Opc.Ua.ObjectIds.Server, "AASRegistry", 2, registryNodeId, StatusCodes.Good);

            AasRegistryClient client = await AasRegistryClient.ForServerAsync(
                session.Object,
                Mock.Of<ITelemetryContext>());

            Assert.That(client.RegistryNodeId, Is.EqualTo(registryNodeId));
        }

        [Test]
        public void ForServerAsyncThrowsWhenRegistryRootIsAbsent()
        {
            Mock<ISession> session = CreateSessionMock();
            SetupBrowsePath(
                session,
                global::Opc.Ua.ObjectIds.Server,
                "AASRegistry",
                2,
                NodeId.Null,
                StatusCodes.BadNoMatch);

            Assert.That(
                async () => await AasRegistryClient.ForServerAsync(session.Object, Mock.Of<ITelemetryContext>()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task TypedGroupAndResourceClientsExposeSourceIdentities()
        {
            Mock<ISession> session = CreateSessionMock();
            ITelemetryContext telemetry = Mock.Of<ITelemetryContext>();
            NodeId shell = new("shell", 2);
            NodeId template = new("template", 2);
            NodeId dictionary = new("dictionary", 2);
            NodeId store = new("store", 2);
            NodeId submodel = new("submodel", 2);
            NodeId concept = new("concept", 2);
            NodeId package = new("package", 2);
            NodeId environment = new("environment", 2);
            SetupStringProperty(session, shell, 2, "AasIdentifier", "urn:shell");
            SetupStringProperty(session, template, 2, "TemplateNamespace", "urn:template");
            SetupStringProperty(session, dictionary, 2, "DictionaryIdentifier", "urn:dictionary");
            SetupStringProperty(session, store, 2, "StoreIdentifier", "urn:store");
            SetupStringProperty(session, submodel, 2, "SubmodelIdentifier", "urn:submodel");
            SetupStringProperty(session, concept, 2, "ConceptIdentifier", "urn:concept");
            SetupStringProperty(session, package, 2, "PackageIdentifier", "urn:package");
            SetupStringProperty(session, environment, 2, "EnvironmentIdentifier", "urn:environment");

            string shellId = await new AasShellGroupClient(session.Object, shell, telemetry).ReadSourceIdentityAsync();
            string templateId = await new AasSubmodelTemplateGroupClient(
                session.Object,
                template,
                telemetry).ReadSourceIdentityAsync();
            string dictionaryId = await new AasConceptDictionaryGroupClient(
                session.Object,
                dictionary,
                telemetry).ReadSourceIdentityAsync();
            string storeId = await new AasPackageStoreGroupClient(session.Object, store, telemetry)
                .ReadSourceIdentityAsync();
            string submodelId = await new AasSubmodelFileClient(session.Object, shell, submodel, telemetry)
                .ReadSourceIdentityAsync();
            string conceptId = await new AasConceptDescriptionFileClient(session.Object, dictionary, concept, telemetry)
                .ReadSourceIdentityAsync();
            string packageId = await new AasPackageFileClient(session.Object, store, package, telemetry)
                .ReadSourceIdentityAsync();
            string environmentId = await new AasEnvironmentFileClient(session.Object, shell, environment, telemetry)
                .ReadSourceIdentityAsync();

            Assert.Multiple(() =>
            {
                Assert.That(shellId, Is.EqualTo("urn:shell"));
                Assert.That(templateId, Is.EqualTo("urn:template"));
                Assert.That(dictionaryId, Is.EqualTo("urn:dictionary"));
                Assert.That(storeId, Is.EqualTo("urn:store"));
                Assert.That(submodelId, Is.EqualTo("urn:submodel"));
                Assert.That(conceptId, Is.EqualTo("urn:concept"));
                Assert.That(packageId, Is.EqualTo("urn:package"));
                Assert.That(environmentId, Is.EqualTo("urn:environment"));
            });
        }

        [Test]
        public async Task LookupShellsByAssetLinkReturnsHitAndMiss()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registryNodeId = new("registry", 2);
            NodeId shellNodeId = new("shell", 2);
            SetupLookup(session, registryNodeId, "serial", "42", [shellNodeId]);
            SetupLookup(session, registryNodeId, "serial", "missing", []);
            var client = new AasRegistryClient(session.Object, registryNodeId, Mock.Of<ITelemetryContext>());

            ArrayOf<NodeId> hit = await client.LookupShellsByAssetLinkAsync("serial", "42");
            ArrayOf<NodeId> miss = await client.LookupShellsByAssetLinkAsync("serial", "missing");

            Assert.Multiple(() =>
            {
                Assert.That(hit, Has.Count.EqualTo(1));
                Assert.That(hit[0], Is.EqualTo(shellNodeId));
                Assert.That(miss, Is.Empty);
            });
        }

        [Test]
        public async Task GetSubmodelSurfacesAccessDeniedAndNotFoundDistinctly()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registryNodeId = new("registry", 2);
            SetupGetSubmodel(
                session,
                registryNodeId,
                "denied",
                StatusCodes.BadUserAccessDenied,
                default,
                string.Empty,
                string.Empty);
            SetupGetSubmodel(
                session,
                registryNodeId,
                "missing",
                StatusCodes.BadNotFound,
                default,
                string.Empty,
                string.Empty);
            SetupGetSubmodel(
                session,
                registryNodeId,
                "concealed",
                StatusCodes.BadNotFound,
                default,
                string.Empty,
                string.Empty);
            var client = new AasRegistryClient(session.Object, registryNodeId, Mock.Of<ITelemetryContext>());

            AasGetSubmodelDocumentResult denied = await client.GetSubmodelAsync("denied");
            AasGetSubmodelDocumentResult missing = await client.GetSubmodelAsync("missing");
            AasGetSubmodelDocumentResult concealed = await client.GetSubmodelAsync("concealed");

            Assert.Multiple(() =>
            {
                Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(concealed.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
                Assert.That(denied.Document.IsNull, Is.True);
                Assert.That(missing.Document.IsNull, Is.True);
                Assert.That(concealed.Document.IsNull, Is.True);
            });
        }

        [Test]
        public async Task GetSubmodelReturnsDocumentOnSuccess()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId registryNodeId = new("registry", 2);
            ByteString document = ByteString.From(new byte[] { 1, 2, 3 });
            SetupGetSubmodel(session, registryNodeId, "submodel", StatusCodes.Good, document, "aas/3.0+json", "application/json");
            var client = new AasRegistryClient(session.Object, registryNodeId, Mock.Of<ITelemetryContext>());

            AasGetSubmodelDocumentResult result = await client.GetSubmodelAsync("submodel");

            Assert.Multiple(() =>
            {
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(result.Document, Is.EqualTo(document));
                Assert.That(result.Format, Is.EqualTo("aas/3.0+json"));
                Assert.That(result.ContentType, Is.EqualTo("application/json"));
            });
        }

        [Test]
        public async Task VersionsResolveNewestVersionNotLaterThanMoment()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("group", 2);
            NodeId version1 = new("version1", 2);
            NodeId version2 = new("version2", 2);
            DateTime first = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime second = new(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            SetupBrowse(session, group, BrowseReference(version2), BrowseReference(version1));
            SetupStringProperty(session, version1, 3, "ResourceId", "resource");
            SetupStringProperty(session, version1, 3, "VersionId", "v1");
            SetupDateTimeProperty(session, version1, 3, "CreatedAt", first);
            SetupDateTimeProperty(session, version1, 3, "ModifiedAt", first);
            SetupStringProperty(session, version2, 3, "ResourceId", "resource");
            SetupStringProperty(session, version2, 3, "VersionId", "v2");
            SetupDateTimeProperty(session, version2, 3, "CreatedAt", second);
            SetupDateTimeProperty(session, version2, 3, "ModifiedAt", second);
            AasSubmodelFileClient client = new(session.Object, group, version2, Mock.Of<ITelemetryContext>());

            ArrayOf<AasRegistryResourceVersionInfo> versions = await client.ListVersionsAsync();
            AasRegistryResourceVersionInfo? beforeFirst = await client.ResolveVersionAsOfAsync(first.AddTicks(-1));
            AasRegistryResourceVersionInfo? between = await client.ResolveVersionAsOfAsync(first.AddDays(1));

            Assert.Multiple(() =>
            {
                Assert.That(versions, Has.Count.EqualTo(2));
                Assert.That(versions[0].VersionId, Is.EqualTo("v1"));
                Assert.That(beforeFirst, Is.Null);
                Assert.That(between, Is.Not.Null);
                Assert.That(between!.VersionId, Is.EqualTo("v1"));
            });
        }

        [Test]
        public async Task PackageDownloadVerifiesDigestByDefault()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("packages", 2);
            NodeId package = new("package", 2);
            ByteString content = ByteString.From(new byte[] { 1, 2, 3 });
            SetupStringProperty(session, package, 2, "Digest", Sha256Hex(content));
            SetupStringProperty(session, package, 2, "DigestAlg", "Sha256");
            SetupFileRead(session, package, content);
            AasPackageFileClient client = new(session.Object, group, package, Mock.Of<ITelemetryContext>());

            AasVerifiedPackage verified = await client.DownloadAsync();

            Assert.Multiple(() =>
            {
                Assert.That(verified.Content, Is.EqualTo(content));
                Assert.That(verified.DigestAlg, Is.EqualTo("Sha256"));
            });
        }

        [Test]
        public void PackageDownloadRejectsTamperedBlob()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("packages", 2);
            NodeId package = new("package", 2);
            SetupStringProperty(session, package, 2, "Digest", Sha256Hex(ByteString.From(new byte[] { 1, 2, 3 })));
            SetupStringProperty(session, package, 2, "DigestAlg", "Sha256");
            SetupFileRead(session, package, ByteString.From(new byte[] { 9, 9, 9 }));
            AasPackageFileClient client = new(session.Object, group, package, Mock.Of<ITelemetryContext>());

            Assert.That(
                async () => await client.DownloadAsync(),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void PackageDownloadRejectsWrongCaseDigestAlgorithm()
        {
            Mock<ISession> session = CreateSessionMock();
            NodeId group = new("packages", 2);
            NodeId package = new("package", 2);
            ByteString content = ByteString.From(new byte[] { 1, 2, 3 });
            SetupStringProperty(session, package, 2, "Digest", Sha256Hex(content));
            SetupStringProperty(session, package, 2, "DigestAlg", "sha256");
            SetupFileRead(session, package, content);
            AasPackageFileClient client = new(session.Object, group, package, Mock.Of<ITelemetryContext>());

            Assert.That(
                async () => await client.DownloadAsync(),
                Throws.TypeOf<ServiceResultException>());
        }

        [Test]
        public void AddAasRegistryClientOverloadsRegisterFactories()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OpcUa:Aas:Client:LazyConnect"] = "false",
                    ["Aas:LazyConnect"] = "true"
                })
                .Build();
            var actionServices = new ServiceCollection();
            var configurationServices = new ServiceCollection();
            var sectionServices = new ServiceCollection();

            new TestOpcUaBuilder(actionServices).AddAasV3RegistryClient(options => options.LazyConnect = false);
            new TestOpcUaBuilder(configurationServices).AddAasV3RegistryClient(configuration);
            new TestOpcUaBuilder(sectionServices).AddAasV3RegistryClient(configuration.GetSection("Aas"));

            using ServiceProvider actionProvider = actionServices.BuildServiceProvider();
            using ServiceProvider configurationProvider = configurationServices.BuildServiceProvider();
            using ServiceProvider sectionProvider = sectionServices.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    actionProvider.GetService<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>(),
                    Is.Not.Null);
                Assert.That(
                    configurationProvider.GetService<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>(),
                    Is.Not.Null);
                Assert.That(
                    sectionProvider.GetService<Func<ManagedSession, CancellationToken, Task<AasRegistryClient>>>(),
                    Is.Not.Null);
            });
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

        private static void SetupBrowsePath(
            Mock<ISession> session,
            NodeId startingNode,
            string browseName,
            ushort ns,
            NodeId target,
            StatusCode statusCode)
        {
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<BrowsePath>>(p =>
                        p.Count == 1 &&
                        p[0].StartingNode == startingNode &&
                        p[0].RelativePath.Elements[0].TargetName.Name == browseName),
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

        private static void SetupStringProperty(
            Mock<ISession> session,
            NodeId owner,
            ushort ns,
            string propertyName,
            string value)
        {
            NodeId propertyNodeId = new(owner + "." + propertyName, ns);
            SetupBrowsePath(session, owner, propertyName, ns, propertyNodeId, StatusCodes.Good);
            SetupReadValue(session, propertyNodeId, new Variant(value));
        }

        private static void SetupDateTimeProperty(
            Mock<ISession> session,
            NodeId owner,
            ushort ns,
            string propertyName,
            DateTime value)
        {
            NodeId propertyNodeId = new(owner + "." + propertyName, ns);
            SetupBrowsePath(session, owner, propertyName, ns, propertyNodeId, StatusCodes.Good);
            SetupReadValue(session, propertyNodeId, new Variant(new DateTimeUtc(value)));
        }

        private static void SetupReadValue(Mock<ISession> session, NodeId nodeId, Variant value)
        {
            session
                .Setup(s => s.ReadAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.Is<ArrayOf<ReadValueId>>(nodes => nodes.Count == 1 && nodes[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse { Results = [new DataValue(value)] });
        }

        private static void SetupBrowse(
            Mock<ISession> session,
            NodeId nodeId,
            params ReferenceDescription[] references)
        {
            session
                .Setup(s => s.BrowseAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ViewDescription>(),
                    It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(b => b.Count == 1 && b[0].NodeId == nodeId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
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

        private static void SetupLookup(
            Mock<ISession> session,
            NodeId registryNodeId,
            string name,
            string value,
            ArrayOf<NodeId> result)
        {
            SetupCall(
                session,
                registryNodeId,
                ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.MethodIds.AASRegistryType_LookupShellsByAssetLink, session.Object.NamespaceUris),
                StatusCodes.Good,
                [new Variant(result)],
                args => args.Count == 2 &&
                    args[0].TryGetValue(out string? n) &&
                    args[1].TryGetValue(out string? v) &&
                    n == name &&
                    v == value);
        }

        private static void SetupGetSubmodel(
            Mock<ISession> session,
            NodeId registryNodeId,
            string identifier,
            StatusCode statusCode,
            ByteString document,
            string format,
            string contentType)
        {
            ArrayOf<Variant> output = StatusCode.IsGood(statusCode)
                ? [new Variant(document), new Variant(format), new Variant(contentType)]
                : [];
            SetupCall(
                session,
                registryNodeId,
                ExpandedNodeId.ToNodeId(Opc.Ua.Aas.V3.MethodIds.AASRegistryType_GetSubmodel, session.Object.NamespaceUris),
                statusCode,
                output,
                args => args.Count == 1 && args[0].TryGetValue(out string? id) && id == identifier);
        }

        private static void SetupFileRead(Mock<ISession> session, NodeId resourceNodeId, ByteString content)
        {
            NodeId open = ExpandedNodeId.ToNodeId(global::Opc.Ua.MethodIds.FileType_Open, session.Object.NamespaceUris);
            NodeId read = ExpandedNodeId.ToNodeId(global::Opc.Ua.MethodIds.FileType_Read, session.Object.NamespaceUris);
            NodeId close = ExpandedNodeId.ToNodeId(global::Opc.Ua.MethodIds.FileType_Close, session.Object.NamespaceUris);
            SetupCall(session, resourceNodeId, open, StatusCodes.Good, [new Variant((uint)1)], _ => true);
            SetupCall(session, resourceNodeId, read, StatusCodes.Good, [new Variant(content)], _ => true);
            SetupCall(session, resourceNodeId, close, StatusCodes.Good, [], _ => true);
        }

        private static void SetupCall(
            Mock<ISession> session,
            NodeId objectId,
            NodeId methodId,
            StatusCode statusCode,
            ArrayOf<Variant> output,
            Func<ArrayOf<Variant>, bool> inputMatch)
        {
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.Is<ArrayOf<CallMethodRequest>>(r =>
                        r.Count == 1 &&
                        r[0].ObjectId == objectId &&
                        r[0].MethodId == methodId &&
                        inputMatch(r[0].InputArguments)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallResponse
                {
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = statusCode,
                            OutputArguments = output
                        }
                    ]
                });
        }

        private static string Sha256Hex(ByteString content)
        {
            using SHA256 sha = SHA256.Create();
#pragma warning disable CA1850
            // TODO: Replace with SHA256.HashData when net472/net48/netstandard2.0 are no longer targeted.
            byte[] hash = sha.ComputeHash(content.Span.ToArray());
#pragma warning restore CA1850
            char[] chars = new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                chars[i * 2] = ToHexNibble(hash[i] >> 4);
                chars[(i * 2) + 1] = ToHexNibble(hash[i] & 0xF);
            }
            return new string(chars);
        }

        private static char ToHexNibble(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }

        private sealed class TestOpcUaBuilder : IOpcUaBuilder
        {
            public TestOpcUaBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
