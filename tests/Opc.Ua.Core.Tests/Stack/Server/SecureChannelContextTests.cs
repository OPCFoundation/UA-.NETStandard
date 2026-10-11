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

namespace Opc.Ua.Core.Tests.Stack.Server
{
    /// <summary>
    /// Tests for the read-only channel evidence carried by <see cref="SecureChannelContext"/>.
    /// </summary>
    [TestFixture]
    [Category("SecureChannelContext")]
    [Parallelizable]
    public class SecureChannelContextTests
    {
        /// <summary>
        /// Missing evidence stays null rather than becoming empty: the session signature
        /// helpers hash an empty certificate but skip a missing one.
        /// </summary>
        [Test]
        public void MissingEvidenceIsNullNotEmpty()
        {
            var context = new SecureChannelContext("1", null, RequestEncoding.Binary);

            Assert.That(context.ClientChannelCertificate.IsNull, Is.True);
            Assert.That(context.ServerChannelCertificate.IsNull, Is.True);
            Assert.That(context.ChannelThumbprint.IsNull, Is.True);
        }

        /// <summary>
        /// Empty evidence stays empty and is not reported as null.
        /// </summary>
        [Test]
        public void EmptyEvidenceIsNotNull()
        {
            var context = new SecureChannelContext("1", null, RequestEncoding.Binary, [], [], []);

            Assert.That(context.ClientChannelCertificate.IsNull, Is.False);
            Assert.That(context.ClientChannelCertificate.IsEmpty, Is.True);
        }

        /// <summary>
        /// The evidence is a view of the transport's buffer, so contexts created for the
        /// requests of one channel share it without copying it.
        /// </summary>
        [Test]
        public void EvidenceWrapsTheTransportBufferWithoutCopying()
        {
            byte[] certificate = [1, 2, 3, 4];

            var first = new SecureChannelContext("1", null, RequestEncoding.Binary, certificate);
            var second = new SecureChannelContext("1", null, RequestEncoding.Binary, certificate);

            Assert.That(first.ClientChannelCertificate.Span.ToArray(), Is.EqualTo(certificate));
            Assert.That(
                first.ClientChannelCertificate.Span.Overlaps(second.ClientChannelCertificate.Span),
                Is.True);
            Assert.That(first.ClientChannelCertificate, Is.EqualTo(second.ClientChannelCertificate));
        }
    }
}
