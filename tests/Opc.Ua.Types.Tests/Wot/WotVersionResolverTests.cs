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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Executes the canonical xRegistry/OPC 10100 version resolution contract.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotVersionResolverTests
    {
        public static IEnumerable<string> Vectors => Directory.GetFiles(Folder, "*.json")
            .Where(path => char.IsDigit(Path.GetFileName(path)[0])).OrderBy(path => path, StringComparer.Ordinal);

        private static string Folder => Path.Combine(TestContext.CurrentContext.TestDirectory, "Wot", "Resolution");

        [Test]
        public void CanonicalManifestMatchesEveryFile()
        {
            string[] lines = File.ReadAllLines(Path.Combine(Folder, "MANIFEST.sha256"));
            string[] names = lines.Select(line => line[66..]).ToArray();
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName)
                .Where(name => name != "MANIFEST.sha256"), Is.EquivalentTo(names));
            Assert.That(names, Is.Ordered.Using<string>(StringComparer.Ordinal));
            foreach (string line in lines)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(Folder, line[66..]));
                Assert.Multiple(() =>
                {
                    Assert.That(WotSpecFixtureManifestTests.Sha256Hex(bytes), Is.EqualTo(line[..64]), line[66..]);
                    Assert.That(bytes, Does.Not.Contain((byte)'\r'), line[66..]);
                    Assert.That(bytes[^1], Is.EqualTo((byte)'\n'), line[66..]);
                });
            }
            Assert.That(Vectors.Count(), Is.EqualTo(64));
        }

        [TestCaseSource(nameof(Vectors))]
        public void CanonicalVectorMatches(string path)
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = json.RootElement;
            ArrayOf<WotResourceSnapshot> resources = root.GetProperty("registry").EnumerateArray().Select(resource =>
                new WotResourceSnapshot(Text(resource, "wotid")!, resource.GetProperty("defaultversionsticky").GetBoolean(),
                    Text(resource, "defaultversionid"), resource.GetProperty("versions").EnumerateArray().Select(version =>
                        new WotVersionSnapshot(Text(version, "versionid")!, Text(version, "versionorigin")!,
                            Text(version, "contentdigest")!, Text(version, "version.model"),
                            Text(version, "version.instance"))).ToArrayOf())).ToArrayOf();
            JsonElement expected = root.GetProperty("expect");
            try
            {
                if (root.TryGetProperty("publish", out JsonElement publish))
                {
                    WotPublicationAssignment result = WotVersionResolver.AssignPublishedVersion(resources,
                        Text(publish, "target")!,
                        Text(publish, "versionid"),
                        Text(publish, "version.model"),
                        Text(publish, "targetKind") ?? "tm",
                        Text(publish, "version.instance"));
                    Assert.That(expected.TryGetProperty("assigned", out JsonElement assigned), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.VersionId, Is.EqualTo(Text(assigned, "versionid")));
                        Assert.That(result.VersionOrigin, Is.EqualTo(Text(assigned, "versionorigin")));
                        Assert.That(result.DefaultVersionId, Is.EqualTo(Text(assigned, "defaultversionid")));
                    });
                }
                else
                {
                    JsonElement reference = root.GetProperty("reference");
                    WotDocumentVersionContext? context = root.TryGetProperty("document", out JsonElement document)
                        ? new WotDocumentVersionContext(Text(document, "version.model"),
                            document.GetProperty("typeLinks").EnumerateArray().Select(link =>
                                new WotTypeLink(Text(link, "target")!, Text(link, "minVersion"))).ToArrayOf()) : null;
                    WotVersionPin? pin = root.TryGetProperty("pin", out JsonElement pinJson)
                        ? new WotVersionPin(Text(pinJson, "digest"), Text(pinJson, "versionid")) : null;
                    WotResolutionResult result = WotVersionResolver.Resolve(resources,
                        new WotReferenceRequest(Text(reference, "kind")!, Text(reference, "target")!,
                            Text(reference, "minVersion"), Text(reference, "floorTerm")),
                        "https://registry.example", context, pin,
                        root.TryGetProperty("allowPrerelease", out JsonElement allow) && allow.GetBoolean());
                    Assert.That(expected.TryGetProperty("result", out JsonElement selected), Is.True);
                    Assert.Multiple(() =>
                    {
                        Assert.That(result.WotId, Is.EqualTo(Text(selected, "wotid")));
                        Assert.That(result.VersionId, Is.EqualTo(Text(selected, "versionid")));
                        Assert.That(result.ContentDigest, Is.EqualTo(Text(selected, "contentdigest")));
                        Assert.That(result.Origin, Is.EqualTo("https://registry.example"));
                    });
                }
            }
            catch (WotVersionException exception)
            {
                Assert.That(Text(expected, "error"), Is.EqualTo(exception.Code));
            }
        }

        [TestCase("1.0.0-alpha", "1.0.0-alpha.1")]
        [TestCase("1.0.0-alpha.1", "1.0.0-alpha.beta")]
        [TestCase("1.0.0-alpha.beta", "1.0.0-beta")]
        [TestCase("1.0.0-beta", "1.0.0-beta.2")]
        [TestCase("1.0.0-beta.2", "1.0.0-beta.11")]
        [TestCase("1.0.0-beta.11", "1.0.0-rc.1")]
        [TestCase("1.0.0-rc.1", "1.0.0")]
        [TestCase("1.0.0-Z", "1.0.0-a")]
        [TestCase("99999999999999999999.0.0", "100000000000000000000.0.0")]
        [TestCase("1.0.0-99999999999999999999", "1.0.0-100000000000000000000")]
        public void SemanticVersionUsesSemVerPrecedence(string lower, string upper)
        {
            var left = WotSemanticVersion.Parse(lower);
            var right = WotSemanticVersion.Parse(upper);
            Assert.Multiple(() =>
            {
                Assert.That(left.CompareTo(right), Is.LessThan(0));
                Assert.That(right.CompareTo(left), Is.GreaterThan(0));
                Assert.That(left.CompareTo(WotSemanticVersion.Parse(lower)), Is.Zero);
            });
        }

        [TestCase("")]
        [TestCase("01.0.0")]
        [TestCase("1.00.0")]
        [TestCase("1.0.00")]
        [TestCase("1.0.0-01")]
        [TestCase("1.0.0-")]
        [TestCase("1.0.0-a..b")]
        [TestCase("1.0.0+a")]
        [TestCase("v1.0.0")]
        [TestCase("1.0.0\n")]
        [TestCase("1.0.0-�")]
        public void InvalidVersionIdentifiersAreRejected(string text)
        {
            Assert.That(WotSemanticVersion.TryParse(text, out _), Is.False);
        }

        [Test]
        public void SemVerIdentifierLengthBoundaryAndEqualityAreStable()
        {
            string limit = new string('1', 124) + ".0.0";
            Assert.That(WotSemanticVersion.TryParse(limit, out WotSemanticVersion? parsed), Is.True);
            Assert.That(WotSemanticVersion.TryParse("1" + limit, out _), Is.False);
            Assert.That(WotSemanticVersion.TryParse(null, out _), Is.False);
            WotSemanticVersion copy = WotSemanticVersion.Parse(limit);
            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.EqualTo(copy));
                Assert.That(parsed!.GetHashCode(), Is.EqualTo(copy.GetHashCode()));
            });
        }

        [TestCase("tm:extends")]
        [TestCase("tm:submodel")]
        [TestCase("ref")]
        public void AuthoredFloorIsExtractedFromCarryingNode(string kind)
        {
            using WotDocument document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"tm:minVersion\":\"1.2.0\",\"tm:ref\":\"urn:parent\"}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.RootElement, kind),
                Is.EqualTo("1.2.0"));
        }

        [TestCase("1.2.0", null, "1.2.0")]
        [TestCase("1.2.0", "1.3.0", "1.3.0")]
        [TestCase("1.3.0", "1.2.0", "1.3.0")]
        [TestCase(null, "1.3.0", "1.3.0")]
        public void TypeLinkFloorUsesTdContext(string? model, string? explicitFloor, string expected)
        {
            string version = model is null ? string.Empty : ",\"version\":{\"model\":\"" + model + "\"}";
            string minimum = explicitFloor is null ? string.Empty : ",\"tm:minVersion\":\"" + explicitFloor + "\"";
            using WotDocument document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"links\":[{\"rel\":\"type\",\"href\":\"urn:parent\"" +
                minimum + "}]" + version + "}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"),
                Is.EqualTo(expected));
        }

        [TestCase("1.3.0", "1.2.0", "1.3.0")]
        [TestCase("1.2.0", "1.3.0", "1.3.0")]
        [TestCase(null, "1.2.0", "1.2.0")]
        [TestCase(null, null, null)]
        public void MultipleTypeLinkFloorUsesOnlyApplicableConstraints(
            string? model, string? explicitFloor, string? expected)
        {
            string version = model is null ? string.Empty : ",\"version\":{\"model\":\"" + model + "\"}";
            string minimum = explicitFloor is null ? string.Empty : ",\"tm:minVersion\":\"" + explicitFloor + "\"";
            using WotDocument document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"links\":[{\"rel\":\"type\",\"href\":\"urn:a\"" + minimum + "}," +
                "{\"rel\":\"type\",\"href\":\"urn:b\",\"tm:minVersion\":\"9.0.0\"}]" + version + "}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"),
                Is.EqualTo(expected));
        }

        [TestCase("https://registry.example/groups/tms/resources/pump/versions/1.1.0")]
        [TestCase("/groups/tms/resources/pump/versions/1.1.0#/properties/p")]
        public void AVersionPinBypassesFloorDerivation(string href)
        {
            using WotDocument document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"version\":{\"model\":\"invalid\"}," +
                "\"links\":[{\"rel\":\"type\",\"href\":\"" + href + "\"}," +
                "{\"rel\":\"type\",\"href\":\"urn:b\"}]}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"), Is.Null);
            Assert.Throws<WotVersionException>(() =>
                WotVersionedReference.GetRequiredVersion(document, document.Links[1], "type"));
        }

        [Test]
        public void MultipleTypeLinksRejectUnattributableDocumentFloor()
        {
            using WotDocument document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"version\":{\"model\":\"1.2.0\"}," +
                "\"links\":[{\"rel\":\"type\",\"href\":\"urn:a\",\"tm:minVersion\":\"1.2.0\"}," +
                "{\"rel\":\"type\",\"href\":\"urn:b\"}]}"));
            WotVersionException? error = Assert.Throws<WotVersionException>(() =>
                WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"));
            Assert.That(error!.Code, Is.EqualTo("ambiguous-floor"));
        }

        [TestCase("\"0.0.0-1\"")]
        [TestCase("\"1.0.0+build\"")]
        [TestCase("1")]
        public void InvalidAuthoredFloorIsRejected(string value)
        {
            using WotDocument document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"tm:minVersion\":" + value + "}"));
            WotVersionException? error = Assert.Throws<WotVersionException>(() =>
                WotVersionedReference.GetRequiredVersion(document, document.RootElement, "ref"));
            Assert.That(error!.Code, Is.EqualTo("invalid-version"));
        }

        [Test]
        public async Task LegacyProviderCannotReturnBytesBelowTheFloorAsync()
        {
            WotVersionException? error = await Assert.ThrowsAsync<WotVersionException>(async () =>
                await WotVersionedReference.ResolveAsync(new LegacyProvider(),
                    new WotReferenceRequest("ref", "urn:parent", "1.1.0"),
                    new WotResolutionContext(), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(error!.Code, Is.EqualTo("unsatisfied"));
            WotResolverResult result = await WotVersionedReference.ResolveAsync(new LegacyProvider(),
                new WotReferenceRequest("ref", "urn:parent", "1.0.0"),
                new WotResolutionContext(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Found, Is.True);
        }

        [Test]
        public async Task VersionedProviderReceivesTheCompleteReferenceAsync()
        {
            var provider = new VersionedProvider();
            var reference = new WotReferenceRequest("extends", "urn:parent", "1.2.0");
            await WotVersionedReference.ResolveAsync(provider, reference,
                new WotResolutionContext(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(provider.Reference, Is.EqualTo(reference));
        }

        private sealed class LegacyProvider : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(string reference,
                WotResolutionContext context, CancellationToken cancellationToken)
            {
                return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(
                    System.Text.Encoding.UTF8.GetBytes("{\"version\":{\"model\":\"1.0.0\"}}")));
            }
        }

        private sealed class VersionedProvider : IWotVersionedThingResolver
        {
            public WotReferenceRequest? Reference { get; private set; }

            public ValueTask<WotResolverResult> ResolveThingAsync(WotReferenceRequest reference,
                WotResolutionContext context, CancellationToken cancellationToken)
            {
                Reference = reference;
                return new ValueTask<WotResolverResult>(WotResolverResult.NotFound);
            }

            public ValueTask<WotResolverResult> ResolveThingAsync(string reference,
                WotResolutionContext context, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("The version-aware overload must be used.");
            }
        }

        private static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
    }
}
