// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Tests.Support;

namespace Opc.Ua.WotCon.Tests.Binding
{
    /// <summary>
    /// Verifies the worked sample custom binder end-to-end: it validates and
    /// compiles a <c>mem://</c> form and its executor performs read / write against
    /// the in-process store, demonstrating the third-party code-behind pattern.
    /// </summary>
    [TestFixture]
    public sealed class WotCustomBinderSampleTests
    {
        [Test]
        public async Task SampleBinderCompilesAndExecutesReadWrite()
        {
            var store = new MemoryWotStore();

            // A custom binding introduces a scheme the shipped endpoint policy does not know
            // about, so the sample opts "mem" in explicitly. Loopback stays blocked.
            var endpointPolicy = new WotEndpointPolicy();
            endpointPolicy.AllowedSchemes.Add("mem");

            var registry = new WotProtocolBinderRegistry(
                [new MemoryWotBinder()],
                [new MemoryWotBindingExecutor(store)],
                endpointPolicy: endpointPolicy);

            const string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"setpoint\":{\"type\":\"number\",\"forms\":[{\"href\":\"mem://store/setpoint\"}]}}}";
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.HasExecutableForms, Is.True);

            WotCompiledForm write = plan.CompiledForms.First(
                f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(
                    new DataValue(new Variant(42.5))).ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
            }

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read).ConfigureAwait(false);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync().ConfigureAwait(false);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42.5));
            }
        }
    }
}
