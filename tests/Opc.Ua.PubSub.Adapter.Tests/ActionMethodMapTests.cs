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
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ActionMethodMap"/>: resolution by
    /// writer/target pair first then action name, and fluent registration.
    /// </summary>
    [TestFixture]
    public sealed class ActionMethodMapTests
    {
        [Test]
        public void TryResolveByTargetIdReturnsBinding()
        {
            var objectId = new NodeId(1u);
            var methodId = new NodeId(2u);
            ActionMethodMap map = new ActionMethodMap().Add(7, 9, objectId, methodId);

            bool resolved = map.TryResolve(
                new PubSubActionTarget { DataSetWriterId = 7, ActionTargetId = 9 },
                out ActionMethodBinding binding);

            Assert.That(resolved, Is.True);
            Assert.That(binding.ObjectId, Is.EqualTo(objectId));
            Assert.That(binding.MethodId, Is.EqualTo(methodId));
        }

        [Test]
        public void TryResolveByActionNameReturnsBinding()
        {
            var objectId = new NodeId(10u);
            var methodId = new NodeId(11u);
            ActionMethodMap map = new ActionMethodMap().Add("Start", objectId, methodId);

            bool resolved = map.TryResolve(
                new PubSubActionTarget { ActionName = "Start" },
                out ActionMethodBinding binding);

            Assert.That(resolved, Is.True);
            Assert.That(binding.MethodId, Is.EqualTo(methodId));
        }

        [Test]
        public void TryResolvePrefersTargetIdOverActionName()
        {
            var byPair = new NodeId(1u);
            var byName = new NodeId(2u);
            ActionMethodMap map = new ActionMethodMap()
                .Add(3, 4, new NodeId(100u), byPair)
                .Add("Action", new NodeId(200u), byName);

            bool resolved = map.TryResolve(
                new PubSubActionTarget
                {
                    DataSetWriterId = 3,
                    ActionTargetId = 4,
                    ActionName = "Action"
                },
                out ActionMethodBinding binding);

            Assert.That(resolved, Is.True);
            Assert.That(binding.MethodId, Is.EqualTo(byPair));
        }

        [Test]
        public void TryResolveUnknownTargetReturnsFalse()
        {
            var map = new ActionMethodMap();

            bool resolved = map.TryResolve(
                new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 1 },
                out ActionMethodBinding binding);

            Assert.That(resolved, Is.False);
            Assert.That(binding, Is.Default);
        }

        [Test]
        public void TryResolveNullTargetReturnsFalse()
        {
            ActionMethodMap map = new ActionMethodMap().Add("X", new NodeId(1u), new NodeId(2u));

            bool resolved = map.TryResolve(null!, out ActionMethodBinding binding);

            Assert.That(resolved, Is.False);
            Assert.That(binding, Is.Default);
        }

        [Test]
        public void AddEmptyActionNameThrows()
        {
            var map = new ActionMethodMap();

            Assert.That(
                () => map.Add(string.Empty, new NodeId(1u), new NodeId(2u)),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("actionName"));
        }

        [Test]
        public void AddReturnsSameInstanceForFluentChaining()
        {
            var map = new ActionMethodMap();
            ActionMethodMap chained = map
                .Add(1, 1, new NodeId(1u), new NodeId(2u))
                .Add("name", new NodeId(3u), new NodeId(4u));

            Assert.That(chained, Is.SameAs(map));
        }

        [Test]
        public void OutputFieldNamesArePreservedInBinding()
        {
            string[] rawNames = ["Result", "Code"];
            ArrayOf<string> names = rawNames.ToArrayOf();
            ActionMethodMap map = new ActionMethodMap()
                .Add(1, 2, new NodeId(1u), new NodeId(2u), names);

            map.TryResolve(
                new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 2 },
                out ActionMethodBinding binding);

            Assert.That(binding.OutputFieldNames.Count, Is.EqualTo(2));
            Assert.That(binding.OutputFieldNames[0], Is.EqualTo("Result"));
            Assert.That(binding.OutputFieldNames[1], Is.EqualTo("Code"));
        }
    }
}
