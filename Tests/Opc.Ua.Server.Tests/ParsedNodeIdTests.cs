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

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ParsedNodeId"/> covering parse/construct round-trips,
    /// escaping, component paths and the various edge cases that return <c>null</c>.
    /// </summary>
    [TestFixture]
    [Category("ParsedNodeId")]
    [Parallelizable(ParallelScope.All)]
    public class ParsedNodeIdTests
    {
        [Test]
        public void ParseReturnsNullForNullNodeId()
        {
            Assert.That(ParsedNodeId.Parse(default), Is.Null);
        }

        [Test]
        public void ParseReturnsNullForNumericNodeId()
        {
            Assert.That(ParsedNodeId.Parse(new NodeId(42, 1)), Is.Null);
        }

        [Test]
        public void ParseReturnsNullForEmptyStringNodeId()
        {
            Assert.That(ParsedNodeId.Parse(new NodeId(string.Empty, 1)), Is.Null);
        }

        [Test]
        public void ParseReturnsNullWhenNoColonSeparator()
        {
            Assert.That(ParsedNodeId.Parse(new NodeId("123", 1)), Is.Null);
        }

        [Test]
        public void ParseReturnsNullWhenFirstCharIsNotDigitAndNotColon()
        {
            // start is set to index 0 (non-digit) and identifier[0] != ':'.
            Assert.That(ParsedNodeId.Parse(new NodeId("abc:def", 1)), Is.Null);
        }

        [Test]
        public void ParseExtractsRootTypeAndRootId()
        {
            ParsedNodeId parsed = ParsedNodeId.Parse(new NodeId("15:MyRoot", 3));

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.RootType, Is.EqualTo(15));
            Assert.That(parsed.RootId, Is.EqualTo("MyRoot"));
            Assert.That(parsed.NamespaceIndex, Is.EqualTo((ushort)3));
            Assert.That(parsed.ComponentPath, Is.Null);
        }

        [Test]
        public void ParseExtractsComponentPath()
        {
            ParsedNodeId parsed = ParsedNodeId.Parse(new NodeId("1:Root?Child/Grandchild", 2));

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.RootType, Is.EqualTo(1));
            Assert.That(parsed.RootId, Is.EqualTo("Root"));
            Assert.That(parsed.ComponentPath, Is.EqualTo("Child/Grandchild"));
        }

        [Test]
        public void ParseHonorsEscapedDelimiterInRootId()
        {
            // The '?' is escaped so it stays part of the root id and is not treated as a path separator.
            ParsedNodeId parsed = ParsedNodeId.Parse(new NodeId("2:Ro&?ot?Path", 1));

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.RootId, Is.EqualTo("Ro?ot"));
            Assert.That(parsed.ComponentPath, Is.EqualTo("Path"));
        }

        [Test]
        public void ConstructRoundTripsWithComponentPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 7,
                RootId = "Root",
                NamespaceIndex = 4,
                ComponentPath = "A/B"
            };

            NodeId nodeId = parsed.Construct();
            ParsedNodeId reparsed = ParsedNodeId.Parse(nodeId);

            Assert.That(nodeId.NamespaceIndex, Is.EqualTo((ushort)4));
            Assert.That(reparsed, Is.Not.Null);
            Assert.That(reparsed.RootType, Is.EqualTo(7));
            Assert.That(reparsed.RootId, Is.EqualTo("Root"));
            Assert.That(reparsed.ComponentPath, Is.EqualTo("A/B"));
        }

        [Test]
        public void ConstructEscapesSpecialCharactersInRootId()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 3,
                RootId = "a?b",
                NamespaceIndex = 1
            };

            NodeId nodeId = parsed.Construct();
            ParsedNodeId reparsed = ParsedNodeId.Parse(nodeId);

            Assert.That(reparsed, Is.Not.Null);
            Assert.That(reparsed.RootId, Is.EqualTo("a?b"));
            Assert.That(reparsed.ComponentPath, Is.Null);
        }

        [Test]
        public void ConstructEscapesAmpersandInRawIdentifier()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 3,
                RootId = "a&b",
                NamespaceIndex = 1
            };

            NodeId nodeId = parsed.Construct();

            Assert.That(nodeId.TryGetValue(out string identifier), Is.True);
            Assert.That(identifier, Is.EqualTo("3:a&&b"));
        }

        [Test]
        public void ConstructAppendsComponentNameWithoutExistingPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 1,
                RootId = "Root",
                NamespaceIndex = 2
            };

            NodeId nodeId = parsed.Construct("Component");
            ParsedNodeId reparsed = ParsedNodeId.Parse(nodeId);

            Assert.That(reparsed, Is.Not.Null);
            Assert.That(reparsed.ComponentPath, Is.EqualTo("Component"));
        }

        [Test]
        public void ConstructAppendsComponentNameToExistingPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 1,
                RootId = "Root",
                NamespaceIndex = 2,
                ComponentPath = "Existing"
            };

            NodeId nodeId = parsed.Construct("Component");
            ParsedNodeId reparsed = ParsedNodeId.Parse(nodeId);

            Assert.That(reparsed, Is.Not.Null);
            Assert.That(reparsed.ComponentPath, Is.EqualTo("Existing/Component"));
        }

        [Test]
        public void StaticConstructBuildsNodeIdFromComponentNames()
        {
            NodeId nodeId = ParsedNodeId.Construct(5, "Root", 3, "A", "B", "C");
            ParsedNodeId reparsed = ParsedNodeId.Parse(nodeId);

            Assert.That(nodeId.NamespaceIndex, Is.EqualTo((ushort)3));
            Assert.That(reparsed, Is.Not.Null);
            Assert.That(reparsed.RootType, Is.EqualTo(5));
            Assert.That(reparsed.RootId, Is.EqualTo("Root"));
            Assert.That(reparsed.ComponentPath, Is.EqualTo("A/B/C"));
        }

        [Test]
        public void StaticConstructWithoutComponentNamesHasNoComponentPath()
        {
            NodeId nodeId = ParsedNodeId.Construct(2, "Root", 1);
            ParsedNodeId reparsed = ParsedNodeId.Parse(nodeId);

            Assert.That(reparsed, Is.Not.Null);
            Assert.That(reparsed.ComponentPath, Is.Null);
        }

        [Test]
        public void CreateIdForComponentReturnsDefaultForNullComponent()
        {
            Assert.That(ParsedNodeId.CreateIdForComponent(null!, 1).IsNull, Is.True);
        }

        [Test]
        public void CreateIdForComponentReturnsNodeIdWhenNoParent()
        {
            var component = new BaseObjectState(null)
            {
                NodeId = new NodeId("orphan", 1),
                SymbolicName = "Orphan"
            };

            NodeId result = ParsedNodeId.CreateIdForComponent(component, 2);

            Assert.That(result, Is.EqualTo(component.NodeId));
        }

        [Test]
        public void CreateIdForComponentReturnsDefaultForNonStringParent()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId(99, 1),
                SymbolicName = "Parent"
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId("child", 1),
                SymbolicName = "Child"
            };

            Assert.That(ParsedNodeId.CreateIdForComponent(child, 2).IsNull, Is.True);
        }

        [Test]
        public void CreateIdForComponentAppendsSymbolicNameToParentWithoutPath()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("1:Root", 1),
                SymbolicName = "Root"
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId("child", 1),
                SymbolicName = "Child"
            };

            NodeId result = ParsedNodeId.CreateIdForComponent(child, 1);

            Assert.That(result.TryGetValue(out string identifier), Is.True);
            Assert.That(identifier, Is.EqualTo("1:Root?Child"));
        }

        [Test]
        public void CreateIdForComponentAppendsSymbolicNameToParentWithExistingPath()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("1:Root?Existing", 1),
                SymbolicName = "Existing"
            };
            var child = new BaseObjectState(parent)
            {
                NodeId = new NodeId("child", 1),
                SymbolicName = "Child"
            };

            NodeId result = ParsedNodeId.CreateIdForComponent(child, 1);

            Assert.That(result.TryGetValue(out string identifier), Is.True);
            Assert.That(identifier, Is.EqualTo("1:Root?Existing/Child"));
        }
    }
}
