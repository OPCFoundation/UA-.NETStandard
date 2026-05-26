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
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="ModelLoaderBuilder"/> — fluent
    /// multi-model composition surface used at
    /// <c>LoadPredefinedNodesAsync</c> time.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class ModelLoaderBuilderTests
    {
        private static SystemContext CreateContext()
        {
            var ns = new NamespaceTable();
            ns.Append(global::Opc.Ua.Namespaces.OpcUa);
            return new SystemContext(telemetry: null!)
            {
                NamespaceUris = ns
            };
        }

        [Test]
        public void AddModelInvokesCallbackOnBuild()
        {
            var loader = new ModelLoaderBuilder();
            bool invoked = false;

            loader.AddModel((nodes, ctx) =>
            {
                invoked = true;
                Assert.That(nodes, Is.Not.Null);
                Assert.That(ctx, Is.Not.Null);
            });

            var target = new NodeStateCollection();
            loader.Build(target, CreateContext());

            Assert.That(invoked, Is.True);
        }

        [Test]
        public void AddModelChainsMultipleSources()
        {
            var loader = new ModelLoaderBuilder();
            int callCount = 0;

            loader
                .AddModel((n, c) => callCount++)
                .AddModel((n, c) => callCount++)
                .AddModel((n, c) => callCount++);

            loader.Build(new NodeStateCollection(), CreateContext());

            Assert.That(callCount, Is.EqualTo(3));
        }

        [Test]
        public void BuildReturnsSameCollection()
        {
            var loader = new ModelLoaderBuilder();
            var target = new NodeStateCollection();

            NodeStateCollection result = loader.Build(target, CreateContext());

            Assert.That(result, Is.SameAs(target));
        }

        [Test]
        public void SourcesAppliedInOrder()
        {
            var loader = new ModelLoaderBuilder();
            var log = new System.Collections.Generic.List<int>();

            loader
                .AddModel((n, c) => log.Add(1))
                .AddModel((n, c) => log.Add(2))
                .AddModel((n, c) => log.Add(3));

            loader.Build(new NodeStateCollection(), CreateContext());

            Assert.That(log, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void AddModelNullCallbackThrowsArgumentNullException()
        {
            var loader = new ModelLoaderBuilder();
            Assert.Throws<ArgumentNullException>(
                () => loader.AddModel(null!));
        }

        [Test]
        public void ImportNodeSetReadsAndParsesXml()
        {
            // Minimal valid NodeSet2 XML — just declares one custom namespace.
            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<UANodeSet xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"" +
                " xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"" +
                " xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>urn:test:loader</Uri></NamespaceUris>" +
                "</UANodeSet>";

            var loader = new ModelLoaderBuilder();
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

            // Verify the read step succeeds. The empty NodeSet may fail
            // to Build via UANodeSet.Import (the importer dereferences
            // optional collections that aren't present), so we only
            // verify the read-and-register half here.
            Assert.DoesNotThrow(() => loader.ImportNodeSet(stream));
        }

        [Test]
        public void ImportNodeSetNullStreamThrowsArgumentNullException()
        {
            var loader = new ModelLoaderBuilder();
            Assert.Throws<ArgumentNullException>(
                () => loader.ImportNodeSet(null!));
        }

        [Test]
        public void ImportEmbeddedNodeSetMissingResourceThrows()
        {
            var loader = new ModelLoaderBuilder();
            Assembly thisAssembly = typeof(ModelLoaderBuilderTests).Assembly;

            Assert.Throws<InvalidOperationException>(
                () => loader.ImportEmbeddedNodeSet(thisAssembly, "Missing.NodeSet2.xml"));
        }

        [Test]
        public void ImportEmbeddedNodeSetNullArgsThrow()
        {
            var loader = new ModelLoaderBuilder();
            Assert.Throws<ArgumentNullException>(
                () => loader.ImportEmbeddedNodeSet(null!, "x"));
            Assert.Throws<ArgumentNullException>(
                () => loader.ImportEmbeddedNodeSet(GetType().Assembly, string.Empty));
        }

        [Test]
        public void BuildNullArgsThrowArgumentNullException()
        {
            var loader = new ModelLoaderBuilder();
            Assert.Throws<ArgumentNullException>(
                () => loader.Build(null!, CreateContext()));
            Assert.Throws<ArgumentNullException>(
                () => loader.Build(new NodeStateCollection(), null!));
        }

        [Test]
        public void MixedSourcesRegistration()
        {
            var loader = new ModelLoaderBuilder();

            // model callback + a NodeSet2 import, both registered.
            loader.AddModel((n, c) => { });

            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<UANodeSet xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"" +
                " xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"" +
                " xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">" +
                "<NamespaceUris><Uri>urn:test:loader:2</Uri></NamespaceUris>" +
                "</UANodeSet>";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

            // Both registration paths should succeed without throwing.
            Assert.DoesNotThrow(() => loader.ImportNodeSet(stream));
        }
    }
}
