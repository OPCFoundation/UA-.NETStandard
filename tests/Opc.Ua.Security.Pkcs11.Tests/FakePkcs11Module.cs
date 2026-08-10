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
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moq;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// A PKCS#11 module that behaves like a token but needs no device.
    /// </summary>
    /// <remarks>
    /// The operations are backed by a real software key rather than by canned
    /// bytes, which is the point: a signature produced through
    /// <see cref="Pkcs11Rsa"/> has to verify against the certificate's public
    /// key, so a wrong DigestInfo prefix, a mismatched MGF1 or the wrong salt
    /// length makes the test fail rather than pass vacuously. That is exactly
    /// the translation logic that otherwise fails silently - a bad prefix
    /// produces a well formed signature attesting to a different algorithm.
    /// <para>
    /// This does not replace the SoftHSM tests. These prove the translation, and
    /// SoftHSM proves the device path; neither substitutes for the other.
    /// </para>
    /// </remarks>
    internal sealed class FakePkcs11Module : IPkcs11LibraryLoader, IDisposable
    {
        public const string DefaultTokenLabel = "fake-token";
        public const string DefaultSerial = "0123456789";
        public const ulong DefaultSlotId = 7;

        /// <summary>
        /// Initializes a module holding one RSA and one ECC identity.
        /// </summary>
        /// <param name="tokenLabel">The label the token reports.</param>
        /// <param name="serial">The serial number the token reports.</param>
        /// <param name="slotId">The slot the token sits in.</param>
        public FakePkcs11Module(
            string tokenLabel = DefaultTokenLabel,
            string serial = DefaultSerial,
            ulong slotId = DefaultSlotId)
        {
            // .NET Framework's default RSA rejects PSS and OAEP with SHA-2, so a
            // device backed by it could not answer the modern policies. CNG can.
#if NETFRAMEWORK
            m_rsaKey = new RSACng();
            m_rsaKey.KeySize = 2048;
#else
            m_rsaKey = RSA.Create(2048);
#endif
            m_eccKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            RsaCertificate = CreateRsaCertificate(m_rsaKey);
            EccCertificate = CreateEccCertificate(m_eccKey);

            TokenLabel = tokenLabel;
            Serial = serial;
            SlotId = slotId;

            Populate();

            m_library = BuildLibrary();
        }

        public string TokenLabel { get; }

        public string Serial { get; }

        public ulong SlotId { get; }

        /// <summary>
        /// The DER encoded RSA certificate the token holds.
        /// </summary>
        public byte[] RsaCertificate { get; }

        /// <summary>
        /// The DER encoded ECC certificate the token holds.
        /// </summary>
        public byte[] EccCertificate { get; }

        /// <summary>
        /// The CKA_ID of the RSA objects.
        /// </summary>
        public byte[] RsaId { get; } = [0x01];

        /// <summary>
        /// The CKA_ID of the ECC objects.
        /// </summary>
        public byte[] EccId { get; } = [0x02];

        /// <summary>
        /// How many times a session was logged in.
        /// </summary>
        public int Logins { get; private set; }

        /// <summary>
        /// How many times the session was logged out.
        /// </summary>
        public int Logouts { get; private set; }

        /// <summary>
        /// Whether the loaded library was disposed.
        /// </summary>
        public bool LibraryDisposed { get; private set; }

        /// <summary>
        /// Whether the session was disposed.
        /// </summary>
        public bool SessionDisposed { get; private set; }

        /// <summary>
        /// Set to make <see cref="ISession.Logout"/> throw, which the token has
        /// to tolerate because a removed device does exactly that.
        /// </summary>
        public bool ThrowOnLogout { get; set; }

        /// <summary>
        /// Set to report no slots, so slot selection has nothing to match.
        /// </summary>
        public bool NoSlots { get; set; }

        /// <inheritdoc/>
        public IPkcs11Library Load(Pkcs11InteropFactories factories, string modulePath)
        {
            LoadedModulePath = modulePath;
            return m_library;
        }

        /// <summary>
        /// The module path the token asked for.
        /// </summary>
        public string? LoadedModulePath { get; private set; }

        /// <summary>
        /// Opens a token over this module.
        /// </summary>
        /// <param name="options">
        /// The options to use, or <c>null</c> for ones that match this token.
        /// </param>
        /// <returns>The token.</returns>
        public Pkcs11Token OpenToken(Pkcs11TokenOptions? options = null)
        {
            return new Pkcs11Token(options ?? CreateOptions(), this);
        }

        /// <summary>
        /// Options that select this token.
        /// </summary>
        /// <param name="pin">The PIN, or <c>null</c> for a public session.</param>
        /// <returns>The options.</returns>
        public Pkcs11TokenOptions CreateOptions(string? pin = "1234")
        {
            return new Pkcs11TokenOptions
            {
                ModulePath = "/fake/module.so",
                TokenLabel = TokenLabel,
                Pin = pin
            };
        }

        public void Dispose()
        {
            m_rsaKey.Dispose();
            m_eccKey.Dispose();
        }

        private IPkcs11Library BuildLibrary()
        {
            Mock<ISession> session = BuildSession();

            var tokenInfo = new Mock<ITokenInfo>();
            tokenInfo.SetupGet(t => t.Label).Returns(() => TokenLabel);
            tokenInfo.SetupGet(t => t.SerialNumber).Returns(() => Serial);

            var slot = new Mock<ISlot>();
            slot.SetupGet(s => s.SlotId).Returns(() => SlotId);
            slot.Setup(s => s.GetTokenInfo()).Returns(tokenInfo.Object);
            slot.Setup(s => s.OpenSession(It.IsAny<SessionType>())).Returns(session.Object);

            var library = new Mock<IPkcs11Library>();
            library
                .Setup(l => l.GetSlotList(It.IsAny<SlotsType>()))
                .Returns(() => NoSlots ? [] : [slot.Object]);
            library.Setup(l => l.Dispose()).Callback(() => LibraryDisposed = true);

            return library.Object;
        }

        private Mock<ISession> BuildSession()
        {
            var session = new Mock<ISession>();

            session
                .Setup(s => s.Login(It.IsAny<CKU>(), It.IsAny<string>()))
                .Callback(() => Logins++);

            session
                .Setup(s => s.Logout())
                .Callback(() =>
                {
                    if (ThrowOnLogout)
                    {
                        throw new Pkcs11Exception("Logout", CKR.CKR_DEVICE_REMOVED);
                    }

                    Logouts++;
                });

            session.Setup(s => s.Dispose()).Callback(() => SessionDisposed = true);

            session
                .Setup(s => s.FindAllObjects(It.IsAny<List<IObjectAttribute>>()))
                .Returns((List<IObjectAttribute> search) => FindObjects(search));

            session
                .Setup(s => s.GetAttributeValue(
                    It.IsAny<IObjectHandle>(), It.IsAny<List<ulong>>()))
                .Returns((IObjectHandle handle, List<ulong> attributes) =>
                    GetAttributes(handle, attributes));

            session
                .Setup(s => s.Sign(
                    It.IsAny<IMechanism>(), It.IsAny<IObjectHandle>(), It.IsAny<byte[]>()))
                .Returns((IMechanism mechanism, IObjectHandle key, byte[] data) =>
                    Sign(mechanism, key, data));

            session
                .Setup(s => s.Decrypt(
                    It.IsAny<IMechanism>(), It.IsAny<IObjectHandle>(), It.IsAny<byte[]>()))
                .Returns((IMechanism mechanism, IObjectHandle key, byte[] data) =>
                    Decrypt(mechanism, data));

            return session;
        }

        private List<IObjectHandle> FindObjects(List<IObjectAttribute> search)
        {
            ulong objectClass = ValueOf(search, (ulong)CKA.CKA_CLASS) ?? 0;
            ulong? keyType = ValueOf(search, (ulong)CKA.CKA_KEY_TYPE);
            byte[]? id = BytesOf(search, (ulong)CKA.CKA_ID);
            string? label = StringOf(search, (ulong)CKA.CKA_LABEL);

            var found = new List<IObjectHandle>();

            foreach (FakeObject candidate in m_objects)
            {
                if (candidate.ObjectClass != objectClass)
                {
                    continue;
                }

                if (keyType.HasValue && candidate.KeyType != keyType.Value)
                {
                    continue;
                }

                if (id != null && !id.SequenceEqual(candidate.Id))
                {
                    continue;
                }

                if (label != null && label != candidate.Label)
                {
                    continue;
                }

                found.Add(candidate.Handle);
            }

            return found;
        }

        private List<IObjectAttribute> GetAttributes(IObjectHandle handle, List<ulong> attributes)
        {
            FakeObject target = m_objects.First(o => o.Handle.ObjectId == handle.ObjectId);

            var values = new List<IObjectAttribute>(attributes.Count);

            foreach (ulong attribute in attributes)
            {
                var value = new Mock<IObjectAttribute>();

                if (attribute == (ulong)CKA.CKA_VALUE)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.Value);
                }
                else if (attribute == (ulong)CKA.CKA_ID)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.Id);
                }
                else
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns([]);
                }

                values.Add(value.Object);
            }

            return values;
        }

        /// <summary>
        /// Performs the signature the mechanism describes with a real key.
        /// </summary>
        /// <remarks>
        /// The mechanism is honoured rather than ignored, so the caller's
        /// translation has to be right for the result to verify.
        /// </remarks>
        private byte[] Sign(IMechanism mechanism, IObjectHandle key, byte[] data)
        {
            FakeObject target = m_objects.First(o => o.Handle.ObjectId == key.ObjectId);

            if (mechanism.Type == (ulong)CKM.CKM_ECDSA)
            {
                Assert(target.KeyType == (ulong)CKK.CKK_EC, "an EC mechanism needs an EC key");
                return m_eccKey.SignHash(data);
            }

            Assert(target.KeyType == (ulong)CKK.CKK_RSA, "an RSA mechanism needs an RSA key");

            if (mechanism.Type == (ulong)CKM.CKM_RSA_PKCS)
            {
                // CKM_RSA_PKCS signs whatever it is handed, so the caller must
                // have wrapped the hash in a DigestInfo. Unwrapping it here is
                // what makes a wrong prefix visible.
                (byte[] hash, HashAlgorithmName algorithm) = UnwrapDigestInfo(data);
                return m_rsaKey.SignHash(hash, algorithm, RSASignaturePadding.Pkcs1);
            }

            if (mechanism.Type == (ulong)CKM.CKM_RSA_PKCS_PSS)
            {
                HashAlgorithmName algorithm = data.Length switch
                {
                    32 => HashAlgorithmName.SHA256,
                    48 => HashAlgorithmName.SHA384,
                    64 => HashAlgorithmName.SHA512,
                    _ => throw new Pkcs11Exception("Sign", CKR.CKR_DATA_LEN_RANGE)
                };

                return m_rsaKey.SignHash(data, algorithm, RSASignaturePadding.Pss);
            }

            throw new Pkcs11Exception("Sign", CKR.CKR_MECHANISM_INVALID);
        }

        private byte[] Decrypt(IMechanism mechanism, byte[] data)
        {
            if (mechanism.Type == (ulong)CKM.CKM_RSA_PKCS)
            {
                return m_rsaKey.Decrypt(data, RSAEncryptionPadding.Pkcs1);
            }

            if (mechanism.Type == (ulong)CKM.CKM_RSA_PKCS_OAEP)
            {
                // SoftHSM only accepts SHA-1 here; a capable device accepts more.
                foreach (RSAEncryptionPadding padding in s_oaepPaddings)
                {
                    try
                    {
                        return m_rsaKey.Decrypt(data, padding);
                    }
                    catch (CryptographicException)
                    {
                        // Try the next hash.
                    }
                }

                throw new Pkcs11Exception("Decrypt", CKR.CKR_ARGUMENTS_BAD);
            }

            throw new Pkcs11Exception("Decrypt", CKR.CKR_MECHANISM_INVALID);
        }

        private static (byte[] Hash, HashAlgorithmName Algorithm) UnwrapDigestInfo(byte[] digestInfo)
        {
            foreach ((byte[] prefix, HashAlgorithmName algorithm, int length) in s_digestInfos)
            {
                if (digestInfo.Length != prefix.Length + length)
                {
                    continue;
                }

                if (!digestInfo.Take(prefix.Length).SequenceEqual(prefix))
                {
                    continue;
                }

                return (digestInfo.Skip(prefix.Length).ToArray(), algorithm);
            }

            throw new Pkcs11Exception("Sign", CKR.CKR_DATA_INVALID);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Pkcs11Exception(message, CKR.CKR_KEY_TYPE_INCONSISTENT);
            }
        }

        private static ulong? ValueOf(List<IObjectAttribute> search, ulong type)
        {
            IObjectAttribute? match = search.FirstOrDefault(a => a.Type == type);
            return match == null ? null : match.GetValueAsUlong();
        }

        private static byte[]? BytesOf(List<IObjectAttribute> search, ulong type)
        {
            IObjectAttribute? match = search.FirstOrDefault(a => a.Type == type);
            return match?.GetValueAsByteArray();
        }

        private static string? StringOf(List<IObjectAttribute> search, ulong type)
        {
            IObjectAttribute? match = search.FirstOrDefault(a => a.Type == type);
            return match?.GetValueAsString();
        }

        private static byte[] CreateRsaCertificate(RSA key)
        {
            var request = new CertificateRequest(
                "CN=FakeTokenRsa", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return certificate.RawData;
        }

        private static byte[] CreateEccCertificate(ECDsa key)
        {
            var request = new CertificateRequest("CN=FakeTokenEcc", key, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
            return certificate.RawData;
        }

        private sealed record FakeObject(
            IObjectHandle Handle,
            ulong ObjectClass,
            ulong KeyType,
            byte[] Id,
            string Label,
            byte[] Value);

        private static IObjectHandle Handle(ulong id)
        {
            var handle = new Mock<IObjectHandle>();
            handle.SetupGet(h => h.ObjectId).Returns(id);
            return handle.Object;
        }

        private static readonly RSAEncryptionPadding[] s_oaepPaddings =
        [
            RSAEncryptionPadding.OaepSHA256,
            RSAEncryptionPadding.OaepSHA384,
            RSAEncryptionPadding.OaepSHA512,
            RSAEncryptionPadding.OaepSHA1
        ];

        private static readonly (byte[] Prefix, HashAlgorithmName Algorithm, int Length)[] s_digestInfos =
        [
            ([0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65,
              0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20], HashAlgorithmName.SHA256, 32),
            ([0x30, 0x41, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65,
              0x03, 0x04, 0x02, 0x02, 0x05, 0x00, 0x04, 0x30], HashAlgorithmName.SHA384, 48),
            ([0x30, 0x51, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65,
              0x03, 0x04, 0x02, 0x03, 0x05, 0x00, 0x04, 0x40], HashAlgorithmName.SHA512, 64)
        ];

        private readonly RSA m_rsaKey;
        private readonly ECDsa m_eccKey;

        // CA2213: the mocked library is disposed by the token that loads it -
        // asserting that it is disposed is one of the things these tests check.
        // Disposing it here as well would make that assertion meaningless.
#pragma warning disable CA2213
        private readonly IPkcs11Library m_library;
#pragma warning restore CA2213

        private readonly List<FakeObject> m_objects = [];

        /// <summary>
        /// Populates the object list the session searches.
        /// </summary>
        private void Populate()
        {
            m_objects.Add(new FakeObject(
                Handle(1), (ulong)CKO.CKO_CERTIFICATE, 0, RsaId, "rsa", RsaCertificate));
            m_objects.Add(new FakeObject(
                Handle(2), (ulong)CKO.CKO_PRIVATE_KEY, (ulong)CKK.CKK_RSA, RsaId, "rsa", []));
            m_objects.Add(new FakeObject(
                Handle(3), (ulong)CKO.CKO_CERTIFICATE, 0, EccId, "ecc", EccCertificate));
            m_objects.Add(new FakeObject(
                Handle(4), (ulong)CKO.CKO_PRIVATE_KEY, (ulong)CKK.CKK_EC, EccId, "ecc", []));
        }
    }
}
