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
 *
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
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;

#nullable enable

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Runs the shared cross-language golden vectors the WoT Binding
    /// specification publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every algorithm the vectors cover is implemented at least twice: once by
    /// the specification's own Python validators and once here. The two only
    /// agree if they are measured against the same values, which is what the
    /// vector file is for - and why this suite reads the published bytes rather
    /// than restating the cases. A disagreement then becomes a failing test on
    /// both sides at once instead of a discovery at interop.
    /// </para>
    /// <para>
    /// The file is vendored and its SHA-256 pinned. An upstream edit therefore
    /// arrives as a failure naming the old digest, not as a silent change of
    /// what this suite is proving.
    /// </para>
    /// <para>
    /// Not every group has a stack API to measure yet. Those are named in
    /// <see cref="s_unprovenGroups"/> with the reason, and the partition is
    /// checked against the file: a group that is in neither list - one the
    /// specification adds - fails. That is the point. An unproven group is a
    /// gap this suite reports; it is not a gap it hides.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("WoT")]
    [Category("WotSpecExamples")]
    [Parallelizable]
    public sealed class WotSpecVectorTests
    {
        /// <summary>
        /// The SHA-256 of the vendored vector file at the pinned specification
        /// commit.
        /// </summary>
        private const string VectorDigest =
            "67508781f2ba03c127f22528ed87bf604c8f897dd7cb65fdd50fc11452f2e4a2";

        [Test]
        public void TheVendoredVectorsAreThePublishedBytes()
        {
            byte[] bytes = ReadVectors();

            Assert.That(
                Sha256Hex(bytes),
                Is.EqualTo(VectorDigest),
                "The vectors are vendored byte-for-byte from the pinned specification " +
                "commit. Re-vendor and re-pin the digest together.");
        }

        private static string Sha256Hex(byte[] bytes)
        {
#if NET5_0_OR_GREATER
            byte[] hash = SHA256.HashData(bytes);
#else
            using var algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(bytes);
#endif
            var text = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                text.Append(value.ToString(
                    "x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        [Test]
        public void TheVectorFileDescribesTheImplementedRevision()
        {
            using JsonDocument document = JsonDocument.Parse(ReadVectors());
            JsonElement root = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(
                    root.GetProperty("revision").GetString(),
                    Is.EqualTo(WotBindingConformance.CurrentRevision));
            });
        }

        /// <summary>
        /// Every group the file publishes is either proved below or named as a
        /// gap. A group in neither list is one the specification added and
        /// nothing here has looked at.
        /// </summary>
        [Test]
        public void EveryPublishedGroupIsEitherProvedOrNamedAsAGap()
        {
            List<string> published = GroupNames();
            var accounted = new HashSet<string>(StringComparer.Ordinal);
            accounted.UnionWith(s_provenGroups);
            accounted.UnionWith(s_unprovenGroups.Select(g => g.Group));

            Assert.Multiple(() =>
            {
                Assert.That(
                    published.Except(accounted, StringComparer.Ordinal),
                    Is.Empty,
                    "The specification publishes a vector group this suite has not " +
                    "considered. Prove it, or name it as a gap with the reason.");
                Assert.That(
                    accounted.Except(published, StringComparer.Ordinal),
                    Is.Empty,
                    "This suite names a group the specification no longer publishes.");
                Assert.That(
                    s_provenGroups.Intersect(
                        s_unprovenGroups.Select(g => g.Group), StringComparer.Ordinal),
                    Is.Empty,
                    "A group cannot be both proved and a gap.");
            });
        }

        /// <summary>
        /// Annex G.3. Ordering is by ascending Unicode code point of the whole
        /// string - not by locale, not case-insensitively, and not by UTF-16
        /// code unit, which is where a naive ordinal sort disagrees above the
        /// Basic Multilingual Plane.
        /// </summary>
        [Test]
        public void CodePointOrderMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("codePointOrder"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    string[] input = Strings(test.GetProperty("input"));
                    string[] expected = Strings(test.GetProperty("sorted"));

                    var actual = new List<string>(input);
                    actual.Sort(WotCodePointComparer.Instance);

                    Assert.That(actual, Is.EqualTo(expected).AsCollection, id);
                }
            });
        }

        /// <summary>
        /// Annex G.4. The measured size of an opaque object is its received
        /// text with insignificant whitespace removed, encoded as UTF-8 - not a
        /// canonical re-serialization, which would measure a form the document
        /// does not contain.
        /// </summary>
        [Test]
        public void CompactMeasurementMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("compactReceivedForm"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    string received = test.GetProperty("received").GetString()!;
                    string compact = test.GetProperty("compact").GetString()!;

                    using JsonDocument parsed = JsonDocument.Parse(received);
                    Assert.That(
                        WotDocument.MeasureCompactUtf8(parsed.RootElement),
                        Is.EqualTo(Encoding.UTF8.GetByteCount(compact)),
                        id);
                }
            });
        }

        /// <summary>
        /// Section 6.1. The data member a select clause fills is the browse
        /// path with its namespace qualification dropped, with <c>Name</c>
        /// appended where the last element names a state Variable, and
        /// <c>ConditionId</c> for the empty path.
        /// </summary>
        [Test]
        public void MaterializedMembersMatchTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("materializedMembers"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    string browsePath = test.GetProperty("browsePath").GetString()!;
                    string[] expected = Strings(test.GetProperty("member"));

                    ArrayOf<string> elements =
                        WotEventSelectClauses.SplitBrowsePath(browsePath);
                    ArrayOf<string> member =
                        WotEventSelectClauses.BuildMemberPath(elements);

                    Assert.That(member.ToList(), Is.EqualTo(expected).AsCollection, id);
                }
            });
        }

        /// <summary>
        /// Section 5.1.5. A compact logical identifier expands through the
        /// referring node's active context before comparison, and a prefix the
        /// context does not bind is read as an absolute IRI - exactly as
        /// JSON-LD reads it.
        /// </summary>
        [Test]
        public void IdentifierExpansionMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("identifierExpansion"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    string value = test.GetProperty("value").GetString()!;
                    JsonElement expected = test.GetProperty("expanded");

                    using WotDocument document = ContextDocument(test.GetProperty("context"));
                    bool isIdentifier = WotEventSelectionResolver.TryExpandLogicalId(
                        document, value, out string expanded);

                    if (expected.ValueKind == JsonValueKind.Null)
                    {
                        Assert.That(
                            isIdentifier,
                            Is.False,
                            $"{id}: the value is not an identifier, so nothing expands it.");
                        continue;
                    }
                    Assert.That(isIdentifier, Is.True, id);
                    Assert.That(expanded, Is.EqualTo(expected.GetString()), id);
                }
            });
        }

        /// <summary>
        /// Section 6.1. An explicit clause replaces the baseline clause with
        /// the same materialized member path in place, and otherwise appends -
        /// so a refinement neither reorders what it did not name nor drops it.
        /// </summary>
        [Test]
        public void EventSelectionOverlayMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("eventSelection"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    List<WotResolvedEventSelectClause> baseline = Clauses(
                        test.GetProperty("baseline"),
                        WotEventSelectClauseSource.LinkedEventType);
                    List<WotResolvedEventSelectClause> explicitClauses = Clauses(
                        test.GetProperty("explicit"),
                        WotEventSelectClauseSource.Explicit);
                    string[] expected = Strings(test.GetProperty("final"));

                    ArrayOf<WotResolvedEventSelectClause> final =
                        WotEventSelectionResolver.Overlay(baseline, explicitClauses);

                    Assert.That(
                        final.ToList().Select(c => c.BrowsePath),
                        Is.EqualTo(expected).AsCollection,
                        id);
                }
            });
        }

        /// <summary>
        /// Annex G.1. The generated NodeId is a function of the target
        /// namespace and the Node's absolute browse path, with a base-namespace
        /// element written bare, every other element namespace-qualified, and a
        /// reserved character inside a name escaped so it cannot imitate a path
        /// separator.
        /// </summary>
        [Test]
        public void GeneratedNodeIdsMatchTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("generatedNodeIds"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    var elements = new List<WotBrowsePathElement>();
                    foreach (JsonElement element in
                        test.GetProperty("browsePathElements").EnumerateArray())
                    {
                        elements.Add(new WotBrowsePathElement(
                            element.GetProperty("namespaceUri").GetString(),
                            element.GetProperty("name").GetString()!));
                    }

                    Assert.That(
                        WotPortableIdentity.GenerateNodeId(
                            test.GetProperty("namespaceUri").GetString()!,
                            elements.ToArrayOf()),
                        Is.EqualTo(test.GetProperty("nodeId").GetString()),
                        id);
                }
            });
        }

        /// <summary>
        /// Section 5.1.1. A persisted NodeId-valued member is an ExpandedNodeId
        /// with no session-local namespace index and no ServerIndex prefix.
        /// </summary>
        [Test]
        public void PortableExpandedNodeIdsMatchTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("expandedNodeIds"))
                {
                    Assert.That(
                        WotPortableIdentity.IsPortableNodeId(
                            test.GetProperty("value").GetString()),
                        Is.EqualTo(test.GetProperty("portable").GetBoolean()),
                        test.GetProperty("id").GetString());
                }
            });
        }

        /// <summary>
        /// Section 5.1.3. A persisted QualifiedName is a compact prefixed name,
        /// a bare namespace-0 name, or the <c>nsu=</c> form; a numeric
        /// NamespaceIndex prefix is never persisted.
        /// </summary>
        [Test]
        public void PortableQualifiedNamesMatchTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("qualifiedNames"))
                {
                    Assert.That(
                        WotPortableIdentity.IsPortableQualifiedName(
                            test.GetProperty("value").GetString()),
                        Is.EqualTo(test.GetProperty("portable").GetBoolean()),
                        test.GetProperty("id").GetString());
                }
            });
        }

        /// <summary>
        /// Section 5.1.4. A relative browse path with no anchor has no starting
        /// Node, so it names a sequence of steps from nowhere.
        /// </summary>
        [Test]
        public void BrowsePathResolvabilityMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("browsePaths"))
                {
                    Assert.That(
                        WotPortableIdentity.IsResolvableBrowsePath(
                            test.GetProperty("path").GetString(),
                            test.GetProperty("anchored").GetBoolean()),
                        Is.EqualTo(test.GetProperty("resolvable").GetBoolean()),
                        test.GetProperty("id").GetString());
                }
            });
        }

        /// <summary>
        /// Annex G.3. The length prefix is what makes the sequence encoding
        /// injective: an item containing U+000A must not serialize as the two
        /// items it would otherwise imitate.
        /// </summary>
        [Test]
        public void SequenceDigestMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("sequenceDigest"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    ArrayOf<string> items = Strings(test.GetProperty("items")).ToArrayOf();

                    Assert.That(
                        Hex(WotPortableIdentity.EncodeSequence(items)),
                        Is.EqualTo(test.GetProperty("encodingUtf8Hex").GetString()),
                        id);
                    Assert.That(
                        Hex(WotPortableIdentity.SequenceDigest(items)),
                        Is.EqualTo(test.GetProperty("sha256").GetString()),
                        id);
                }
            });
        }

        /// <summary>
        /// Section 12.6. ViewVersion is a function of the resolved membership
        /// alone, taken as a set, so duplicates and input order cannot change
        /// it.
        /// </summary>
        [Test]
        public void ViewVersionMatchesTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("viewVersion"))
                {
                    Assert.That(
                        WotPortableIdentity.ComputeViewVersion(
                            Strings(test.GetProperty("members")).ToArrayOf()),
                        Is.EqualTo(test.GetProperty("viewVersion").GetUInt32()),
                        test.GetProperty("id").GetString());
                }
            });
        }

        /// <summary>
        /// Annex G.4. An opaque member is located by its exact JSON Pointer,
        /// not by the value it happens to parse to: two occurrences carrying
        /// equal values written differently measure differently, and pairing
        /// them by value would bound each against the other's text.
        /// </summary>
        [Test]
        public void OpaquePointersMatchTheVectors()
        {
            Assert.Multiple(() =>
            {
                foreach (JsonElement test in Cases("opaquePointers"))
                {
                    string id = test.GetProperty("id").GetString()!;
                    using JsonDocument parsed = JsonDocument.Parse(
                        test.GetProperty("document").GetString()!);
                    List<WotOpaqueMember> found =
                        WotBindingConformance.FindOpaqueMembers(parsed.RootElement).ToList();
                    List<JsonElement> expected =
                        [.. test.GetProperty("expected").EnumerateArray()];

                    Assert.That(found, Has.Count.EqualTo(expected.Count), id);
                    for (int ii = 0; ii < expected.Count; ii++)
                    {
                        Assert.That(
                            found[ii].Pointer,
                            Is.EqualTo(expected[ii].GetProperty("pointer").GetString()),
                            id);
                        Assert.That(
                            found[ii].Member,
                            Is.EqualTo(expected[ii].GetProperty("member").GetString()),
                            id);
                        Assert.That(
                            found[ii].CompactUtf8Length,
                            Is.EqualTo(Encoding.UTF8.GetByteCount(
                                expected[ii].GetProperty("compact").GetString()!)),
                            id);
                    }
                }
            });
        }

        private static string Hex(ByteString value)
        {
            var text = new StringBuilder(value.Length * 2);
            foreach (byte octet in value.Span)
            {
                text.Append(octet.ToString(
                    "x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static List<WotResolvedEventSelectClause> Clauses(
            JsonElement paths, WotEventSelectClauseSource source)
        {
            var clauses = new List<WotResolvedEventSelectClause>();
            foreach (string path in Strings(paths))
            {
                clauses.Add(new WotResolvedEventSelectClause(
                    WotEventSelectClauses.BaseEventTypeId, path, source, null));
            }
            return clauses;
        }

        /// <summary>
        /// Builds a document whose active context binds exactly the prefixes a
        /// case states, which is the context an identifier expands through.
        /// </summary>
        private static WotDocument ContextDocument(JsonElement context)
        {
            var bindings = new StringBuilder();
            foreach (JsonProperty binding in context.EnumerateObject())
            {
                if (bindings.Length > 0)
                {
                    bindings.Append(',');
                }
                bindings.Append(JsonSerializer.Serialize(binding.Name))
                    .Append(':')
                    .Append(JsonSerializer.Serialize(binding.Value.GetString()));
            }
            return WotDocument.Parse(Encoding.UTF8.GetBytes(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\",{" +
                bindings +
                "}],\"@type\":\"tm:ThingModel\",\"title\":\"Vectors\"}"));
        }

        /// <summary>
        /// The groups this suite measures against a stack API.
        /// </summary>
        private static readonly string[] s_provenGroups =
        [
            "browsePaths",
            "codePointOrder",
            "compactReceivedForm",
            "eventSelection",
            "expandedNodeIds",
            "generatedNodeIds",
            "identifierExpansion",
            "materializedMembers",
            "opaquePointers",
            "qualifiedNames",
            "sequenceDigest",
            "viewVersion"
        ];

        /// <summary>
        /// The groups no public or internal stack algorithm implements, and
        /// why. A group belongs here only when there is nothing to measure -
        /// not when the algorithm exists but is inconvenient to reach, because
        /// then the vector would be checking a second implementation rather
        /// than the one the stack uses.
        /// </summary>
        /// <remarks>
        /// The list is empty, and asserting an empty list is the point: every
        /// group the specification publishes is now run against the code a
        /// conversion actually calls.
        /// </remarks>
        private static readonly (string Group, string Reason)[] s_unprovenGroups = [];

        private static List<string> GroupNames()
        {
            using JsonDocument document = JsonDocument.Parse(ReadVectors());
            var names = new List<string>();
            foreach (JsonProperty group in
                document.RootElement.GetProperty("groups").EnumerateObject())
            {
                names.Add(group.Name);
            }
            return names;
        }

        /// <summary>
        /// Reads the cases of one group, failing rather than running nothing
        /// when the group is missing.
        /// </summary>
        private static List<JsonElement> Cases(string group)
        {
            using JsonDocument document = JsonDocument.Parse(ReadVectors());
            Assert.That(
                document.RootElement.GetProperty("groups")
                    .TryGetProperty(group, out JsonElement element),
                Is.True,
                $"The vector file publishes no group '{group}'.");

            var cases = new List<JsonElement>();
            foreach (JsonElement test in element.GetProperty("cases").EnumerateArray())
            {
                cases.Add(test.Clone());
            }
            Assert.That(cases, Is.Not.Empty, $"Group '{group}' publishes no case.");
            return cases;
        }

        private static string[] Strings(JsonElement array)
        {
            return [.. array.EnumerateArray().Select(e => e.GetString()!)];
        }

        private static byte[] ReadVectors()
        {
            Assembly assembly = typeof(WotSpecVectorTests).Assembly;
            string resource = assembly
                .GetManifestResourceNames()
                .Single(n => n.EndsWith("wot-binding-vectors.json", StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException("The vectors are not embedded.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
