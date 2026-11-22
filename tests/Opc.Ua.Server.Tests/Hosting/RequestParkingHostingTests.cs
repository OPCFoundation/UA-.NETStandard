// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    [TestFixture]
    [Category("Server")]
    [Category("Hosting")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RequestParkingHostingTests
    {
        [Test]
        public void FluentPolicyRegistrationIsAppliedBeforeHostedStartup()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(static _ => { });
            var policy = new DelegateRequestParkingPolicy(static request => request is CallRequest);
            Assert.That(builder.WithRequestParking(policy), Is.SameAs(builder));
            using ServiceProvider container = services.BuildServiceProvider();
            using var server = new ServerBase(NUnitTelemetryContext.Create());

            OpcUaServerHostedService.ApplyRequestParking(server, container);

            Assert.That(container.GetRequiredService<IRequestParkingPolicy>(), Is.SameAs(policy));
            Assert.That(server.RequestParkingPolicy, Is.SameAs(policy));
            Assert.That(server.RequestParkingPolicy.CanPark(new CallRequest()), Is.True);
            Assert.That(server.RequestParkingPolicy.CanPark(new ReadRequest()), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OptionalInjectedPolicyOverridesOrPreservesDirectConstructorPolicy(bool register)
        {
            var direct = new DelegateRequestParkingPolicy(static request => request is CallRequest);
            var injected = new DelegateRequestParkingPolicy(static request => request is ReadRequest);
            var services = new ServiceCollection();
            if (register)
            {
                services.AddSingleton<IRequestParkingPolicy>(_ => injected);
            }
            using ServiceProvider container = services.BuildServiceProvider();
            using var server = new ServerBase(NUnitTelemetryContext.Create(), null, direct);

            Assert.That(server.RequestParkingPolicy, Is.SameAs(direct));
            OpcUaServerHostedService.ApplyRequestParking(server, container);

            Assert.That(server.RequestParkingPolicy, Is.SameAs(register ? injected : direct));
        }

        [Test]
        public void MissingPolicyLeavesDefaultServerUnchanged()
        {
            using ServiceProvider container = new ServiceCollection().BuildServiceProvider();
            using var server = new ServerBase(NUnitTelemetryContext.Create());
            OpcUaServerHostedService.ApplyRequestParking(server, container);
            Assert.That(server.RequestParkingPolicy, Is.Null);
        }

        [Test]
        public void FluentRegistrationRejectsNullArguments()
        {
            var policy = new DelegateRequestParkingPolicy(static _ => false);
            Assert.That(() => OpcUaServerBuilderExtensions.WithRequestParking(null!, policy),
                Throws.ArgumentNullException);
            IOpcUaServerBuilder builder = new ServiceCollection().AddOpcUa().AddServer(static _ => { });
            Assert.That(() => builder.WithRequestParking(null!), Throws.ArgumentNullException);
        }
    }
}
