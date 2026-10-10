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

#if NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRetainedProvenanceKeepsItsPublishedGenerationAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(ConditionSourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotProjectionDocument document = ConditionProjection(mode, source.EndpointUrl);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionHandle active = await host.AddAsync(document, ct).ConfigureAwait(false);
            NativeConditionEvents? retained = null;
            try
            {
                uint producingGeneration = checked((uint)active.Generation);
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                retained = await NativeConditionEvents.OpenAsync(destination.Session, ct, owner).ConfigureAwait(false);
                ByteString originalId = ByteString.From(new byte[] { 0xD3, 0xF1, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", true, true, originalId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> fields = await retained.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("condition", metadata), ct).ConfigureAwait(false);
                var client = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await client.GetEventProvenanceAsync(eventId, ct)
                    .ConfigureAwait(false);
                active = await host.ShadowReloadAsync(active, document, ct).ConfigureAwait(false);
                Assert.That(active.Generation, Is.GreaterThan(producingGeneration));
                WoTEventOriginDataType after = await client.GetEventProvenanceAsync(eventId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(before.Generation, Is.EqualTo(producingGeneration));
                    Assert.That(after.Generation, Is.EqualTo(producingGeneration));
                    Assert.That(after.SourceEventId, Is.EqualTo(originalId));
                    Assert.That(after.SourceConditionId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                    Assert.That(after.SourceBranchId, Is.EqualTo(new ExpandedNodeId("Retained", SourceNamespace)));
                    Assert.That(after.ReceiveTime, Is.EqualTo(before.ReceiveTime));
                    Assert.That(after.EncodingMask, Is.EqualTo(0xFFU));
                }
            }
            finally
            {
                if (retained is not null)
                {
                    await retained.DisposeAsync().ConfigureAwait(false);
                }
                await host.RemoveAsync(active, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
#endif
