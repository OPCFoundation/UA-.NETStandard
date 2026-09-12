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

#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("EncryptedSecret")]
    public sealed class EccSecretBufferLifetimeRegressionTests
    {
        [Test]
        public async Task DecryptClearsOwnedPayloadAndKeysWithoutErasingHeadersOrReturnedSecretAsync(
            [Values(false, true)] bool p384,
            [Values(0, 17)] int offset,
            [Values("success", "nonce", "padding", "cipher")] string outcome)
        {
            ECCurve curve = p384 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256;
            string policy = p384 ? SecurityPolicies.ECC_nistP384 : SecurityPolicies.ECC_nistP256;
            using Certificate sender = CertificateBuilder.Create("CN=Buffer Sender").SetECCurve(curve).CreateForECDsa();
            using Certificate receiver = CertificateBuilder.Create("CN=Buffer Receiver").SetECCurve(curve).CreateForECDsa();
            using Nonce senderNonce = Nonce.CreateNonce(policy);
            using Nonce receiverNonce = Nonce.CreateNonce(policy);
            using var issuers = new CertificateCollection();
            ServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            EncryptedSecret encryptor = EncryptedSecret.CreateForEcc(
                context, policy, issuers, receiver, receiverNonce, sender, senderNonce,
                doNotEncodeSenderCertificate: true);
            byte[] encoded = encryptor.Encrypt(s_secret, s_nonce);
            byte[] buffer = Enumerable.Repeat((byte)0x7A, offset + encoded.Length + 13).ToArray();
            encoded.CopyTo(buffer, offset);
            byte[]? key = null;
            byte[]? iv = null;
            byte[]? prefix = null;
            byte[]? suffix = null;
            ArraySegment<byte> working = default;
            var decryptor = new EncryptedSecret(
                context, policy, issuers, receiver, receiverNonce, sender, null, null, false,
                (data, security, encryptionKey, initializationVector) =>
                {
                    key = encryptionKey;
                    iv = initializationVector;
                    working = data;
                    prefix = data.Array!.AsSpan(0, data.Offset).ToArray();
                    suffix = data.Array.AsSpan(data.Offset + data.Count).ToArray();
                    if (outcome == "cipher")
                    {
                        throw new CryptographicException("controlled cipher failure");
                    }
                    ArraySegment<byte> plaintext = CryptoUtils.SymmetricDecryptAndVerify(
                        data, security, encryptionKey, initializationVector);
                    if (outcome == "padding")
                    {
                        plaintext.Array![plaintext.Offset + plaintext.Count - 1] = 1;
                    }
                    return plaintext;
                });
            try
            {
                if (outcome == "success")
                {
                    byte[] result = await decryptor.DecryptAsync(
                        DateTime.UtcNow.AddHours(-1), s_nonce, buffer, offset, encoded.Length, context.Telemetry)
                        .ConfigureAwait(false);
                    Assert.That(result, Is.EqualTo(s_secret));
                    CryptoUtils.ZeroMemory(result);
                }
                else if (outcome == "cipher")
                {
                    Assert.ThrowsAsync<CryptographicException>(async () => await decryptor.DecryptAsync(
                        DateTime.UtcNow.AddHours(-1), s_nonce, buffer, offset, encoded.Length, context.Telemetry)
                        .ConfigureAwait(false));
                }
                else
                {
                    ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                        async () => await decryptor.DecryptAsync(
                            DateTime.UtcNow.AddHours(-1), outcome == "nonce" ? s_wrongNonce : s_nonce,
                            buffer, offset, encoded.Length, context.Telemetry).ConfigureAwait(false))!;
                    Assert.That(exception.StatusCode,
                        Is.EqualTo(outcome == "nonce" ? StatusCodes.BadNonceInvalid : StatusCodes.BadDecodingError));
                }
                Assert.That(key, Is.Not.Null);
                Assert.That(iv, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(IsCleared(key), Is.True);
                    Assert.That(IsCleared(iv), Is.True);
                    Assert.That(IsCleared(buffer.AsSpan(working.Offset, working.Count)), Is.True);
                    Assert.That(buffer.AsSpan(0, working.Offset).ToArray(), Is.EqualTo(prefix));
                    Assert.That(buffer.AsSpan(working.Offset + working.Count).ToArray(), Is.EqualTo(suffix));
                });
            }
            finally
            {
                decryptor.SenderNonce?.Dispose();
                CryptoUtils.ZeroMemory(buffer);
                if (key != null)
                {
                    CryptoUtils.ZeroMemory(key);
                }
                if (iv != null)
                {
                    CryptoUtils.ZeroMemory(iv);
                }
            }
        }

        private static bool IsCleared(ReadOnlySpan<byte> value)
        {
            foreach (byte item in value)
            {
                if (item != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static readonly byte[] s_secret = [1, 5, 9, 13, 17, 21];
        private static readonly byte[] s_nonce = [2, 4, 6, 8];
        private static readonly byte[] s_wrongNonce = [2, 4, 6, 9];
    }
}
