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

#if NET10_0
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class PubSubRuntimeToolsTests
    {
        [Test]
        public async Task StoppedRuntimeToolsReturnStoppedStateAsync()
        {
            await using PubSubRuntimeManager manager =
                PubSubMcpTestHelpers.NewManager();

            PubSubRuntimeStatus status = await PubSubRuntimeTools.StatusAsync(manager)
                .ConfigureAwait(false);
            ArrayOf<PubSubReceivedDataSet> received =
                await PubSubRuntimeTools.ReadReceivedAsync(manager, true)
                    .ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await PubSubRuntimeTools.StopAsync(manager)
                .ConfigureAwait(false);

            Assert.That(status.IsRunning, Is.False);
            Assert.That(received, Is.Empty);
            Assert.That(stopped.Mode, Is.EqualTo("Stopped"));
        }

        [Test]
        public async Task PublisherRuntimeToolsStartPublishAndStopAsync()
        {
            await using PubSubRuntimeManager manager =
                PubSubMcpTestHelpers.NewManager();
            int port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();
            string endpoint = $"opc.udp://127.0.0.1:{port}";

            PubSubRuntimeStatus started =
                await PubSubRuntimeTools.StartPublisherAsync(
                    manager,
                    endpoint,
                    12,
                    34,
                    "Value:Int32").ConfigureAwait(false);
            PubSubRuntimePublishResult published =
                await PubSubRuntimeTools.PublishAsync(manager, "Value=42")
                    .ConfigureAwait(false);
            PubSubRuntimeStatus status = await PubSubRuntimeTools.StatusAsync(manager)
                .ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await PubSubRuntimeTools.StopAsync(manager)
                .ConfigureAwait(false);

            Assert.That(started.Mode, Is.EqualTo("Publisher"));
            Assert.That(published.Fields, Has.Count.EqualTo(1));
            Assert.That(status.IsRunning, Is.True);
            Assert.That(stopped.IsRunning, Is.False);
        }

        [Test]
        public async Task SubscriberRuntimeToolStartsAndStopsAsync()
        {
            await using PubSubRuntimeManager manager =
                PubSubMcpTestHelpers.NewManager();
            int port = PubSubMcpTestHelpers.ReserveEphemeralLoopbackPort();

            PubSubRuntimeStatus started =
                await PubSubRuntimeTools.StartSubscriberAsync(
                    manager,
                    $"opc.udp://127.0.0.1:{port}",
                    1,
                    2,
                    "Value:Int32").ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await PubSubRuntimeTools.StopAsync(manager)
                .ConfigureAwait(false);

            Assert.That(started.Mode, Is.EqualTo("Subscriber"));
            Assert.That(stopped.Mode, Is.EqualTo("Stopped"));
        }
    }
}
#endif
