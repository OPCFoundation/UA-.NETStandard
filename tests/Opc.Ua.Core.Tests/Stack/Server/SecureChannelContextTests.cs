// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
