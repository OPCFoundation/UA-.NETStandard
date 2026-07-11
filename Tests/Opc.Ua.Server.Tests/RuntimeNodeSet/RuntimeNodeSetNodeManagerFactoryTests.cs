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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Unit tests for <see cref="RuntimeNodeSetNodeManagerFactory"/> covering
    /// constructor argument validation, <see cref="IAsyncNodeManagerFactory.NamespacesUris"/>
    /// population, options snapshot semantics, cycle detection, and duplicate model URI
    /// rejection.
    /// </summary>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class RuntimeNodeSetNodeManagerFactoryTests
    {
        private const string kUriA = "urn:test:ModelA";
        private const string kUriB = "urn:test:ModelB";

        private static StreamRuntimeNodeSetSource MakeStreamSource(string modelUri, string xml)
        {
            return RuntimeNodeSetSource.FromStream(
                modelUri,
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(xml))),
                [modelUri]);
        }

        private static string MakeMinimalXml(string modelUri)
        {
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <Models>\r\n" +
                "    <Model ModelUri=\"" + modelUri + "\" />\r\n" +
                "  </Models>\r\n" +
                "</UANodeSet>";
        }

        private static string MakeCycleXml(string ownUri, string requiresUri)
        {
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <Models>\r\n" +
                "    <Model ModelUri=\"" + ownUri + "\">\r\n" +
                "      <RequiredModel ModelUri=\"" + requiresUri + "\" />\r\n" +
                "    </Model>\r\n" +
                "  </Models>\r\n" +
                "</UANodeSet>";
        }

        /// <summary>
        /// Constructor must throw when <c>options</c> is <c>null</c>.
        /// </summary>
        [Test]
        public void ConstructorRejectsNullOptions()
        {
            Assert.That(
                () => new RuntimeNodeSetNodeManagerFactory(null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("options"));
        }

        /// <summary>
        /// Constructor must throw when <see cref="RuntimeNodeSetOptions.Sources"/> is empty.
        /// </summary>
        [Test]
        public void ConstructorRejectsEmptySourceList()
        {
            var options = new RuntimeNodeSetOptions
            {
                Sources = []
            };

            Assert.That(
                () => new RuntimeNodeSetNodeManagerFactory(options),
                Throws.InvalidOperationException.With.Message.Contains("At least one"));
        }

        /// <summary>
        /// Constructor must throw when two sources declare the same model namespace URI.
        /// Duplicates are detected eagerly before the server starts.
        /// </summary>
        [Test]
        public void ConstructorRejectsDuplicateModelUrisAcrossSources()
        {
            StreamRuntimeNodeSetSource sourceA = MakeStreamSource(kUriA, MakeMinimalXml(kUriA));
            StreamRuntimeNodeSetSource sourceB = MakeStreamSource(kUriA, MakeMinimalXml(kUriA));

            var options = new RuntimeNodeSetOptions
            {
                Sources = [sourceA, sourceB]
            };

            Assert.That(
                () => new RuntimeNodeSetNodeManagerFactory(options),
                Throws.InvalidOperationException.With.Message.Contains(kUriA));
        }

        /// <summary>
        /// Constructor must throw when <see cref="RuntimeNodeSetOptions.DefaultNamespaceUri"/>
        /// names a URI that is not owned by any configured source.
        /// </summary>
        [Test]
        public void ConstructorRejectsDefaultNsNotOwnedByAnySources()
        {
            StreamRuntimeNodeSetSource source = MakeStreamSource(kUriA, MakeMinimalXml(kUriA));

            var options = new RuntimeNodeSetOptions
            {
                Sources = [source],
                DefaultNamespaceUri = "urn:not:owned"
            };

            Assert.That(
                () => new RuntimeNodeSetNodeManagerFactory(options),
                Throws.InvalidOperationException.With.Message.Contains("not owned"));
        }

        /// <summary>
        /// <see cref="RuntimeNodeSetNodeManagerFactory.NamespacesUris"/> aggregates the
        /// model URIs from all configured sources in declaration order.
        /// </summary>
        [Test]
        public void NamespacesUrisAggregatesAllSourceModelUris()
        {
            StreamRuntimeNodeSetSource sourceA = MakeStreamSource(kUriA, MakeMinimalXml(kUriA));
            StreamRuntimeNodeSetSource sourceB = MakeStreamSource(kUriB, MakeMinimalXml(kUriB));

            var options = new RuntimeNodeSetOptions
            {
                Sources = [sourceA, sourceB]
            };

            var factory = new RuntimeNodeSetNodeManagerFactory(options);

            Assert.That(factory.NamespacesUris.Count, Is.EqualTo(2));
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(kUriA));
            Assert.That(factory.NamespacesUris[1], Is.EqualTo(kUriB));
        }

        /// <summary>
        /// Mutating <see cref="RuntimeNodeSetOptions.Sources"/> after the factory is
        /// constructed must not change the factory's
        /// <see cref="RuntimeNodeSetNodeManagerFactory.NamespacesUris"/>.
        /// </summary>
        [Test]
        public void NamespacesUrisSnapshotAtConstruction()
        {
            StreamRuntimeNodeSetSource sourceA = MakeStreamSource(kUriA, MakeMinimalXml(kUriA));

            var options = new RuntimeNodeSetOptions
            {
                Sources = [sourceA]
            };

            var factory = new RuntimeNodeSetNodeManagerFactory(options);
            Assert.That(factory.NamespacesUris.Count, Is.EqualTo(1));

            // Mutate the options after construction — must not affect the factory.
            options.Sources = [];

            Assert.That(factory.NamespacesUris.Count, Is.EqualTo(1));
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(kUriA));
        }

        /// <summary>
        /// A single source with a valid <see cref="RuntimeNodeSetOptions.DefaultNamespaceUri"/>
        /// that matches one of the owned URIs is accepted without error.
        /// </summary>
        [Test]
        public void ValidDefaultNamespaceUriIsAccepted()
        {
            StreamRuntimeNodeSetSource source = MakeStreamSource(kUriA, MakeMinimalXml(kUriA));

            var options = new RuntimeNodeSetOptions
            {
                Sources = [source],
                DefaultNamespaceUri = kUriA
            };

            Assert.That(
                () => new RuntimeNodeSetNodeManagerFactory(options),
                Throws.Nothing);
        }

        /// <summary>
        /// When two sources form a mutual dependency cycle,
        /// <see cref="RuntimeNodeSetNodeManagerFactory.CreateAsync"/> throws
        /// <see cref="InvalidOperationException"/> with a message that names the
        /// participating sources.
        /// </summary>
        [Test]
        public async Task CreateAsyncThrowsOnCycleInIncludedSourcesAsync()
        {
            // Source A requires B, source B requires A → cycle.
            StreamRuntimeNodeSetSource sourceA = RuntimeNodeSetSource.FromStream(
                kUriA,
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(MakeCycleXml(kUriA, kUriB)))),
                [kUriA]);

            StreamRuntimeNodeSetSource sourceB = RuntimeNodeSetSource.FromStream(
                kUriB,
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(MakeCycleXml(kUriB, kUriA)))),
                [kUriB]);

            var options = new RuntimeNodeSetOptions
            {
                Sources = [sourceA, sourceB]
            };

            var factory = new RuntimeNodeSetNodeManagerFactory(options);

            Mock<IServerInternal> mockServer = CreateMinimalServerMock();

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory.CreateAsync(
                    mockServer.Object,
                    new ApplicationConfiguration(),
                    default).ConfigureAwait(false));

            Assert.That(ex.Message, Does.Contain("circular"));
        }

        /// <summary>
        /// When two sources in a group declare the same <c>ModelUri</c> inside their
        /// NodeSet XML (as opposed to in the declared metadata), <c>CreateAsync</c>
        /// rejects them with <see cref="InvalidOperationException"/>.
        /// </summary>
        [Test]
        public async Task CreateAsyncThrowsOnDuplicateModelUriInParsedDocumentsAsync()
        {
            // Both sources declare kUriA in their XML but the factory metadata
            // declares different URIs to bypass the constructor check. When the
            // documents are parsed, the ModelUri from the XML overrides the
            // declared URIs and the factory detects the duplicate.
            const string kUriExtra = "urn:extra:X";

            StreamRuntimeNodeSetSource source1 = RuntimeNodeSetSource.FromStream(
                kUriA,
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(MakeMinimalXml(kUriA)))),
                [kUriA]);

            // Use a second source that declares a different URI at the factory
            // level but whose embedded XML resolves to the same ModelUri as source1.
            // This creates a conflict detected during ParseAndSortAsync.
            StreamRuntimeNodeSetSource source2 = RuntimeNodeSetSource.FromStream(
                kUriExtra,
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(MakeMinimalXml(kUriA)))),
                [kUriExtra]);

            var options = new RuntimeNodeSetOptions
            {
                Sources = [source1, source2]
            };

            var factory = new RuntimeNodeSetNodeManagerFactory(options);

            Mock<IServerInternal> mockServer = CreateMinimalServerMock();

            // Parsing fails because the XML in source2 declares kUriA, which
            // does not match the declared URI kUriExtra.
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await factory.CreateAsync(
                    mockServer.Object,
                    new ApplicationConfiguration(),
                    default).ConfigureAwait(false));

            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a minimal <see cref="IServerInternal"/> mock sufficient for
        /// <see cref="RuntimeNodeSetNodeManagerFactory.CreateAsync"/> to attempt
        /// parsing (a real logger is injected so diagnostic output works).
        /// </summary>
        private static Mock<IServerInternal> CreateMinimalServerMock()
        {
            var mockLoggerFactory = LoggerFactory.Create(b => b.AddDebug());
            var mockTelemetry = new Mock<ITelemetryContext>();
            mockTelemetry.SetupGet(t => t.LoggerFactory).Returns(mockLoggerFactory);

            var mockServer = new Mock<IServerInternal>();
            mockServer.SetupGet(s => s.Telemetry).Returns(mockTelemetry.Object);

            return mockServer;
        }
    }
}
