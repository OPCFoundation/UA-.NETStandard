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
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Unit tests for <see cref="RuntimeNodeSetSource"/>,
    /// <see cref="FileRuntimeNodeSetSource"/>, and
    /// <see cref="StreamRuntimeNodeSetSource"/> covering argument
    /// validation, metadata extraction, stream ownership, and
    /// cancellation forwarding.
    /// </summary>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class RuntimeNodeSetSourceTests
    {
        private const string kTestNamespaceUri = "urn:opcfoundation.org:RuntimeNodeSetUnitTest";

        private const string kMinimalNodeSetXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
            "  <NamespaceUris>\r\n" +
            "    <Uri>" + kTestNamespaceUri + "</Uri>\r\n" +
            "  </NamespaceUris>\r\n" +
            "  <Models>\r\n" +
            "    <Model ModelUri=\"" + kTestNamespaceUri + "\" />\r\n" +
            "  </Models>\r\n" +
            "</UANodeSet>";

        private const string kNoModelsNodeSetXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
            "  <NamespaceUris>\r\n" +
            "    <Uri>" + kTestNamespaceUri + "</Uri>\r\n" +
            "  </NamespaceUris>\r\n" +
            "</UANodeSet>";

        private string m_testNodeSetFile;

        /// <summary>
        /// Writes the minimal test NodeSet file to the work directory before each test.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            string workDir = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "RuntimeNodeSetSourceTests");
            Directory.CreateDirectory(workDir);
            m_testNodeSetFile = Path.Combine(workDir, Guid.NewGuid().ToString("N") + ".NodeSet2.xml");
            File.WriteAllText(m_testNodeSetFile, kMinimalNodeSetXml, Encoding.UTF8);
        }

        /// <summary>
        /// Removes the temporary file after each test.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(m_testNodeSetFile) && File.Exists(m_testNodeSetFile))
            {
                File.Delete(m_testNodeSetFile);
            }
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromFile(string)"/> must throw when
        /// <c>filePath</c> is <c>null</c>.
        /// </summary>
        [Test]
        public void FromFileRejectsNullFilePath()
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromFile(null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("filePath"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromFile(string, ArrayOf{string})"/> must throw
        /// when <c>filePath</c> is <c>null</c>.
        /// </summary>
        [Test]
        public void FromFileWithExplicitNsRejectsNullFilePath()
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromFile(null, [kTestNamespaceUri]),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("filePath"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromFile(string, ArrayOf{string})"/> must throw
        /// when <c>modelNamespaceUris</c> is the default (null) <see cref="ArrayOf{T}"/>.
        /// </summary>
        [Test]
        public void FromFileWithExplicitNsRejectsNullModelNamespaceUris()
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromFile(m_testNodeSetFile, default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("modelNamespaceUris"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromStream"/> must throw when
        /// <c>name</c> is <c>null</c>, empty, or whitespace.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void FromStreamRejectsBlankName(string name)
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromStream(
                    name,
                    _ => new ValueTask<Stream>(Stream.Null),
                    [kTestNamespaceUri]),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("name"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromStream"/> must throw when
        /// <c>openStream</c> is <c>null</c>.
        /// </summary>
        [Test]
        public void FromStreamRejectsNullOpenStream()
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromStream(
                    "source",
                    null,
                    [kTestNamespaceUri]),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("openStream"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromStream"/> must throw when
        /// <c>modelNamespaceUris</c> is the default (null) <see cref="ArrayOf{T}"/>.
        /// </summary>
        [Test]
        public void FromStreamRejectsNullModelNamespaceUris()
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromStream(
                    "source",
                    _ => new ValueTask<Stream>(Stream.Null),
                    default),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("modelNamespaceUris"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.FromStream"/> must reject an empty
        /// owned namespace declaration.
        /// </summary>
        [Test]
        public void FromStreamRejectsEmptyModelNamespaceUris()
        {
            Assert.That(
                () => RuntimeNodeSetSource.FromStream(
                    "source",
                    _ => new ValueTask<Stream>(Stream.Null),
                    []),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("modelNamespaceUris"));
        }

        /// <summary>
        /// <see cref="FileRuntimeNodeSetSource.Name"/> returns the file path
        /// supplied at construction.
        /// </summary>
        [Test]
        public void FileSourceNameIsFilePath()
        {
            FileRuntimeNodeSetSource source = RuntimeNodeSetSource.FromFile(m_testNodeSetFile);

            Assert.That(source.Name, Is.EqualTo(m_testNodeSetFile));
        }

        /// <summary>
        /// <see cref="FileRuntimeNodeSetSource.FilePath"/> exposes the file path.
        /// </summary>
        [Test]
        public void FileSourceFilePathPropertyMatchesSuppliedPath()
        {
            FileRuntimeNodeSetSource source = RuntimeNodeSetSource.FromFile(m_testNodeSetFile);

            Assert.That(source.FilePath, Is.EqualTo(m_testNodeSetFile));
        }

        /// <summary>
        /// <see cref="FileRuntimeNodeSetSource.ModelNamespaceUris"/> reflects the
        /// model URIs extracted from the file's <c>Models</c> section at construction time.
        /// </summary>
        [Test]
        public void FileSourceModelNamespaceUrisExtractedFromFile()
        {
            FileRuntimeNodeSetSource source = RuntimeNodeSetSource.FromFile(m_testNodeSetFile);

            Assert.That(source.ModelNamespaceUris.Count, Is.EqualTo(1));
            Assert.That(source.ModelNamespaceUris[0], Is.EqualTo(kTestNamespaceUri));
        }

        /// <summary>
        /// <see cref="FileRuntimeNodeSetSource.OpenReadAsync"/> returns a fresh readable
        /// stream positioned at the beginning of the file.
        /// </summary>
        [Test]
        public async Task FileSourceOpenReadReturnsReadableStreamAsync()
        {
            FileRuntimeNodeSetSource source = RuntimeNodeSetSource.FromFile(m_testNodeSetFile);

            Stream stream = await source.OpenReadAsync().ConfigureAwait(false);
            using (stream)
            {
                Assert.That(stream, Is.Not.Null);
                Assert.That(stream.CanRead, Is.True);
                Assert.That(stream.Position, Is.Zero);
            }
        }

        /// <summary>
        /// <see cref="FileRuntimeNodeSetSource.OpenReadAsync"/> propagates a cancelled
        /// <see cref="CancellationToken"/> by throwing
        /// <see cref="OperationCanceledException"/>.
        /// </summary>
        [Test]
        public void FileSourceOpenReadHonoursCancellation()
        {
            FileRuntimeNodeSetSource source = RuntimeNodeSetSource.FromFile(m_testNodeSetFile);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await source.OpenReadAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// Constructing a <see cref="FileRuntimeNodeSetSource"/> for a file without a
        /// <c>Models</c> section throws unless explicit namespace URIs are provided.
        /// </summary>
        [Test]
        public void FileSourceRequiresModelsOrExplicitNamespaceUris()
        {
            string noModelsFile = Path.Combine(
                Path.GetDirectoryName(m_testNodeSetFile),
                Guid.NewGuid().ToString("N") + ".NoModels.NodeSet2.xml");
            File.WriteAllText(noModelsFile, kNoModelsNodeSetXml, Encoding.UTF8);

            try
            {
                Assert.That(
                    () => RuntimeNodeSetSource.FromFile(noModelsFile),
                    Throws.InvalidOperationException.With.Message.Contains("Models section"));
            }
            finally
            {
                File.Delete(noModelsFile);
            }
        }

        /// <summary>
        /// Constructing a <see cref="FileRuntimeNodeSetSource"/> for a file without a
        /// <c>Models</c> section succeeds when explicit namespace URIs are provided.
        /// </summary>
        [Test]
        public void FileSourceWithExplicitNsSucceedsForLegacyNodeSet()
        {
            string noModelsFile = Path.Combine(
                Path.GetDirectoryName(m_testNodeSetFile),
                Guid.NewGuid().ToString("N") + ".NoModelsExplicit.NodeSet2.xml");
            File.WriteAllText(noModelsFile, kNoModelsNodeSetXml, Encoding.UTF8);

            try
            {
                FileRuntimeNodeSetSource source = RuntimeNodeSetSource.FromFile(
                    noModelsFile,
                    [kTestNamespaceUri]);

                Assert.That(source.ModelNamespaceUris.Count, Is.EqualTo(1));
                Assert.That(source.ModelNamespaceUris[0], Is.EqualTo(kTestNamespaceUri));
            }
            finally
            {
                File.Delete(noModelsFile);
            }
        }

        /// <summary>
        /// <see cref="StreamRuntimeNodeSetSource.Name"/> returns the name supplied
        /// at construction.
        /// </summary>
        [Test]
        public void StreamSourceNameMatchesConstructorArgument()
        {
            StreamRuntimeNodeSetSource source = RuntimeNodeSetSource.FromStream(
                "MySource",
                _ => new ValueTask<Stream>(Stream.Null),
                [kTestNamespaceUri]);

            Assert.That(source.Name, Is.EqualTo("MySource"));
        }

        /// <summary>
        /// <see cref="StreamRuntimeNodeSetSource.ModelNamespaceUris"/> returns exactly
        /// the URIs supplied at construction.
        /// </summary>
        [Test]
        public void StreamSourceModelNamespaceUrisMatchDeclared()
        {
            ArrayOf<string> declared = ["urn:a", "urn:b"];

            StreamRuntimeNodeSetSource source = RuntimeNodeSetSource.FromStream(
                "S",
                _ => new ValueTask<Stream>(Stream.Null),
                declared);

            Assert.That(source.ModelNamespaceUris.Count, Is.EqualTo(2));
            Assert.That(source.ModelNamespaceUris[0], Is.EqualTo("urn:a"));
            Assert.That(source.ModelNamespaceUris[1], Is.EqualTo("urn:b"));
        }

        /// <summary>
        /// <see cref="StreamRuntimeNodeSetSource.OpenReadAsync"/> returns the stream
        /// produced by the delegate supplied at construction.
        /// </summary>
        [Test]
        public async Task StreamSourceOpenReadDelegatesToSuppliedFactoryAsync()
        {
            var expected = new MemoryStream(Encoding.UTF8.GetBytes(kMinimalNodeSetXml));

            StreamRuntimeNodeSetSource source = RuntimeNodeSetSource.FromStream(
                "TestStream",
                _ => new ValueTask<Stream>(expected),
                [kTestNamespaceUri]);

            Stream result = await source.OpenReadAsync().ConfigureAwait(false);

            Assert.That(result, Is.SameAs(expected));
            expected.Dispose();
        }

        /// <summary>
        /// <see cref="StreamRuntimeNodeSetSource.OpenReadAsync"/> forwards the
        /// <see cref="CancellationToken"/> to the delegate.
        /// </summary>
        [Test]
        public async Task StreamSourceOpenReadForwardsCancellationTokenAsync()
        {
            CancellationToken capturedToken = CancellationToken.None;

            StreamRuntimeNodeSetSource source = RuntimeNodeSetSource.FromStream(
                "CancelTest",
                ct =>
                {
                    capturedToken = ct;
                    return new ValueTask<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(kMinimalNodeSetXml)));
                },
                [kTestNamespaceUri]);

            using var cts = new CancellationTokenSource();

            Stream stream = await source.OpenReadAsync(cts.Token).ConfigureAwait(false);
            stream.Dispose();

            Assert.That(capturedToken, Is.EqualTo(cts.Token));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.ScanModelUris"/> extracts the
        /// <c>ModelUri</c> attributes from a file's <c>Models</c> section.
        /// </summary>
        [Test]
        public void ScanModelUrisExtractsModelUrisFromFile()
        {
            ArrayOf<string> uris = RuntimeNodeSetSource.ScanModelUris(m_testNodeSetFile);

            Assert.That(uris.Count, Is.EqualTo(1));
            Assert.That(uris[0], Is.EqualTo(kTestNamespaceUri));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.ScanModelUris"/> returns an empty array
        /// when the file has no <c>Models</c> section.
        /// </summary>
        [Test]
        public void ScanModelUrisReturnsEmptyForFileWithoutModels()
        {
            string noModelsFile = Path.Combine(
                Path.GetDirectoryName(m_testNodeSetFile),
                Guid.NewGuid().ToString("N") + ".ScanNoModels.NodeSet2.xml");
            File.WriteAllText(noModelsFile, kNoModelsNodeSetXml, Encoding.UTF8);

            try
            {
                ArrayOf<string> uris = RuntimeNodeSetSource.ScanModelUris(noModelsFile);

                Assert.That(uris.Count, Is.Zero);
            }
            finally
            {
                File.Delete(noModelsFile);
            }
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.ExtractModelUris"/> returns the
        /// <c>Models[].ModelUri</c> values when the <c>Models</c> array is present.
        /// </summary>
        [Test]
        public void ExtractModelUrisUsesModelsSection()
        {
            UANodeSet nodeSet = UANodeSet.Read(
                new MemoryStream(Encoding.UTF8.GetBytes(kMinimalNodeSetXml)));

            ArrayOf<string> uris = RuntimeNodeSetSource.ExtractModelUris(nodeSet, default);

            Assert.That(uris.Count, Is.EqualTo(1));
            Assert.That(uris[0], Is.EqualTo(kTestNamespaceUri));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetSource.ExtractModelUris"/> falls back to the
        /// caller-declared URIs when the NodeSet has no <c>Models</c> element.
        /// </summary>
        [Test]
        public void ExtractModelUrisFallsBackToDeclaredWhenModelsAbsent()
        {
            UANodeSet nodeSet = UANodeSet.Read(
                new MemoryStream(Encoding.UTF8.GetBytes(kNoModelsNodeSetXml)));

            ArrayOf<string> declared = [kTestNamespaceUri];

            ArrayOf<string> uris = RuntimeNodeSetSource.ExtractModelUris(nodeSet, declared);

            Assert.That(uris.Count, Is.EqualTo(1));
            Assert.That(uris[0], Is.EqualTo(kTestNamespaceUri));
        }
    }
}
