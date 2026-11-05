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
    /// Executes current shared resolution cases and the retained implementation regression corpus.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    public sealed class WotVersionResolverTests
    {
        public static IEnumerable<string> Vectors => Directory.GetFiles(Folder, "*.json")
            .Where(path => char.IsDigit(Path.GetFileName(path)[0])).OrderBy(path => path, StringComparer.Ordinal);

        private static string Folder => Path.Combine(TestContext.CurrentContext.TestDirectory, "Wot", "Resolution");

        /// <summary>
        /// Gets the independently authored cases from the current shared source.
        /// </summary>
        public static IEnumerable<TestCaseData> SharedVectors
        {
            get
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
                    TestContext.CurrentContext.TestDirectory, "Wot", "SharedResolution", "vectors.json")));
                foreach (JsonProperty vector in document.RootElement.EnumerateObject())
                {
                    yield return new TestCaseData(vector.Value.GetRawText())
                        .SetName("SharedVectorMatches(" + vector.Name + ")");
                }
            }
        }

        [Test]
        public void LegacyRegressionManifestMatchesEveryFile()
        {
            string[] lines = File.ReadAllLines(Path.Combine(Folder, "MANIFEST.sha256"));
            string[] names = [.. lines.Select(line => line[66..])];
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
        public void LegacyRegressionVectorMatches(string path)
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            AssertVector(json.RootElement);
        }

        /// <summary>
        /// Executes a current shared case without deriving its expected result from the resolver.
        /// </summary>
        [TestCaseSource(nameof(SharedVectors))]
        public void SharedVectorMatches(string value)
        {
            using var json = JsonDocument.Parse(value);
            AssertVector(json.RootElement);
        }

        /// <summary>
        /// Checks one normalized case against its literal expected assignment, selection or domain error.
        /// </summary>
        private static void AssertVector(JsonElement root)
        {
            var resources = root.GetProperty("registry").EnumerateArray().Select(resource =>
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
                            document.TryGetProperty("typeLinks", out JsonElement typeLinks)
                                ? typeLinks.EnumerateArray().Select(link =>
                                    new WotTypeLink(Text(link, "target")!, Text(link, "minVersion"))).ToArrayOf() : [])
                        {
                            VersionId = Text(document, "versionid"),
                            InstanceVersion = Text(document, "version.instance")
                        } : null;
                    WotVersionPin? pin = root.TryGetProperty("pin", out JsonElement pinJson)
                        ? new WotVersionPin(Text(pinJson, "digest"), Text(pinJson, "versionid")) : null;
                    WotResolutionResult result = WotVersionResolver.Resolve(resources,
                        new WotReferenceRequest(Text(reference, "kind")!, Text(reference, "target")!,
                            Text(reference, "minVersion"), Text(reference, "floorTerm"))
                        {
                            Local = reference.TryGetProperty("local", out JsonElement local) && local.GetBoolean()
                        },
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

        /// <summary>
        /// Verifies local floors against the authored containing revision, never the latest stored version.
        /// </summary>
        [TestCase("ref", "1.2.0", "1.2.0", null)]
        [TestCase("ref", "1.2.0", "1.3.0", "unsatisfied")]
        [TestCase("ref", null, "1.1.0", "unsatisfied")]
        [TestCase("ref", "v1.2", "1.1.0", "invalid-version")]
        [TestCase("ref", "1.2.0+build", "1.1.0", "invalid-version")]
        [TestCase("ref", "invalid", null, null)]
        [TestCase("td-ref", "2.0.0", "1.1.0", "unsatisfied")]
        [TestCase("td-ref", "2.0.0-beta", "1.1.0", "unsatisfied")]
        [TestCase("td-ref", "0.2.0", "0.1.0", null)]
        [TestCase("td-ref", "1.0.0-beta", "0.1.0", "unsatisfied")]
        [TestCase("td-ref", "1.2.0", "1.1.0", null)]
        public void LocalReferenceChecksContainingAuthoredVersion(
            string kind, string? authored, string? floor, string? error)
        {
            ArrayOf<WotResourceSnapshot> resources =
            [
                new("urn:local", false, "9.0.0",
                    [new("1.2.0", "authored", "old"), new("9.0.0", "authored", "new")])
            ];
            var reference = new WotReferenceRequest(kind, "urn:local", floor) { Local = true };
            var document = new WotDocumentVersionContext(kind == "ref" ? authored : "99.0.0", [])
            {
                VersionId = "1.2.0",
                InstanceVersion = kind == "td-ref" ? authored : "99.0.0"
            };
            if (error is not null)
            {
                WotVersionException? exception = Assert.Throws<WotVersionException>(() =>
                    WotVersionResolver.Resolve(resources, reference, "urn:origin", document));
                Assert.That(exception!.Code, Is.EqualTo(error));
                return;
            }
            WotResolutionResult result = WotVersionResolver.Resolve(resources, reference, "urn:origin", document);
            Assert.That(result, Is.EqualTo(new WotResolutionResult("urn:local", "1.2.0", "old", "urn:origin")));
        }

        /// <summary>
        /// Verifies that a local reference cannot fall back to live selection without revision context.
        /// </summary>
        [Test]
        public void LocalReferenceRequiresContainingRevision()
        {
            Assert.Throws<ArgumentException>(() => WotVersionResolver.Resolve(
                [new("urn:local", false, "1.0.0", [new("1.0.0", "authored", "digest")])],
                new WotReferenceRequest("ref", "urn:local") { Local = true }, "urn:origin"));
            WotVersionException? missing = Assert.Throws<WotVersionException>(() => WotVersionResolver.Resolve(
                [new("urn:local", false, "1.0.0", [new("1.0.0", "authored", "digest")])],
                new WotReferenceRequest("ref", "urn:local") { Local = true }, "urn:origin",
                new WotDocumentVersionContext("1.0.0", []) { VersionId = "missing" }));
            Assert.That(missing!.Code, Is.EqualTo("not-found"));
        }

        /// <summary>
        /// Verifies the common floor term in the normalized resolver contract for both target kinds.
        /// </summary>
        [TestCase("ref")]
        [TestCase("td-ref")]
        public void NormalizedCommonFloorTermUsesSemVerSelection(string kind)
        {
            WotResolutionResult result = WotVersionResolver.Resolve(
                [new("urn:parent", false, "1.2.0", [new("1.2.0", "authored", "digest")])],
                new WotReferenceRequest(kind, "urn:parent", "1.1.0", "uav:minVersion"), "urn:origin");
            Assert.That(result.VersionId, Is.EqualTo("1.2.0"));
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
            var copy = WotSemanticVersion.Parse(limit);
            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.EqualTo(copy));
                Assert.That(parsed!.GetHashCode(), Is.EqualTo(copy.GetHashCode()));
            });
        }

        /// <summary>
        /// Verifies that a valid authored floor is not restricted to the stored VersionId length.
        /// </summary>
        [Test]
        public void LongSemVerFloorRemainsAValidRequirement()
        {
            string floor = "1.0.0-" + new string('a', 130);
            Assert.That(WotSemanticVersion.TryParse(floor, out _), Is.False);
            WotResolutionResult result = WotVersionResolver.Resolve(
                [new("urn:parent", false, "1.2.0", [new("1.2.0", "authored", "digest")])],
                new WotReferenceRequest("ref", "urn:parent", floor), "urn:origin");
            Assert.That(result.VersionId, Is.EqualTo("1.2.0"));
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"version\":{\"model\":\"" +
                floor +
                "\"}," +
                "\"links\":[{\"rel\":\"type\",\"href\":\"urn:parent\",\"uav:minVersion\":\"" +
                floor +
                "\"}]}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"),
                Is.EqualTo(floor));
        }

        [TestCase("tm:extends")]
        [TestCase("tm:submodel")]
        [TestCase("ref")]
        public void AuthoredFloorIsExtractedFromCarryingNode(string kind)
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                                     /*lang=json,strict*/
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
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"links\":[{\"rel\":\"type\",\"href\":\"urn:parent\"" +
                minimum +
                "}]" +
                version +
                "}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"),
                Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies the common SemVer floor vocabulary in its original carrying context.
        /// </summary>
        [TestCase("uav:minVersion")]
        [TestCase("http://opcfoundation.org/UA/WoT-Binding/minVersion")]
        [TestCase("floor")]
        public void CommonFloorUsesCarryingContext(string term)
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@context\":{\"floor\":\"http://opcfoundation.org/UA/WoT-Binding/minVersion\"}," +
                "\"" +
                term +
                "\":\"1.2.0\"}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.RootElement, "td-ref"),
                Is.EqualTo("1.2.0"));
        }

        /// <summary>
        /// Verifies that ambiguous or unsupported requirement annotations cannot become unconstrained selection.
        /// </summary>
        [TestCase("\"uav:minVersion\":null", "invalid-version")]
        [TestCase("\"uav:minVersion\":\"1.0.0\",\"tm:minVersion\":\"1.0.0\"", "invalid-floor")]
        [TestCase("\"uav:compatibilityLine\":\"urn:line\"", "invalid-line")]
        public void InvalidCommonRequirementIsRejected(string annotation, string code)
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{" + annotation + "}"));
            WotVersionException? error = Assert.Throws<WotVersionException>(() =>
                WotVersionedReference.GetRequiredVersion(document, document.RootElement, "ref"));
            Assert.That(error!.Code, Is.EqualTo(code));
        }

        /// <summary>
        /// Verifies that common floors participate in every type link's ambiguity and implicit-floor checks.
        /// </summary>
        [Test]
        public void CommonTypeFloorsCombineWithTheDocumentFloor()
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"version\":{\"model\":\"1.3.0\"}," +
                "\"links\":[{\"rel\":\"type\",\"href\":\"urn:a\",\"uav:minVersion\":\"1.2.0\"}," +
                "{\"rel\":\"type\",\"href\":\"urn:b\",\"uav:minVersion\":\"1.4.0\"}]}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"),
                Is.EqualTo("1.3.0"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[1], "type"),
                Is.EqualTo("1.4.0"));
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
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"links\":[{\"rel\":\"type\",\"href\":\"urn:a\"" +
                minimum +
                "}," +
                "{\"rel\":\"type\",\"href\":\"urn:b\",\"tm:minVersion\":\"9.0.0\"}]" +
                version +
                "}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"),
                Is.EqualTo(expected));
        }

        [TestCase("https://registry.example/groups/tms/resources/pump/versions/1.1.0")]
        [TestCase("/groups/tms/resources/pump/versions/1.1.0#/properties/p")]
        public void AVersionPinBypassesFloorDerivation(string href)
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"@type\":\"Thing\",\"version\":{\"model\":\"invalid\"}," +
                "\"links\":[{\"rel\":\"type\",\"href\":\"" +
                href +
                "\"}," +
                "{\"rel\":\"type\",\"href\":\"urn:b\"}]}"));
            Assert.That(WotVersionedReference.GetRequiredVersion(document, document.Links[0], "type"), Is.Null);
            Assert.Throws<WotVersionException>(() =>
                WotVersionedReference.GetRequiredVersion(document, document.Links[1], "type"));
        }

        /// <summary>
        /// Verifies authored requirement validity independently of raw exact replay selection.
        /// </summary>
        [TestCase("\"tm:minVersion\":null", "invalid-version")]
        [TestCase("\"td:minVersion\":\"1.0.0\"", "invalid-floor")]
        [TestCase("\"uav:minVersion\":\"1.0.0\",\"tm:minVersion\":\"1.0.0\"", "invalid-floor")]
        [TestCase("\"uav:compatibilityLine\":\"urn:line\"", "invalid-line")]
        public void ExactAuthoredReferenceStillRequiresValidRequirementShape(string annotation, string code)
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                "{\"href\":\"urn:registry/resources/pump/versions/1.0.0\"," + annotation + "}"));
            WotVersionException? error = Assert.Throws<WotVersionException>(() =>
                WotVersionedReference.GetRequiredVersion(document, document.RootElement, "ref"));
            Assert.That(error!.Code, Is.EqualTo(code));
            WotResolutionResult replay = WotVersionResolver.Resolve(
                [new("urn:pump", false, "1.0.0", [new("1.0.0", "authored", "digest")])],
                new WotReferenceRequest("ref", "urn:pump", "invalid"), "urn:origin",
                pin: new WotVersionPin(VersionId: "1.0.0"));
            Assert.That(replay.VersionId, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void MultipleTypeLinksRejectUnattributableDocumentFloor()
        {
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
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
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
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

        /// <summary>
        /// Verifies that local document acquisition preserves the exact bytes without calling a provider.
        /// </summary>
        [TestCase("#/properties")]
        [TestCase("urn:local#/properties")]
        public async Task LocalReferenceNeverAcquiresANewerDocumentAsync(string target)
        {
            var provider = new VersionedProvider();
            using var document = WotDocument.Parse(System.Text.Encoding.UTF8.GetBytes(
                                     /*lang=json,strict*/
                                     "{\"id\":\"urn:local\",\"version\":{\"model\":\"1.2.0\"},\"properties\":{}}"));
            WotResolverResult result = await WotVersionedReference.ResolveAsync(provider,
                new WotReferenceRequest("ref", target, "1.1.0"),
                new WotResolutionContext(), document, CancellationToken.None).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(result.Content.ToArray(), Is.EqualTo(document.Utf8Json.ToArray()));
                Assert.That(provider.Reference, Is.Null);
            });
            WotVersionException? error = await Assert.ThrowsAsync<WotVersionException>(async () =>
                await WotVersionedReference.ResolveAsync(provider,
                    new WotReferenceRequest("ref", target, "1.3.0"),
                    new WotResolutionContext(), document, CancellationToken.None).ConfigureAwait(false))
                .ConfigureAwait(false);
            Assert.That(error!.Code, Is.EqualTo("unsatisfied"));
            Assert.That(provider.Reference, Is.Null);
        }

        private sealed class LegacyProvider : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(string reference,
                WotResolutionContext context, CancellationToken cancellationToken)
            {
                return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(
                    System.Text.Encoding.UTF8.GetBytes(/*lang=json,strict*/ "{\"version\":{\"model\":\"1.0.0\"}}")));
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

        private static string? Text(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        }
    }
}
