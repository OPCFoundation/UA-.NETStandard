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

#nullable enable

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Roles;

namespace Opc.Ua.Client.Tests.ClientBuilder
{
    /// <summary>
    /// Patch-coverage tests for <see cref="OpcUaSubClientBuilderExtensions"/>.
    /// Exercises the null guards on the <c>IOpcUaBuilder</c> overloads and
    /// the happy-path of each <c>IOpcUaClientBuilder</c> overload (both paths
    /// are new in this PR and not yet reached by sibling fixtures).
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class OpcUaSubClientBuilderExtensionsCoverageTests
    {
        [Test]
        public void AddHistorianOnBuilderNullBuilderThrows()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddHistorian(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddHistorianOnClientBuilderRegistersFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            builder.AddHistorian();

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<HistoryClientFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddRoleManagementOnBuilderNullBuilderThrows()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddRoleManagement(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddRoleManagementOnClientBuilderRegistersFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            builder.AddRoleManagement();

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<RoleManagementClientFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddFileTransferOnBuilderNullBuilderThrows()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddFileTransfer(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddFileTransferOnClientBuilderRegistersFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            builder.AddFileTransfer();

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<FileTransferClientFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddAliasNamesOnBuilderNullBuilderThrows()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddAliasNames(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddAliasNamesOnClientBuilderRegistersFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            builder.AddAliasNames();

            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<AliasNameClientFactory>(), Is.Not.Null);
        }
    }
}
