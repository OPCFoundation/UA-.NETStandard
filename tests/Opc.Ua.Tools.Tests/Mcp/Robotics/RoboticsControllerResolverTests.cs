// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET10_0_OR_GREATER
using System;
using NUnit.Framework;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests for scoped resource resolution within controller lookup tables.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsControllerResolverTests
    {
        [Test]
        public void ResolveScopedResourceByNodeIdReturnsNodeId()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Tool1"), "Tool1")
            ];

            NodeId result = RoboticsControllerResolver.ResolveScopedResource("ns=2;s=T1", entries, "tool");

            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void ResolveScopedResourceByUniqueNameReturnsNodeId()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Tool1"), "Tool1"),
                new RobotIntentNodeLookupEntry(new NodeId("T2", 2), new QualifiedName("Tool2"), "Tool2")
            ];

            NodeId result = RoboticsControllerResolver.ResolveScopedResource("Tool1", entries, "tool");

            Assert.That(result.ToString(), Is.EqualTo("ns=2;s=T1"));
        }

        [Test]
        public void ResolveScopedResourceWithAmbiguousNameThrows()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Tool"), "Tool"),
                new RobotIntentNodeLookupEntry(new NodeId("T2", 2), new QualifiedName("Tool"), "Tool")
            ];

            Assert.That(
                () => RoboticsControllerResolver.ResolveScopedResource("Tool", entries, "tool"),
                Throws.ArgumentException.With.Message.Contains("Ambiguous"));
        }

        [Test]
        public void ResolveScopedResourceWithUnknownNameThrows()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Tool1"), "Tool1")
            ];

            Assert.That(
                () => RoboticsControllerResolver.ResolveScopedResource("NoSuchTool", entries, "tool"),
                Throws.ArgumentException.With.Message.Contains("No tool named"));
        }

        [Test]
        public void ResolveScopedResourceWithEmptyReturnsNullNodeId()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries = [];

            NodeId result = RoboticsControllerResolver.ResolveScopedResource(string.Empty, entries, "tool");

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ResolveScopedResourceExactCaseMatchFindsEntry()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Gripper"), "Gripper")
            ];

            NodeId result = RoboticsControllerResolver.ResolveScopedResource("Gripper", entries, "tool");

            Assert.That(result.ToString(), Is.EqualTo("ns=2;s=T1"));
        }

        [Test]
        public void ResolveScopedResourceDifferentCaseDoesNotMatch()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Gripper"), "Gripper")
            ];

            Assert.That(
                () => RoboticsControllerResolver.ResolveScopedResource("gripper", entries, "tool"),
                Throws.ArgumentException.With.Message.Contains("No tool named"));
        }

        [Test]
        public void ResolveScopedResourceTrimsWhitespace()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Gripper"), "Gripper")
            ];

            NodeId result = RoboticsControllerResolver.ResolveScopedResource("  Gripper  ", entries, "tool");

            Assert.That(result.ToString(), Is.EqualTo("ns=2;s=T1"));
        }

        [Test]
        public void ResolveScopedResourceNotFoundListsNamesAndNodeIds()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Tool1"), "Tool1"),
                new RobotIntentNodeLookupEntry(new NodeId("T2", 2), new QualifiedName("Tool2"), "Tool2")
            ];

            var ex = Assert.Throws<ArgumentException>(
                () => RoboticsControllerResolver.ResolveScopedResource("NoSuchTool", entries, "tool"));

            Assert.That(ex!.Message, Does.Contain("Tool1"));
            Assert.That(ex.Message, Does.Contain("ns=2;s=T1"));
            Assert.That(ex.Message, Does.Contain("Tool2"));
            Assert.That(ex.Message, Does.Contain("ns=2;s=T2"));
        }

        [Test]
        public void ResolveScopedResourceMatchesBrowseNameCandidate()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(
                    new NodeId("T1", 2), new QualifiedName("GripperBrowse", 2), "Gripper")
            ];

            NodeId byName = RoboticsControllerResolver.ResolveScopedResource("Gripper", entries, "tool");
            NodeId byBrowseName = RoboticsControllerResolver.ResolveScopedResource(
                "GripperBrowse", entries, "tool");

            Assert.Multiple(() =>
            {
                Assert.That(byName.ToString(), Is.EqualTo("ns=2;s=T1"));
                Assert.That(byBrowseName.ToString(), Is.EqualTo("ns=2;s=T1"));
            });
        }

        [Test]
        public void ResolveScopedResourceAmbiguousErrorListsNamesAndNodeIds()
        {
            ArrayOf<RobotIntentNodeLookupEntry> entries =
            [
                new RobotIntentNodeLookupEntry(new NodeId("T1", 2), new QualifiedName("Tool"), "Tool"),
                new RobotIntentNodeLookupEntry(new NodeId("T2", 2), new QualifiedName("Tool"), "Tool")
            ];

            var ex = Assert.Throws<ArgumentException>(
                () => RoboticsControllerResolver.ResolveScopedResource("Tool", entries, "tool"));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Does.Contain("Tool (ns=2;s=T1)"));
                Assert.That(ex.Message, Does.Contain("Tool (ns=2;s=T2)"));
            });
        }
    }
}
#endif
