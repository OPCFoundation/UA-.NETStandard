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
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.Server.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for <see cref="FileSystemNodeId"/>.
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class FileSystemNodeIdTests
    {
        [Test]
        public void ConstructIdForComponentReturnsNullForNullComponent()
        {
            NodeId result = FileSystemNodeId.ConstructIdForComponent(null, 1);
            Assert.That(result, Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void ConstructIdForComponentReturnsOwnIdWhenParentMissing()
        {
            var orphan = new BaseObjectState(null)
            {
                NodeId = new NodeId("Orphan", 1),
                SymbolicName = "Orphan"
            };

            NodeId result = FileSystemNodeId.ConstructIdForComponent(orphan, 1);
            Assert.That(result, Is.EqualTo(new NodeId("Orphan", 1)));
        }

        [Test]
        public void ConstructIdForComponentAppendsQuestionMarkSeparatorForFirstComponent()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("1:folder/file.txt", 1)
            };
            var child = new BaseObjectState(parent)
            {
                SymbolicName = "Open"
            };

            NodeId result = FileSystemNodeId.ConstructIdForComponent(child, 1);
            Assert.That(result, Is.EqualTo(new NodeId("1:folder/file.txt?Open", 1)));
        }

        [Test]
        public void ConstructIdForComponentAppendsSlashSeparatorForNestedComponent()
        {
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("2:folder/file.txt?Open", 1)
            };
            var child = new BaseObjectState(parent)
            {
                SymbolicName = "InputArguments"
            };

            NodeId result = FileSystemNodeId.ConstructIdForComponent(child, 1);
            Assert.That(result, Is.EqualTo(new NodeId("2:folder/file.txt?Open/InputArguments", 1)));
        }
    }
}
