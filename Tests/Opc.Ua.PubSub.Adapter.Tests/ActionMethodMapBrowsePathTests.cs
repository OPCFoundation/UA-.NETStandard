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
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Unit tests for browse-path overloads on <see cref="ActionMethodMap"/>.
    /// </summary>
    [TestFixture]
    public sealed class ActionMethodMapBrowsePathTests
    {
        [Test]
        public void AddBrowsePathOverloadStoresBrowsePathSentinels()
        {
            var map = new ActionMethodMap().Add(
                "Reset",
                "/2:Demo",
                "/2:Demo/2:Reset");

            bool resolved = map.TryResolve(
                new PubSubActionTarget { ActionName = "Reset" },
                out ActionMethodBinding binding);

            Assert.That(resolved, Is.True);
            Assert.That(NodeBrowsePath.IsBrowsePath(binding.ObjectId), Is.True);
            Assert.That(NodeBrowsePath.IsBrowsePath(binding.MethodId), Is.True);
            Assert.That(binding.ObjectId.IdentifierAsString, Is.EqualTo("/2:Demo"));
            Assert.That(binding.MethodId.IdentifierAsString, Is.EqualTo("/2:Demo/2:Reset"));
        }
    }
}
