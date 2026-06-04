/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    [TestFixture]
    [Category("Fluent")]
    public class BrowsePathResolverTests
    {
        [Test]
        public void ParseSegmentsSingleNameUsesDefaultNamespace()
        {
            List<QualifiedName> segments = BrowsePathResolver.ParseSegments("Boilers", 2);

            Assert.That(segments, Has.Count.EqualTo(1));
            Assert.That(segments[0].Name, Is.EqualTo("Boilers"));
            Assert.That(segments[0].NamespaceIndex, Is.EqualTo((ushort)2));
        }

        [Test]
        public void ParseSegmentsMultipleNamesAllUseDefaultNamespace()
        {
            List<QualifiedName> segments = BrowsePathResolver.ParseSegments(
                "Boilers/Boiler1/Pipe/Valve",
                3);

            Assert.That(segments, Has.Count.EqualTo(4));
            foreach (QualifiedName name in segments)
            {
                Assert.That(name.NamespaceIndex, Is.EqualTo((ushort)3));
            }
            Assert.That(segments[0].Name, Is.EqualTo("Boilers"));
            Assert.That(segments[3].Name, Is.EqualTo("Valve"));
        }

        [Test]
        public void ParseSegmentsNamespacePrefixIsPerSegment()
        {
            List<QualifiedName> segments = BrowsePathResolver.ParseSegments(
                "ns=5;Methods/Increment",
                2);

            Assert.That(segments, Has.Count.EqualTo(2));
            Assert.That(segments[0].Name, Is.EqualTo("Methods"));
            Assert.That(segments[0].NamespaceIndex, Is.EqualTo((ushort)5));
            Assert.That(segments[1].Name, Is.EqualTo("Increment"));
            Assert.That(segments[1].NamespaceIndex, Is.EqualTo((ushort)2));
        }

        [Test]
        public void ParseSegmentsTrimsLeadingAndTrailingSlash()
        {
            List<QualifiedName> segments = BrowsePathResolver.ParseSegments("/A/B/", 1);

            Assert.That(segments, Has.Count.EqualTo(2));
            Assert.That(segments[0].Name, Is.EqualTo("A"));
            Assert.That(segments[1].Name, Is.EqualTo("B"));
        }

        [Test]
        public void ParseSegmentsNamespacePrefixSupportsMixedCase()
        {
            List<QualifiedName> segments = BrowsePathResolver.ParseSegments(
                "NS=7;Foo",
                0);

            Assert.That(segments[0].NamespaceIndex, Is.EqualTo((ushort)7));
            Assert.That(segments[0].Name, Is.EqualTo("Foo"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("/")]
        [TestCase("//")]
        [TestCase("A//B")]
        public void ParseSegmentsRejectsEmptyOrEmptySegments(string input)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => BrowsePathResolver.ParseSegments(input, 0));

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameInvalid));
        }

        [TestCase("ns=;Foo")]
        [TestCase("ns=abc;Foo")]
        [TestCase("ns=99999999;Foo")]
        [TestCase("ns=5;")]
        [TestCase("ns=5Foo")]
        public void ParseSegmentsRejectsMalformedNamespacePrefix(string input)
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => BrowsePathResolver.ParseSegments(input, 0));

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameInvalid));
        }

        private static SystemContext CreateContext()
        {
            return new SystemContext(telemetry: null);
        }

        [Test]
        public void ResolveWalksRootResolverAndFindChild()
        {
            SystemContext systemContext = CreateContext();

            var child = new BaseObjectState(null)
            {
                NodeId = new NodeId("ChildNode", 2),
                BrowseName = new QualifiedName("Child", 2),
                DisplayName = new LocalizedText("Child")
            };
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("RootNode", 2),
                BrowseName = new QualifiedName("Root", 2),
                DisplayName = new LocalizedText("Root")
            };
            root.AddChild(child);

            NodeState resolved = BrowsePathResolver.Resolve(
                systemContext,
                "Root/Child",
                defaultNamespaceIndex: 2,
                rootResolver: bn => bn == root.BrowseName ? root : null);

            Assert.That(resolved, Is.SameAs(child));
        }

        [Test]
        public void ResolveThrowsWhenRootMissing()
        {
            SystemContext systemContext = CreateContext();

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => BrowsePathResolver.Resolve(
                    systemContext,
                    "Missing",
                    0,
                    rootResolver: _ => null));

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void ResolveThrowsWhenChildMissing()
        {
            SystemContext systemContext = CreateContext();
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("R", 2),
                BrowseName = new QualifiedName("Root", 2),
                DisplayName = new LocalizedText("Root")
            };

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => BrowsePathResolver.Resolve(
                    systemContext,
                    "Root/Missing",
                    2,
                    rootResolver: _ => root));

            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        // ── Cross-namespace name-only fallback ─────────────────────

        [Test]
        public void ResolveFallsBackToLocalNameForCrossNamespaceChild()
        {
            // Mirrors the OPC 40223 PumpType / Machinery scenario:
            // the pump root lives in the Pumps namespace (ns=2), but
            // its inherited Operational child carries the Machinery
            // namespace (ns=3). Authors should be able to write paths
            // without sprinkling ns=N; prefixes on every cross-namespace
            // segment.
            SystemContext systemContext = CreateContext();
            var operational = new BaseObjectState(null)
            {
                NodeId = new NodeId("OpNode", 3),
                BrowseName = new QualifiedName("Operational", 3),
                DisplayName = new LocalizedText("Operational")
            };
            var pump = new BaseObjectState(null)
            {
                NodeId = new NodeId("Pump1", 2),
                BrowseName = new QualifiedName("Pump #1", 2),
                DisplayName = new LocalizedText("Pump #1")
            };
            pump.AddChild(operational);

            NodeState resolved = BrowsePathResolver.Resolve(
                systemContext,
                "Pump #1/Operational",
                defaultNamespaceIndex: 2,
                rootResolver: bn => bn == pump.BrowseName ? pump : null);

            Assert.That(resolved, Is.SameAs(operational));
        }

        [Test]
        public void ResolveAmbiguousLocalNameThrowsBadBrowseNameDuplicated()
        {
            SystemContext systemContext = CreateContext();
            var child2 = new BaseObjectState(null)
            {
                NodeId = new NodeId("C2", 2),
                BrowseName = new QualifiedName("Foo", 2),
                DisplayName = new LocalizedText("Foo")
            };
            var child3 = new BaseObjectState(null)
            {
                NodeId = new NodeId("C3", 3),
                BrowseName = new QualifiedName("Foo", 3),
                DisplayName = new LocalizedText("Foo")
            };
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("R", 2),
                BrowseName = new QualifiedName("Root", 2),
                DisplayName = new LocalizedText("Root")
            };
            root.AddChild(child2);
            root.AddChild(child3);

            // The default-namespace exact lookup matches child2 in ns=2
            // — that case resolves cleanly even with two same-name
            // children. Force the ambiguity by requesting an unknown
            // namespace via explicit prefix so the fallback kicks in.
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => BrowsePathResolver.Resolve(
                    systemContext,
                    "Root/ns=99;Foo",
                    defaultNamespaceIndex: 2,
                    rootResolver: bn => bn == root.BrowseName ? root : null));

            Assert.That(
                ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameDuplicated));
        }

        [Test]
        public void ResolveExactNamespaceMatchSkipsFallback()
        {
            // When two children share a local name in different
            // namespaces, an explicit ns= prefix picks the right one
            // without engaging the cross-namespace fallback (so no
            // BadBrowseNameDuplicated).
            SystemContext systemContext = CreateContext();
            var child2 = new BaseObjectState(null)
            {
                NodeId = new NodeId("C2", 2),
                BrowseName = new QualifiedName("Foo", 2),
                DisplayName = new LocalizedText("Foo")
            };
            var child3 = new BaseObjectState(null)
            {
                NodeId = new NodeId("C3", 3),
                BrowseName = new QualifiedName("Foo", 3),
                DisplayName = new LocalizedText("Foo")
            };
            var root = new BaseObjectState(null)
            {
                NodeId = new NodeId("R", 2),
                BrowseName = new QualifiedName("Root", 2),
                DisplayName = new LocalizedText("Root")
            };
            root.AddChild(child2);
            root.AddChild(child3);

            NodeState resolved = BrowsePathResolver.Resolve(
                systemContext,
                "Root/ns=3;Foo",
                defaultNamespaceIndex: 2,
                rootResolver: bn => bn == root.BrowseName ? root : null);

            Assert.That(resolved, Is.SameAs(child3));
        }
    }
}
