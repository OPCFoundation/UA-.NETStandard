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
