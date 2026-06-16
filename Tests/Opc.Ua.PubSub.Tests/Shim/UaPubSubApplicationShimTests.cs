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
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Tests;

#pragma warning disable UA0023 // Tests intentionally reference the legacy types under test.
#pragma warning disable CS0618 // Same: legacy types are obsolete.

namespace Opc.Ua.PubSub.Tests.Shim
{
    /// <summary>
    /// Verifies the [Obsolete] markers wired onto the legacy 1.04
    /// PubSub top-level types so consumers see UA0023 migration
    /// diagnostics. The legacy implementations themselves are
    /// unchanged in Phase 9.
    /// </summary>
    [TestFixture]
    public class UaPubSubApplicationShimTests
    {
        [Test]
        public void UaPubSubApplication_IsMarkedObsolete()
        {
            AssertObsolete(typeof(UaPubSubApplication));
        }

        [Test]
        public void IUaPubSubConnection_IsMarkedObsolete()
        {
            AssertObsolete(typeof(IUaPubSubConnection));
        }

        [Test]
        public void IUaPublisher_IsMarkedObsolete()
        {
            AssertObsolete(typeof(IUaPublisher));
        }

        [Test]
        public void IUaPubSubDataStore_IsMarkedObsolete()
        {
            AssertObsolete(typeof(IUaPubSubDataStore));
        }

        [Test]
        public void UaPubSubDataStore_IsMarkedObsolete()
        {
            AssertObsolete(typeof(UaPubSubDataStore));
        }

        [Test]
        public void UaPubSubConfigurator_IsMarkedObsolete()
        {
            AssertObsolete(typeof(Opc.Ua.PubSub.Configuration.UaPubSubConfigurator));
        }

        [Test]
        public void UaPubSubDataStore_ReadWrite_RoundTrip()
        {
            var store = new UaPubSubDataStore();
            var nodeId = new NodeId(42);
            var value = new DataValue(new Variant(123));
            store.WritePublishedDataItem(nodeId, Attributes.Value, value);
            Assert.That(
                store.TryReadPublishedDataItem(nodeId, Attributes.Value, out DataValue read),
                Is.True);
            Assert.That(read.WrappedValue.TryGetValue(out int v), Is.True);
            Assert.That(v, Is.EqualTo(123));
        }

        private static void AssertObsolete(Type type)
        {
            ObsoleteAttribute? obsolete = type
                .GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false)
                .OfType<ObsoleteAttribute>()
                .FirstOrDefault();
            Assert.That(obsolete, Is.Not.Null);
            Assert.That(
                obsolete!.Message,
                Does.Contain("Docs/migrate/2.0.x/pubsub.md"));
        }
    }
}
