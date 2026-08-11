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
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Aas.Server.Packaging;

namespace Opc.Ua.Aas.Tests.Packages
{
    /// <summary>
    /// Tests AAS package integrity rules from clause 6.5.4.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public sealed class AasPackageIntegrityTests
    {
        [Test]
        public async Task PublishedVersionExposesImmutableDigestAndDigestAlg()
        {
            ByteString blob = Bytes("package");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            var store = new InMemoryAasPackageStore();

            AasPackageVersion version = await store.PublishAsync(new AasPackagePublishRequest(
                "pkg", "v1", blob, digest, AasPackageIntegrity.Sha256));

            Assert.Multiple(() =>
            {
                Assert.That(version.Digest, Is.EqualTo(digest));
                Assert.That(version.DigestAlg, Is.EqualTo(AasPackageIntegrity.Sha256));
                Assert.That(typeof(AasPackageVersion).GetProperty(nameof(AasPackageVersion.Digest))!.SetMethod,
                    Is.Null);
                Assert.That(typeof(AasPackageVersion).GetProperty(nameof(AasPackageVersion.DigestAlg))!.SetMethod,
                    Is.Null);
            });
        }

        [Test]
        public void DigestAlgorithmsAcceptOnlyExactAasSpellings()
        {
            ByteString blob = Bytes("package");

            Assert.Multiple(() =>
            {
                Assert.That(AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256), Is.Not.Empty);
                Assert.That(AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha384), Is.Not.Empty);
                Assert.That(AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha512), Is.Not.Empty);
                Assert.That(
                    () => AasPackageIntegrity.ComputeDigest(blob, "sha256"),
                    Throws.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void DigestWithAlgorithmPrefixIsRejected()
        {
            ByteString blob = Bytes("package");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyConsumerBlob(
                blob, AasPackageIntegrity.Sha256, "sha256:" + digest);

            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public void MismatchedBlobIsRejectedBeforeItIsReadable()
        {
            ByteString blob = Bytes("package");
            ByteString tampered = Bytes("tampered");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            var store = new InMemoryAasPackageStore();

            Assert.Multiple(() =>
            {
                Assert.That(
                    async () => await store.PublishAsync(new AasPackagePublishRequest(
                        "pkg", "v1", tampered, digest, AasPackageIntegrity.Sha256)),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(
                    async () => await store.ReadAsync("pkg", "v1"),
                    Throws.TypeOf<KeyNotFoundException>());
            });
        }

        [Test]
        public void ConsumerSideVerificationCatchesTamperedBlob()
        {
            ByteString blob = Bytes("package");
            ByteString tampered = Bytes("tampered");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyConsumerBlob(
                tampered, AasPackageIntegrity.Sha256, digest);

            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public async Task OciBindingPublishesManifestDigestWithPrefixAndDigestWithoutPrefix()
        {
            ByteString blob = Bytes("package");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + digest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");
            var store = new InMemoryAasPackageStore();

            AasPackageVersion version = await store.PublishOciAsync(new AasOciPackagePublishRequest(
                "pkg", manifest, manifestDigest, blob, digest, AasPackageIntegrity.Sha256, "_Tag.Case-1"));

            Assert.Multiple(() =>
            {
                Assert.That(version.ManifestDigest, Does.StartWith("sha256:"));
                Assert.That(version.Digest, Does.Not.Contain(":"));
                Assert.That(version.OciTag, Is.EqualTo("_Tag.Case-1"));
                Assert.That(version.VersionId, Is.EqualTo(AasPackageIntegrity.VersionIdFromManifestDigest(
                    manifestDigest)));
            });
        }

        [TestCase(0)]
        [TestCase(2)]
        public void OciManifestRequiresExactlyOnePackageLayerDescriptor(int layers)
        {
            ByteString blob = Bytes("package");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = ManifestWithLayerCount("sha256:" + digest, layers);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");

            AasPackageIntegrityResult result = AasPackageIntegrity.VerifyOciBinding(
                manifest, manifestDigest, blob, AasPackageIntegrity.Sha256, digest);

            Assert.That(result.Succeeded, Is.False);
        }

        [Test]
        public void InvalidOciTagIsRejected()
        {
            ByteString blob = Bytes("package");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            ByteString manifest = Manifest("sha256:" + digest);
            string manifestDigest = AasPackageIntegrity.ComputeManifestDigest(manifest, "sha256");
            var store = new InMemoryAasPackageStore();

            Assert.That(
                async () => await store.PublishOciAsync(new AasOciPackagePublishRequest(
                    "pkg", manifest, manifestDigest, blob, digest, AasPackageIntegrity.Sha256, "-bad")),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task MovingTagToNewManifestRetainsDistinctVersions()
        {
            ByteString firstBlob = Bytes("first");
            ByteString secondBlob = Bytes("second");
            string firstDigest = AasPackageIntegrity.ComputeDigest(firstBlob, AasPackageIntegrity.Sha256);
            string secondDigest = AasPackageIntegrity.ComputeDigest(secondBlob, AasPackageIntegrity.Sha256);
            ByteString firstManifest = Manifest("sha256:" + firstDigest);
            ByteString secondManifest = Manifest("sha256:" + secondDigest);
            var store = new InMemoryAasPackageStore();
            await store.PublishOciAsync(new AasOciPackagePublishRequest(
                "pkg",
                firstManifest,
                AasPackageIntegrity.ComputeManifestDigest(firstManifest, "sha256"),
                firstBlob,
                firstDigest,
                AasPackageIntegrity.Sha256,
                "current"));
            await store.PublishOciAsync(new AasOciPackagePublishRequest(
                "pkg",
                secondManifest,
                AasPackageIntegrity.ComputeManifestDigest(secondManifest, "sha256"),
                secondBlob,
                secondDigest,
                AasPackageIntegrity.Sha256,
                "current"));

            ArrayOf<AasPackageVersion> versions = await store.ListVersionsAsync("pkg");

            Assert.Multiple(() =>
            {
                Assert.That(versions.Count, Is.EqualTo(2));
                Assert.That(CountDistinctVersionIds(versions), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task ReferrerDoesNotMutatePackageVersionCollectionOrLifecycle()
        {
            ByteString blob = Bytes("package");
            string digest = AasPackageIntegrity.ComputeDigest(blob, AasPackageIntegrity.Sha256);
            var store = new InMemoryAasPackageStore();
            await store.PublishAsync(new AasPackagePublishRequest(
                "pkg", "v1", blob, digest, AasPackageIntegrity.Sha256));
            ulong epoch = store.Epoch;
            DateTimeOffset modifiedAt = store.ModifiedAt;

            await store.AddReferrerAsync(
                "pkg",
                new AasPackageReferrerResource("attestation", "application/vnd.example.attestation", digest));
            ArrayOf<AasPackageVersion> versions = await store.ListVersionsAsync("pkg");

            Assert.Multiple(() =>
            {
                Assert.That(versions.Count, Is.EqualTo(1));
                Assert.That(store.Epoch, Is.EqualTo(epoch));
                Assert.That(store.ModifiedAt, Is.EqualTo(modifiedAt));
            });
        }

        private static ByteString Bytes(string value)
        {
            return ByteString.From(Encoding.UTF8.GetBytes(value));
        }

        private static int CountDistinctVersionIds(ArrayOf<AasPackageVersion> versions)
        {
            var versionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int ii = 0; ii < versions.Count; ii++)
            {
                versionIds.Add(versions[ii].VersionId);
            }

            return versionIds.Count;
        }

        private static ByteString Manifest(string digest)
        {
            return ManifestWithLayerCount(digest, 1);
        }

        private static ByteString ManifestWithLayerCount(string digest, int layers)
        {
            var builder = new StringBuilder("{\"schemaVersion\":2,\"layers\":[");
            for (int ii = 0; ii < layers; ii++)
            {
                if (ii > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"mediaType\":\"application/aasx\",\"digest\":\"");
                builder.Append(digest);
                builder.Append("\",\"size\":7}");
            }

            builder.Append("]}");
            return ByteString.From(Encoding.UTF8.GetBytes(builder.ToString()));
        }
    }
}
