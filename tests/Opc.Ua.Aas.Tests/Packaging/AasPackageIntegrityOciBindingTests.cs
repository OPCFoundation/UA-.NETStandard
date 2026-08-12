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
using System.Text;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Packaging;

namespace Opc.Ua.Aas.Tests.Packages
{
    /// <summary>
    /// Negative-path behaviour of the four-step OCI manifest-to-package binding of clause 6.5.4.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasPackageIntegrityOciBindingTests
    {
        private const string BlobText = "package";
        private const string ForeignDigest =
            "0000000000000000000000000000000000000000000000000000000000000000";

        [Test]
        public void VerifyOciBindingAcceptsAConsistentManifestChain()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + digest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Message, Is.Empty);
            });
        }

        [Test]
        public void VerifyOciBindingRejectsATamperedBlobEvenWhenTheManifestChainIsIntact()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + digest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, Bytes(BlobText + "-tampered"), AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Does.Contain("blob digest does not match"));
            });
        }

        [Test]
        public void VerifyOciBindingRejectsManifestBytesThatDoNotHashToTheManifestDigest()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + digest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");
            ByteString swappedManifest = Manifest("sha256:" + ForeignDigest);

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                swappedManifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Is.EqualTo("OCI manifest bytes do not match ManifestDigest."));
            });
        }

        [TestCase("", TestName = "ManifestDigestIsEmpty")]
        [TestCase("sha256", TestName = "ManifestDigestHasNoSeparator")]
        [TestCase("sha256:", TestName = "ManifestDigestHasNothingAfterTheSeparator")]
        [TestCase(":deadbeef", TestName = "ManifestDigestHasNothingBeforeTheSeparator")]
        [TestCase("md5:deadbeef", TestName = "ManifestDigestUsesAnUnsupportedAlgorithm")]
        [TestCase("SHA256:deadbeef", TestName = "ManifestDigestAlgorithmIsUpperCase")]
        [TestCase("sha256:DEADBEEF", TestName = "ManifestDigestHexIsUpperCase")]
        [TestCase("sha256:nothex", TestName = "ManifestDigestIsNotHexadecimal")]
        public void VerifyOciBindingRejectsAMalformedManifestDigest(string manifestDigest)
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + digest);

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo(
                        "ManifestDigest must be a lower-case algorithm prefix and lower-case hexadecimal digest."));
            });
        }

        [Test]
        public void VerifyOciBindingRejectsAManifestWithoutALayersArray()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Bytes("{\"schemaVersion\":2,\"layers\":{}}");
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Is.EqualTo("OCI manifest does not contain a layers array."));
            });
        }

        [Test]
        public void VerifyOciBindingRejectsAManifestThatIsNotJson()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Bytes("{ this is not json");
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Does.StartWith("OCI manifest is not valid JSON:"));
            });
        }

        [Test]
        public void VerifyOciBindingIgnoresLayerEntriesWithoutAStringDigest()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);

            // Only the third entry is a well-formed descriptor, so it must be treated as the single
            // package layer instead of tripping the "exactly one descriptor" rule.
            ByteString manifest = Bytes(
                "{\"schemaVersion\":2,\"layers\":[" +
                "\"not-an-object\"," +
                "{\"mediaType\":\"application/aasx\"}," +
                "{\"mediaType\":\"application/aasx\",\"digest\":\"sha256:" + digest + "\",\"size\":7}," +
                "{\"mediaType\":\"application/aasx\",\"digest\":42}]}");
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.That(result.Succeeded, Is.True);
        }

        [TestCase("deadbeef", TestName = "LayerDigestHasNoAlgorithmPrefix")]
        [TestCase("md5:deadbeef", TestName = "LayerDigestUsesAnUnsupportedAlgorithm")]
        [TestCase("sha256:DEADBEEF", TestName = "LayerDigestHexIsUpperCase")]
        public void VerifyOciBindingRejectsAMalformedPackageLayerDigest(string layerDigest)
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest(layerDigest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo("OCI package layer digest is not a lower-case prefixed digest."));
            });
        }

        [Test]
        public void VerifyOciBindingRejectsALayerDigestAlgorithmThatDisagreesWithDigestAlg()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);

            // The layer descriptor claims sha512 while the published DigestAlg is Sha256; the binding
            // must not be accepted on the strength of the matching hexadecimal digest alone.
            ByteString manifest = Manifest("sha512:" + digest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo("OCI package layer digest does not match DigestAlg and Digest."));
            });
        }

        [Test]
        public void VerifyOciBindingRejectsALayerDigestThatDisagreesWithThePublishedDigest()
        {
            ByteString blob = Bytes(BlobText);
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + ForeignDigest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo("OCI package layer digest does not match DigestAlg and Digest."));
            });
        }

        [Test]
        public void VerifyConsumerBlobReportsTheUnsupportedAlgorithmInsteadOfThrowing()
        {
            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyConsumerBlob(
                Bytes(BlobText),
                "sha256",
                ForeignDigest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Message, Does.Contain("DigestAlg must be exactly Sha256, Sha384 or Sha512."));
            });
        }

        [TestCase("sha256:deadbeef", TestName = "ConsumerDigestCarriesAnAlgorithmPrefix")]
        [TestCase("DEADBEEF", TestName = "ConsumerDigestIsUpperCase")]
        [TestCase("zz", TestName = "ConsumerDigestIsNotHexadecimal")]
        public void VerifyConsumerBlobRejectsADigestThatIsNotBareLowerCaseHex(string digest)
        {
            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyConsumerBlob(
                Bytes(BlobText),
                AasPackageIntegrity.Sha256,
                digest);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.Message,
                    Is.EqualTo("Digest must be lower-case hexadecimal without an algorithm prefix."));
            });
        }

        [TestCase("sha256", AasPackageIntegrity.Sha256)]
        [TestCase("sha384", AasPackageIntegrity.Sha384)]
        [TestCase("sha512", AasPackageIntegrity.Sha512)]
        public void MapOciAlgorithmTranslatesLowerCaseOciNamesToAasSpellings(string oci, string expected)
        {
            Assert.That(AasPackageIntegrity.MapOciAlgorithm(oci), Is.EqualTo(expected));
        }

        [TestCase("Sha256", TestName = "MapOciAlgorithmRejectsTheAasSpelling")]
        [TestCase("SHA256", TestName = "MapOciAlgorithmRejectsUpperCase")]
        [TestCase("md5", TestName = "MapOciAlgorithmRejectsAnUnsupportedAlgorithm")]
        public void MapOciAlgorithmRejectsAnythingOutsideTheThreeExactNames(string oci)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => AasPackageIntegrity.MapOciAlgorithm(oci))!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.ParamName, Is.EqualTo("ociAlgorithm"));
                Assert.That(
                    exception.Message,
                    Does.Contain("OCI descriptor algorithm must be exactly sha256, sha384 or sha512."));
            });
        }

        [Test]
        public void MapOciAlgorithmRejectsNull()
        {
            Assert.That(
                () => AasPackageIntegrity.MapOciAlgorithm(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("ociAlgorithm"));
        }

        [Test]
        public void ValidateDigestAlgorithmRejectsNull()
        {
            Assert.That(
                () => AasPackageIntegrity.ValidateDigestAlgorithm(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("digestAlg"));
        }

        [Test]
        public void ComputeManifestDigestKeepsTheAlgorithmPrefixAndAgreesWithTheBareDigest()
        {
            ByteString manifest = Manifest("sha256:" + ForeignDigest);

            string prefixed = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha512");
            string bare = AasPackageIntegrity.ComputeDigest(manifest, AasPackageIntegrity.Sha512);

            Assert.That(prefixed, Is.EqualTo("sha512:" + bare));
        }

        [Test]
        public void VersionIdFromManifestDigestIsDeterministicAndDistinguishesManifests()
        {
            string first = AasPackageIntegrity.VersionIdFromManifestDigest("sha256:" + ForeignDigest);
            string repeat = AasPackageIntegrity.VersionIdFromManifestDigest("sha256:" + ForeignDigest);
            string other = AasPackageIntegrity.VersionIdFromManifestDigest("sha512:" + ForeignDigest);

            Assert.Multiple(() =>
            {
                Assert.That(first, Does.StartWith("oci."));
                Assert.That(repeat, Is.EqualTo(first));
                Assert.That(other, Is.Not.EqualTo(first));
            });
        }

        [Test]
        public void VersionIdFromManifestDigestRejectsNull()
        {
            Assert.That(
                () => AasPackageIntegrity.VersionIdFromManifestDigest(null!),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("manifestDigest"));
        }

        [Test]
        public void IntegrityResultsCompareOnBothOutcomeAndMessage()
        {
            AasPackageIntegrityResult success = AasPackageIntegrityResult.Success();
            AasPackageIntegrityResult sameSuccess = AasPackageIntegrityResult.Success();
            AasPackageIntegrityResult failure = AasPackageIntegrityResult.Fail("broken");
            AasPackageIntegrityResult sameFailure = AasPackageIntegrityResult.Fail("broken");
            AasPackageIntegrityResult otherFailure = AasPackageIntegrityResult.Fail("Broken");

            bool equalSuccesses = success == sameSuccess;
            bool equalFailures = failure == sameFailure;
            bool caseSensitiveMessages = failure != otherFailure;
            bool outcomesDiffer = success != failure;
            bool boxedEquals = success.Equals((object)sameSuccess);
            bool foreignTypeEquals = success.Equals("not a result");

            Assert.Multiple(() =>
            {
                Assert.That(equalSuccesses, Is.True);
                Assert.That(equalFailures, Is.True);
                Assert.That(caseSensitiveMessages, Is.True);
                Assert.That(outcomesDiffer, Is.True);
                Assert.That(boxedEquals, Is.True);
                Assert.That(foreignTypeEquals, Is.False);
                Assert.That(failure.GetHashCode(), Is.EqualTo(sameFailure.GetHashCode()));
                Assert.That(failure.Message, Is.EqualTo("broken"));
            });
        }

        [Test]
        public void IntegrityResultNormalizesANullMessageToEmpty()
        {
            var result = new AasPackageIntegrityResult(false, null!);

            Assert.Multiple(() =>
            {
                Assert.That(result.Message, Is.Empty);
                Assert.That(result.Succeeded, Is.False);
            });
        }

        private static ByteString Bytes(string value)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(value));
        }

        private static ByteString Manifest(string layerDigest)
        {
            return Bytes(
                "{\"schemaVersion\":2,\"layers\":[{\"mediaType\":\"application/aasx\",\"digest\":\"" +
                layerDigest + "\",\"size\":7}]}");
        }
    }
}
