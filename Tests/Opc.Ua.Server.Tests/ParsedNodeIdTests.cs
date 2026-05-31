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

using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    public class ParsedNodeIdTests
    {
        [Test]
        public void ParseReturnsNullForNullNodeId()
        {
            var result = ParsedNodeId.Parse(default);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseReturnsNullForNumericNodeId()
        {
            var nodeId = new NodeId(42, 1);
            var result = ParsedNodeId.Parse(nodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseReturnsNullForEmptyStringNodeId()
        {
            var nodeId = new NodeId(string.Empty, 1);
            var result = ParsedNodeId.Parse(nodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseReturnsNullWhenNoColonSeparator()
        {
            var nodeId = new NodeId("abc", 1);
            var result = ParsedNodeId.Parse(nodeId);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ParseExtractsRootTypeAndRootId()
        {
            var nodeId = new NodeId("1:Temperature", 2);
            var result = ParsedNodeId.Parse(nodeId);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RootType, Is.EqualTo(1));
            Assert.That(result.RootId, Is.EqualTo("Temperature"));
            Assert.That(result.NamespaceIndex, Is.EqualTo(2));
            Assert.That(result.ComponentPath, Is.Null);
        }

        [Test]
        public void ParseExtractsMultiDigitRootType()
        {
            var nodeId = new NodeId("123:SomeRoot", 0);
            var result = ParsedNodeId.Parse(nodeId);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RootType, Is.EqualTo(123));
            Assert.That(result.RootId, Is.EqualTo("SomeRoot"));
        }

        [Test]
        public void ParseExtractsComponentPath()
        {
            var nodeId = new NodeId("1:Root?Child/SubChild", 0);
            var result = ParsedNodeId.Parse(nodeId);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RootId, Is.EqualTo("Root"));
            Assert.That(result.ComponentPath, Is.EqualTo("Child/SubChild"));
        }

        [Test]
        public void ParseHandlesEscapeCharacter()
        {
            // '&' escapes the next character: '&?' means literal '?'
            var nodeId = new NodeId("0:Root&?Still?Path", 0);
            var result = ParsedNodeId.Parse(nodeId);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RootId, Is.EqualTo("Root?Still"));
            Assert.That(result.ComponentPath, Is.EqualTo("Path"));
        }

        [Test]
        public void ParseHandlesEscapedQuestionMark()
        {
            var nodeId = new NodeId("0:Root&?Literal", 0);
            var result = ParsedNodeId.Parse(nodeId);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.RootId, Is.EqualTo("Root?Literal"));
            Assert.That(result.ComponentPath, Is.Null);
        }

        [Test]
        public void ConstructCreatesNodeIdWithoutComponentPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 1,
                RootId = "Temperature",
                NamespaceIndex = 2,
                ComponentPath = null!
            };

            var nodeId = parsed.Construct();

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("1:Temperature"));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(2));
        }

        [Test]
        public void ConstructCreatesNodeIdWithComponentPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 2,
                RootId = "Root",
                NamespaceIndex = 1,
                ComponentPath = "Child/SubChild"
            };

            var nodeId = parsed.Construct();

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("2:Root?Child/SubChild"));
        }

        [Test]
        public void ConstructWithComponentNameAppendsToEmptyPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 0,
                RootId = "Root",
                NamespaceIndex = 0,
                ComponentPath = null!
            };

            var nodeId = parsed.Construct("NewComponent");

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("0:Root?NewComponent"));
        }

        [Test]
        public void ConstructWithComponentNameAppendsToExistingPath()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 0,
                RootId = "Root",
                NamespaceIndex = 0,
                ComponentPath = "Existing"
            };

            var nodeId = parsed.Construct("NewComponent");

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("0:Root?Existing/NewComponent"));
        }

        [Test]
        public void ConstructEscapesQuestionMarkInRootId()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 0,
                RootId = "Root?Special",
                NamespaceIndex = 0,
                ComponentPath = null!
            };

            var nodeId = parsed.Construct();

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            // '?' in RootId is escaped to '&?'
            Assert.That(id, Is.EqualTo("0:Root&?Special"));
        }

        [Test]
        public void StaticConstructCreatesNodeIdWithComponents()
        {
            var nodeId = ParsedNodeId.Construct(1, "Root", 3, "Child1", "Child2");

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("1:Root?Child1/Child2"));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(3));
        }

        [Test]
        public void StaticConstructCreatesNodeIdWithoutComponents()
        {
            var nodeId = ParsedNodeId.Construct(0, "Alone", 1);

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("0:Alone"));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo(1));
        }

        [Test]
        public void ParseAndConstructRoundTrips()
        {
            var original = new NodeId("5:MyDevice?Sensors/Temperature", 4);
            var parsed = ParsedNodeId.Parse(original);

            Assert.That(parsed, Is.Not.Null);

            var reconstructed = parsed!.Construct();

            Assert.That(reconstructed.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("5:MyDevice?Sensors/Temperature"));
            Assert.That(reconstructed.NamespaceIndex, Is.EqualTo(4));
        }

        [Test]
        public void ConstructNullRootIdProducesValidNodeId()
        {
            var parsed = new ParsedNodeId
            {
                RootType = 0,
                RootId = null!,
                NamespaceIndex = 0,
                ComponentPath = null!
            };

            var nodeId = parsed.Construct();

            Assert.That(nodeId.TryGetValue(out string id), Is.True);
            Assert.That(id, Is.EqualTo("0:"));
        }
    }
}
