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

#nullable enable

using NUnit.Framework;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Round-trip and parse tests for <see cref="FileSystemNodeId"/>.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemNodeIdRoundTripTests
    {
        [Test]
        public void BuildRootRoundTrips()
        {
            NodeId nodeId = FileSystemNodeId.BuildRoot(3);

            Assert.That(FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.RootType, Is.EqualTo(FileSystemNodeId.Root));
            Assert.That(parsed.ProviderPath, Is.Empty);
            Assert.That(parsed.NamespaceIndex, Is.EqualTo(3));
            Assert.That(parsed.ComponentPath, Is.Null);
        }

        [Test]
        public void BuildDirectoryRoundTrips()
        {
            NodeId nodeId = FileSystemNodeId.BuildDirectory("folder/sub", 2);

            Assert.That(FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.RootType, Is.EqualTo(FileSystemNodeId.Directory));
            Assert.That(parsed.ProviderPath, Is.EqualTo("folder/sub"));
        }

        [Test]
        public void BuildFileRoundTrips()
        {
            NodeId nodeId = FileSystemNodeId.BuildFile("folder/file.txt", 5);

            Assert.That(FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.RootType, Is.EqualTo(FileSystemNodeId.File));
            Assert.That(parsed.ProviderPath, Is.EqualTo("folder/file.txt"));
            Assert.That(parsed.NamespaceIndex, Is.EqualTo(5));
        }

        [Test]
        public void ProviderPathWithSpecialCharactersIsEscapedAndRoundTrips()
        {
            NodeId nodeId = FileSystemNodeId.BuildFile("a&b?c/d", 1);

            Assert.That(FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.ProviderPath, Is.EqualTo("a&b?c/d"));
            Assert.That(parsed.ComponentPath, Is.Null);
        }

        [Test]
        public void ToNodeIdWithComponentNameAppendsQuestionMark()
        {
            var id = new FileSystemNodeId(FileSystemNodeId.File, "file.txt", 1);

            var nodeId = id.ToNodeId("Open");

            Assert.That(FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.ProviderPath, Is.EqualTo("file.txt"));
            Assert.That(parsed.ComponentPath, Is.EqualTo("Open"));
        }

        [Test]
        public void ToNodeIdWithExistingComponentPathAppendsSlash()
        {
            var id = new FileSystemNodeId(FileSystemNodeId.File, "file.txt", 1, "Open");

            var nodeId = id.ToNodeId("InputArguments");

            Assert.That(FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.ComponentPath, Is.EqualTo("Open/InputArguments"));
        }

        [Test]
        public void TryParseReturnsFalseForNullNodeId()
        {
            Assert.That(FileSystemNodeId.TryParse(NodeId.Null, out _), Is.False);
        }

        [Test]
        public void TryParseReturnsFalseForNumericNodeId()
        {
            Assert.That(FileSystemNodeId.TryParse(new NodeId(42u, 1), out _), Is.False);
        }

        [Test]
        public void TryParseReturnsFalseWhenColonMissing()
        {
            Assert.That(FileSystemNodeId.TryParse(new NodeId("123", 1), out _), Is.False);
        }

        [Test]
        public void TryParseReturnsFalseWhenSeparatorIsNotColon()
        {
            Assert.That(FileSystemNodeId.TryParse(new NodeId("12x", 1), out _), Is.False);
        }

        [Test]
        public void TryParseTreatsLeadingColonAsRootTypeZero()
        {
            Assert.That(FileSystemNodeId.TryParse(new NodeId(":path", 1), out FileSystemNodeId parsed), Is.True);
            Assert.That(parsed.RootType, Is.Zero);
            Assert.That(parsed.ProviderPath, Is.EqualTo("path"));
        }

        [Test]
        public void ConstructorNormalizesNullProviderPathToEmpty()
        {
            var id = new FileSystemNodeId(FileSystemNodeId.Root, null!, 0);

            Assert.That(id.ProviderPath, Is.Empty);
        }
    }
}
