// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Opc.Ua.Pumps;
using Opc.Ua.Pumps.Client;
using Opc.Ua.Pumps.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Exercises generated Pumps encodings and direct fluent hosting registrations in the native test binary.
    /// </summary>
    public sealed class PumpsAotTests
    {
        /// <summary>
        /// Retains generated structure codecs and both hosting factories without connecting to a server.
        /// </summary>
        [Test]
        public async Task PumpEncodingsAndHostingFactoriesAreReachableAsync()
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(
                logging => logging.SetMinimumLevel(LogLevel.Warning));
            var message = ServiceMessageContext.Create(telemetry);
            message.NamespaceUris.GetIndexOrAppend(Opc.Ua.Pumps.Namespaces.Pumps);
            message.Factory.Builder.AddOpcUaPumps().Commit();
            var address = new PhysicalAddressDataType();
            byte[] encoded = BinaryEncoder.EncodeMessage(address, message);
            PhysicalAddressDataType decoded = BinaryDecoder.DecodeMessage<PhysicalAddressDataType>(encoded, message);
            await Assert.That(decoded.IsEqual(address)).IsTrue();

            var services = new ServiceCollection();
            services.AddOpcUa().AddServer(options => options.ApplicationName = "PumpsAotServer").AddPumps();
            services.AddOpcUa().AddClient(options => options.ApplicationName = "PumpsAotClient").AddPumpsClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            await Assert.That(provider.GetRequiredService<PumpsNodeManagerFactory>()).IsNotNull();
            await Assert.That(provider.GetRequiredService<Func<CancellationToken, Task<PumpsClient>>>()).IsNotNull();
        }
    }
}
