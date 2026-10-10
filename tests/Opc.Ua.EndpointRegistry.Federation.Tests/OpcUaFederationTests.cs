/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.EndpointRegistry.Server;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;
using Quickstarts.ReferenceServer;
using static Opc.Ua.EndpointRegistry.Federation.Tests.FederationTestSupport;

namespace Opc.Ua.EndpointRegistry.Federation.Tests
{
    [TestFixture]
    [Category("EndpointRegistry")]
    [NonParallelizable]
    public sealed class OpcUaFederationTests
    {
        private const string RemoteUri = "https://catalog.example.test/messagegroups/g/messages/m";

        [SetUp]
        public async Task StartAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pki = Path.Combine(TestContext.CurrentContext.WorkDirectory, nameof(OpcUaFederationTests),
                Guid.NewGuid().ToString("N"));
            m_resolution = new EndpointRegistryResolutionOptions { LocalOriginUri = "urn:test:first" };
            m_first = await StartServerAsync("first", m_resolution).ConfigureAwait(false);
            m_second = await StartServerAsync("second",
                new EndpointRegistryResolutionOptions { LocalOriginUri = "urn:test:second" }).ConfigureAwait(false);
            m_client = new ClientFixture(m_telemetry);
            await m_client.LoadClientConfigurationAsync(Path.Combine(m_pki, "client")).ConfigureAwait(false);
        }

        [TearDown]
        public async Task StopAsync()
        {
            if (m_client is not null)
            {
                await m_client.DisposeAsync().ConfigureAwait(false);
            }
            if (m_first is not null)
            {
                await m_first.StopAsync().ConfigureAwait(false);
            }
            if (m_second is not null)
            {
                await m_second.StopAsync().ConfigureAwait(false);
            }
            if (m_pki is not null && Directory.Exists(m_pki))
            {
                Directory.Delete(m_pki, recursive: true);
            }
        }

        [TestCase("")]
        [TestCase("/versions/1")]
        public async Task UaDiscoveryRecognizesVersionSuffixAfterAGroupNamedVersionsAsync(string suffix)
        {
            using ISession session = await ConnectAsync(m_second!).ConfigureAwait(false);
            await WriteAsync(session, """{"messagegroupid":"versions","messages":{"m":{"messageid":"m"}}}""",
                "versions").ConfigureAwait(false);
            string xid = "/messagegroups/versions/messages/m" + suffix;

            RegistryEntityReferenceDataType discovered = await Provider(session, "urn:test:second")
                .DiscoverMessageAsync(xid).ConfigureAwait(false);

            Assert.That(discovered.Xid, Is.EqualTo(xid));
            Assert.That(discovered.Role, Is.EqualTo(suffix.Length == 0 ? "MetadataResource" : "MetadataVersion"));
            Assert.That(discovered.HasNativeTarget, Is.True);
            Assert.That(discovered.NativeTarget.IsNull, Is.False);
            Assert.That(new FederationSourceKey(discovered).LogicalXid,
                Is.EqualTo("/messagegroups/versions/messages/m"));
        }

        [Test]
        public async Task TwoSecuredServersResolveRemoteBaseWithDistinctSourceKeys()
        {
            using ISession first = await ConnectAsync(m_first!).ConfigureAwait(false);
            using ISession second = await ConnectAsync(m_second!).ConfigureAwait(false);
            Assert.That(first.ConfiguredEndpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(second.ConfiguredEndpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            RegistryRecordMapper mapper = SessionMapper(first);
            await WriteAsync(second,
                """
                {"messagegroupid":"g","messages":{"m":{"messageid":"m",
                 "self":"https://catalog.example.test/messagegroups/g/messages/m",
                 "description":"remote base","x-object":{"base":1.00},"datacontenttype":"application/json"}}}
                """)
                .ConfigureAwait(false);
            await WriteAsync(first,
                """
                {"messagegroupid":"g","messages":{"m":{"messageid":"m",
                 "basemessageuri":"https://catalog.example.test/messagegroups/g/messages/m","x-object":{"child":null}}}}
                """)
                .ConfigureAwait(false);
            OpcUaFederationProvider remote = Provider(second, "urn:test:second");
            RegistryEntityReferenceDataType remoteSource = await remote.DiscoverMessageAsync(Xid).ConfigureAwait(false);
            FederationResolutionCache cache = await remote.PreloadAsync(new FederationResolutionCache(), remoteSource)
                .ConfigureAwait(false);
            RegistryEntityReferenceDataType localSource = await Provider(first, "urn:test:first").DiscoverMessageAsync(Xid)
                .ConfigureAwait(false);
            Assert.That(localSource.NativeTarget, Is.EqualTo(remoteSource.NativeTarget),
                "Equal local ids/NodeIds must still be scoped by origin.");
            Assert.That(new FederationSourceKey(localSource), Is.Not.EqualTo(new FederationSourceKey(remoteSource)));
            m_resolution!.Provider = cache;
            await second.CloseAsync().ConfigureAwait(false);

            var root = RootClient(first);
            var request = new MessageResolutionRequestDataType
            {
                Reference = Xid,
                RequiredSemantics = ["base-overlay-v1"],
                References =
                [
                    new MessageReferenceBindingDataType { Context = localSource, ReferenceUri = Xid, Target = localSource },
                    new MessageReferenceBindingDataType { Context = localSource, ReferenceUri = RemoteUri, Target = remoteSource }
                ]
            };
            NativeMessageResolutionResultDataType result = await root.ResolveMessageAsync(request).ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo("complete"), Detail(result));
            RegistryObjectValueDataType expected = Json(
                """
                {"messageid":"m","basemessageuri":"https://catalog.example.test/messagegroups/g/messages/m",
                 "description":"remote base","x-object":{"base":1.00,"child":null},"datacontenttype":"application/json","epoch":1,
                 "self":"https://catalog.example.test/messagegroups/g/messages/m"}
                """);
            Assert.That(RegistryValues.Identical(mapper.Restore(result.Definition), expected), Is.True,
                System.Text.Encoding.UTF8.GetString(RegistryValues.ToJson(mapper.Restore(result.Definition)).ToArray()));
            Assert.Multiple(() =>
            {
                Assert.That(result.Sources.Count, Is.EqualTo(2));
                Assert.That(result.Sources[0].OriginUri, Is.EqualTo("urn:test:first"));
                Assert.That(result.Sources[1].OriginUri, Is.EqualTo("urn:test:second"));
                Assert.That(result.Sources[1].NativeTarget.NamespaceIndex, Is.Zero);
                Assert.That(result.Sources[1].NativeTarget.ServerIndex, Is.Zero);
                Assert.That(result.Sources[1].NativeTarget.NamespaceUri, Is.EqualTo(Namespaces.EndpointRegistry));
            });
            request.References = [request.References[0]];
            NativeMessageResolutionResultDataType missing = await root.ResolveMessageAsync(request).ConfigureAwait(false);
            Assert.That(missing.Status, Is.EqualTo("missing-inputs"));
            Assert.That(missing.Issues[0].Code, Is.EqualTo("E_REFERENCE_MISSING"));
            Assert.That(missing.Definition, Is.Null);
            Assert.That(missing.Sources.Count, Is.EqualTo(1));
            request.RequiredSemantics = ["wire-codec"];
            NativeMessageResolutionResultDataType unsupported = await root.ResolveMessageAsync(request).ConfigureAwait(false);
            Assert.That(unsupported.Status, Is.EqualTo("unsupported-semantics"));
            Assert.That(unsupported.Issues[0].Code, Is.EqualTo("E_SEMANTICS_UNSUPPORTED"));
            Assert.That(unsupported.Sources.Count, Is.Zero);
        }

        [TestCase("application")]
        [TestCase("root")]
        [TestCase("type")]
        [TestCase("version")]
        [TestCase("native-target")]
        [TestCase("origin")]
        public async Task OpcUaProviderRejectsWrongPinsBeforeCaching(string pin)
        {
            using ISession second = await ConnectAsync(m_second!).ConfigureAwait(false);
            await WriteAsync(second, """{"messagegroupid":"g","messages":{"m":{"messageid":"m"}}}""").ConfigureAwait(false);
            OpcUaFederationProvider provider = Provider(second, "urn:test:second");
            RegistryEntityReferenceDataType source = await provider.DiscoverMessageAsync(Xid).ConfigureAwait(false);
            if (pin == "application")
            {
                Assert.Throws<ArgumentException>(() => new OpcUaFederationProvider(second,
                    new FederationTrustBinding(source, "urn:test:wrong", ObjectIds.EndpointRegistry, [source.Locator!]),
                    SessionMapper(second), m_telemetry!));
                return;
            }
            if (pin == "root" || pin == "type")
            {
                provider = new OpcUaFederationProvider(second,
                    new FederationTrustBinding(source, second.ConfiguredEndpoint.Description.Server.ApplicationUri!,
                        pin == "root" ? source.NativeTarget : ObjectIds.EndpointRegistry, [source.Locator!],
                        pin == "type" ? XRegistry.ObjectTypeIds.GroupType : ObjectTypeIds.MessageDefinitionType),
                    SessionMapper(second), m_telemetry!);
            }
            if (pin == "version")
            {
                source.Xid += "/versions/2";
                source.Role = "MetadataVersion";
            }
            if (pin == "native-target")
            {
                source.NativeTarget = ObjectIds.EndpointRegistry;
            }
            if (pin == "origin")
            {
                source.OriginUri = "urn:test:wrong";
            }
            var empty = new FederationResolutionCache();
            Assert.CatchAsync<Exception>(async () => await provider.PreloadAsync(empty, source).ConfigureAwait(false));
            Assert.That(empty.Count, Is.Zero);
        }

        [Test]
        public async Task RealAuthorizedLocatorRelocationRequiresFreshSessionAndRetainsPortablePins()
        {
            FederationResolutionCache previous;
            RegistryEntityReferenceDataType oldSource;
            using (ISession oldSession = await ConnectAsync(m_second!).ConfigureAwait(false))
            {
                await WriteAsync(oldSession, """{"messagegroupid":"g","messages":{"m":{"messageid":"m"}}}""").ConfigureAwait(false);
                OpcUaFederationProvider oldProvider = Provider(oldSession, "urn:test:second");
                oldSource = await oldProvider.DiscoverMessageAsync(Xid).ConfigureAwait(false);
                previous = await oldProvider.PreloadAsync(new FederationResolutionCache(), oldSource).ConfigureAwait(false);
            }
            await m_second!.StopAsync().ConfigureAwait(false);
            m_second = await StartServerAsync("second",
                new EndpointRegistryResolutionOptions { LocalOriginUri = "urn:test:second" }).ConfigureAwait(false);
            using ISession session = await ConnectAsync(m_second).ConfigureAwait(false);
            await WriteAsync(session, """{"messagegroupid":"g","messages":{"m":{"messageid":"m"}}}""").ConfigureAwait(false);
            OpcUaFederationProvider movedProvider = Provider(session, "urn:test:second");
            RegistryEntityReferenceDataType moved = await movedProvider.DiscoverMessageAsync(Xid).ConfigureAwait(false);
            FederationResolutionCache fresh = await movedProvider.PreloadAsync(previous, moved).ConfigureAwait(false);
            Assert.That(moved.Locator, Is.Not.EqualTo(oldSource.Locator));
            Assert.That(new FederationSourceKey(moved), Is.EqualTo(new FederationSourceKey(oldSource)));
            Assert.That(moved.NativeTarget, Is.EqualTo(oldSource.NativeTarget));
            Assert.That(fresh.Count, Is.EqualTo(previous.Count));
            Assert.That((await fresh.ReadMessageAsync(moved, default).ConfigureAwait(false))!.Source.Locator,
                Is.EqualTo(moved.Locator));
        }

        [Test]
        public async Task GroupPreloadVerifiesCollectionRootAndNativeMetadataAsync()
        {
            using ISession first = await ConnectAsync(m_first!).ConfigureAwait(false);
            using ISession second = await ConnectAsync(m_second!).ConfigureAwait(false);
            await WriteAsync(first, """{"messagegroupid":"g","messages":{"m":{"messageid":"m"}}}""")
                .ConfigureAwait(false);
            await WriteAsync(second, """{"messagegroupid":"g","messages":{"m":{"messageid":"m"}}}""")
                .ConfigureAwait(false);
            FederationGroupSnapshot local = await Provider(first, "urn:test:first").PreloadGroupAsync("/messagegroups/g")
                .ConfigureAwait(false);
            FederationGroupSnapshot remote = await Provider(second, "urn:test:second").PreloadGroupAsync("/messagegroups/g")
                .ConfigureAwait(false);
            Assert.That(local.Origin, Is.Not.EqualTo(remote.Origin));
            Assert.That(local.Source.Xid, Is.EqualTo(remote.Source.Xid));
            Assert.That(remote.Metadata, Is.InstanceOf<MessageGroupDataType>());
            Assert.That(((MessageGroupDataType)remote.Metadata).MessageGroupId, Is.EqualTo("g"));
            Assert.That(remote.Source.NativeTarget.NamespaceIndex, Is.Zero);
            Assert.That(remote.RegistryRoot, Is.EqualTo(ObjectIds.EndpointRegistry));
            Assert.That(remote.Epoch, Is.GreaterThan(0));
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await Provider(second, "urn:test:second").PreloadGroupAsync("/messagegroups/g", local)
                    .ConfigureAwait(false));
        }
        private async Task<ServerFixture<FederationServer>> StartServerAsync(string id, EndpointRegistryResolutionOptions resolution)
        {
            var fixture = new ServerFixture<FederationServer>(telemetry => new FederationServer(telemetry, resolution))
            {
                AutoAccept = true,
                SecurityNone = false
            };
            await fixture.LoadConfigurationAsync(Path.Combine(m_pki!, id)).ConfigureAwait(false);
            fixture.Config.ApplicationUri = "urn:test:federation:" + id;
            fixture.Config.ServerConfiguration!.UserTokenPolicies +=
                new UserTokenPolicy(UserTokenType.UserName) { SecurityPolicyUri = SecurityPolicies.Basic256Sha256 };
            await fixture.StartAsync().ConfigureAwait(false);
            return fixture;
        }

        private async Task<ISession> ConnectAsync(ServerFixture<FederationServer> fixture)
        {
            ISession session = await m_client!.ConnectAsync(
                new Uri($"opc.tcp://localhost:{fixture.Port}/{nameof(FederationServer)}"),
                SecurityPolicies.Basic256Sha256, userIdentity: new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
            session.MessageContext.Factory.Builder.AddOpcUaXRegistry().AddOpcUaSchemaRegistry().AddOpcUaEndpointRegistry().Commit();
            return session;
        }

        private OpcUaFederationProvider Provider(ISession session, string origin) => new(session,
            new FederationTrustBinding(new RegistryEntityReferenceDataType { OriginUri = origin },
                session.ConfiguredEndpoint.Description.Server.ApplicationUri!, ObjectIds.EndpointRegistry,
                [session.ConfiguredEndpoint.Description.EndpointUrl!]), SessionMapper(session), m_telemetry!);

        private static RegistryRecordMapper SessionMapper(ISession session) =>
            EndpointRegistryNativeCatalog.CreateMapper(session.MessageContext,
                [new SchemaRegistry.Formats.JsonSchemaFormatProvider(), new SchemaRegistry.Formats.AvroSchemaFormatProvider()]);

        private async Task WriteAsync(ISession session, string json, string groupId = "g")
        {
            var access = new NativeRegistryAccessTypeClient(session,
                ExpandedNodeId.ToNodeId(EndpointRegistryWellKnown.EndpointRegistryTypedAccess, session.NamespaceUris), m_telemetry!);
            RegistryReadResultDataType existing = await access.ReadDocumentAsync(new RegistryReadRequestDataType
            {
                TargetXid = "/messagegroups/" + groupId,
                DocumentKind = "metadata",
                View = 0,
                MaxItems = 100
            }).ConfigureAwait(false);
            var definition = Json(json);
            RegistryObjectValueDataType? previous = null;
            if (StatusCode.IsGood(existing.StatusCode))
            {
                Assert.That(existing.Document.TryGetValue(out previous, session.MessageContext), Is.True);
            }
            PreserveOwned(definition, previous);
            RegistryMutationResultDataType result = await access.WriteDocumentAsync(new RegistryWriteRequestDataType
            {
                TargetXid = "/messagegroups/" + groupId,
                ExpectedEpoch = existing.Epoch,
                Definition = SessionMapper(session).Project(definition, nameof(MessageGroupDataType))
            }).ConfigureAwait(false);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good),
                result.Issues.Count == 0 ? string.Empty : result.Issues[0].Detail);
        }

        private static void PreserveOwned(RegistryObjectValueDataType value, RegistryObjectValueDataType? previous)
        {
            var members = new System.Collections.Generic.List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in value.Members)
            {
                if (member.Name is "epoch" or "self" or "xid" or "createdat" or "modifiedat")
                {
                    continue;
                }
                if (member.Value is RegistryObjectValueDataType children && member.Name == "messages")
                {
                    RegistryObjectValueDataType? oldChildren = previous?.Members.ToArray()?
                        .FirstOrDefault(item => item.Name == "messages")?.Value as RegistryObjectValueDataType;
                    foreach (RegistryMemberDataType child in children.Members)
                    {
                        PreserveOwned((RegistryObjectValueDataType)child.Value, oldChildren?.Members.ToArray()?
                            .FirstOrDefault(item => item.Name == child.Name)?.Value as RegistryObjectValueDataType);
                    }
                }
                members.Add(member);
            }
            if (previous is not null)
            {
                foreach (RegistryMemberDataType member in previous.Members)
                {
                    if (member.Name is "epoch" or "self" or "xid" or "createdat" or "modifiedat")
                    {
                        members.Add(member);
                    }
                }
            }
            value.Members = members.ToArray();
        }
        private EndpointRegistryTypeClient RootClient(ISession session) => new(session,
            ExpandedNodeId.ToNodeId(ObjectIds.EndpointRegistry, session.NamespaceUris), m_telemetry!);

        private static string Detail(NativeMessageResolutionResultDataType result) =>
            result.Issues.Count == 0 ? string.Empty : result.Issues[0].Detail ?? string.Empty;

        private sealed class FederationServer : ReferenceServer
        {
            public FederationServer(ITelemetryContext telemetry, EndpointRegistryResolutionOptions resolution) : base(telemetry)
            {
                AddNodeManager(new EndpointRegistryNodeManagerFactory(new EndpointRegistryServerOptions
                {
                    Generic = new EndpointRegistryCatalogOptions
                    {
                        RegistryId = "federated-messages",
                        Collections = ["messagegroups"],
                        PublicBaseUri = "https://catalog.example.test"
                    },
                    Resolution = resolution
                }));
            }
        }

        private ITelemetryContext? m_telemetry;
        private string? m_pki;
        private EndpointRegistryResolutionOptions? m_resolution;
        private ServerFixture<FederationServer>? m_first;
        private ServerFixture<FederationServer>? m_second;
        private ClientFixture? m_client;
    }
}
