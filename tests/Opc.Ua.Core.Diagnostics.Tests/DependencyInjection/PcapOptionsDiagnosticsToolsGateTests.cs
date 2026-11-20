/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Pcap.Tests.DependencyInjection
{
    /// <summary>
    /// Tests the diagnostics tools opt-in flag exposed through pcap
    /// dependency injection options.
    /// </summary>
    [TestFixture]
    public sealed class PcapOptionsDiagnosticsToolsGateTests
    {
        [Test]
        public void PcapOptionsEnableDiagnosticsToolsDefaultsToFalse()
        {
            var options = new PcapOptions();

            Assert.That(options.EnableDiagnosticsTools, Is.False);
        }

        [Test]
        public void PcapOptionsEnableDiagnosticsToolsCanBeSet()
        {
            var options = new PcapOptions
            {
                EnableDiagnosticsTools = true
            };

            Assert.That(options.EnableDiagnosticsTools, Is.True);
        }

        [Test]
        public async Task AddPcapInvokesUserConfigureCallbackForDiagnosticsToolsFlag()
        {
            var services = new ServiceCollection();

            services.AddPcap(options => options.EnableDiagnosticsTools = true);
            await using ServiceProvider provider = services.BuildServiceProvider();

            PcapOptions options = provider.GetRequiredService<PcapOptions>();

            Assert.That(options.EnableDiagnosticsTools, Is.True);
        }
    }
}
