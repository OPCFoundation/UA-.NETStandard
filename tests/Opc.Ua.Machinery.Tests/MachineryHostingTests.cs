// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.ISA95.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Covers the hosting registrations, and above all what they refuse: two
    /// managers owning the same address space is the failure this layer exists
    /// to make loud and early.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryHostingTests
    {
        [Test]
        public void AddMachineryRegistersTheNodeManagerAndTheProvider()
        {
            IOpcUaServerBuilder builder = CreateBuilder();

            builder.AddMachinery();

            Assert.That(
                builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(MachineryNodeManagerFactory)),
                Is.True);
            Assert.That(
                builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(IMachineryModelProvider)),
                Is.True);
            Assert.That(
                builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(DiAddressSpaceOwnership)),
                Is.True,
                "AddMachinery owns the Device Integration address space.");
        }

        [Test]
        public void AddMachineryClaimsIsa95OnlyWhenJobsAreSelected()
        {
            IOpcUaServerBuilder withJobs = CreateBuilder();
            withJobs.AddMachinery(options => options.Parts = MachineryParts.All);
            Assert.That(
                withJobs.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(Isa95AddressSpaceOwnership)),
                Is.True);

            IOpcUaServerBuilder withoutJobs = CreateBuilder();
            withoutJobs.AddMachinery(
                options => options.Parts = MachineryParts.BuildingBlocks);
            Assert.That(
                withoutJobs.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(Isa95AddressSpaceOwnership)),
                Is.False,
                "A server without OPC 40001-3 does not load the ISA-95 models.");
        }

        [Test]
        public void ASecondDiOwningRegistrationIsRefused()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.AddMachinery();

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => builder.AddMachinery())!;
            Assert.That(exception.Message, Does.Contain("already owned by"));
            Assert.That(
                exception.Message,
                Does.Contain("ConfigureMachineryFor"),
                "The failure must name the way out.");
        }

        [Test]
        public void AddMachineryAfterAnotherDiOwnerIsRefused()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.Services.AddSingleton(new DiAddressSpaceOwnership("AddRobotics"));

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => builder.AddMachinery())!;
            Assert.That(exception.Message, Does.Contain("AddRobotics"));
        }

        [Test]
        public void AddMachineryAfterAnIsa95OwnerIsRefusedWhenJobsAreSelected()
        {
            IOpcUaServerBuilder builder = CreateBuilder();
            builder.Services.AddSingleton(new Isa95AddressSpaceOwnership("AddIsa95Server"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => builder.AddMachinery(options => options.Parts = MachineryParts.All))!;
            Assert.That(exception.Message, Does.Contain("AddIsa95Server"));
        }

        [Test]
        public void AnInvalidPartCombinationIsRefusedAtRegistration()
        {
            IOpcUaServerBuilder builder = CreateBuilder();

            Assert.Throws<ServiceResultException>(() =>
                builder.AddMachinery(options => options.Parts = MachineryParts.Energy));
        }

        [Test]
        public void AddMachineryResultsNeedsNoDiOwnership()
        {
            IOpcUaServerBuilder builder = CreateBuilder();

            builder.AddMachineryResults();

            Assert.That(
                builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(MachineryResultNodeManagerFactory)),
                Is.True);
            Assert.That(
                builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(DiAddressSpaceOwnership)),
                Is.False,
                "OPC 40001-101 needs UA core only, so it can sit next to a " +
                "Device Integration server.");
            Assert.That(
                builder.Services.Any(descriptor =>
                    descriptor.ServiceType == typeof(IMachineryResultStore)),
                Is.True);
        }

        [Test]
        public void TheFactoryAnnouncesDiAmongItsNamespaces()
        {
            var factory = new MachineryNodeManagerFactory(
                new MachineryServerOptions { Parts = MachineryParts.BuildingBlocks });

            string[] namespaceUris = [.. factory.NamespacesUris];

            Assert.That(namespaceUris, Contains.Item(Opc.Ua.Di.Namespaces.OpcUaDi));
            Assert.That(namespaceUris, Contains.Item(Namespaces.Machinery));
            Assert.That(namespaceUris, Contains.Item(Opc.Ua.IA.Namespaces.IA));
            Assert.That(
                namespaceUris,
                Contains.Item(MachineryServerOptions.DefaultInstanceNamespaceUri));
        }

        private static IOpcUaServerBuilder CreateBuilder()
        {
            var services = new ServiceCollection();
            return services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "MachineryHostingTests";
                options.ApplicationUri = "urn:localhost:OPCFoundation:MachineryHostingTests";
            });
        }
    }
}
