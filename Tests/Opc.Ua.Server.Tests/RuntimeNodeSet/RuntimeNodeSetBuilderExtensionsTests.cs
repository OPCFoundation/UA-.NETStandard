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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.Tests.RuntimeNodeSet
{
    /// <summary>
    /// Coverage tests for the <c>AddRuntimeNodeSet</c>
    /// <see cref="IOpcUaServerBuilder"/> extension methods (file path,
    /// options, and options-callback overloads) including argument
    /// validation and DI service registration.
    /// </summary>
    [TestFixture]
    [Category("RuntimeNodeSet")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public sealed class RuntimeNodeSetBuilderExtensionsTests
    {
        private const string kNamespaceUri = "urn:opcfoundation.org:RuntimeNodeSetBuilderTest";

        private const string kMinimalNodeSetXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
            "  <NamespaceUris>\r\n" +
            "    <Uri>" + kNamespaceUri + "</Uri>\r\n" +
            "  </NamespaceUris>\r\n" +
            "  <Models>\r\n" +
            "    <Model ModelUri=\"" + kNamespaceUri + "\" />\r\n" +
            "  </Models>\r\n" +
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
                "RuntimeNodeSetBuilderExtensionsTests");
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
        /// The file-path overload rejects a <c>null</c> builder.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetFilePathRejectsNullBuilder()
        {
            IOpcUaServerBuilder builder = null;

            Assert.That(
                () => builder.AddRuntimeNodeSet(m_testNodeSetFile),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        /// <summary>
        /// The file-path overload rejects a <c>null</c> file path.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetFilePathRejectsNullFilePath()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            Assert.That(
                () => builder.AddRuntimeNodeSet((string)null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("filePath"));
        }

        /// <summary>
        /// The file-path overload registers both the async factory and the
        /// node-manager registration wrapper.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetFilePathRegistersFactoryAndRegistration()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(ConfigureServer)
                .AddRuntimeNodeSet(m_testNodeSetFile);

            Assert.Multiple(() =>
            {
                Assert.That(
                    services,
                    Has.Some.Matches<ServiceDescriptor>(
                        d => d.ServiceType == typeof(IAsyncNodeManagerFactory)));
                Assert.That(
                    services,
                    Has.Some.Matches<ServiceDescriptor>(
                        d => d.ServiceType == typeof(OpcUaServerNodeManagerRegistration)));
            });

            using ServiceProvider sp = services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = sp.GetRequiredService<IAsyncNodeManagerFactory>();
            Assert.That(factory.NamespacesUris, Has.Count.EqualTo(1));
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(kNamespaceUri));
        }

        /// <summary>
        /// The file-path overload accepts and stores a fluent configure callback.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetFilePathWithConfigureRegistersFactory()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder()
                .AddRuntimeNodeSet(m_testNodeSetFile, _ => { });

            Assert.That(builder, Is.Not.Null);
        }

        /// <summary>
        /// The options overload rejects a <c>null</c> builder.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetOptionsRejectsNullBuilder()
        {
            IOpcUaServerBuilder builder = null;
            var options = new RuntimeNodeSetOptions
            {
                Sources = [MakeStreamSource()]
            };

            Assert.That(
                () => builder.AddRuntimeNodeSet(options),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        /// <summary>
        /// The options overload rejects a <c>null</c> options instance.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetOptionsRejectsNullOptions()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            Assert.That(
                () => builder.AddRuntimeNodeSet((RuntimeNodeSetOptions)null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("options"));
        }

        /// <summary>
        /// The options overload registers the factory and returns the same builder.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetOptionsRegistersFactory()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            var options = new RuntimeNodeSetOptions
            {
                Sources = [MakeStreamSource()]
            };

            IOpcUaServerBuilder result = builder.AddRuntimeNodeSet(options);

            Assert.That(result, Is.SameAs(builder));

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = sp.GetRequiredService<IAsyncNodeManagerFactory>();
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(kNamespaceUri));
        }

        /// <summary>
        /// The options-callback overload rejects a <c>null</c> builder.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetActionRejectsNullBuilder()
        {
            IOpcUaServerBuilder builder = null;

            Assert.That(
                () => builder.AddRuntimeNodeSet(_ => { }),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        /// <summary>
        /// The options-callback overload rejects a <c>null</c> callback.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetActionRejectsNullConfigure()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            Assert.That(
                () => builder.AddRuntimeNodeSet((Action<RuntimeNodeSetOptions>)null),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("configure"));
        }

        /// <summary>
        /// The options-callback overload invokes the callback and registers the factory.
        /// </summary>
        [Test]
        public void AddRuntimeNodeSetActionInvokesCallbackAndRegistersFactory()
        {
            bool callbackInvoked = false;
            IOpcUaServerBuilder builder = CreateServerBuilder()
                .AddRuntimeNodeSet(options =>
                {
                    callbackInvoked = true;
                    options.Sources = [MakeStreamSource()];
                });

            Assert.That(callbackInvoked, Is.True);

            using ServiceProvider sp = builder.Services.BuildServiceProvider();
            IAsyncNodeManagerFactory factory = sp.GetRequiredService<IAsyncNodeManagerFactory>();
            Assert.That(factory.NamespacesUris[0], Is.EqualTo(kNamespaceUri));
        }

        private static IOpcUaServerBuilder CreateServerBuilder()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            return services.AddOpcUa().AddServer(ConfigureServer);
        }

        private static void ConfigureServer(OpcUaServerOptions options)
        {
            options.ApplicationName = "RuntimeNodeSetBuilderTestServer";
            options.ApplicationUri = "urn:test:RuntimeNodeSetBuilderTestServer";
            options.ProductUri = "urn:test:product";
        }

        private static StreamRuntimeNodeSetSource MakeStreamSource()
        {
            return RuntimeNodeSetSource.FromStream(
                "RuntimeNodeSetBuilderTest",
                _ => new ValueTask<Stream>(
                    new MemoryStream(Encoding.UTF8.GetBytes(kMinimalNodeSetXml))),
                [kNamespaceUri]);
        }
    }
}
