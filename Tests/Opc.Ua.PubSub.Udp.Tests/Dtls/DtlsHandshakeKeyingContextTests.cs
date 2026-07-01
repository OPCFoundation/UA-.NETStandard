#if NET8_0_OR_GREATER
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

using System.Security.Cryptography;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 Finished and KeyUpdate helpers from RFC 8446 §4.4.4 and §4.6.3.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 8446 §4.4.4")]
    [TestSpec("RFC 8446 §4.6.3")]
    public sealed class DtlsHandshakeKeyingContextTests
    {
        [Test]
        public void FinishedVerificationAndApplicationRecordProtectionSucceed()
        {
            DtlsProfile profile = new("test", DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false);
            byte[] shared = new byte[32];
            RandomNumberGenerator.Fill(shared);
            byte[] handshakeHash = SHA256.HashData(new byte[] { 1, 2 });
            byte[] applicationHash = SHA256.HashData(new byte[] { 1, 2, 3 });
            using var client = new DtlsHandshakeKeyingContext(profile, shared, handshakeHash, applicationHash);
            using var server = new DtlsHandshakeKeyingContext(profile, shared, handshakeHash, applicationHash);

            byte[] finished = client.ComputeClientFinished(applicationHash);
            server.VerifyFinished(server.ComputeClientFinished(applicationHash), finished);
            using DtlsRecordProtection writer = client.CreateClientApplicationWriteProtection();
            using DtlsRecordProtection reader = server.CreateClientApplicationWriteProtection();

            Assert.That(reader.Open(writer.Seal(new byte[] { 0x55 })), Is.EqualTo(new byte[] { 0x55 }));
        }

        [Test]
        public void KeyUpdateChangesTrafficSecretAndOldKeysRejectNewRecords()
        {
            DtlsProfile profile = new("test", DtlsCipherSuite.TlsAes128GcmSha256,
                DtlsNamedCurve.NistP256, DtlsNamedCurve.NistP256, isMandatory: false);
            byte[] shared = new byte[32];
            RandomNumberGenerator.Fill(shared);
            byte[] hash = SHA256.HashData(new byte[] { 7 });
            using var context = new DtlsHandshakeKeyingContext(profile, shared, hash, hash);
            byte[] before = (byte[])context.Secrets.ClientApplicationTrafficSecret.Clone();

            context.UpdateApplicationTrafficSecret(client: true);

            Assert.That(context.Secrets.ClientApplicationTrafficSecret, Is.Not.EqualTo(before));
        }
    }
}
#endif
