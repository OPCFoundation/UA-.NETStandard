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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.SchemaRegistry.Server.Tests
{
    [TestFixture]
    [Category("SchemaRegistry")]
    [NonParallelizable]
    public sealed class SchemaRegistryWireTests
    {
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_directory = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "schema-wire-" + Guid.NewGuid().ToString("N"));
            m_server = new ServerFixture<SchemaServer>(telemetry => new SchemaServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_client = new ClientFixture(m_telemetry);
            await m_server.LoadConfigurationAsync(Path.Combine(m_directory, "server")).ConfigureAwait(false);
            m_server.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
            await m_server.StartAsync().ConfigureAwait(false);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_directory, "client")).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task StopAsync()
        {
            if (m_client is not null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server is not null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            if (m_directory is not null && Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        [TestCase("JsonSchema/2020-12")]
        [TestCase("Avro/1.11")]
        [TestCase("ApacheArrow/1.0")]
        public async Task TypedSchemaWriteReadGetSchemaAndOpaqueLookupAgreeAsync(string format)
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256, "sysadmin", "demo")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient access = await AccessAsync(session).ConfigureAwait(false);
            ISchemaFormatProvider provider = Provider(format);
            SchemaContentDataType content = Content(provider);
            SchemaReferenceDataType reference = Reference(format, "Schema");
            var request = new TypedSchemaWriteRequestDataType { Reference = reference, Content = content };
            TypedSchemaReadResultDataType written = await access.WriteSchemaAsync(request).ConfigureAwait(false);
            Assert.That(written.StatusCode, Is.EqualTo(StatusCodes.Good), Issue(written));
            TypedSchemaReadResultDataType read = await access.ReadSchemaAsync(written.Document.Reference)
                .ConfigureAwait(false);
            Assert.That(read.Document.Content.IsEqual(content), Is.True);
            var registry = new SchemaRegistryTypeClient(session,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, session.NamespaceUris), m_telemetry!);
            (ByteString bytes, string returnedFormat, string contentType) = await registry.GetSchemaAsync(
                written.Document.Reference.SchemaId).ConfigureAwait(false);
            Assert.That(returnedFormat, Is.EqualTo(format));
            Assert.That(contentType, Is.EqualTo(provider.ContentType));
            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.SchemaRegistry);
            DataValue opaque = await session.ReadValueAsync(new NodeId(written.Document.Reference.SchemaId, ns))
                .ConfigureAwait(false);
            Assert.That(opaque.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(opaque.WrappedValue.TryGetValue(out ByteString document), Is.True);
            Assert.That(document, Is.EqualTo(bytes));
            Assert.That(provider.Parse(document.Span).IsEqual(content), Is.True);
            NodeId fileNode = await PathAsync(session,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, session.NamespaceUris),
                new QualifiedName(kGroupId, ns), new QualifiedName(ResourceId(format, "Schema"), ns),
                new QualifiedName(BrowseNames.Versions, ns), new QualifiedName("1", ns)).ConfigureAwait(false);
            var file = new FileTypeClient(session, fileNode, m_telemetry!);
            uint handle = await file.OpenAsync(1).ConfigureAwait(false);
            ByteString downloaded = await file.ReadAsync(handle, 65536).ConfigureAwait(false);
            await file.CloseAsync(handle).ConfigureAwait(false);
            Assert.That(downloaded, Is.EqualTo(bytes));
            request.ExpectedEpoch = written.Document.Epoch;
            TypedSchemaReadResultDataType noOp = await access.WriteSchemaAsync(request).ConfigureAwait(false);
            Assert.That(noOp.Document.Epoch, Is.EqualTo(written.Document.Epoch));
            Assert.That(noOp.Document.Reference.SchemaId, Is.EqualTo(written.Document.Reference.SchemaId));
            request.ExpectedEpoch = 99;
            TypedSchemaReadResultDataType stale = await access.WriteSchemaAsync(request).ConfigureAwait(false);
            Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(stale.Document, Is.Null);
        }

        [Test]
        public async Task MutationsRequireAuthorizationAndAnEncryptedChannelAsync()
        {
            using ISession reader = await ConnectAsync(SecurityPolicies.Basic256Sha256, "user1", "password")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient readerAccess = await AccessAsync(reader).ConfigureAwait(false);
            var request = new TypedSchemaWriteRequestDataType
            {
                Reference = Reference("JsonSchema/2020-12", "denied"),
                Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
            };
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(
                async () => await readerAccess.WriteSchemaAsync(request).ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            using ISession insecure = await ConnectAsync(SecurityPolicies.None, "sysadmin", "demo")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient insecureAccess = await AccessAsync(insecure).ConfigureAwait(false);
            ServiceResultException plain = Assert.ThrowsAsync<ServiceResultException>(
                async () => await insecureAccess.WriteSchemaAsync(request).ConfigureAwait(false))!;
            Assert.That(plain.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public async Task AmbiguousFingerprintsDoNotRevealHiddenVersionsAsync()
        {
            using ISession admin = await ConnectAsync(SecurityPolicies.Basic256Sha256, "sysadmin", "demo")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient access = await AccessAsync(admin).ConfigureAwait(false);
            TypedSchemaReadResultDataType visible = await access.WriteSchemaAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = Reference("JsonSchema/2020-12", "visible"),
                Content = new JsonSchemaFormatProvider().Parse("""{"type":"number","minimum":1}"""u8)
            }).ConfigureAwait(false);
            TypedSchemaReadResultDataType hidden = await access.WriteSchemaAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = Reference("JsonSchema/2020-12", "hidden"),
                Content = new JsonSchemaFormatProvider().Parse("""{"type":"number","minimum":1.0}"""u8)
            }).ConfigureAwait(false);
            Assert.That(visible.StatusCode, Is.EqualTo(StatusCodes.Good), Issue(visible));
            Assert.That(hidden.StatusCode, Is.EqualTo(StatusCodes.Good), Issue(hidden));
            Assert.That(hidden.Document.Reference.SchemaId, Is.EqualTo(visible.Document.Reference.SchemaId));
            var registry = new SchemaRegistryTypeClient(admin,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, admin.NamespaceUris), m_telemetry!);
            ServiceResultException ambiguous = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await registry.GetSchemaAsync(visible.Document.Reference.SchemaId).ConfigureAwait(false))!;
            Assert.That(ambiguous.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            ushort ns = admin.NamespaceUris.GetIndexOrAppend(Namespaces.SchemaRegistry);
            ServiceResultException collision = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await admin.ReadValueAsync(new NodeId(visible.Document.Reference.SchemaId, ns))
                    .ConfigureAwait(false))!;
            Assert.That(collision.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            using ISession reader = await ConnectAsync(SecurityPolicies.Basic256Sha256, "user1", "password")
                .ConfigureAwait(false);
            var readerRegistry = new SchemaRegistryTypeClient(reader,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, reader.NamespaceUris), m_telemetry!);
            (ByteString bytes, _, _) = await readerRegistry.GetSchemaAsync(visible.Document.Reference.SchemaId)
                .ConfigureAwait(false);
            Assert.That(bytes, Is.EqualTo(ByteString.From("""{"type":"number","minimum":1}"""u8.ToArray())));
            NativeSchemaAccessTypeClient readerAccess = await AccessAsync(reader).ConfigureAwait(false);
            TypedSchemaReadResultDataType denied = await readerAccess.ReadSchemaAsync(hidden.Document.Reference)
                .ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            NodeId nativeContent = await PathAsync(reader,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, reader.NamespaceUris),
                new QualifiedName(kGroupId, ns), new QualifiedName(ResourceId("JsonSchema/2020-12", "hidden"), ns),
                new QualifiedName(BrowseNames.Versions, ns), new QualifiedName("1", ns),
                new QualifiedName(BrowseNames.NativeContent, ns)).ConfigureAwait(false);
            ServiceResultException hiddenRead = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await reader.ReadValueAsync(nativeContent).ConfigureAwait(false))!;
            Assert.That(hiddenRead.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task SchemaSnapshotPinsOversizedContentsAndBothRevisionSpacesAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256, "sysadmin", "demo")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient access = await AccessAsync(session).ConfigureAwait(false);
            SchemaReferenceDataType reference = Reference("JsonSchema/2020-12", "large");
            string description = new('x', 8192);
            var provider = new JsonSchemaFormatProvider();
            TypedSchemaReadResultDataType registered = await access.WriteSchemaAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = reference,
                Content = provider.Parse(System.Text.Encoding.UTF8.GetBytes(
                    "{\"type\":\"number\",\"description\":\"" + description + "\"}"))
            }).ConfigureAwait(false);
            Assert.That(registered.StatusCode, Is.EqualTo(StatusCodes.Good), Issue(registered));
            ushort xns = session.NamespaceUris.GetIndexOrAppend(XRegistry.Namespaces.xRegistry);
            NodeId typedAccess = await PathAsync(session,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, session.NamespaceUris),
                new QualifiedName(XRegistry.BrowseNames.TypedAccess, xns)).ConfigureAwait(false);
            var snapshots = new NativeRegistryAccessTypeClient(session, typedAccess, m_telemetry!);
            RegistrySnapshotOpenResultDataType opened = await snapshots.OpenDocumentAsync(
                new RegistrySnapshotOpenRequestDataType
                {
                    TargetXid = reference.Entity.Xid,
                    DocumentKind = "schema",
                    View = 1,
                    ExpectedEpoch = registered.Document.Epoch
                }).ConfigureAwait(false);
            Assert.That(opened.TargetEpoch, Is.EqualTo(1));
            Assert.That(opened.RegistryEpoch, Is.GreaterThanOrEqualTo(opened.TargetEpoch));
            await access.WriteSchemaAsync(new TypedSchemaWriteRequestDataType
            {
                Reference = reference,
                Content = provider.Parse("""{"type":"number","description":"changed"}"""u8),
                ExpectedEpoch = 1
            }).ConfigureAwait(false);
            var text = new System.Text.StringBuilder();
            ByteString continuation = ByteString.Empty;
            while (true)
            {
                RegistrySnapshotReadResultDataType part = await snapshots.ReadDocumentPartAsync(
                    new RegistrySnapshotReadRequestDataType
                    {
                        SnapshotId = opened.SnapshotId,
                        Path =
                        [
                            new RegistryPathElementDataType { Kind = 0, Name = "Content" },
                            new RegistryPathElementDataType { Kind = 0, Name = "Root" },
                            new RegistryPathElementDataType { Kind = 0, Name = "Description" }
                        ],
                        Offset = (ulong)text.Length,
                        MaxItems = 256,
                        MaxBytes = 1024,
                        ContinuationPoint = continuation
                    }).ConfigureAwait(false);
                Assert.That(part.Value.TryGetValue(out string chunk), Is.True);
                text.Append(chunk);
                if (part.Complete)
                {
                    break;
                }
                continuation = part.ContinuationPoint;
            }
            Assert.That(text.ToString(), Is.EqualTo(description));
            Assert.That((await snapshots.CloseDocumentAsync(opened.SnapshotId).ConfigureAwait(false)).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            TypedSchemaReadResultDataType current = await access.ReadSchemaAsync(reference).ConfigureAwait(false);
            await using XRegistry.Client.RegistrySnapshotClient client = await XRegistry.Client.RegistrySnapshotClient
                .OpenAsync(snapshots,
                    new RegistrySnapshotOpenRequestDataType
                    {
                        TargetXid = reference.Entity.Xid,
                        DocumentKind = "schema",
                        View = 1,
                        ExpectedEpoch = current.Document.Epoch
                    }, 2, 256).ConfigureAwait(false);
            ByteString fingerprint = await client.ReadByteStringAsync(
            [
                new RegistryPathElementDataType { Kind = 0, Name = "Reference" },
                new RegistryPathElementDataType { Kind = 0, Name = "SchemaId" }
            ]).ConfigureAwait(false);
            Assert.That(fingerprint, Is.EqualTo(current.Document.Reference.SchemaId));
            ServiceResultException wrongKind = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadStringAsync(
                [
                    new RegistryPathElementDataType { Kind = 0, Name = "Reference" },
                    new RegistryPathElementDataType { Kind = 0, Name = "SchemaId" }
                ]).ConfigureAwait(false))!;
            Assert.That(wrongKind.StatusCode, Is.EqualTo(StatusCodes.BadTypeMismatch));
        }

        [Test]
        public async Task RegisterSchemaCreatesAnUnconfiguredNativeSubjectAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256, "sysadmin", "demo")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient access = await AccessAsync(session).ConfigureAwait(false);
            var registration = new SchemaRegistrationDataType
            {
                NamespaceUri = "urn:example:new-namespace",
                SchemaName = "DynamicTemperature",
                Format = "JsonSchema/2020-12",
                VersionId = "1",
                EntityUri = "https://schemas.example.test/dynamic-v1",
                ResourceUri = "https://schemas.example.test/dynamic",
                MakeDefault = true
            };
            TypedSchemaReadResultDataType registered = await access.RegisterSchemaAsync(
                new TypedSchemaRegistrationRequestDataType
                {
                    Registration = registration,
                    Content = new JsonSchemaFormatProvider().Parse("""{"type":"number"}"""u8)
                }).ConfigureAwait(false);
            Assert.That(registered.StatusCode, Is.EqualTo(StatusCodes.Good), Issue(registered));
            Assert.That(registered.Document.Reference.Entity.Xid,
                Does.EndWith("/DynamicTemperature.jsonschema/versions/1"));
            SchemaReferenceDataType logical = (SchemaReferenceDataType)registered.Document.Reference.Clone();
            logical.Entity.Xid = logical.Entity.Xid!.Substring(0,
                logical.Entity.Xid.LastIndexOf("/versions/", StringComparison.Ordinal));
            logical.Entity.Role = "LogicalResource";
            logical.EntityUri = registration.ResourceUri;
            logical.SelectedObjectUri = registration.ResourceUri;
            TypedSchemaReadResultDataType read = await access.ReadSchemaAsync(logical).ConfigureAwait(false);
            Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Document.Reference.Entity.Xid, Is.EqualTo(registered.Document.Reference.Entity.Xid));
        }

        [Test]
        public async Task RawUploadIsSessionBoundAndPublishesOnlyOnValidatedCloseAsync()
        {
            using ISession session = await ConnectAsync(SecurityPolicies.Basic256Sha256, "sysadmin", "demo")
                .ConfigureAwait(false);
            NativeSchemaAccessTypeClient access = await AccessAsync(session).ConfigureAwait(false);
            var registration = new SchemaRegistrationDataType
            {
                NamespaceUri = "urn:example:uploads",
                SchemaName = "RawTemperature",
                Format = "JsonSchema/2020-12",
                VersionId = "1",
                EntityUri = "https://schemas.example.test/raw-v1"
            };
            (NodeId fileId, uint handle) = await access.BeginSchemaUploadAsync(registration).ConfigureAwait(false);
            var file = new FileTypeClient(session, fileId, m_telemetry!);
            ByteString raw = ByteString.From("{ \"type\" : \"number\", \"default\" : 1.00 }\n"u8.ToArray());
            await file.WriteAsync(handle, raw).ConfigureAwait(false);
            using ISession other = await ConnectAsync(SecurityPolicies.Basic256Sha256, "sysadmin", "demo")
                .ConfigureAwait(false);
            var guessed = new FileTypeClient(other, fileId, m_telemetry!);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await guessed.CloseAsync(handle).ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await file.CloseAsync(handle).ConfigureAwait(false);
            var registry = new SchemaRegistryTypeClient(session,
                ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, session.NamespaceUris), m_telemetry!);
            ByteString fingerprint = new JsonSchemaFormatProvider().ComputeSchemaId(raw.Span);
            (ByteString downloaded, _, _) = await registry.GetSchemaAsync(fingerprint).ConfigureAwait(false);
            Assert.That(downloaded, Is.EqualTo(raw));
            registration.SchemaName = "InvalidUpload";
            registration.EntityUri = "https://schemas.example.test/invalid-upload-v1";
            (NodeId badFileId, uint badHandle) = await access.BeginSchemaUploadAsync(registration).ConfigureAwait(false);
            var badFile = new FileTypeClient(session, badFileId, m_telemetry!);
            await badFile.WriteAsync(badHandle, ByteString.From("not-json"u8.ToArray())).ConfigureAwait(false);
            ServiceResultException invalid = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await badFile.CloseAsync(badHandle).ConfigureAwait(false))!;
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }
        private async Task<ISession> ConnectAsync(string policy, string user, string password)
        {
            ISession session = await m_client!.ConnectAsync(
                new Uri($"opc.tcp://localhost:{m_server!.Port}/{nameof(SchemaServer)}"), policy,
                userIdentity: new UserIdentity(user, System.Text.Encoding.UTF8.GetBytes(password)))
                .ConfigureAwait(false);
            session.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().Commit();
            return session;
        }

        private async Task<NativeSchemaAccessTypeClient> AccessAsync(ISession session)
        {
            ushort ns = session.NamespaceUris.GetIndexOrAppend(Namespaces.SchemaRegistry);
            TranslateBrowsePathsToNodeIdsResponse translated = await session.TranslateBrowsePathsToNodeIdsAsync(null,
            [
                new BrowsePath
                {
                    StartingNode = ExpandedNodeId.ToNodeId(ObjectIds.SchemaRegistry, session.NamespaceUris),
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasComponent,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(BrowseNames.TypedSchemas, ns)
                            }
                        ]
                    }
                }
            ], default).ConfigureAwait(false);
            Assert.That(translated.Results[0].Targets.Count, Is.GreaterThan(0));
            return new NativeSchemaAccessTypeClient(session,
                ExpandedNodeId.ToNodeId(translated.Results[0].Targets[0].TargetId, session.NamespaceUris), m_telemetry!);
        }

        private static ISchemaFormatProvider Provider(string format) => format switch
        {
            "JsonSchema/2020-12" => new JsonSchemaFormatProvider(),
            "Avro/1.11" => new AvroSchemaFormatProvider(),
            _ => new ArrowSchemaFormatProvider()
        };

        private static SchemaContentDataType Content(ISchemaFormatProvider provider)
        {
            if (provider.Format == "JsonSchema/2020-12")
            {
                return provider.Parse("""{"type":"number"}"""u8);
            }
            if (provider.Format == "Avro/1.11")
            {
                return provider.Parse("\"string\""u8);
            }
            return provider.Parse(ByteString.FromHexString(
                "FFFFFFFF800000001000000000000A000C000600050008000A000000000104000C000000080008000000040008000000" +
                "040000000100000014000000100014000800000007000C00000010001000000000000002100000001C00000004000000" +
                "00000000020000006964000008000C00080007000800000000000001400000000000000000000000").Span);
        }

        private static SchemaReferenceDataType Reference(string format, string name)
        {
            string xid = ResourceXid(format, name) + "/versions/1";
            return new SchemaReferenceDataType
            {
                Entity = new RegistryEntityReferenceDataType
                {
                    OriginUri = "urn:test:schema-wire",
                    Xid = xid,
                    Role = "ExactVersion"
                },
                EntityUri = "https://schemas.example.test" + xid,
                SelectedObjectUri = "https://schemas.example.test" + xid,
                Format = format
            };
        }

        private static string ResourceXid(string format, string name) =>
            "/schemagroups/" + kGroupId + "/schemas/" + ResourceId(format, name);

        private static string ResourceId(string format, string name) => name + "." + (format switch
        {
            "JsonSchema/2020-12" => "jsonschema",
            "Avro/1.11" => "avro",
            "ApacheArrow/1.0" => "arrow",
            _ => throw new ArgumentException("Unsupported fixture format.", nameof(format))
        });

        private static string Issue(TypedSchemaReadResultDataType result) =>
            result.Issues.Count == 0 ? string.Empty : result.Issues[0].Detail!;

        private static async Task<NodeId> PathAsync(ISession session, NodeId root, params QualifiedName[] names)
        {
            var elements = new RelativePathElement[names.Length];
            for (int index = 0; index < elements.Length; index++)
            {
                elements[index] = new RelativePathElement
                {
                    ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    TargetName = names[index]
                };
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(null,
                [new BrowsePath { StartingNode = root, RelativePath = new RelativePath { Elements = elements } }],
                default).ConfigureAwait(false);
            Assert.That(response.Results[0].Targets.Count, Is.GreaterThan(0), string.Join<QualifiedName>("/", names));
            return ExpandedNodeId.ToNodeId(response.Results[0].Targets[0].TargetId, session.NamespaceUris);
        }

        private sealed class SchemaServer : ReferenceServer
        {
            public SchemaServer(ITelemetryContext telemetry) : base(telemetry)
            {
                var options = new SchemaRegistryServerOptions { Enabled = true, OriginUri = "urn:test:schema-wire" };
                options.IsVisible = (context, reference) =>
                    reference.Entity.Xid!.IndexOf("/hidden.jsonschema/", StringComparison.Ordinal) < 0 ||
                    context is SessionSystemContext caller &&
                    caller.UserIdentity?.GrantedRoleIds.Contains(Ua.ObjectIds.WellKnownRole_SecurityAdmin) == true;
                options.NamespaceUris.Add(kGroupId, "http://contoso.org/UA/Pumps/");
                foreach (string format in new[] { "JsonSchema/2020-12", "Avro/1.11", "ApacheArrow/1.0" })
                {
                    options.SchemaNames.Add(ResourceXid(format, "Schema"), "Schema");
                }
                foreach (string name in new[] { "visible", "hidden", "large" })
                {
                    options.SchemaNames.Add(ResourceXid("JsonSchema/2020-12", name), name);
                }
                AddNodeManager(new SchemaRegistryNodeManagerFactory(options));
            }
        }

        private ITelemetryContext? m_telemetry;
        private const string kGroupId = "org.contoso.UA.Pumps";
        private string? m_directory;
        private ServerFixture<SchemaServer>? m_server;
        private ClientFixture? m_client;
    }
}
