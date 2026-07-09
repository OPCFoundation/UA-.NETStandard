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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.WebApi;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.ComplexTypes.Tests.Hosting
{
    [TestFixture]
    [Category("Client")]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ManagedClientBuilderTests
    {
        [Test]
        public void AddClientChainsComplexTypes()
        {
            var services = new ServiceCollection();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddClient(ConfigureValidClient)
                .AddComplexTypes()
                .AddAlarms()
                .AddWebApiTransportChannel(options => options.Encoding = WebApiEncoding.Verbose);

            Assert.That(builder.Services, Is.SameAs(services));
            using ServiceProvider sp = services.BuildServiceProvider();
            Assert.That(sp.GetService<ComplexTypeSystemFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<IComplexTypeSystemFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<AlarmClientFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<WebApiClientOptions>(), Is.Not.Null);
            Assert.That(sp.GetRequiredService<WebApiClientOptions>().Encoding, Is.EqualTo(WebApiEncoding.Verbose));
        }

        [Test]
        public void AddComplexTypesRejectsNullClientBuilder()
        {
            Assert.That(
                () => ((IOpcUaClientBuilder)null!).AddComplexTypes(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddManagedClientRegistersClientReconnectAndComplexTypes()
        {
            var services = new ServiceCollection();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddManagedClient(ConfigureValidClient);

            using ServiceProvider sp = services.BuildServiceProvider();
            OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(options.Session.LoadComplexTypes, Is.True);
            Assert.That(options.Session.ReconnectPolicy, Is.Not.Null);
            Assert.That(sp.GetService<IManagedSessionFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<System.Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ManagedSession>>>(),
                Is.Not.Null);
            Assert.That(sp.GetService<ComplexTypeSystemFactory>(), Is.Not.Null);
            Assert.That(sp.GetService<IComplexTypeSystemFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddManagedClientConfigurationOverloadEnablesComplexTypes()
        {
            var services = new ServiceCollection();
            IConfiguration configuration = new ConfigurationBuilder().Build();

            IOpcUaClientBuilder builder = services.AddOpcUa()
                .AddManagedClient(configuration);

            using ServiceProvider sp = services.BuildServiceProvider();
            OpcUaClientOptions options = sp.GetRequiredService<OpcUaClientOptions>();

            Assert.That(builder.Services, Is.SameAs(services));
            Assert.That(options.Session.LoadComplexTypes, Is.True);
            Assert.That(sp.GetService<IComplexTypeSystemFactory>(), Is.Not.Null);
        }

        [Test]
        public void AddManagedClientRejectsNullArguments()
        {
            var services = new ServiceCollection();

            Assert.That(
                () => OpcUaComplexTypesBuilderExtensions.AddManagedClient(null!, ConfigureValidClient),
                Throws.ArgumentNullException);
            Assert.That(
                () => services.AddOpcUa().AddManagedClient((System.Action<OpcUaClientOptions>)null!),
                Throws.ArgumentNullException);
        }

        private static void ConfigureValidClient(OpcUaClientOptions options)
        {
            options.Configuration = CreateConfig();
            options.Session = new ManagedSessionOptions
            {
                Endpoint = new ConfiguredEndpoint(
                    null,
                    new EndpointDescription
                    {
                        EndpointUrl = "opc.tcp://localhost:4840"
                    },
                    null)
            };
        }

        private static ApplicationConfiguration CreateConfig()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new ApplicationConfiguration(telemetry)
            {
                ApplicationUri = "urn:test:managed-client",
                ApplicationName = "managed-client",
                ClientConfiguration = new ClientConfiguration()
            };
        }
    }
}
