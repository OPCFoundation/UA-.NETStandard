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
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Default <see cref="IPushCertificateKeyGenerator"/> that genuinely
    /// incorporates the caller-supplied additional entropy (the §7.10.10
    /// <c>Nonce</c>) into the generated private key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NIST SP&#160;800-90A HMAC&#8209;DRBG (over HMAC&#8209;SHA256) is
    /// instantiated from a fresh server-side cryptographic random seed
    /// concatenated with the caller nonce, and the private-key material is
    /// derived exclusively from that DRBG. Because the server seed is always
    /// present, a weak or adversarial nonce can never <i>weaken</i> the key;
    /// a strong nonce genuinely <i>adds</i> entropy, exactly as §7.10.10
    /// intends.
    /// </para>
    /// <para>
    /// For RSA certificate types the key is generated from managed
    /// <see cref="BigInteger"/> prime generation so the DRBG output fully
    /// determines the key on every supported target framework. For ECC
    /// certificate types the DRBG derives the private scalar and the platform
    /// computes the public point; on runtimes that cannot import a
    /// private-only EC scalar (.NET Framework / netstandard2.1) genuine
    /// additional-entropy incorporation is unavailable, so an ECC
    /// regenerate-key request fails with <see cref="StatusCodes.BadNotSupported"/>
    /// rather than silently generating a key that ignores the caller nonce —
    /// this limitation is documented in <c>Docs/CertificateManager.md</c>.
    /// RSA regenerate-key requests remain nonce-derived on every framework.
    /// </para>
    /// </remarks>
    public sealed class AdditionalEntropyCertificateKeyGenerator : IPushCertificateKeyGenerator
    {
        /// <summary>
        /// Creates the generator using the shared default certificate factory.
        /// </summary>
        /// <param name="certificateFactory">
        /// The factory used to assemble the certificate structure and
        /// extensions. When <see langword="null"/>,
        /// <see cref="DefaultCertificateFactory.Instance"/> is used.
        /// </param>
        public AdditionalEntropyCertificateKeyGenerator(ICertificateFactory? certificateFactory = null)
            : this(certificateFactory ?? DefaultCertificateFactory.Instance, CreateServerEntropy)
        {
        }

        /// <summary>
        /// Test-only constructor that injects a deterministic server-entropy
        /// source so the genuine incorporation of the nonce into the private
        /// key can be verified reproducibly.
        /// </summary>
        internal AdditionalEntropyCertificateKeyGenerator(
            ICertificateFactory certificateFactory,
            Func<int, byte[]> serverEntropySource)
        {
            m_certificateFactory = certificateFactory ??
                throw new ArgumentNullException(nameof(certificateFactory));
            m_serverEntropySource = serverEntropySource ??
                throw new ArgumentNullException(nameof(serverEntropySource));
        }

        /// <inheritdoc/>
        public Certificate CreateApplicationCertificate(
            PushCertificateKeyGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            byte[] serverEntropy = m_serverEntropySource(kServerSeedBytes);
            byte[] nonce = request.AdditionalEntropy.IsNull
                ? []
                : request.AdditionalEntropy.ToArray();

            using var drbg = new HmacDrbg(serverEntropy, nonce, s_personalization);
            Array.Clear(serverEntropy, 0, serverEntropy.Length);
            Array.Clear(nonce, 0, nonce.Length);

            if (IsRsaCertificateType(request.CertificateTypeId))
            {
                return CreateRsaCertificate(request, drbg, cancellationToken);
            }

            return CreateEccCertificate(request, drbg, cancellationToken);
        }

        private Certificate CreateRsaCertificate(
            PushCertificateKeyGenerationRequest request,
            HmacDrbg drbg,
            CancellationToken cancellationToken)
        {
            int keySize = request.KeySizeInBits == 0
                ? CertificateFactory.DefaultKeySize
                : request.KeySizeInBits;

            RSAParameters parameters = GenerateRsaParameters(keySize, drbg, cancellationToken);
            using RSA rsa = RSA.Create();
            try
            {
                rsa.ImportParameters(parameters);
            }
            finally
            {
                ClearRsaParameters(ref parameters);
            }

            // Build a self-signed certificate that carries the standard OPC UA
            // application extensions and the generated public key, signed by
            // the generated private key.
            using Certificate publicOnly = m_certificateFactory
                .CreateApplicationCertificate(
                    request.ApplicationUri,
                    request.ApplicationName,
                    request.SubjectName,
                    DomainNamesOrNull(request.DomainNames))
                .SetNotBefore(request.NotBefore)
                .SetNotAfter(request.NotAfter)
                .SetRSAPublicKey(rsa)
                .CreateForRSA(X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1));

            using var keyHolder = new X509KeyHolder(rsa, request.NotBefore, request.NotAfter);
            return m_certificateFactory.CreateWithPrivateKey(publicOnly, keyHolder.Certificate);
        }

        private Certificate CreateEccCertificate(
            PushCertificateKeyGenerationRequest request,
            HmacDrbg drbg,
            CancellationToken cancellationToken)
        {
            ECCurve curve = CryptoUtils.GetCurveFromCertificateTypeId(request.CertificateTypeId)
                ?? throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The Ecc certificate type is not supported.");

            using ECDsa ecdsa = CreateEcdsaKey(curve, drbg, cancellationToken);

            using Certificate publicOnly = m_certificateFactory
                .CreateApplicationCertificate(
                    request.ApplicationUri,
                    request.ApplicationName,
                    request.SubjectName,
                    DomainNamesOrNull(request.DomainNames))
                .SetNotBefore(request.NotBefore)
                .SetNotAfter(request.NotAfter)
                .SetECDsaPublicKey(ecdsa)
                .CreateForECDsa(X509SignatureGenerator.CreateForECDsa(ecdsa));

            using var keyHolder = new X509KeyHolder(ecdsa, request.NotBefore, request.NotAfter);
            return m_certificateFactory.CreateWithPrivateKey(publicOnly, keyHolder.Certificate);
        }

        private static ECDsa CreateEcdsaKey(
            ECCurve curve,
            HmacDrbg drbg,
            CancellationToken cancellationToken)
        {
#if NET5_0_OR_GREATER
            cancellationToken.ThrowIfCancellationRequested();

            byte[] order;
            using (ECDsa probe = ECDsa.Create(curve))
            {
                ECParameters explicitParameters = probe.ExportExplicitParameters(false);
                order = explicitParameters.Curve.Order!;
            }

            // Derive the private scalar d in [1, n-1] from the DRBG. Drawing a
            // few extra bytes before reducing modulo (n-1) keeps the modular
            // bias negligible.
            BigInteger n = FromBigEndianUnsigned(order);
            BigInteger wide = FromBigEndianUnsigned(drbg.Generate(order.Length + 8));
            BigInteger d = (wide % (n - BigInteger.One)) + BigInteger.One;

            var parameters = new ECParameters
            {
                Curve = curve,
                D = ToFixedBigEndian(d, order.Length)
            };
            return ECDsa.Create(parameters);
#else
            // .NET Framework / netstandard2.1 cannot import a private-only EC
            // scalar (Q is a required field) and this assembly has no EC point-
            // multiplication primitive to derive the public point for an
            // arbitrary named curve, so the caller-supplied §7.10.10 additional
            // entropy cannot be genuinely incorporated into an ECC private key
            // on this target framework. Rather than silently fall back to a
            // platform-generated key that ignores the mandated Nonce, fail
            // explicitly with Bad_NotSupported. RSA keys remain fully nonce-
            // derived on every target framework. Documented in
            // Docs/CertificateManager.md.
            _ = curve;
            _ = drbg;
            _ = cancellationToken;
            throw new ServiceResultException(
                StatusCodes.BadNotSupported,
                "Regenerating an ECC private key with additional entropy (OPC 10000-12 §7.10.10) is " +
                "not supported on this target framework; use an RSA CertificateType or run the server " +
                "on .NET 8 or later.");
#endif
        }

        private static RSAParameters GenerateRsaParameters(
            int keySizeInBits,
            HmacDrbg drbg,
            CancellationToken cancellationToken)
        {
            var e = new BigInteger(65537);
            int halfBits = keySizeInBits / 2;

            BigInteger p = GeneratePrime(halfBits, e, drbg, cancellationToken);
            BigInteger q;
            BigInteger minDistance = BigInteger.One << (halfBits - 100);
            do
            {
                q = GeneratePrime(halfBits, e, drbg, cancellationToken);
            }
            while (BigInteger.Abs(p - q) < minDistance);

            // The CRT parameter InverseQ = q^-1 mod p requires p > q.
            if (p < q)
            {
                (p, q) = (q, p);
            }

            BigInteger n = p * q;
            BigInteger pMinus1 = p - BigInteger.One;
            BigInteger qMinus1 = q - BigInteger.One;
            BigInteger lambda = pMinus1 / BigInteger.GreatestCommonDivisor(pMinus1, qMinus1) * qMinus1;
            BigInteger d = ModInverse(e, lambda);

            int modulusBytes = keySizeInBits / 8;
            int halfBytes = (halfBits + 7) / 8;

            return new RSAParameters
            {
                Modulus = ToFixedBigEndian(n, modulusBytes),
                Exponent = ToFixedBigEndian(e, 3),
                P = ToFixedBigEndian(p, halfBytes),
                Q = ToFixedBigEndian(q, halfBytes),
                D = ToFixedBigEndian(d, modulusBytes),
                DP = ToFixedBigEndian(d % pMinus1, halfBytes),
                DQ = ToFixedBigEndian(d % qMinus1, halfBytes),
                InverseQ = ToFixedBigEndian(ModInverse(q, p), halfBytes)
            };
        }

        private static BigInteger GeneratePrime(
            int bits,
            BigInteger e,
            HmacDrbg drbg,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BigInteger candidate = RandomOddCandidate(bits, drbg);

                // Ensure gcd(e, candidate-1) == 1. e (65537) is prime, so this
                // holds exactly when (candidate-1) is not a multiple of e.
                if ((candidate - BigInteger.One) % e == BigInteger.Zero)
                {
                    continue;
                }

                if (IsProbablePrime(candidate, drbg, cancellationToken))
                {
                    return candidate;
                }
            }
        }

        private static bool IsProbablePrime(
            BigInteger n,
            HmacDrbg drbg,
            CancellationToken cancellationToken)
        {
            foreach (int smallPrime in s_smallPrimes)
            {
                if (n == smallPrime)
                {
                    return true;
                }

                if (n % smallPrime == BigInteger.Zero)
                {
                    return false;
                }
            }

            BigInteger d = n - BigInteger.One;
            int s = 0;
            while (d.IsEven)
            {
                d >>= 1;
                s++;
            }

            BigInteger nMinus1 = n - BigInteger.One;
            for (int round = 0; round < kMillerRabinRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BigInteger a = RandomInRange(2, nMinus1 - BigInteger.One, drbg);
                BigInteger x = BigInteger.ModPow(a, d, n);
                if (x == BigInteger.One || x == nMinus1)
                {
                    continue;
                }

                bool witnessedComposite = true;
                for (int r = 0; r < s - 1; r++)
                {
                    x = BigInteger.ModPow(x, 2, n);
                    if (x == nMinus1)
                    {
                        witnessedComposite = false;
                        break;
                    }
                }

                if (witnessedComposite)
                {
                    return false;
                }
            }

            return true;
        }

        private static BigInteger RandomOddCandidate(int bits, HmacDrbg drbg)
        {
            int byteLength = (bits + 7) / 8;
            BigInteger value = FromBigEndianUnsigned(drbg.Generate(byteLength));

            // Keep the low `bits` bits, then force the top two bits and the
            // low bit. Setting the two most-significant bits guarantees the
            // product of two such primes has exactly the requested modulus
            // size; forcing the low bit makes the candidate odd.
            BigInteger mask = (BigInteger.One << bits) - BigInteger.One;
            value &= mask;
            value |= (BigInteger.One << (bits - 1))
                | (BigInteger.One << (bits - 2))
                | BigInteger.One;
            return value;
        }

        private static BigInteger RandomInRange(BigInteger minInclusive, BigInteger maxInclusive, HmacDrbg drbg)
        {
            BigInteger range = maxInclusive - minInclusive;
            int bits = BitLength(range);
            int byteLength = (bits + 7) / 8;
            while (true)
            {
                BigInteger candidate = FromBigEndianUnsigned(drbg.Generate(byteLength))
                    & ((BigInteger.One << bits) - BigInteger.One);
                if (candidate <= range)
                {
                    return minInclusive + candidate;
                }
            }
        }

        private static BigInteger ModInverse(BigInteger value, BigInteger modulus)
        {
            BigInteger gcd = ExtendedGcd(((value % modulus) + modulus) % modulus, modulus,
                out BigInteger x, out _);
            if (gcd != BigInteger.One)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "The generated key material is not invertible.");
            }

            return ((x % modulus) + modulus) % modulus;
        }

        private static BigInteger ExtendedGcd(BigInteger a, BigInteger b, out BigInteger x, out BigInteger y)
        {
            if (b == BigInteger.Zero)
            {
                x = BigInteger.One;
                y = BigInteger.Zero;
                return a;
            }

            BigInteger gcd = ExtendedGcd(b, a % b, out BigInteger x1, out BigInteger y1);
            x = y1;
            y = x1 - (a / b) * y1;
            return gcd;
        }

        private static int BitLength(BigInteger value)
        {
            int bits = 0;
            while (value > BigInteger.Zero)
            {
                bits++;
                value >>= 1;
            }

            return bits;
        }

        private static BigInteger FromBigEndianUnsigned(ReadOnlySpan<byte> bigEndian)
        {
            // Reverse into little-endian and append a zero byte so the value
            // is always interpreted as non-negative.
            var littleEndian = new byte[bigEndian.Length + 1];
            for (int i = 0; i < bigEndian.Length; i++)
            {
                littleEndian[i] = bigEndian[bigEndian.Length - 1 - i];
            }

            return new BigInteger(littleEndian);
        }

        private static byte[] ToFixedBigEndian(BigInteger value, int length)
        {
            byte[] littleEndian = value.ToByteArray();
            int significant = littleEndian.Length;
            // Drop the trailing sign byte produced for non-negative values.
            if (significant > 1 && littleEndian[significant - 1] == 0)
            {
                significant--;
            }

            if (significant > length)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInternalError,
                    "The generated key material does not fit the expected size.");
            }

            var bigEndian = new byte[length];
            for (int i = 0; i < significant; i++)
            {
                bigEndian[length - 1 - i] = littleEndian[i];
            }

            Array.Clear(littleEndian, 0, littleEndian.Length);
            return bigEndian;
        }

        private static string[]? DomainNamesOrNull(ArrayOf<string> domainNames)
        {
            return domainNames.IsNull ? null : domainNames.ToArray();
        }

        private static bool IsRsaCertificateType(NodeId certificateTypeId)
        {
            return certificateTypeId.IsNull
                || certificateTypeId == ObjectTypeIds.ApplicationCertificateType
                || certificateTypeId == ObjectTypeIds.RsaMinApplicationCertificateType
                || certificateTypeId == ObjectTypeIds.RsaSha256ApplicationCertificateType
                || certificateTypeId == ObjectTypeIds.HttpsCertificateType;
        }

        private static void ClearRsaParameters(ref RSAParameters parameters)
        {
            ClearIfPresent(parameters.D);
            ClearIfPresent(parameters.P);
            ClearIfPresent(parameters.Q);
            ClearIfPresent(parameters.DP);
            ClearIfPresent(parameters.DQ);
            ClearIfPresent(parameters.InverseQ);
        }

        private static void ClearIfPresent(byte[]? value)
        {
            if (value != null)
            {
                Array.Clear(value, 0, value.Length);
            }
        }

        private static byte[] CreateServerEntropy(int count)
        {
            var buffer = new byte[count];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(buffer);
            return buffer;
        }

        private const int kServerSeedBytes = 48;
        private const int kMillerRabinRounds = 64;

        private static readonly byte[] s_personalization =
            System.Text.Encoding.ASCII.GetBytes("OPC UA PushManagement CreateSigningRequest key");

        private static readonly int[] s_smallPrimes =
        [
            2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71,
            73, 79, 83, 89, 97, 101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151,
            157, 163, 167, 173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233,
            239, 241, 251
        ];

        private readonly ICertificateFactory m_certificateFactory;
        private readonly Func<int, byte[]> m_serverEntropySource;

        /// <summary>
        /// A minimal self-signed certificate that carries a generated private
        /// key so it can be re-attached to the OPC UA certificate structure
        /// through <see cref="ICertificateFactory.CreateWithPrivateKey"/>
        /// (which detaches the key from its transient generator).
        /// </summary>
        private sealed class X509KeyHolder : IDisposable
        {
            public X509KeyHolder(RSA key, DateTime notBefore, DateTime notAfter)
            {
                var request = new CertificateRequest(
                    "CN=Transient Key Holder",
                    key,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                m_x509 = request.CreateSelfSigned(notBefore, notAfter);
                Certificate = Certificate.From(m_x509);
            }

            public X509KeyHolder(ECDsa key, DateTime notBefore, DateTime notAfter)
            {
                var request = new CertificateRequest(
                    "CN=Transient Key Holder",
                    key,
                    HashAlgorithmName.SHA256);
                m_x509 = request.CreateSelfSigned(notBefore, notAfter);
                Certificate = Certificate.From(m_x509);
            }

            public Certificate Certificate { get; }

            public void Dispose()
            {
                Certificate.Dispose();
                m_x509.Dispose();
            }

            private readonly X509Certificate2 m_x509;
        }

        /// <summary>
        /// NIST SP&#160;800-90A HMAC&#8209;DRBG instantiated over
        /// HMAC&#8209;SHA256. Used single-shot (no reseed counting) to derive
        /// the private-key material from a server seed mixed with the caller
        /// nonce.
        /// </summary>
        private sealed class HmacDrbg : IDisposable
        {
            public HmacDrbg(ReadOnlySpan<byte> entropy, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> personalization)
            {
                m_key = new byte[kOutputLength];
                m_value = new byte[kOutputLength];
                for (int i = 0; i < kOutputLength; i++)
                {
                    m_value[i] = 0x01;
                }

                var seed = new byte[entropy.Length + nonce.Length + personalization.Length];
                entropy.CopyTo(seed);
                nonce.CopyTo(seed.AsSpan(entropy.Length));
                personalization.CopyTo(seed.AsSpan(entropy.Length + nonce.Length));
                Update(seed);
                Array.Clear(seed, 0, seed.Length);
            }

            public byte[] Generate(int numberOfBytes)
            {
                var output = new byte[numberOfBytes];
                int produced = 0;
                while (produced < numberOfBytes)
                {
                    m_value = Hmac(m_key, m_value);
                    int take = Math.Min(kOutputLength, numberOfBytes - produced);
                    Array.Copy(m_value, 0, output, produced, take);
                    produced += take;
                }

                Update(ReadOnlySpan<byte>.Empty);
                return output;
            }

            public void Dispose()
            {
                Array.Clear(m_key, 0, m_key.Length);
                Array.Clear(m_value, 0, m_value.Length);
            }

            private void Update(ReadOnlySpan<byte> providedData)
            {
                m_key = Hmac(m_key, m_value, 0x00, providedData);
                m_value = Hmac(m_key, m_value);
                if (!providedData.IsEmpty)
                {
                    m_key = Hmac(m_key, m_value, 0x01, providedData);
                    m_value = Hmac(m_key, m_value);
                }
            }

            private static byte[] Hmac(byte[] key, byte[] value)
            {
                using var hmac = new HMACSHA256(key);
                return hmac.ComputeHash(value);
            }

            private static byte[] Hmac(byte[] key, byte[] value, byte separator, ReadOnlySpan<byte> providedData)
            {
                var buffer = new byte[value.Length + 1 + providedData.Length];
                Array.Copy(value, buffer, value.Length);
                buffer[value.Length] = separator;
                providedData.CopyTo(buffer.AsSpan(value.Length + 1));
                using var hmac = new HMACSHA256(key);
                byte[] result = hmac.ComputeHash(buffer);
                Array.Clear(buffer, 0, buffer.Length);
                return result;
            }

            private const int kOutputLength = 32;
            private byte[] m_key;
            private byte[] m_value;
        }
    }
}
