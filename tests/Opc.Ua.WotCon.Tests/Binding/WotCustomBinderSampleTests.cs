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

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.WotCon.Binding;
using Opc.Ua.WotCon.Binding.Samples;
using Opc.Ua.WotCon.V2;

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
        public async Task SampleBinder_CompilesAndExecutesReadWrite()
        {
            var store = new MemoryWotStore();
            var registry = new WotProtocolBinderRegistry(
                new IWotProtocolBinder[] { new MemoryWotBinder() },
                new IWotBindingExecutor[] { new MemoryWotBindingExecutor(store) });

            string td = "{\"@context\":\"https://www.w3.org/2022/wot/td/v1.1\",\"title\":\"t\"," +
                "\"properties\":{\"setpoint\":{\"type\":\"number\",\"forms\":[{\"href\":\"mem://store/setpoint\"}]}}}";
            WotBindingPlan plan = registry.Prepare(WotBindingPlanRequest.FromDocument(
                "xid", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(td)));

            Assert.That(plan.FullySupported, Is.True);
            Assert.That(plan.HasExecutableForms, Is.True);

            WotCompiledForm write = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm read = plan.CompiledForms.First(f => f.Operation == WoTBindingCapabilityEnum.ReadProperty);

            IWotBindingChannel writeChannel = await registry.OpenChannelAsync(write);
            await using (writeChannel.ConfigureAwait(false))
            {
                WotWriteResult result = await writeChannel.WriteAsync(new DataValue(new Variant(42.5)));
                Assert.That(result.Success, Is.True);
            }

            IWotBindingChannel readChannel = await registry.OpenChannelAsync(read);
            await using (readChannel.ConfigureAwait(false))
            {
                WotReadResult result = await readChannel.ReadAsync();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Value.WrappedValue.AsBoxedObject(), Is.EqualTo(42.5));
            }
        }
    }
}
