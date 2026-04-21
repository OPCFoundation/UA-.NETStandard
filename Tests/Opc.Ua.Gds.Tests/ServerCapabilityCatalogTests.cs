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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture]
    [Category("GDS")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ServerCapabilitiesTests
    {
        [Test]
        public void ConstructorCreatesInstance()
        {
            var capabilities = new ServerCapabilityCatalog();
            Assert.That(capabilities, Is.Not.Null);
        }

        [Test]
        public void ConstructorPopulatesFromGeneratedCatalog()
        {
            var capabilities = new ServerCapabilityCatalog();
            int count = capabilities.Count();
            Assert.That(count, Is.EqualTo(ServerCapabilities.All.Count));
            Assert.That(count, Is.GreaterThan(0));
        }

        [Test]
        public void FindReturnsCapabilityById()
        {
            var capabilities = new ServerCapabilityCatalog();
            ServerCapability result = capabilities.Find(ServerCapabilities.DA);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo("DA"));
            Assert.That(result.Description, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void FindReturnsCapabilityForGdsAndLds()
        {
            var capabilities = new ServerCapabilityCatalog();
            Assert.That(capabilities.Find(ServerCapabilities.GDS), Is.Not.Null);
            Assert.That(capabilities.Find(ServerCapabilities.LDS), Is.Not.Null);
        }

        [Test]
        public void FindReturnsNullForUnknownId()
        {
            var capabilities = new ServerCapabilityCatalog();
            ServerCapability result = capabilities.Find("UNKNOWN_XYZ");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindReturnsNullForNullId()
        {
            var capabilities = new ServerCapabilityCatalog();
            ServerCapability result = capabilities.Find(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetEnumeratorEnumeratesCapabilities()
        {
            var capabilities = new ServerCapabilityCatalog();
            var list = new List<ServerCapability>();

            list.AddRange(capabilities);

            Assert.That(list, Has.Count.EqualTo(ServerCapabilities.All.Count));
            Assert.That(list.All(c => !string.IsNullOrEmpty(c.Id)), Is.True);
        }
    }
}
