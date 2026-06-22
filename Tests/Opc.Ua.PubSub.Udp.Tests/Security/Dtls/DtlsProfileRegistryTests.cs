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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Security.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Security.Dtls
{
    /// <summary>
    /// Verifies the fail-closed DTLS profile registry required by Part 14 §7.3.2.4.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("7.3.2.4")]
    [TestSpec("RFC 9147")]
    [TestSpec("RFC 8446")]
    public sealed class DtlsProfileRegistryTests
    {
        [Test]
        public void ResolveMandatoryCurve25519AndCurve448ProfilesThrows()
        {
            var registry = new DtlsProfileRegistry(CreateFullBclSupport());
            string[] mandatoryProfiles =
            [
                "ECC_curve25519",
                "ECC_curve25519_AesGcm",
                "ECC_curve448",
                "ECC_curve448_AesGcm"
            ];

            foreach (string profile in mandatoryProfiles)
            {
                Assert.That(
                    () => registry.Resolve(profile),
                    Throws.TypeOf<NotSupportedException>()
                        .With.Message.Contains("no downgrade is allowed"),
                    profile);
            }
        }

        [Test]
        public void ResolveSupportedNistAndBrainpoolProfilesSucceeds()
        {
            var registry = new DtlsProfileRegistry(CreateFullBclSupport());
            string[] optionalProfiles =
            [
                "ECC_nistP256",
                "ECC_nistP384",
                "ECC_brainpoolP256r1",
                "ECC_brainpoolP384r1",
                "ECC_nistP256_AesGcm",
                "ECC_nistP384_AesGcm",
                "ECC_brainpoolP256r1_AesGcm",
                "ECC_brainpoolP384r1_AesGcm",
                "ECC_nistP256_ChaChaPoly",
                "ECC_nistP384_ChaChaPoly",
                "ECC_brainpoolP256r1_ChaChaPoly",
                "ECC_brainpoolP384r1_ChaChaPoly"
            ];

            foreach (string profile in optionalProfiles)
            {
                Assert.That(registry.Resolve(profile).Name, Is.EqualTo(profile), profile);
            }
        }

        [Test]
        public void ResolveWithUnavailablePrimitiveThrows()
        {
            var registry = new DtlsProfileRegistry(new DtlsPrimitiveSupport(
                HasAesGcm: true,
                HasAes128Gcm: true,
                HasAes256Gcm: true,
                HasChaCha20Poly1305: false,
                HasHkdf: true,
                HasNistP256: true,
                HasNistP384: true,
                HasBrainpoolP256r1: true,
                HasBrainpoolP384r1: true));

            Assert.That(
                () => registry.Resolve("ECC_nistP256_ChaChaPoly"),
                Throws.TypeOf<NotSupportedException>()
                    .With.Message.Contains("not supported by the current .NET BCL/runtime"));
        }

        [Test]
        public void SupportedProfilesExcludesUnsupportedEntries()
        {
            var registry = new DtlsProfileRegistry(new DtlsPrimitiveSupport(
                HasAesGcm: false,
                HasAes128Gcm: false,
                HasAes256Gcm: false,
                HasChaCha20Poly1305: false,
                HasHkdf: true,
                HasNistP256: true,
                HasNistP384: false,
                HasBrainpoolP256r1: false,
                HasBrainpoolP384r1: false));

            Assert.That(registry.SupportedProfiles.Select(profile => profile.Name), Is.EqualTo(s_nistP256ProfileNames));
        }

#if !NET8_0_OR_GREATER
        [Test]
        public void CurrentRuntimeOnLowTargetFrameworkRegistersNoProfiles()
        {
            var registry = new DtlsProfileRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(registry.SupportedProfiles, Is.Empty);
                Assert.That(
                    () => registry.Resolve("ECC_nistP256_AesGcm"),
                    Throws.TypeOf<NotSupportedException>(),
                    "net48/netstandard2.1 must fail closed instead of substituting unsupported DTLS primitives.");
            });
        }
#endif

        private static DtlsPrimitiveSupport CreateFullBclSupport()
        {
            return new DtlsPrimitiveSupport(
                HasAesGcm: true,
                HasAes128Gcm: true,
                HasAes256Gcm: true,
                HasChaCha20Poly1305: true,
                HasHkdf: true,
                HasNistP256: true,
                HasNistP384: true,
                HasBrainpoolP256r1: true,
                HasBrainpoolP384r1: true);
        }

        private static readonly string[] s_nistP256ProfileNames = ["ECC_nistP256"];
    }
}
