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
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Configuration;
using Opc.Ua.Lds.Server.Hosting;

namespace Opc.Ua.Lds.Tests.Hosting
{
    /// <summary>
    /// Unit tests for the <see cref="OpcUaLdsServerBuilderExtensions"/>
    /// fluent DI surface. Verifies argument validation, expected service
    /// registrations, duplicate-registration guard and coexistence with the
    /// regular OPC UA server.
    /// </summary>
    [TestFixture]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class OpcUaLdsServerBuilderTests
    {
        [Test]
        public void AddLdsServerThrowsForNullArgs()
        {
            Assert.That(
                () => OpcUaLdsServerBuilderExtensions.AddLdsServer(null!, _ => { }),
                Throws.ArgumentNullException);

            var services = new ServiceCollection();
            IOpcUaBuilder builder = services.AddOpcUa();
            Assert.That(
                () => builder.AddLdsServer((Action<LdsServerOptions>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddLdsServerRegistersExpectedServices()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa().AddLdsServer(opt =>
            {
                opt.ApplicationName = "TestLds";
                opt.ApplicationUri = "urn:localhost:UA:TestLds";
                opt.ProductUri = "uri:opcfoundation.org:TestLds";
                opt.EndpointUrls.Add("opc.tcp://localhost:0/TestLds");
                opt.EnableMulticast = false;
            });

            using ServiceProvider sp = services.BuildServiceProvider();

            Assert.That(sp.GetService<IOptions<LdsServerOptions>>(), Is.Not.Null);
            Assert.That(sp.GetService<IOptions<LdsServerOptions>>()!.Value.ApplicationName,
                Is.EqualTo("TestLds"));
            Assert.That(sp.GetService<ITelemetryContext>(), Is.Not.Null);
            Assert.That(sp.GetService<IApplicationInstanceFactory>(), Is.Not.Null);

            IEnumerable<IHostedService> hosted = sp.GetServices<IHostedService>();
            bool foundLdsHost = false;
            foreach (IHostedService h in hosted)
            {
                if (h is LdsServerHostedService)
                {
                    foundLdsHost = true;
                    break;
                }
            }
            Assert.That(foundLdsHost,
                "AddLdsServer should register an LdsServerHostedService as IHostedService.");
        }

        [Test]
        public void AddLdsServerReturnsBuilderWithServices()
        {
            var services = new ServiceCollection();
            ILdsServerBuilder builder = services.AddOpcUa()
                .AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds";
                });

            Assert.That(builder, Is.Not.Null);
            Assert.That(builder.Services, Is.SameAs(services));
        }

        [Test]
        public void AddLdsServerThrowsOnDuplicateRegistration()
        {
            var services = new ServiceCollection();
            IOpcUaBuilder opcUa = services.AddOpcUa();
            opcUa.AddLdsServer(opt =>
            {
                opt.ApplicationUri = "urn:localhost:UA:TestLds";
                opt.ProductUri = "uri:opcfoundation.org:TestLds";
            });

            Assert.That(
                () => opcUa.AddLdsServer(opt =>
                {
                    opt.ApplicationUri = "urn:localhost:UA:TestLds2";
                    opt.ProductUri = "uri:opcfoundation.org:TestLds2";
                }),
                Throws.InvalidOperationException);
        }

        [Test]
        public void AddLdsServerCanCoexistWithAddServer()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            Assert.That(
                () => services.AddOpcUa()
                    .AddServer(opt =>
                    {
                        opt.ApplicationName = "TestServer";
                        opt.ApplicationUri = "urn:localhost:UA:TestServer";
                        opt.ProductUri = "uri:opcfoundation.org:TestServer";
                        opt.EndpointUrls.Add("opc.tcp://localhost:0/TestServer");
                    }),
                Throws.Nothing);

            Assert.That(
                () => services.AddOpcUa()
                    .AddLdsServer(opt =>
                    {
                        opt.ApplicationName = "TestLds";
                        opt.ApplicationUri = "urn:localhost:UA:TestLds";
                        opt.ProductUri = "uri:opcfoundation.org:TestLds";
                        opt.EndpointUrls.Add("opc.tcp://localhost:0/TestLds");
                        opt.EnableMulticast = false;
                    }),
                Throws.Nothing);

            using ServiceProvider sp = services.BuildServiceProvider();
            IEnumerable<IHostedService> hosted = sp.GetServices<IHostedService>();
            bool foundLds = false;
            bool foundServer = false;
            foreach (IHostedService h in hosted)
            {
                if (h is LdsServerHostedService)
                {
                    foundLds = true;
                }
                else if (h.GetType().Name == "OpcUaServerHostedService")
                {
                    foundServer = true;
                }
            }
            Assert.That(foundLds, "LDS hosted service must be registered.");
            Assert.That(foundServer, "Regular server hosted service must be registered.");
        }
    }
}
