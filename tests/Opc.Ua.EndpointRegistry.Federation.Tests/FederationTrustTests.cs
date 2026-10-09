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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.XRegistry;
using static Opc.Ua.EndpointRegistry.Federation.Tests.FederationTestSupport;

namespace Opc.Ua.EndpointRegistry.Federation.Tests
{
    [TestFixture]
    [Category("EndpointRegistry")]
    public sealed class FederationTrustTests
    {
        [TestCase("origin")]
        [TestCase("application")]
        [TestCase("root")]
        [TestCase("root-type")]
        [TestCase("target-type")]
        [TestCase("target")]
        [TestCase("ownership")]
        [TestCase("xid")]
        [TestCase("node-class")]
        [TestCase("role")]
        [TestCase("document")]
        [TestCase("max-versions")]
        [TestCase("version")]
        [TestCase("locator")]
        public void EvidencePinsAreIndependentlyChecked(string pin)
        {
            RegistryEntityReferenceDataType source = Source();
            FederationTrustBinding binding = Binding(source);
            RegistryEntityReferenceDataType observedOrigin = Source();
            if (pin == "origin")
            {
                observedOrigin.OriginUri = "urn:test:impostor";
            }
            if (pin == "locator")
            {
                observedOrigin.Locator = "opc.tcp://untrusted:4840";
                source.Locator = observedOrigin.Locator;
            }
            if (pin == "version")
            {
                source.Xid += "/versions/2";
                source.Role = "MetadataVersion";
            }
            FederationMetadataObservation evidence = Evidence(observedOrigin,
                application: pin == "application" ? "urn:test:impostor" : Application,
                root: RootEvidence(pin == "root" ? Target : Root,
                    pin == "root-type" ? [XRegistry.ObjectTypeIds.RegistryType] : default),
                target: TargetEvidence(pin == "target" ? new ExpandedNodeId("wrong", 0, Namespaces.EndpointRegistry) : Target,
                    pin == "target-type" ? [Ua.ObjectTypeIds.FileType] : default,
                    pin == "node-class" ? NodeClass.Variable : NodeClass.Object,
                    pin == "role" ? "LogicalResource" : "MetadataResource"),
                owner: pin == "ownership" ? Target : Root,
                logicalXid: pin == "xid" ? "/messagegroups/other/messages/m" : Xid,
                hasDocument: pin == "document", maxVersions: pin == "max-versions" ? 2u : 1u);

            Assert.Throws<ArgumentException>(() => new FederationMetadataSelector().Select(source, binding, evidence));
            Assert.Throws<ArgumentException>(() => new FederationResolutionCache().WithObservation(binding, evidence, Observation(source)));
        }

        [Test]
        public async Task CacheIsImmutableAndSourcesNeverCollide()
        {
            RegistryEntityReferenceDataType first = Source();
            RegistryEntityReferenceDataType second = Source("urn:test:two");
            var empty = new FederationResolutionCache();
            EndpointRegistryMessageObservation supplied = Observation(first);
            FederationResolutionCache one = empty.WithObservation(Binding(first), Evidence(first), supplied);
            FederationResolutionCache two = one.WithObservation(Binding(second), Evidence(second), Observation(second));
            supplied.Source.OriginUri = "urn:test:mutated";
            supplied.Metadata.Members[0].Value = new RegistryStringValueDataType { Kind = 2, Value = "wrong" };
            EndpointRegistryMessageObservation read = (await two.ReadMessageAsync(first = Source(), default)
                .ConfigureAwait(false))!;
            read.Source.OriginUri = "urn:test:reader-mutation";
            read.Metadata.Members = [];
            EndpointRegistryMessageObservation again = (await two.ReadMessageAsync(first, default).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(empty.Count, Is.Zero);
                Assert.That(one.Count, Is.EqualTo(1));
                Assert.That(two.Count, Is.EqualTo(2));
                Assert.That(new FederationSourceKey(first), Is.Not.EqualTo(new FederationSourceKey(second)));
                Assert.That(again.Source.OriginUri, Is.EqualTo("urn:test:one"));
                Assert.That(RegistryValues.Identical(again.Metadata, Json("""{"messageid":"m","x-n":1.00,"x-null":null}""")), Is.True);
            });
        }

        [Test]
        public async Task AuthorizedRelocationRetainsAllPins()
        {
            RegistryEntityReferenceDataType source = Source();
            FederationResolutionCache old = new FederationResolutionCache().WithObservation(
                Binding(source), Evidence(source), Observation(source));
            RegistryEntityReferenceDataType moved = Source(locator: "opc.tcp://localhost:4841");
            FederationResolutionCache current = old.WithObservation(Binding(moved), Evidence(moved), Observation(moved));
            EndpointRegistryMessageObservation after = (await current.ReadMessageAsync(moved, default).ConfigureAwait(false))!;
            Assert.That(new FederationSourceKey(after.Source), Is.EqualTo(new FederationSourceKey(source)));
            Assert.That(after.Source.NativeTarget, Is.EqualTo(source.NativeTarget));
            Assert.That(after.Source.Locator, Is.EqualTo(moved.Locator));
            moved.NativeTarget = new ExpandedNodeId("new-target", 0, Namespaces.EndpointRegistry);
            Assert.Throws<ArgumentException>(() => old.WithObservation(Binding(moved),
                Evidence(moved, target: TargetEvidence(moved.NativeTarget)), Observation(moved)));
        }

        [Test]
        public async Task SoleVersionAndPortableTableReorders()
        {
            RegistryEntityReferenceDataType source = Source(xid: Xid + "/versions/1");
            FederationResolutionCache cache = new FederationResolutionCache().WithObservation(
                Binding(source), Evidence(source), Observation(source));
            Assert.That((await cache.ReadMessageAsync(source, default).ConfigureAwait(false))!.Source.Role,
                Is.EqualTo("MetadataVersion"));
            Assert.That(new FederationSourceKey(source), Is.Not.EqualTo(new FederationSourceKey(Source())));
            var namespaces = new NamespaceTable();
            namespaces.Append(Namespaces.EndpointRegistry);
            var servers = new StringTable();
            servers.Append("urn:test:local");
            servers.Append(Application);
            source.NativeTarget = new ExpandedNodeId(Target.InnerNodeId.WithNamespaceIndex(1), null, 1);
            RegistryEntityReferenceDataType first = FederationPortableIdentity.ResolveReference(source, namespaces, servers, Binding(source));
            var reorderedNamespaces = new NamespaceTable();
            reorderedNamespaces.Append("urn:test:unused");
            reorderedNamespaces.Append(Namespaces.EndpointRegistry);
            var reorderedServers = new StringTable();
            reorderedServers.Append(Application);
            reorderedServers.Append("urn:test:local");
            source.NativeTarget = new ExpandedNodeId(Target.InnerNodeId.WithNamespaceIndex(2), null, 0);
            RegistryEntityReferenceDataType reordered = FederationPortableIdentity.ResolveReference(source,
                reorderedNamespaces, reorderedServers, Binding(source));
            Assert.That(reordered.NativeTarget, Is.EqualTo(first.NativeTarget));
            Assert.That(new FederationSourceKey(reordered), Is.EqualTo(new FederationSourceKey(first)));
        }

        [TestCase("""{"messageid":"wrong"}""", "E_REFERENCE_IDENTITY")]
        [TestCase("""{"versionid":"2"}""", "E_REFERENCE_IDENTITY")]
        [TestCase("""{"versions":{"2":{"versionid":"2"}}}""", "E_REFERENCE_IDENTITY")]
        [TestCase("""{"epoch":4}""", "E_SNAPSHOT")]
        public void RawIdentityAndEpochMustAgreeWithIndependentEvidence(string raw, string code)
        {
            RegistryEntityReferenceDataType source = Source();
            RegistryRuleException error = Assert.Throws<RegistryRuleException>(() => new FederationResolutionCache()
                .WithObservation(Binding(source), Evidence(source), Observation(source, raw)))!;
            Assert.That(error.Code, Is.EqualTo(code));
        }

        [Test]
        public void EmptyCacheHonorsCancellationAndInvalidOriginFailsClosed()
        {
            var canceled = new CancellationToken(true);
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await new FederationResolutionCache().ReadMessageAsync(Source(), canceled).ConfigureAwait(false));
            var mixed = Source();
            mixed.ApplicationUri = Application;
            Assert.Throws<ArgumentException>(() => Binding(mixed));
            var paired = Source();
            paired.OriginUri = string.Empty;
            paired.ApplicationUri = Application;
            paired.RegistryNode = Root;
            Assert.That(new FederationSourceKey(paired).Origin.RegistryNode, Is.EqualTo(Root));
            Assert.Throws<ArgumentException>(() => Binding(paired, root: Target));
        }
    }
}
