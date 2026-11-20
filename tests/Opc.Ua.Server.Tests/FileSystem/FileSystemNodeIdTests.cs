// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
            NodeId result = FileSystemNodeId.ConstructIdForComponent(null!, 1);
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
