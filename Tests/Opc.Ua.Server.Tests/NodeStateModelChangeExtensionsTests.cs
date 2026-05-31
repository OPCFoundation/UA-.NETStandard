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

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.NodeManager;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for the <c>NodeState</c> extension helpers that support
    /// Part 5 §9.32.2 NodeVersion correlation
    /// (<see cref="NodeStateModelChangeExtensions"/>).
    /// </summary>
    [TestFixture]
    [Category("NodeState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class NodeStateModelChangeExtensionsTests
    {
        /// <summary>
        /// <see cref="NodeStateModelChangeExtensions.EnableModelChangeTracking"/>
        /// is idempotent — invoking it again on a node that already has a
        /// NodeVersion property must not attach a second copy.
        /// </summary>
        [Test]
        public void EnableModelChangeTracking_IsIdempotent()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("X", 1),
                BrowseName = new QualifiedName("X", 1)
            };

            PropertyState<string> first = node.EnableModelChangeTracking(1);
            PropertyState<string> second = node.EnableModelChangeTracking(1);

            Assert.That(second, Is.SameAs(first));

            int matches = 0;
            var children = new List<BaseInstanceState>();
            node.GetChildren(null!, children);
            foreach (BaseInstanceState child in children)
            {
                if (child.BrowseName == new QualifiedName(BrowseNames.NodeVersion, 0))
                {
                    matches++;
                }
            }
            Assert.That(matches, Is.EqualTo(1));
        }

        /// <summary>
        /// <see cref="NodeStateModelChangeExtensions.HasNodeVersion"/>
        /// reflects the presence of the NodeVersion property.
        /// </summary>
        [Test]
        public void HasNodeVersion_TrueOnlyAfterEnableTracking()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("X", 1),
                BrowseName = new QualifiedName("X", 1)
            };

            Assert.That(node.HasNodeVersion(), Is.False);

            node.EnableModelChangeTracking(1);
            Assert.That(node.HasNodeVersion(), Is.True);
        }

        /// <summary>
        /// <see cref="NodeStateModelChangeExtensions.BumpNodeVersion"/>
        /// increments the value monotonically and is a no-op on nodes
        /// that don't have a NodeVersion property.
        /// </summary>
        [Test]
        public void BumpNodeVersion_IncrementsOnTrackedNode_NoOpOnUntrackedNode()
        {
            var untracked = new BaseObjectState(null) { BrowseName = new QualifiedName("U", 1) };
            untracked.BumpNodeVersion(null!);
            Assert.That(untracked.HasNodeVersion(), Is.False);

            var tracked = new BaseObjectState(null) { BrowseName = new QualifiedName("T", 1) };
            PropertyState<string> v = tracked.EnableModelChangeTracking(1);
            Assert.That(v.Value, Is.EqualTo("1"));

            tracked.BumpNodeVersion(null!);
            Assert.That(v.Value, Is.EqualTo("2"));

            tracked.BumpNodeVersion(null!);
            Assert.That(v.Value, Is.EqualTo("3"));
        }

        [Test]
        public void BumpNodeVersion_ThrowsWhenNodeNull()
        {
            Assert.That(
                () => NodeStateModelChangeExtensions.BumpNodeVersion(null!, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void EnableModelChangeTracking_ThrowsWhenNodeNull()
        {
            Assert.That(
                () => NodeStateModelChangeExtensions.EnableModelChangeTracking(null!, 1),
                Throws.ArgumentNullException);
        }

        [Test]
        public void HasNodeVersion_ReturnsFalseForNullNode()
        {
            Assert.That(NodeStateModelChangeExtensions.HasNodeVersion(null!), Is.False);
        }

        [Test]
        public void GetNodeVersionProperty_ReturnsNullForNullNode()
        {
            Assert.That(NodeStateModelChangeExtensions.GetNodeVersionProperty(null!), Is.Null);
        }

        [Test]
        public void EnableModelChangeTracking_OnWriteValueFiresCallbackWhenNotFrameworkBump()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("Y", 1),
                BrowseName = new QualifiedName("Y", 1)
            };

            bool callbackFired = false;
            node.EnableModelChangeTracking(1, (ctx, n) => { callbackFired = true; });

            // Simulate external write via the OnWriteValue callback
            PropertyState<string> nodeVersion = node.GetNodeVersionProperty()!;
            var value = new Variant("99");
            var statusCode = StatusCodes.Good;
            var timestamp = default(DateTimeUtc);
            nodeVersion.OnWriteValue(null!, nodeVersion, default, default, ref value, ref statusCode, ref timestamp);

            Assert.That(callbackFired, Is.True);
        }

        [Test]
        public void EnableModelChangeTracking_OnWriteValueSuppressedDuringFrameworkBump()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("Z", 1),
                BrowseName = new QualifiedName("Z", 1)
            };

            bool callbackFired = false;
            node.EnableModelChangeTracking(1, (ctx, n) => { callbackFired = true; });

            // BumpNodeVersion sets the framework flag, so the callback should not fire
            node.BumpNodeVersion(null!);

            Assert.That(callbackFired, Is.False);
        }

        [Test]
        public void IsInsideFrameworkNodeVersionBump_FalseByDefault()
        {
            Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.False);
        }

        [Test]
        public void BumpNodeVersion_HandlesNonNumericValue()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("NaN", 1),
                BrowseName = new QualifiedName("NaN", 1)
            };

            PropertyState<string> v = node.EnableModelChangeTracking(1);
            v.Value = "not-a-number";

            node.BumpNodeVersion(null!);

            // Non-numeric values reset to 0, then increment to 1
            Assert.That(v.Value, Is.EqualTo("1"));
        }

        [Test]
        public void BumpNodeVersion_HandlesEmptyStringValue()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("Empty", 1),
                BrowseName = new QualifiedName("Empty", 1)
            };

            PropertyState<string> v = node.EnableModelChangeTracking(1);
            v.Value = "";

            node.BumpNodeVersion(null!);

            Assert.That(v.Value, Is.EqualTo("1"));
        }

        [Test]
        public void BumpNodeVersion_WrapsOnOverflow()
        {
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("Max", 1),
                BrowseName = new QualifiedName("Max", 1)
            };

            PropertyState<string> v = node.EnableModelChangeTracking(1);
            v.Value = ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

            node.BumpNodeVersion(null!);

            // Should wrap and skip 0
            Assert.That(v.Value, Is.EqualTo("1"));
        }
    }
}
