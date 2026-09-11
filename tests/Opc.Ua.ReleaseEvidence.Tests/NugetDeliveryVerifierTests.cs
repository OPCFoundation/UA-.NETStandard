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
using System.Diagnostics;
using System.Formats.Asn1;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.ReleaseEvidence.Tests
{
    /// <summary>
    /// Exercises content equivalence independently of the separately required package-signature authentication.
    /// </summary>
    [TestFixture]
    public sealed class NugetDeliveryVerifierTests
    {
        /// <summary>
        /// Verifies that recompression and countersigning can preserve package content without authenticating delivery.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RepositoryCountersigningPreservesContentButDoesNotAuthenticateDeliveryAsync(
            bool countersign)
        {
            string work = CreateWorkspace();
            try
            {
                using RSA author = RSA.Create(2048);
                byte[] content = "Synthetic NuGet signature content"u8.ToArray();
                byte[] primarySignature = author.SignData(
                    content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                Assert.That(author.VerifyData(
                    content, primarySignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
                byte[] cms = CreateCms(content, primarySignature, author.ExportSubjectPublicKeyInfo(), false);
                byte[] deliveredCms = CreateCms(
                    content, primarySignature, author.ExportSubjectPublicKeyInfo(), countersign);
                await CreatePackageAsync(Path.Combine(work, "author.nupkg"), cms).ConfigureAwait(false);
                await CreatePackageAsync(
                    Path.Combine(work, "delivered.nupkg"), deliveredCms, compression: CompressionLevel.NoCompression)
                    .ConfigureAwait(false);
                (int code, string output) = await RunAsync(work).ConfigureAwait(false);
                Assert.That(code, Is.Zero, output);
                using JsonDocument report = await ReadReportAsync(work).ConfigureAwait(false);
                JsonElement root = report.RootElement;
                Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("content-matched"));
                Assert.That(root.GetProperty("contentPreserved").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("authorSignaturePreserved").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("signatureVerificationPerformed").GetBoolean(), Is.False);
                Assert.That(root.GetProperty("unmetControls").ToString(), Does.Contain("SIGNATURE_VERIFIED"));
                Assert.That(root.GetProperty("authorContentDigest").GetString(),
                    Is.EqualTo(root.GetProperty("deliveredContentDigest").GetString()));
                Assert.That(root.GetProperty("authorArchiveDigest").GetString(),
                    Is.Not.EqualTo(root.GetProperty("deliveredArchiveDigest").GetString()));
                Assert.That(root.GetProperty("content").GetArrayLength(), Is.EqualTo(2));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that changed package contents, identity, or author signatures produce an equivalence failure.
        /// </summary>
        [TestCase("payload")]
        [TestCase("added")]
        [TestCase("removed")]
        [TestCase("identity")]
        [TestCase("signature")]
        [TestCase("unsigned")]
        [TestCase("signed-content")]
        public async Task ChangedContentOrAuthorSignatureCannotBeEquivalentAsync(string mutation)
        {
            string work = CreateWorkspace();
            try
            {
                using RSA author = RSA.Create(2048);
                byte[] content = "Synthetic NuGet signature content"u8.ToArray();
                byte[] signature = author.SignData(content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                byte[] publicKey = author.ExportSubjectPublicKeyInfo();
                byte[] cms = CreateCms(content, signature, publicKey, false);
                await CreatePackageAsync(Path.Combine(work, "author.nupkg"), cms).ConfigureAwait(false);
                byte[]? deliveredCms = cms;
                if (mutation == "signature")
                {
                    using RSA replacement = RSA.Create(2048);
                    deliveredCms = CreateCms(
                        content, replacement.SignData(content, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
                        replacement.ExportSubjectPublicKeyInfo(), false);
                }
                else if (mutation == "unsigned")
                {
                    deliveredCms = null;
                }
                else if (mutation == "signed-content")
                {
                    deliveredCms = CreateCms("different content"u8.ToArray(), signature, publicKey, false);
                }
                await CreatePackageAsync(
                    Path.Combine(work, "delivered.nupkg"), deliveredCms, mutation).ConfigureAwait(false);
                (int code, string output) = await RunAsync(work).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                using JsonDocument report = await ReadReportAsync(work).ConfigureAwait(false);
                Assert.That(report.RootElement.GetProperty("status").GetString(), Is.EqualTo("content-mismatch"));
                Assert.That(report.RootElement.GetProperty("signatureVerificationPerformed").GetBoolean(), Is.False);
                string control = mutation is "signature" or "unsigned" or "signed-content"
                    ? "SIGNATURE_VERIFIED" : "ARTIFACT_INTEGRITY";
                Assert.That(report.RootElement.GetProperty("unmetControls").ToString(), Does.Contain(control));
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that malformed signatures and unsupported archive layouts fail without writing a receipt.
        /// </summary>
        [TestCase("malformed-cms")]
        [TestCase("duplicate")]
        [TestCase("unsafe-path")]
        [TestCase("signature-case")]
        [TestCase("symbols")]
        public async Task UnsupportedOrMalformedArchivesReturnFatalWithoutReceiptAsync(string mutation)
        {
            string work = CreateWorkspace();
            try
            {
                await CreatePackageAsync(
                    Path.Combine(work, "author.nupkg"),
                    mutation == "malformed-cms" ? [1, 2, 3] : null, mutation).ConfigureAwait(false);
                await CreatePackageAsync(Path.Combine(work, "delivered.nupkg"), null).ConfigureAwait(false);
                if (mutation == "symbols")
                {
                    File.Move(Path.Combine(work, "author.nupkg"), Path.Combine(work, "author.snupkg"));
                }
                (int code, string output) = await RunAsync(work, mutation == "symbols").ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(2), output);
                Assert.That(File.Exists(Path.Combine(work, "report.json")), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        /// <summary>
        /// Verifies that identical unsigned packages match content but cannot satisfy author-signature preservation.
        /// </summary>
        [Test]
        public async Task IdenticalUnsignedPackagesNeverPreserveAnAuthorSignatureAsync()
        {
            string work = CreateWorkspace();
            try
            {
                await CreatePackageAsync(Path.Combine(work, "author.nupkg"), null).ConfigureAwait(false);
                File.Copy(Path.Combine(work, "author.nupkg"), Path.Combine(work, "delivered.nupkg"));
                (int code, string output) = await RunAsync(work).ConfigureAwait(false);
                Assert.That(code, Is.EqualTo(1), output);
                using JsonDocument report = await ReadReportAsync(work).ConfigureAwait(false);
                Assert.That(report.RootElement.GetProperty("contentPreserved").GetBoolean(), Is.True);
                Assert.That(report.RootElement.GetProperty("authorSignaturePreserved").GetBoolean(), Is.False);
            }
            finally
            {
                Directory.Delete(work, true);
            }
        }

        private static byte[] CreateCms(byte[] content, byte[] signature, byte[] publicKey, bool countersign)
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.2.840.113549.1.7.2");
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                using (writer.PushSequence())
                {
                    writer.WriteInteger(3);
                    using (writer.PushSetOf())
                    {
                        WriteAlgorithm(writer, "2.16.840.1.101.3.4.2.1");
                    }
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("1.2.840.113549.1.7.1");
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                        {
                            writer.WriteOctetString(content);
                        }
                    }
                    using (writer.PushSetOf())
                    {
                        WriteSigner(writer, signature, publicKey, countersign);
                    }
                }
            }
            return writer.Encode();
        }

        private static void WriteSigner(AsnWriter writer, byte[] signature, byte[] publicKey, bool countersign)
        {
            using (writer.PushSequence())
            {
                writer.WriteInteger(3);
                writer.WriteOctetString(SHA256.HashData(publicKey), new Asn1Tag(TagClass.ContextSpecific, 0));
                WriteAlgorithm(writer, "2.16.840.1.101.3.4.2.1");
                WriteAlgorithm(writer, "1.2.840.113549.1.1.1");
                writer.WriteOctetString(signature);
                if (countersign)
                {
                    using RSA repository = RSA.Create(2048);
                    byte[] counterSignature = repository.SignData(
                        signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                    Assert.That(repository.VerifyData(
                        signature, counterSignature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
                    using (writer.PushSetOf(new Asn1Tag(TagClass.ContextSpecific, 1, true)))
                    using (writer.PushSequence())
                    {
                        writer.WriteObjectIdentifier("1.2.840.113549.1.9.6");
                        using (writer.PushSetOf())
                        {
                            WriteSigner(writer, counterSignature, repository.ExportSubjectPublicKeyInfo(), false);
                        }
                    }
                }
            }
        }

        private static void WriteAlgorithm(AsnWriter writer, string oid)
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(oid);
                writer.WriteNull();
            }
        }

        private static async Task CreatePackageAsync(
            string path,
            byte[]? signature,
            string mutation = "",
            CompressionLevel compression = CompressionLevel.Optimal)
        {
            using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
            string version = mutation == "identity" ? "2.0.1" : "2.0.0";
            await WriteEntryAsync(archive, "Synthetic.nuspec", Encoding.UTF8.GetBytes(
                $"<package><metadata><id>Synthetic</id><version>{version}</version></metadata></package>"),
                compression).ConfigureAwait(false);
            if (mutation != "removed")
            {
                await WriteEntryAsync(archive, "lib/net10.0/Synthetic.dll",
                    Encoding.UTF8.GetBytes(mutation == "payload" ? "changed payload" : "synthetic payload"),
                    compression).ConfigureAwait(false);
            }
            if (mutation is "added" or "duplicate" or "unsafe-path")
            {
                string entry = mutation switch
                {
                    "duplicate" => "lib/net10.0/SYNTHETIC.dll",
                    "unsafe-path" => "../outside.txt",
                    _ => "extra.txt"
                };
                await WriteEntryAsync(archive, entry, "additional content"u8.ToArray(), compression)
                    .ConfigureAwait(false);
            }
            if (signature != null || mutation == "signature-case")
            {
                await WriteEntryAsync(archive, mutation == "signature-case" ? ".SIGNATURE.p7s" : ".signature.p7s",
                    signature ?? [1], compression).ConfigureAwait(false);
            }
        }

        private static async Task WriteEntryAsync(
            ZipArchive archive,
            string path,
            byte[] bytes,
            CompressionLevel compression)
        {
            ZipArchiveEntry entry = archive.CreateEntry(path, compression);
            entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using Stream stream = entry.Open();
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }

        private static async Task<JsonDocument> ReadReportAsync(string work)
        {
            return JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(work, "report.json")).ConfigureAwait(false));
        }

        private static string CreateWorkspace()
        {
            string path = Path.Combine(
                TestContext.CurrentContext.TestDirectory, ".delivery", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static async Task<(int Code, string Output)> RunAsync(string work, bool symbols = false)
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "UA.slnx")))
            {
                root = root.Parent;
            }
            string repository = root?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
            string configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
            string tool = Path.Combine(repository, "tools", "Opc.Ua.ReleaseEvidence", "bin", configuration,
                "net10.0", "Opc.Ua.ReleaseEvidence.dll");
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repository
            };
            foreach (string argument in new[]
            {
                tool, "verify-delivery", "--author", Path.Combine(work, symbols ? "author.snupkg" : "author.nupkg"),
                "--delivered", Path.Combine(work, "delivered.nupkg"), "--output", Path.Combine(work, "report.json")
            })
            {
                start.ArgumentList.Add(argument);
            }
            using Process process = Process.Start(start) ?? throw new IOException("Could not launch evidence CLI.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
        }
    }
}
