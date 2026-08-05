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
using NUnit.Framework;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Server.Tests
{
    [TestFixture]
    [Category("Robotics")]
    [Category("RobotIntent")]
    public sealed class FrameTreeTests
    {
        [Test]
        public void ResolvesFrameToRootAcrossMultiLevelTree()
        {
            FrameTree tree = BuildTree();

            bool resolved = tree.TryResolveToRoot("tool", out Pose3DDataType transform, out string? error);

            Assert.That(resolved, Is.True);
            Assert.That(error, Is.Null);
            AssertPositionEqual([1.0, 2.0, 0.0], transform.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, 0.0, 1.0], transform.Orientation.Span, 1e-12);
        }

        [Test]
        public void ResolvesRootFrameAsIdentityBecauseRootTransformIsIgnored()
        {
            var tree = new FrameTree();
            Assert.That(
                tree.TryAdd(
                    "world",
                    string.Empty,
                    Pose("world", 9.0, 8.0, 7.0),
                    FrameRoleEnum.World,
                    out string? addError),
                Is.True);
            Assert.That(addError, Is.Null);

            bool resolved = tree.TryResolveToRoot("world", out Pose3DDataType transform, out string? error);

            Assert.That(resolved, Is.True);
            Assert.That(error, Is.Null);
            AssertPositionEqual([0.0, 0.0, 0.0], transform.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, 0.0, 1.0], transform.Orientation.Span, 1e-12);
        }

        [Test]
        public void ExpressesPoseAcrossMultiLevelTree()
        {
            FrameTree tree = BuildTree();
            Pose3DDataType pose = Pose("tool", 0.0, 0.0, 3.0);

            bool expressed = tree.TryExpress(pose, "world", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(result.FrameId, Is.EqualTo("world"));
            AssertPositionEqual([1.0, 2.0, 3.0], result.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, 0.0, 1.0], result.Orientation.Span, 1e-12);
        }

        [Test]
        public void ExpressesPoseRelativeToIntermediateTargetFrame()
        {
            FrameTree tree = BuildTree();
            Pose3DDataType pose = Pose("tool", 0.0, 0.0, 3.0);

            bool expressed = tree.TryExpress(pose, "base", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(result.FrameId, Is.EqualTo("base"));
            AssertPositionEqual([0.0, 2.0, 3.0], result.Position.Span, 1e-12);
            AssertQuaternionEqual([0.0, 0.0, 0.0, 1.0], result.Orientation.Span, 1e-12);
        }

        [Test]
        public void SelfExpressionReturnsPoseUnchangedWithoutResolvingFrame()
        {
            var tree = new FrameTree();
            Pose3DDataType pose = Pose("missing", 4.0, 5.0, 6.0);

            bool expressed = tree.TryExpress(pose, "missing", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(result.FrameId, Is.EqualTo("missing"));
            AssertPositionEqual(pose.Position.Span, result.Position.Span, 1e-12);
            AssertQuaternionEqual(pose.Orientation.Span, result.Orientation.Span, 1e-12);
        }

        [Test]
        public void CycleDetectionRejectsSelfParentWithReason()
        {
            var tree = new FrameTree();

            bool added = tree.TryAdd(
                "world",
                "world",
                Pose("world"),
                FrameRoleEnum.World,
                out string? error);

            Assert.That(added, Is.False);
            Assert.That(error, Is.EqualTo("A frame cannot be its own parent."));
        }

        [Test]
        public void UnknownParentIsRejectedWithReason()
        {
            var tree = new FrameTree();

            bool added = tree.TryAdd(
                "base",
                "missing",
                Pose("base"),
                FrameRoleEnum.Base,
                out string? error);

            Assert.That(added, Is.False);
            Assert.That(error, Is.EqualTo("The parent frame 'missing' is unknown."));
        }

        [Test]
        public void UnknownTargetFrameIsRejectedWithReason()
        {
            FrameTree tree = BuildTree();

            bool expressed = tree.TryExpress(Pose("tool"), "missing", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.False);
            Assert.That(error, Is.EqualTo("The frame 'missing' is unknown."));
            Assert.That(result.FrameId, Is.EqualTo("missing"));
        }

        [Test]
        public void UnknownSourceFrameIsRejectedWithReason()
        {
            FrameTree tree = BuildTree();

            bool expressed = tree.TryExpress(Pose("missing"), "world", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.False);
            Assert.That(error, Is.EqualTo("The frame 'missing' is unknown."));
            Assert.That(result.FrameId, Is.EqualTo("world"));
        }

        [Test]
        public void UnknownResolveFrameIsRejectedWithReason()
        {
            FrameTree tree = BuildTree();

            bool resolved = tree.TryResolveToRoot("missing", out Pose3DDataType transform, out string? error);

            Assert.That(resolved, Is.False);
            Assert.That(error, Is.EqualTo("The frame 'missing' is unknown."));
            AssertPositionEqual([0.0, 0.0, 0.0], transform.Position.Span, 1e-12);
        }

        [Test]
        public void DisjointSubtreesAreRejectedWithDistinctReason()
        {
            var tree = new FrameTree();
            AddFrame(tree, "worldA", string.Empty, Pose("worldA"), FrameRoleEnum.World);
            AddFrame(tree, "baseA", "worldA", Pose("baseA", 1.0, 0.0, 0.0), FrameRoleEnum.Base);
            AddFrame(tree, "worldB", string.Empty, Pose("worldB"), FrameRoleEnum.World);
            AddFrame(tree, "baseB", "worldB", Pose("baseB", 0.0, 1.0, 0.0), FrameRoleEnum.Base);

            bool expressed = tree.TryExpress(Pose("baseA"), "baseB", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.False);
            Assert.That(error, Is.EqualTo(
                "The source frame 'baseA' and target frame 'baseB' do not share a common root."));
            Assert.That(result.FrameId, Is.EqualTo("baseB"));
        }

        [Test]
        public void DuplicateFrameIsRejectedWithReason()
        {
            FrameTree tree = BuildTree();

            bool added = tree.TryAdd(
                "tool",
                "base",
                Pose("tool"),
                FrameRoleEnum.Tool,
                out string? error);

            Assert.That(added, Is.False);
            Assert.That(error, Is.EqualTo("The frame 'tool' already exists."));
        }

        [Test]
        public void AddRejectsEmptyFrameAndInvalidTransform()
        {
            var tree = new FrameTree();

            bool emptyAdded = tree.TryAdd(
                string.Empty,
                string.Empty,
                Pose(string.Empty),
                FrameRoleEnum.World,
                out string? emptyError);
            bool invalidAdded = tree.TryAdd(
                "world",
                string.Empty,
                new Pose3DDataType
                {
                    Position = ArrayOf.Create([0.0, 0.0]),
                    Orientation = ArrayOf.Create([0.0, 0.0, 0.0, 1.0])
                },
                FrameRoleEnum.World,
                out string? invalidError);

            Assert.That(emptyAdded, Is.False);
            Assert.That(emptyError, Is.EqualTo("The frame identifier must not be empty."));
            Assert.That(invalidAdded, Is.False);
            Assert.That(invalidError, Does.Contain("position"));
        }

        [Test]
        public void PublicMethodsRejectNullArguments()
        {
            var tree = new FrameTree();

            Assert.Throws<ArgumentNullException>(() => tree.TryAdd(
                null!,
                string.Empty,
                Pose("world"),
                FrameRoleEnum.World,
                out _));
            Assert.Throws<ArgumentNullException>(() => tree.TryAdd(
                "world",
                null!,
                Pose("world"),
                FrameRoleEnum.World,
                out _));
            Assert.Throws<ArgumentNullException>(() => tree.TryAdd(
                "world",
                string.Empty,
                null!,
                FrameRoleEnum.World,
                out _));
            Assert.Throws<ArgumentNullException>(() => tree.TryResolveToRoot(null!, out _, out _));
            Assert.Throws<ArgumentNullException>(() => tree.TryExpress(null!, "world", out _, out _));
            Assert.Throws<ArgumentNullException>(() => tree.TryExpress(Pose("world"), null!, out _, out _));
        }

        [Test]
        public void ExpressRejectsInvalidPoseBeforeResolvingFrames()
        {
            FrameTree tree = BuildTree();
            var invalid = new Pose3DDataType
            {
                FrameId = "tool",
                Position = ArrayOf.Create([0.0, 0.0, 0.0]),
                Orientation = ArrayOf.Create([0.0, 0.0, 2.0, 0.0])
            };

            bool expressed = tree.TryExpress(invalid, "world", out Pose3DDataType result, out string? error);

            Assert.That(expressed, Is.False);
            Assert.That(error, Does.Contain("orientation"));
            Assert.That(result.FrameId, Is.EqualTo("world"));
        }

        private static FrameTree BuildTree()
        {
            var tree = new FrameTree();

            AddFrame(tree, "world", string.Empty, Pose("world"), FrameRoleEnum.World);
            AddFrame(tree, "base", "world", Pose("base", 1.0, 0.0, 0.0), FrameRoleEnum.Base);
            AddFrame(tree, "tool", "base", Pose("tool", 0.0, 2.0, 0.0), FrameRoleEnum.Tool);

            return tree;
        }

        private static void AddFrame(
            FrameTree tree,
            string frameId,
            string parentFrameId,
            Pose3DDataType transform,
            FrameRoleEnum role)
        {
            Assert.That(tree.TryAdd(frameId, parentFrameId, transform, role, out string? error), Is.True);
            Assert.That(error, Is.Null);
        }

        private static Pose3DDataType Pose(string frameId, double x = 0.0, double y = 0.0, double z = 0.0)
        {
            return new Pose3DDataType
            {
                FrameId = frameId,
                Position = ArrayOf.Create([x, y, z]),
                Orientation = ArrayOf.Create([0.0, 0.0, 0.0, 1.0])
            };
        }

        private static void AssertPositionEqual(
            ReadOnlySpan<double> expected,
            ReadOnlySpan<double> actual,
            double tolerance)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance));
            }
        }

        private static void AssertQuaternionEqual(
            ReadOnlySpan<double> expected,
            ReadOnlySpan<double> actual,
            double tolerance)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance));
            }
        }
    }
}
