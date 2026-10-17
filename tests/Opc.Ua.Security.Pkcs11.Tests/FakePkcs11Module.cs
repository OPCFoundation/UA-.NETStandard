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
                .Setup(s => s.CreateObject(It.IsAny<List<IObjectAttribute>>()))
                .Returns((List<IObjectAttribute> template) => CreateObject(template));

            session
                .Setup(s => s.DestroyObject(It.IsAny<IObjectHandle>()))
                .Callback((IObjectHandle handle) => DestroyObject(handle));

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
            byte[]? modulus = BytesOf(search, (ulong)CKA.CKA_MODULUS);
            byte[]? ecPoint = BytesOf(search, (ulong)CKA.CKA_EC_POINT);

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

                if (modulus != null && (candidate.Modulus == null || !modulus.SequenceEqual(candidate.Modulus)))
                {
                    continue;
                }

                if (ecPoint != null && (candidate.EcPoint == null || !ecPoint.SequenceEqual(candidate.EcPoint)))
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

                if (UnreadableAttributes.Contains(attribute))
                {
                    // As Pkcs11Interop: the value of an attribute the token
                    // reports as unreadable cannot be retrieved.
                    value.SetupGet(a => a.CannotBeRead).Returns(true);
                    value.Setup(a => a.GetValueAsByteArray())
                        .Throws(new InvalidOperationException("The attribute cannot be read."));
                }
                else if (attribute == (ulong)CKA.CKA_VALUE)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.Value);
                }
                else if (attribute == (ulong)CKA.CKA_ID)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.Id);
                }
                else if (attribute == (ulong)CKA.CKA_LABEL)
                {
                    value.Setup(a => a.GetValueAsString()).Returns(target.Label);
                }
                else if (attribute == (ulong)CKA.CKA_MODULUS)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.Modulus ?? []);
                }
                else if (attribute == (ulong)CKA.CKA_PUBLIC_EXPONENT)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.PublicExponent ?? []);
                }
                else if (attribute == (ulong)CKA.CKA_EC_POINT)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.EcPoint ?? []);
                }
                else if (attribute == (ulong)CKA.CKA_EC_PARAMS)
                {
                    value.Setup(a => a.GetValueAsByteArray()).Returns(target.EcParams ?? []);
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
            byte[] Value)
        {
            public byte[]? Modulus { get; init; }

            public byte[]? PublicExponent { get; init; }

            public byte[]? EcPoint { get; init; }

            public byte[]? EcParams { get; init; }

            public Dictionary<ulong, byte[]> Extra { get; init; } = [];
        }

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
            RSAParameters rsa = m_rsaKey.ExportParameters(false);
            ECParameters ecc = m_eccKey.ExportParameters(false);
            byte[] point = [0x04, .. ecc.Q.X!, .. ecc.Q.Y!];
            byte[] curve;
            using (Certificate eccCertificate = Certificate.FromRawData(EccCertificate))
            {
                curve = eccCertificate.PublicKey.EncodedParameters?.RawData ??
                    throw new InvalidOperationException("The ECC test certificate carries no curve parameters.");
            }

            m_objects.Add(new FakeObject(
                Handle(1), (ulong)CKO.CKO_CERTIFICATE, 0, RsaId, "rsa", RsaCertificate));
            m_objects.Add(new FakeObject(
                Handle(2), (ulong)CKO.CKO_PRIVATE_KEY, (ulong)CKK.CKK_RSA, RsaId, "rsa", [])
            {
                Modulus = rsa.Modulus,
                PublicExponent = rsa.Exponent
            });
            m_objects.Add(new FakeObject(
                Handle(3), (ulong)CKO.CKO_CERTIFICATE, 0, EccId, "ecc", EccCertificate));
            m_objects.Add(new FakeObject(
                Handle(4), (ulong)CKO.CKO_PRIVATE_KEY, (ulong)CKK.CKK_EC, EccId, "ecc", []));
            m_objects.Add(new FakeObject(
                Handle(5), (ulong)CKO.CKO_PUBLIC_KEY, (ulong)CKK.CKK_EC, EccId, "ecc", [])
            {
                // CKA_EC_POINT is the DER encoding of the ECPoint OCTET STRING.
                EcPoint = [0x04, (byte)point.Length, .. point],
                EcParams = curve
            });
            m_nextHandle = 6;
        }

        /// <summary>
        /// The number of objects of the given class the token holds.
        /// </summary>
        /// <param name="objectClass">The object class to count.</param>
        /// <returns>The number of objects.</returns>
        public int CountObjects(CKO objectClass)
        {
            return m_objects.Count(o => o.ObjectClass == (ulong)objectClass);
        }

        /// <summary>
        /// Attributes the token reports as unreadable (CKR_ATTRIBUTE_TYPE_INVALID
        /// or CKR_ATTRIBUTE_SENSITIVE) on every object.
        /// </summary>
        public HashSet<ulong> UnreadableAttributes { get; } = [];

        /// <summary>
        /// Replaces the curve (CKA_EC_PARAMS) of the EC public key object, so
        /// that the token holds the same point on another curve.
        /// </summary>
        /// <param name="ecParams">The DER encoded curve.</param>
        public void SetEccPublicKeyCurve(byte[] ecParams)
        {
            int index = m_objects.FindIndex(o =>
                o.ObjectClass == (ulong)CKO.CKO_PUBLIC_KEY && o.KeyType == (ulong)CKK.CKK_EC);
            m_objects[index] = m_objects[index] with { EcParams = ecParams };
        }

        /// <summary>
        /// The CKA_ID of the certificate object holding the given encoding.
        /// </summary>
        /// <param name="encoded">The DER encoded certificate.</param>
        /// <returns>The CKA_ID, or <c>null</c> when the token does not hold it.</returns>
        public byte[]? CertificateId(byte[] encoded)
        {
            return m_objects
                .FirstOrDefault(o =>
                    o.ObjectClass == (ulong)CKO.CKO_CERTIFICATE &&
                    o.Value.SequenceEqual(encoded))?
                .Id;
        }

        /// <summary>
        /// Replaces the CKA_ID of the certificate object holding the given encoding.
        /// </summary>
        /// <param name="encoded">The DER encoded certificate.</param>
        /// <param name="id">The new CKA_ID.</param>
        public void SetCertificateId(byte[] encoded, byte[] id)
        {
            int index = m_objects.FindIndex(o =>
                o.ObjectClass == (ulong)CKO.CKO_CERTIFICATE &&
                o.Value.SequenceEqual(encoded));
            m_objects[index] = m_objects[index] with { Id = id };
        }

        /// <summary>
        /// Replaces the CKA_ID of every object with the given label.
        /// </summary>
        /// <param name="label">The CKA_LABEL of the objects.</param>
        /// <param name="id">The new CKA_ID.</param>
        public void SetIdsOfLabel(string label, byte[] id)
        {
            for (int ii = 0; ii < m_objects.Count; ii++)
            {
                if (m_objects[ii].Label == label)
                {
                    m_objects[ii] = m_objects[ii] with { Id = id };
                }
            }
        }

        /// <summary>
        /// The value of an attribute of the certificate object holding the given encoding.
        /// </summary>
        /// <param name="encoded">The DER encoded certificate.</param>
        /// <param name="attribute">The attribute to read.</param>
        /// <returns>The value, or <c>null</c> when it was not written.</returns>
        public byte[]? CertificateAttribute(byte[] encoded, CKA attribute)
        {
            FakeObject? target = m_objects.FirstOrDefault(o =>
                o.ObjectClass == (ulong)CKO.CKO_CERTIFICATE &&
                o.Value.SequenceEqual(encoded));
            return target != null && target.Extra.TryGetValue((ulong)attribute, out byte[]? value)
                ? value
                : null;
        }

        /// <summary>
        /// Issues a new certificate for the RSA key the token holds, as a CA
        /// would when it renews the token's identity.
        /// </summary>
        /// <param name="subject">The subject of the new certificate.</param>
        /// <returns>The DER encoded certificate.</returns>
        public byte[] IssueRsaCertificate(string subject)
        {
            var request = new CertificateRequest(
                subject, m_rsaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            return certificate.RawData;
        }

        /// <summary>
        /// Issues a new certificate for the ECC key the token holds.
        /// </summary>
        /// <param name="subject">The subject of the new certificate.</param>
        /// <returns>The DER encoded certificate.</returns>
        public byte[] IssueEccCertificate(string subject)
        {
            var request = new CertificateRequest(subject, m_eccKey, HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            return certificate.RawData;
        }

        /// <summary>
        /// Issues a certificate whose RSA public key has the modulus of the RSA
        /// key the token holds but another public exponent, so it does not
        /// belong to that key.
        /// </summary>
        /// <param name="subject">The subject of the new certificate.</param>
        /// <returns>The DER encoded certificate.</returns>
        public byte[] IssueRsaCertificateWithForeignExponent(string subject)
        {
            RSAParameters parameters = m_rsaKey.ExportParameters(false);
            using var publicKey = RSA.Create();
            publicKey.ImportParameters(new RSAParameters
            {
                Modulus = parameters.Modulus,
                Exponent = [0x03]
            });
            using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(
                subject, publicKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.Create(
                new X500DistinguishedName("CN=ForeignExponentIssuer"),
                X509SignatureGenerator.CreateForECDsa(issuerKey),
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(2),
                [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);
            return certificate.RawData;
        }

        private IObjectHandle CreateObject(List<IObjectAttribute> template)
        {
            ulong objectClass = ValueOf(template, (ulong)CKA.CKA_CLASS) ??
                throw new Pkcs11Exception("CreateObject", CKR.CKR_TEMPLATE_INCOMPLETE);

            if (objectClass != (ulong)CKO.CKO_CERTIFICATE)
            {
                // The token only takes certificates from the store under test.
                throw new Pkcs11Exception("CreateObject", CKR.CKR_TEMPLATE_INCONSISTENT);
            }

            var extra = new Dictionary<ulong, byte[]>();
            foreach (IObjectAttribute attribute in template)
            {
                if (attribute.Type is (ulong)CKA.CKA_SUBJECT or
                    (ulong)CKA.CKA_ISSUER or
                    (ulong)CKA.CKA_SERIAL_NUMBER)
                {
                    extra[attribute.Type] = attribute.GetValueAsByteArray();
                }
            }

            IObjectHandle handle = Handle(m_nextHandle++);
            m_objects.Add(new FakeObject(
                handle,
                objectClass,
                0,
                BytesOf(template, (ulong)CKA.CKA_ID) ?? [],
                StringOf(template, (ulong)CKA.CKA_LABEL) ?? string.Empty,
                BytesOf(template, (ulong)CKA.CKA_VALUE) ??
                    throw new Pkcs11Exception("CreateObject", CKR.CKR_TEMPLATE_INCOMPLETE))
            {
                Extra = extra
            });
            CreatedObjects++;
            return handle;
        }

        private void DestroyObject(IObjectHandle handle)
        {
            if (m_objects.RemoveAll(o => o.Handle.ObjectId == handle.ObjectId) == 0)
            {
                throw new Pkcs11Exception("DestroyObject", CKR.CKR_OBJECT_HANDLE_INVALID);
            }

            DestroyedObjects++;
        }

        /// <summary>
        /// How many objects were created on the token.
        /// </summary>
        public int CreatedObjects { get; private set; }

        /// <summary>
        /// How many objects were destroyed on the token.
        /// </summary>
        public int DestroyedObjects { get; private set; }

        private ulong m_nextHandle;
    }
}
