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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Lds.Server.Hosting;

namespace Opc.Ua.Lds.Tests.Hosting
{
    /// <summary>
    /// Coverage tests for the new option properties on
    /// <see cref="LdsServerOptions"/> (multicast loopback toggle,
    /// extra server capabilities).
    /// </summary>
    [TestFixture]
    [Category("Lds")]
    [Category("Hosting")]
    [Parallelizable]
    public sealed class LdsServerOptionsCoverageTests
    {
        [Test]
        public void DefaultsAreReasonable()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(_ => { });
            using ServiceProvider sp = services.BuildServiceProvider();

            LdsServerOptions opts = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;
            Assert.That(opts.EnableMulticast, Is.True);
            Assert.That(opts.MulticastLoopbackOnly, Is.False);
            Assert.That(opts.ServerCapabilities, Is.Empty);
        }

        [Test]
        public void MulticastLoopbackOnlyRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(o => o.MulticastLoopbackOnly = true);
            using ServiceProvider sp = services.BuildServiceProvider();

            LdsServerOptions opts = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;
            Assert.That(opts.MulticastLoopbackOnly, Is.True);
        }

        [Test]
        public void ExtraServerCapabilitiesRoundTrip()
        {
            var services = new ServiceCollection();
            services.AddOpcUa().AddLdsServer(o =>
            {
                o.ServerCapabilities.Add("DA");
                o.ServerCapabilities.Add("AC");
            });
            using ServiceProvider sp = services.BuildServiceProvider();

            LdsServerOptions opts = sp.GetRequiredService<IOptions<LdsServerOptions>>().Value;
            Assert.That(opts.ServerCapabilities, Has.Count.EqualTo(2));
            Assert.That(opts.ServerCapabilities[0], Is.EqualTo("DA"));
            Assert.That(opts.ServerCapabilities[1], Is.EqualTo("AC"));
        }
    }
}
