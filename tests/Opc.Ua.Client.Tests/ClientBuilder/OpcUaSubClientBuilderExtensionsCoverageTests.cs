// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client.AliasNames;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Historian;
using Opc.Ua.Client.Network;
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

        /// <summary>
        /// Rejects null root and client builders.
        /// </summary>
        [Test]
        public void AddNetworkModelClientRejectsNullBuilders()
        {
            Assert.That(
                () => ((IOpcUaBuilder)null!).AddNetworkModelClient(),
                Throws.ArgumentNullException);
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddNetworkModelClient(),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Preserves the fluent builder and registers one container-owned client factory.
        /// </summary>
        [Test]
        public void AddNetworkModelClientRegistersOneFactoryAndPreservesTheBuilder()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(_ => { });

            IOpcUaClientBuilder returned = builder.AddNetworkModelClient();
            builder.AddNetworkModelClient();

            using ServiceProvider sp = services.BuildServiceProvider();
            NetworkModelClientFactory factory = sp.GetRequiredService<NetworkModelClientFactory>();
            Assert.That(returned, Is.SameAs(builder));
            Assert.That(sp.GetServices<NetworkModelClientFactory>().Count(), Is.EqualTo(1));
            Assert.That(factory, Is.SameAs(sp.GetRequiredService<NetworkModelClientFactory>()));
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
