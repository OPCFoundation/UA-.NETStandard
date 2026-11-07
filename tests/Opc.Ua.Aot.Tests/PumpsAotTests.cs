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
