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

#nullable enable

using System;
using System.Linq;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the fluent active/active entry point
    /// (<see cref="ReplicatedServerBuilderExtensions.UseActiveActiveRedundancy"/>): that one call registers
    /// both the replicated address space and the replicated session store, and that the shared options flow
    /// through.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ActiveActiveRedundancyBuilderExtensionsTests
    {
        [Test]
        public void UseActiveActiveRedundancyThrowsOnNullBuilder()
        {
            Assert.That(
                () => ReplicatedServerBuilderExtensions.UseActiveActiveRedundancy(null!, _ => { }),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseActiveActiveRedundancyReturnsSameBuilder()
        {
            var builder = new DiTestServerBuilder();

            IOpcUaServerBuilder result = builder.UseActiveActiveRedundancy();

            Assert.That(result, Is.SameAs(builder));
        }

        [Test]
        public async System.Threading.Tasks.Task UseActiveActiveRedundancyRegistersBothReplicatedStoresAsync()
        {
            var builder = new DiTestServerBuilder();

            builder.UseActiveActiveRedundancy(aa => aa.AllowUnauthenticatedGossip = true);

            await using ServiceProvider sp = builder.Services.BuildServiceProvider();

            // A single call wires the address space (a startup task) and the
            // session store (a session manager factory).
            Assert.That(
                sp.GetServices<IServerStartupTask>().ToArray(),
                Has.Some.InstanceOf<ReplicatedAddressSpaceStartupTask>());
            Assert.That(
                sp.GetRequiredService<ISessionManagerFactory>(),
                Is.InstanceOf<ReplicatedSessionManagerFactory>());
        }

        [Test]
        public void UseActiveActiveRedundancyInvokesConfigureWithOptions()
        {
            var builder = new DiTestServerBuilder();
            ActiveActiveRedundancyOptions? captured = null;

            builder.UseActiveActiveRedundancy(aa =>
            {
                captured = aa;
                aa.GossipPort = 5000;
                aa.EnableFastReconnect = true;
                aa.AddPeer(new IPEndPoint(IPAddress.Loopback, 5100));
            });

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.GossipPort, Is.EqualTo(5000));
            Assert.That(captured.EnableFastReconnect, Is.True);
            Assert.That(captured.Peers, Has.Count.EqualTo(1));
        }
    }

    /// <summary>
    /// Unit tests for <see cref="ActiveActiveRedundancyOptions"/> defaults and guards.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class ActiveActiveRedundancyOptionsTests
    {
        [Test]
        public void DefaultsAreSecureAndSane()
        {
            var options = new ActiveActiveRedundancyOptions();

            Assert.That(options.BindAddress, Is.EqualTo(IPAddress.Any));
            Assert.That(options.GossipPort, Is.EqualTo(4840));
            Assert.That(options.AllowUnauthenticatedGossip, Is.False);
            Assert.That(options.EnableFastReconnect, Is.False);
            Assert.That(options.Tls, Is.Null);
            Assert.That(options.TimeProvider, Is.SameAs(TimeProvider.System));
            Assert.That(options.Peers, Is.Empty);
        }

        [Test]
        public void AddPeerAppendsAndReturnsSelf()
        {
            var options = new ActiveActiveRedundancyOptions();
            var endpoint = new IPEndPoint(IPAddress.Loopback, 4840);

            ActiveActiveRedundancyOptions result = options.AddPeer(endpoint);

            Assert.That(result, Is.SameAs(options));
            Assert.That(options.Peers, Has.Count.EqualTo(1));
            Assert.That(options.Peers[0], Is.SameAs(endpoint));
        }

        [Test]
        public void AddPeerRejectsNull()
        {
            var options = new ActiveActiveRedundancyOptions();

            Assert.That(() => options.AddPeer(null!), Throws.ArgumentNullException);
        }
    }
}
