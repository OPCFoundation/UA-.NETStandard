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

using System;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Coverage tests for <see cref="AliasNameStoreRegistry"/> —
    /// registration, conflict detection, store routing, and dispatch
    /// of <c>BadNotImplemented</c> when no store owns the category.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameStoreRegistryTests
    {
        private static readonly NodeId s_a = new("A", 2);
        private static readonly NodeId s_b = new("B", 2);

        private static InMemoryAliasNameStore Store(NodeId rootId, string name)
        {
            var d = new AliasNameCategoryDescriptor(
                rootId, new QualifiedName(name, 2));
            return new InMemoryAliasNameStore([d]);
        }

        [Test]
        public void RegisterTrackedAndRoutesByCategory()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore storeA = Store(s_a, "A");
            using InMemoryAliasNameStore storeB = Store(s_b, "B");

            registry.Register(storeA);
            registry.Register(storeB);

            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(storeA));
            Assert.That(registry.GetStoreForCategory(s_b), Is.SameAs(storeB));
            Assert.That(registry.GetStoreForCategory(new NodeId("C", 2)),
                Is.Null);
        }

        [Test]
        public void DuplicateRootCategoryIsRejected()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore one = Store(s_a, "A");
            using InMemoryAliasNameStore two = Store(s_a, "A2");
            registry.Register(one);
            Assert.That(() => registry.Register(two),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async System.Threading.Tasks.Task DispatchBadNotImplementedWhenNoStoreOwnsCategoryAsync()
        {
            using var registry = new AliasNameStoreRegistry();
            (ServiceResult result, _) = await registry
                .DispatchFindAliasAsync(s_a, "%", NodeId.Null,
                    new TypeTable(new NamespaceTable()))
                .ConfigureAwait(false);
            Assert.That(result.StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNotImplemented));
        }

        [Test]
        public void UnregisterRemovesRouting()
        {
            using var registry = new AliasNameStoreRegistry();
            using InMemoryAliasNameStore storeA = Store(s_a, "A");
            registry.Register(storeA);
            Assert.That(registry.GetStoreForCategory(s_a), Is.SameAs(storeA));
            registry.Unregister(storeA);
            Assert.That(registry.GetStoreForCategory(s_a), Is.Null);
        }
    }
}
