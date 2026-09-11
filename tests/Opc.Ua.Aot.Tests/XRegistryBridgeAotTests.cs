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

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Executes owned JSON metadata, exact document bytes, DI and conditional replay
    /// without reflection-based serialization in a published NativeAOT process.
    /// </summary>
    public sealed class XRegistryBridgeAotTests
    {
        [Test]
        public async Task TransactionEnvelopeAndProviderPreserveGuardsAndBytesAsync()
        {
            using var model = JsonDocument.Parse("""
                {"groups":{"groups":{"singular":"group",
                "resources":{"schemas":{"singular":"schema","hasdocument":true}}}}}
                """);
            var services = new ServiceCollection();
            services.AddXRegistryTransactions(new XRegistryTransactionalOptions
            {
                RegistryId = "aot-registry",
                Model = model.RootElement,
                PublicRoot = new Uri("https://registry.example")
            });
            using ServiceProvider provider = services.BuildServiceProvider();
            IXRegistryEndpoint endpoint = provider.GetRequiredService<IXRegistryEndpoint>();
            var caller = new XRegistryCallContext("aot-test")
            {
                IsAuthenticated = true,
                Roles = ["xregistry.write"]
            };
            using var body = JsonDocument.Parse("""{"versionid":"v1"}""");
            var codec = new XRegistryProtocolCodec();
            var write = new XRegistryRequest(XRegistryAction.Replace, "/groups/g/schemas/r")
            {
                Context = caller,
                Metadata = body.RootElement,
                Document = ByteString.From(new byte[] { 0, 255, 3 }),
                OperationId = "aot-write"
            };
            XRegistryRequest decoded = codec.DecodeRequest(codec.EncodeRequest(write), caller);
            XRegistryResponse created = await endpoint.ExecuteAsync(decoded).ConfigureAwait(false);
            XRegistryResponse replay = await endpoint.ExecuteAsync(decoded).ConfigureAwait(false);
            using var guard = JsonDocument.Parse("""{"epoch":0}""");
            var update = new XRegistryRequest(XRegistryAction.Merge, "/groups/g/schemas/r/versions/v1")
            {
                Context = caller,
                Metadata = guard.RootElement,
                View = XRegistryView.Metadata
            };
            IXRegistryPreparedOperation prepared = await provider.GetRequiredService<IXRegistryPreparedEndpoint>()
                .PrepareAsync(update).ConfigureAwait(false);
            await using var preparationLifetime = prepared.ConfigureAwait(false);
            XRegistryResponse beforeCommit = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1")
                {
                    Context = caller,
                    View = XRegistryView.Metadata
                }).ConfigureAwait(false);
            XRegistryResponse touched = await prepared.CommitAsync().ConfigureAwait(false);
            XRegistryResponse stale = await endpoint.ExecuteAsync(update).ConfigureAwait(false);
            XRegistryResponse read = await endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Read, "/groups/g/schemas/r/versions/v1") { Context = caller })
                .ConfigureAwait(false);
            XRegistryResponse wire = codec.DecodeResponse(codec.EncodeResponse(read));
            await Assert.That(created.StatusCode).IsEqualTo(201);
            await Assert.That(replay.StatusCode).IsEqualTo(201);
            await Assert.That(touched.StatusCode).IsEqualTo(200);
            await Assert.That(beforeCommit.Metadata.GetProperty("epoch").GetUInt64()).IsEqualTo(0UL);
            await Assert.That(ReferenceEquals(touched, prepared.Response)).IsTrue();
            await Assert.That(stale.StatusCode).IsEqualTo(400);
            await Assert.That(wire.Document.Span.SequenceEqual(new byte[] { 0, 255, 3 })).IsTrue();
            await Assert.That(wire.Metadata.GetProperty("epoch").GetUInt64()).IsEqualTo(1UL);
        }
    }
}
