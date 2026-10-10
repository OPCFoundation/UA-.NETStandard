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
using NUnit.Framework;
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Tests.NativeTestSupport;

namespace Opc.Ua.EndpointRegistry.Tests
{
    [TestFixture]
    [Category("EndpointRegistry")]
    public sealed class EndpointRegistryMessageResolverTests
    {
        [Test]
        public async Task BaseOverlayPreservesExactExtensionsAndExplicitNullAsync()
        {
            var provider = new Provider();
            provider.Add("base", """{"messageid":"base","description":"base","x-number":1.00,"x-remove":"old"}""");
            provider.Add("derived",
                """{"messageid":"derived","basemessageuri":"/messagegroups/g/messages/base","x-remove":null}""");

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, Request("derived"))
                .ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good),
                result.Issues.Count == 0 ? string.Empty : result.Issues[0].Detail);
            Assert.That(result.Status, Is.EqualTo("complete"));
            Assert.That(result.Sources.Count, Is.EqualTo(2));
            Assert.That(result.Schema, Is.Null);
            Assert.That(RegistryValues.Identical(Mapper.Restore(result.Definition), Json(
                """
                {"messageid":"derived","description":"base","x-number":1.00,"x-remove":null,
                 "basemessageuri":"/messagegroups/g/messages/base"}
                """)), Is.True);
            Assert.That(BinaryRoundTrip(result).IsEqual(result), Is.True);
        }

        [TestCase("")]
        [TestCase("/versions/1")]
        public async Task GroupNamedVersionsDoesNotChangeTheMessageRoleAsync(string suffix)
        {
            var provider = new Provider { GroupId = "versions" };
            provider.Add("base", """{"messageid":"base","description":"inherited"}""");
            provider.Add("derived",
                """{"messageid":"derived","basemessageuri":"/messagegroups/versions/messages/base"}""");
            var request = new MessageResolutionRequestDataType
            {
                Reference = "/messagegroups/versions/messages/derived" + suffix
            };

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, request).ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good),
                result.Issues.Count == 0 ? string.Empty : result.Issues[0].Detail);
            Assert.That(result.Status, Is.EqualTo("complete"));
            Assert.That(result.Definition.Description, Is.EqualTo("inherited"));
            Assert.That(result.Sources.Count, Is.EqualTo(2));
            Assert.That(result.Sources[0].Xid, Is.EqualTo(request.Reference));
            Assert.That(result.Sources[0].Role,
                Is.EqualTo(suffix.Length == 0 ? "MetadataResource" : "MetadataVersion"));
            Assert.That(result.Sources[1].Xid, Is.EqualTo("/messagegroups/versions/messages/base"));
            Assert.That(result.Sources[1].Role, Is.EqualTo("MetadataResource"));
        }

        [Test]
        public async Task MissingInputAndUnsupportedSemanticsAreExplicitAsync()
        {
            var provider = new Provider();
            NativeMessageResolutionResultDataType missing = await ResolveAsync(provider, Request("missing"))
                .ConfigureAwait(false);
            Assert.That(missing.Status, Is.EqualTo("missing-inputs"));
            Assert.That(missing.Issues[0].Code, Is.EqualTo("E_REFERENCE_MISSING"));
            Assert.That(missing.Definition, Is.Null);
            Assert.That(missing.Schema, Is.Null);
            MessageResolutionRequestDataType request = Request("missing");
            request.RequiredSemantics = ["vendor-required"];
            NativeMessageResolutionResultDataType unsupported = await ResolveAsync(provider, request)
                .ConfigureAwait(false);
            Assert.That(unsupported.Status, Is.EqualTo("unsupported-semantics"));
            Assert.That(unsupported.Issues[0].Code, Is.EqualTo("E_SEMANTICS_UNSUPPORTED"));
        }

        [Test]
        public async Task CycleFailureRetainsVerifiedSourcesAsync()
        {
            var provider = new Provider();
            provider.Add("a", """{"messageid":"a","basemessageuri":"/messagegroups/g/messages/b"}""");
            provider.Add("b", """{"messageid":"b","basemessageuri":"/messagegroups/g/messages/a/versions/1"}""");

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, Request("a"))
                .ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo("invalid-input"));
            Assert.That(result.Issues[0].Code, Is.EqualTo("E_MESSAGE_CYCLE"), result.Issues[0].Detail);
            Assert.That(result.Sources.Count, Is.EqualTo(2));
            Assert.That(result.Definition, Is.Null);
        }

        [Test]
        public async Task RelativeReferencesCannotCrossOriginsAsync()
        {
            var provider = new Provider();
            MessageResolutionRequestDataType request = Request("a");
            request.References =
            [
                new MessageReferenceBindingDataType
                {
                    Context = Origin(),
                    ReferenceUri = request.Reference,
                    Target = Source("a", "urn:test:other")
                }
            ];

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, request).ConfigureAwait(false);

            Assert.That(result.Issues[0].Code, Is.EqualTo("E_REFERENCE_ORIGIN"), result.Issues[0].Detail);
            Assert.That(provider.ReadCalls, Is.Zero);
        }

        [Test]
        public async Task ProviderSourcesMustMatchTheSelectedIdentityAsync()
        {
            var provider = new Provider();
            provider.Add("a", """{"messageid":"a"}""");
            provider.Observations["a"].Source = Source("a", "urn:test:impostor");

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, Request("a"))
                .ConfigureAwait(false);

            Assert.That(result.Issues[0].Code, Is.EqualTo("E_REFERENCE_ORIGIN"));
            Assert.That(result.Sources.Count, Is.Zero);
        }

        [Test]
        public async Task SoleVersionSelectionMustMatchTheObservedVersionAsync()
        {
            var provider = new Provider();
            provider.Add("a", """{"messageid":"a","versionid":"1"}""");

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, Request("a/versions/2"))
                .ConfigureAwait(false);

            Assert.That(result.Issues[0].Code, Is.EqualTo("E_REFERENCE_IDENTITY"), result.Issues[0].Detail);
            Assert.That(result.Definition, Is.Null);
        }

        [Test]
        public async Task SchemaProviderReceivesTheSelectedMessageOriginAsync()
        {
            const string absolute = "https://remote.example.test/message";
            var provider = new Provider();
            provider.Add("a",
                """
                {"messageid":"a","self":"https://remote.example.test/message",
                 "dataschemaformat":"JsonSchema/2020-12","dataschemauri":"https://remote.example.test/schema"}
                """);
            provider.Observations["a"].Source = Source("a", "urn:test:remote");
            var request = new MessageResolutionRequestDataType
            {
                Reference = absolute,
                CheckSchema = true,
                References =
                [
                    new MessageReferenceBindingDataType
                    {
                        Context = Origin(),
                        ReferenceUri = absolute,
                        Target = Source("a", "urn:test:remote")
                    }
                ]
            };

            NativeMessageResolutionResultDataType result = await ResolveAsync(provider, request).ConfigureAwait(false);

            Assert.That(result.Issues[0].Code, Is.EqualTo("E_SCHEMA_PROVIDER_MISSING"), result.Issues[0].Detail);
            Assert.That(provider.SchemaOrigin!.OriginUri, Is.EqualTo("urn:test:remote"));
        }

        [Test]
        public void ApplicationOriginsRequirePortableRootNodeIds()
        {
            var root = new RegistryEntityReferenceDataType
            {
                ApplicationUri = "urn:test:server",
                RegistryNode = new ExpandedNodeId(64100u, 2)
            };
            Assert.Throws<ArgumentException>(() => new EndpointRegistryOriginKey(root));
            root.RegistryNode = new ExpandedNodeId(64100u, 0, Namespaces.EndpointRegistry);
            Assert.That(new EndpointRegistryOriginKey(root).ApplicationUri, Is.EqualTo("urn:test:server"));
        }

        private static ValueTask<NativeMessageResolutionResultDataType> ResolveAsync(
            Provider provider, MessageResolutionRequestDataType request)
        {
            return new EndpointRegistryMessageResolver().ResolveAsync(request,
                new EndpointRegistryMessageResolutionContext
                {
                    LocalOrigin = Origin(),
                    Mapper = Mapper,
                    Provider = provider
                });
        }

        private static MessageResolutionRequestDataType Request(string id)
        {
            return new MessageResolutionRequestDataType { Reference = "/messagegroups/g/messages/" + id };
        }

        private static RegistryEntityReferenceDataType Origin()
        {
            return new RegistryEntityReferenceDataType { OriginUri = "urn:test:local" };
        }

        private static RegistryEntityReferenceDataType Source(
            string id, string origin = "urn:test:local", string group = "g")
        {
            return new RegistryEntityReferenceDataType
            {
                OriginUri = origin,
                Xid = "/messagegroups/" + group + "/messages/" + id,
                Role = "MetadataResource"
            };
        }

        private sealed class Provider : IEndpointRegistryResolutionProvider
        {
            public Dictionary<string, EndpointRegistryMessageObservation> Observations { get; } =
                new(StringComparer.Ordinal);

            public int ReadCalls { get; private set; }

            public string GroupId { get; set; } = "g";

            public RegistryEntityReferenceDataType? SchemaOrigin { get; private set; }

            public void Add(string id, string json)
            {
                Observations.Add(id, new EndpointRegistryMessageObservation
                {
                    Source = Source(id, group: GroupId),
                    Metadata = (RegistryObjectValueDataType)Json(json),
                    Epoch = 1,
                    VersionId = "1"
                });
            }

            public ValueTask<EndpointRegistryMessageObservation?> ReadMessageAsync(
                RegistryEntityReferenceDataType reference, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadCalls++;
                string id = reference.Xid!.Split('/')[4];
                if (!Observations.TryGetValue(id, out EndpointRegistryMessageObservation? observation))
                {
                    return new ValueTask<EndpointRegistryMessageObservation?>((EndpointRegistryMessageObservation?)null);
                }
                var source = (RegistryEntityReferenceDataType)observation.Source.Clone();
                source.Xid = reference.Xid;
                source.Role = reference.Role;
                return new ValueTask<EndpointRegistryMessageObservation?>(new EndpointRegistryMessageObservation
                {
                    Source = source,
                    Metadata = observation.Metadata,
                    Epoch = observation.Epoch,
                    VersionId = observation.VersionId
                });
            }

            public ValueTask<SchemaDocumentDataType?> ResolveSchemaAsync(
                MessageDefinitionDataType definition,
                RegistryEntityReferenceDataType origin,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SchemaOrigin = origin;
                return new ValueTask<SchemaDocumentDataType?>((SchemaDocumentDataType?)null);
            }
        }
    }
}
