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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Pumps.Server.Hosting;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Covers the OPC 40223 hosting surface without standing a server up: the
    /// DI registrations, the ownership guard that refuses a second Device
    /// Integration manager, the options validation, the factory namespace
    /// list, and the thin client factory the container resolves.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("Hosting")]
    public sealed class PumpsHostingTests
    {
        [Test]
        public void AddPumpsRegistersTheNodeManagerFactoryAndClaimsDi()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            builder.AddPumps(options => options.OrganizeIntoMachinesFolder = false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    builder.Services.Any(descriptor =>
                        descriptor.ServiceType == typeof(PumpsNodeManagerFactory)),
                    Is.True,
                    "AddPumps registers the OPC 40223 node manager factory.");
                Assert.That(
                    builder.Services.Any(descriptor =>
                        descriptor.ServiceType == typeof(DiAddressSpaceOwnership)),
                    Is.True,
                    "AddPumps owns the Device Integration address space.");
            });
        }

        [Test]
        public void AddPumpsWithNullBuilderThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => OpcUaPumpsServerBuilderExtensions.AddPumps(null!));
        }

        [Test]
        public void ASecondDiOwningRegistrationIsRefused()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddPumps();

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => builder.AddPumps())!;
            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Does.Contain("already owned by"));
                Assert.That(
                    exception.Message,
                    Does.Contain("AddOpcUaPumps"),
                    "The failure must name the way out.");
            });
        }

        [Test]
        public void AddPumpsAfterAnotherDiOwnerIsRefused()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.Services.AddSingleton(new DiAddressSpaceOwnership("AddRobotics"));

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => builder.AddPumps())!;
            Assert.That(exception.Message, Does.Contain("AddRobotics"));
        }

        [Test]
        public void ConfigurePumpsRegistersAConfiguratorForBothOverloads()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();
            builder.AddPumps();

            builder.ConfigurePumps(_ => { });
            builder.ConfigurePumps(_ => default);

            Assert.Multiple(() =>
            {
                Assert.That(
                    builder.Services.Count(descriptor =>
                        descriptor.ServiceType == typeof(IDiPostSetupConfigurator)),
                    Is.EqualTo(2),
                    "each ConfigurePumps overload registers one configurator.");
                Assert.That(
                    builder.Services.Any(descriptor =>
                        descriptor.ServiceType == typeof(IDiPostSetupRunner)),
                    Is.True,
                    "ConfigurePumps registers the post-setup runner.");
            });
        }

        [Test]
        public void ConfigurePumpsRejectsNullArguments()
        {
            IOpcUaServerBuilder builder = CreateServerBuilder();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    OpcUaPumpsServerBuilderExtensions.ConfigurePumps(
                        null!,
                        (Func<IPumpsSetupContext, ValueTask>)(_ => default)));
                Assert.Throws<ArgumentNullException>(() =>
                    builder.ConfigurePumps((Func<IPumpsSetupContext, ValueTask>)null!));
                Assert.Throws<ArgumentNullException>(() =>
                    builder.ConfigurePumps((Action<IPumpsSetupContext>)null!));
            });
        }

        [Test]
        public void AddPumpsClientRegistersAndResolvesTheFactory()
        {
            var services = new ServiceCollection();
            IOpcUaClientBuilder builder = services.AddOpcUa().AddClient(options =>
            {
                options.ApplicationName = "PumpsClientCoverage";
                options.ApplicationUri =
                    "urn:localhost:OPCFoundation:PumpsClientCoverage";
            });

            builder.AddPumpsClient();

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    provider.GetRequiredService<PumpsClientFactory>(),
                    Is.Not.Null);
                Assert.That(
                    provider.GetRequiredService<
                        Func<CancellationToken, Task<PumpsClient>>>(),
                    Is.Not.Null);
            });
        }

        [Test]
        public void AddPumpsClientWithNullBuilderThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => OpcUaPumpsClientBuilderExtensions.AddPumpsClient(null!));
        }

        [Test]
        public void PumpsClientFactoryConstructorRejectsNulls()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    new PumpsClientFactory(null!, NUnitTelemetryContext.Create()));
                Assert.Throws<ArgumentNullException>(() =>
                    new PumpsClientFactory(
                        _ => Task.FromResult<ManagedSession>(null!),
                        null!));
            });
        }

        [Test]
        public void SessionPumpsExtensionsRejectsANullSession()
        {
            // The telemetry-null branch needs a live session and is covered by
            // PumpsCoverageTests, where one is connected.
            Assert.Throws<ArgumentNullException>(
                () => SessionPumpsExtensions.Pumps(null!, NUnitTelemetryContext.Create()));
        }

        [Test]
        public void OptionsValidationRejectsEmptyAdditionalNamespaceUris()
        {
            Assert.Multiple(() =>
            {
                ArgumentException empty = Assert.Throws<ArgumentException>(() =>
                    new PumpsServerOptions
                    {
                        AdditionalNamespaceUris = [string.Empty]
                    }.Validate())!;
                Assert.That(empty.ParamName, Is.EqualTo("AdditionalNamespaceUris"));

                Assert.Throws<ArgumentException>(() =>
                    new PumpsServerOptions
                    {
                        AdditionalNamespaceUris = [null!]
                    }.Validate());

                // Unset, the list is empty and passes, returning self.
                var options = new PumpsServerOptions();
                Assert.That(options.Validate(), Is.SameAs(options));
                Assert.That(options.AdditionalNamespaceUris.Count, Is.Zero);

                new PumpsServerOptions
                {
                    AdditionalNamespaceUris = ["urn:vendor:extra"]
                }.Validate();
            });
        }

        [Test]
        public void FactoryNamespacesUrisVaryWithTheOptions()
        {
            // Default constructor: no options, so Industrial Automation is on.
            string[] defaults = [.. new PumpsNodeManagerFactory().NamespacesUris];
            Assert.Multiple(() =>
            {
                Assert.That(defaults, Contains.Item(Namespaces.Pumps));
                Assert.That(defaults, Contains.Item(Opc.Ua.Machinery.Namespaces.Machinery));
                Assert.That(defaults, Contains.Item(Opc.Ua.Di.Namespaces.OpcUaDi));
                Assert.That(defaults, Contains.Item(Opc.Ua.IA.Namespaces.IA));
            });

            // Industrial Automation turned off drops IA and keeps the rest.
            var withoutIa = new PumpsNodeManagerFactory(
                runner: null,
                Options.Create(new PumpsServerOptions
                {
                    LoadIndustrialAutomationModel = false
                }));
            string[] withoutIaUris = [.. withoutIa.NamespacesUris];
            Assert.Multiple(() =>
            {
                Assert.That(withoutIaUris, Does.Not.Contain(Opc.Ua.IA.Namespaces.IA));
                Assert.That(withoutIaUris, Contains.Item(Namespaces.Pumps));
                Assert.That(withoutIaUris, Contains.Item(Opc.Ua.Machinery.Namespaces.Machinery));
            });

            // Additional namespace URIs are appended.
            var withExtra = new PumpsNodeManagerFactory(
                runner: null,
                Options.Create(new PumpsServerOptions
                {
                    AdditionalNamespaceUris = ["urn:vendor:extra"]
                }));
            Assert.That(
                (string[])[.. withExtra.NamespacesUris],
                Contains.Item("urn:vendor:extra"));
        }

        private static IOpcUaServerBuilder CreateServerBuilder()
        {
            var services = new ServiceCollection();
            return services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "PumpsHostingTests";
                options.ApplicationUri = "urn:localhost:OPCFoundation:PumpsHostingTests";
            });
        }
    }
}
