/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Pcap.Tests.Capture
{
    /// <summary>
    /// Tests for the non-DI, env-var driven server capture bootstrap
    /// <see cref="PcapServerCapture"/>.
    /// </summary>
    [TestFixture]
    public sealed class PcapServerCaptureTests : TempDirectoryFixture
    {
        [Test]
        public async Task TryStartFromEnvironmentReturnsNullAndInstallsNothingWhenUnset()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();

            IAsyncDisposable? handle = await PcapServerCapture.TryStartFromEnvironmentAsync(
                bindings,
                static _ => null,
                loggerFactory: null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(handle, Is.Null,
                "No env variables set must be a complete no-op.");
            Assert.That(
                bindings.GetListenerFactory(Utils.UriSchemeOpcTcp),
                Is.Not.InstanceOf<PcapTransportListenerBinding>(),
                "Nothing must be installed when the env variables are unset.");
        }

        [Test]
        public async Task TryStartFromEnvironmentInstallsServerBindingWhenPcapFileSet()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            string pcapPath = CreateTempPath("server-capture.pcap");

            IAsyncDisposable? handle = await PcapServerCapture.TryStartFromEnvironmentAsync(
                bindings,
                name => name == PcapEnvironmentVariableNames.OpcuaPcapFile ? pcapPath : null,
                loggerFactory: null,
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                Assert.That(handle, Is.Not.Null,
                    "A capture handle must be returned when OPCUA_PCAP_FILE is set.");
                Assert.That(
                    bindings.GetListenerFactory(Utils.UriSchemeOpcTcp),
                    Is.InstanceOf<PcapTransportListenerBinding>(),
                    "The server listener binding must be installed.");
                Assert.That(
                    bindings.GetChannelFactory(Utils.UriSchemeOpcTcp),
                    Is.Not.InstanceOf<PcapTransportChannelBinding>(),
                    "The client channel binding must not be installed for a server.");
            }
            finally
            {
                if (handle is not null)
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task TryStartFromEnvironmentInstallsServerBindingForKeyLogOnly()
        {
            var bindings = DefaultTransportBindingRegistry.WithDefaultTcp();
            string keyLogPath = CreateTempPath("server-keys.uakeys.json");

            IAsyncDisposable? handle = await PcapServerCapture.TryStartFromEnvironmentAsync(
                bindings,
                name => name == PcapEnvironmentVariableNames.OpcuaKeyLogFile ? keyLogPath : null,
                loggerFactory: null,
                CancellationToken.None).ConfigureAwait(false);

            try
            {
                Assert.That(handle, Is.Not.Null,
                    "A handle must be returned when only OPCUA_KEYLOGFILE is set.");
                Assert.That(
                    bindings.GetListenerFactory(Utils.UriSchemeOpcTcp),
                    Is.InstanceOf<PcapTransportListenerBinding>(),
                    "The server listener binding must be installed so the keylog " +
                    "observer sees server channels.");
            }
            finally
            {
                if (handle is not null)
                {
                    await handle.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public void TryStartFromEnvironmentThrowsOnNullServerBindings()
        {
            Assert.That(
                async () => await PcapServerCapture.TryStartFromEnvironmentAsync(
                    serverBindings: null!).ConfigureAwait(false),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("serverBindings"));
        }
    }
}
