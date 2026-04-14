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
using System.IO;
using System.Linq;
using System.Text;
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
        private static ServerCapabilities CreateTestCapabilities()
        {
            const string csv = "DA,Live Data\nAC,Alarms and Conditions\nHD,Historical Data\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);
            return capabilities;
        }

        [Test]
        public void ConstructorCreatesInstance()
        {
            var capabilities = new ServerCapabilities();
            Assert.That(capabilities, Is.Not.Null);
        }

        [Test]
        public void FindReturnsCapabilityById()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            ServerCapability result = capabilities.Find("DA");
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Id, Is.EqualTo("DA"));
            Assert.That(result.Description, Is.EqualTo("Live Data"));
        }

        [Test]
        public void FindReturnsNullForUnknownId()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            ServerCapability result = capabilities.Find("UNKNOWN_XYZ");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void FindReturnsNullForNullId()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            ServerCapability result = capabilities.Find(null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetEnumeratorEnumeratesCapabilities()
        {
            ServerCapabilities capabilities = CreateTestCapabilities();
            var list = new List<ServerCapability>();

            list.AddRange(capabilities);

            Assert.That(list, Has.Count.EqualTo(3));
            Assert.That(list.All(c => !string.IsNullOrEmpty(c.Id)), Is.True);
        }

        [Test]
        public void LoadFromCustomStream()
        {
            const string csv = "TEST,Test Capability\nFOO,Foo Capability\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);

            Assert.That(capabilities.Count(), Is.EqualTo(2));
            ServerCapability test = capabilities.Find("TEST");
            Assert.That(test, Is.Not.Null);
            Assert.That(test.Description, Is.EqualTo("Test Capability"));
        }

        [Test]
        public void LoadFromEmptyStreamProducesEmptyList()
        {
            using var stream = new MemoryStream([]);
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);

            Assert.That(capabilities.Count(), Is.Zero);
        }

        [Test]
        public void LoadSkipsLinesWithoutComma()
        {
            const string csv = "GOOD,Good Description\nBADLINE\nALSO_GOOD,Also Good\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var capabilities = new ServerCapabilities();
            capabilities.Load(stream);

            Assert.That(capabilities.Count(), Is.EqualTo(2));
            Assert.That(capabilities.Find("BADLINE"), Is.Null);
        }
    }
}
