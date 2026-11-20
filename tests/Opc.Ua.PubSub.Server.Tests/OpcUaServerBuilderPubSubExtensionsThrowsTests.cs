/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Negative-path coverage for
    /// <c>OpcUaServerBuilderPubSubExtensions.AddPubSub</c>: missing
    /// PubSub runtime + missing OPC UA server must surface
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1", Summary = "DI registration error contract")]
    public class OpcUaServerBuilderPubSubExtensionsThrowsTests
    {
        [Test]
        public void AddPubSub_WhenPubSubRuntimeNotRegistered_ThrowsInvalidOperationException()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(
                () => serverBuilder.AddPubSub());
            Assert.That(
                ex!.Message,
                Does.Contain("IOpcUaBuilder.AddPubSub").Or.Contains("PubSub runtime"));
        }

        [Test]
        public void AddPubSub_WithConfigurationSection_WhenRuntimeNotRegistered_Throws()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder serverBuilder = services
                .AddOpcUa()
                .AddServer(opt => { });

            IConfigurationRoot config = new ConfigurationBuilder().Build();
            Assert.That(
                () => serverBuilder.AddPubSub(config),
                Throws.InvalidOperationException);
        }
    }
}
