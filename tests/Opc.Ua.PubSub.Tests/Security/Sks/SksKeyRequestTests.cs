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
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="SksKeyRequest"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.2")]
    public class SksKeyRequestTests
    {
        [Test]
        public void Constructor_RecordsAllFields()
        {
            var request = new SksKeyRequest("group-1", 5U, 3U);
            Assert.That(request.SecurityGroupId, Is.EqualTo("group-1"));
            Assert.That(request.StartingTokenId, Is.EqualTo(5U));
            Assert.That(request.RequestedKeyCount, Is.EqualTo(3U));
        }

        [Test]
        public void Equality_TreatsRequestsWithSameFieldsAsEqual()
        {
            var a = new SksKeyRequest("g", 1U, 2U);
            var b = new SksKeyRequest("g", 1U, 2U);
            var c = new SksKeyRequest("g", 1U, 3U);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void Defaults_AreZeroValuedRecord()
        {
            SksKeyRequest empty = default;
            Assert.That(empty.SecurityGroupId, Is.Null);
            Assert.That(empty.StartingTokenId, Is.Zero);
            Assert.That(empty.RequestedKeyCount, Is.Zero);
        }
    }
}
