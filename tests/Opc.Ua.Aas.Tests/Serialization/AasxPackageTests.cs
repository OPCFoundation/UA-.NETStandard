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

using Opc.Ua.Aas.V3;
using System;
using System.IO;
using System.IO.Packaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Serialization
{
    /// <summary>
    /// Tests AASX package reading and writing.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasxPackageTests
    {
        [Test]
        public async Task JsonPackageRoundTripsEnvironmentAndSupplementaryFiles()
        {
            AasEnvironment environment = CreateEnvironment("json");
            var supplementaryFiles = new ArrayOf<AasxSupplementaryFile>(new[]
            {
                new AasxSupplementaryFile(
                    new Uri("/aasx/files/readme.txt", UriKind.Relative),
                    "text/plain",
                    ByteString.From(Encoding.UTF8.GetBytes("supplement")))
            });

            using var packageStream = new MemoryStream();
            await new AasxPackageWriter()
                .WriteAsync(packageStream, environment, supplementaryFiles)
                .ConfigureAwait(false);
            packageStream.Position = 0;

            AasxPackageReadResult result = await new AasxPackageReader()
                .ReadAsync(packageStream)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.Submodels.Value[0].Id, Is.EqualTo("submodel-json"));
                Assert.That(result.SupplementaryFiles.Count, Is.EqualTo(1));
                Assert.That(result.SupplementaryFiles[0].PartUri.OriginalString, Is.EqualTo("/aasx/files/readme.txt"));
                Assert.That(
                    Encoding.UTF8.GetString(result.SupplementaryFiles[0].Content.ToArray()),
                    Is.EqualTo("supplement"));
            });
        }

        [Test]
        public async Task ReaderFollowsRelationshipsToEnvironmentPart()
        {
            using var packageStream = new MemoryStream();
            await WriteManualJsonPackageAsync(
                packageStream,
                new Uri("/not-the-default/environment.json", UriKind.Relative),
                CreateEnvironment("relationship")).ConfigureAwait(false);
            packageStream.Position = 0;

            AasxPackageReadResult result = await new AasxPackageReader()
                .ReadAsync(packageStream)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.Submodels.Value[0].Id, Is.EqualTo("submodel-relationship"));
            });
        }

        [Test]
        public async Task XmlPackageReadsEnvironment()
        {
            using var packageStream = new MemoryStream();
            await new AasxPackageWriter()
                .WriteAsync(
                    packageStream,
                    CreateEnvironment("xml"),
                    ArrayOf<AasxSupplementaryFile>.Empty,
                    AasxPackageSerialization.Xml)
                .ConfigureAwait(false);
            packageStream.Position = 0;

            AasxPackageReadResult result = await new AasxPackageReader()
                .ReadAsync(packageStream)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Error, Is.Null);
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.Submodels.Value[0].Id, Is.EqualTo("submodel-xml"));
            });
        }

        [Test]
        public async Task MalformedPackageReturnsDiagnostic()
        {
            using var packageStream = new MemoryStream(Encoding.UTF8.GetBytes("not a package"));

            AasxPackageReadResult result = await new AasxPackageReader()
                .ReadAsync(packageStream)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Error, Does.Contain("malformed"));
            });
        }

        [Test]
        public async Task EmptyEnvironmentRoundTrips()
        {
            using var packageStream = new MemoryStream();
            await new AasxPackageWriter()
                .WriteAsync(packageStream, new AasEnvironment())
                .ConfigureAwait(false);
            packageStream.Position = 0;

            AasxPackageReadResult result = await new AasxPackageReader()
                .ReadAsync(packageStream)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.Environment!.AssetAdministrationShells.IsPresent, Is.False);
                Assert.That(result.Environment.Submodels.IsPresent, Is.False);
            });
        }

        private static AasEnvironment CreateEnvironment(string suffix)
        {
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(new[]
                {
                    new AasSubmodel
                    {
                        Id = "submodel-" + suffix,
                        IdShort = AasOptional<string>.Present("Submodel" + suffix)
                    }
                })
            };
        }

        private static async Task WriteManualJsonPackageAsync(
            Stream stream,
            Uri environmentUri,
            AasEnvironment environment)
        {
            using Package package = Package.Open(stream, FileMode.Create, FileAccess.ReadWrite);
            Uri originUri = PackUriHelper.CreatePartUri(new Uri("/aasx/origin", UriKind.Relative));
            PackagePart originPart = package.CreatePart(
                originUri,
                "application/vnd.admin-shell.aasx-origin",
                CompressionOption.Maximum);
            package.CreateRelationship(originUri, TargetMode.Internal, AasxPackageRelationshipTypes.Origin);

            Uri partUri = PackUriHelper.CreatePartUri(environmentUri);
            PackagePart environmentPart = package.CreatePart(
                partUri,
                "application/asset-administration-shell+json",
                CompressionOption.Maximum);
            originPart.CreateRelationship(partUri, TargetMode.Internal, AasxPackageRelationshipTypes.Environment);

            using Stream environmentStream = environmentPart.GetStream(FileMode.Create, FileAccess.Write);
            await new AasJsonWriter().WriteAsync(environmentStream, environment, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
