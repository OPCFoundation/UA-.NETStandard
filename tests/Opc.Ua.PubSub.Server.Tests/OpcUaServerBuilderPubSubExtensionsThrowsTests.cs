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
