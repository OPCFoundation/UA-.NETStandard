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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies independent model selection and a single shared namespace owner.
    /// </summary>
    [TestFixture]
    public sealed class MachineToolHostingTests
    {
        /// <summary>
        /// A wireless-only registration does not claim or load MachineTool/ISA95 namespaces.
        /// </summary>
        [Test]
        public void WirelessOnlyHostingKeepsItsIndependentNamespaceClosure()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder server = services.AddOpcUa().AddServer(options =>
                options.ApplicationName = nameof(MachineToolHostingTests));
            server.AddMachineTool(options => options.Parts = MachineToolParts.Wmtp);
            using ServiceProvider provider = services.BuildServiceProvider();
            MachineryNodeManagerFactory owner = provider.GetRequiredService<MachineryNodeManagerFactory>();

            Assert.That(owner.NamespacesUris.ToArray(), Does.Contain(WMTP.Namespaces.WMTP));
            Assert.That(owner.NamespacesUris.ToArray(), Does.Not.Contain(Namespaces.MachineTool));
            Assert.That(
                owner.NamespacesUris.ToArray(),
                Does.Not.Contain(Opc.Ua.ISA95.JobControl.V2.Namespaces.ISA95JobControlV2));
            Assert.That(services.Count(service => service.ServiceType == typeof(DiAddressSpaceOwnership)), Is.EqualTo(1));
        }

        /// <summary>
        /// Repeated composition augments the selected Machinery manager rather than registering another.
        /// </summary>
        [Test]
        public void MachineToolCompositionRetainsOneMachineryOwner()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder server = services.AddOpcUa().AddServer(options =>
                options.ApplicationName = nameof(MachineToolHostingTests));
            server.AddMachinery(options =>
                options.Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs);
            server.AddMachineToolToMachinery();
            server.AddMachineToolToMachinery();
            using ServiceProvider provider = services.BuildServiceProvider();
            MachineryNodeManagerFactory owner = provider.GetRequiredService<MachineryNodeManagerFactory>();

            Assert.That(owner.NamespacesUris.ToArray(), Does.Contain(Namespaces.MachineTool));
            Assert.That(owner.NamespacesUris.ToArray(), Does.Not.Contain(WMTP.Namespaces.WMTP));
            Assert.That(services.Count(service => service.ServiceType == typeof(MachineryNodeManagerFactory)),
                Is.EqualTo(1));
            Assert.That(provider.GetServices<IMachineryModelProvider>().OfType<MachineToolModelProvider>().Count(),
                Is.EqualTo(1));
        }

        /// <summary>
        /// Unsupported selections are refused before any namespace owner is registered.
        /// </summary>
        [TestCase(MachineToolParts.None)]
        [TestCase((MachineToolParts)4)]
        public void InvalidModelSelectionsDoNotRegisterAnOwner(MachineToolParts parts)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder server = services.AddOpcUa().AddServer(options =>
                options.ApplicationName = nameof(MachineToolHostingTests));

            Assert.Throws<ArgumentOutOfRangeException>(() => server.AddMachineTool(options => options.Parts = parts));

            Assert.That(services.Any(service => service.ServiceType == typeof(DiAddressSpaceOwnership)), Is.False);
        }

        /// <summary>
        /// Selected MachineTool dependencies must belong to the actual configured Machinery owner.
        /// </summary>
        [Test]
        public void CompositionRejectsAnOwnerMissingRequiredParts()
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder server = services.AddOpcUa().AddServer(options =>
                options.ApplicationName = nameof(MachineToolHostingTests));
            server.AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks);
            server.AddMachineToolToMachinery();
            using ServiceProvider provider = services.BuildServiceProvider();

            ServiceResultException error = Assert.Throws<ServiceResultException>(
                () => provider.GetRequiredService<MachineryNodeManagerFactory>())!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }
    }
}
