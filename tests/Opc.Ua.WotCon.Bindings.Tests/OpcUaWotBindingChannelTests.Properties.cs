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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    public sealed partial class OpcUaWotBindingChannelTests
    {
        [Test]
        public async Task IndexedPropertyOperationsChangeOnlyRequestedUpstreamSlice()
        {
            WotBindingPlan plan = CreatePropertyPlan(
                "nsu=" + ReferenceServerNamespace + ";s=Scalar_Static_Arrays_Int32",
                """{ "type": "array", "items": { "type": "integer" } }""");
            WotCompiledForm write = plan.CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm read = plan.CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.ReadProperty);
            await using IWotBindingChannel writeChannel = await m_registry.OpenChannelAsync(write)
                .ConfigureAwait(false);
            await using IWotBindingChannel readChannel = await m_registry.OpenChannelAsync(read)
                .ConfigureAwait(false);
            ArrayOf<int> initial = [1, 2, 3];
            WotWriteResult initialized = await writeChannel.WriteAsync(new DataValue(new Variant(initial)))
                .ConfigureAwait(false);
            Assert.That(initialized.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(writeChannel, Is.InstanceOf<IWotPropertyBindingChannel>());
            Assert.That(readChannel, Is.InstanceOf<IWotPropertyBindingChannel>());

            var writer = (IWotPropertyBindingChannel)writeChannel;
            var reader = (IWotPropertyBindingChannel)readChannel;
            ServiceMessageContext context = CreatePropertyContext();
            ArrayOf<int> replacement = [9];

            WotWriteResult written = await writer.WriteAsync(new WotWriteRequest(
                new DataValue(new Variant(replacement)), context, NumericRange.Parse("1"))).ConfigureAwait(false);

            Assert.That(written.Status, Is.EqualTo(StatusCodes.Good));
            WotReadResult whole = await readChannel.ReadAsync().ConfigureAwait(false);
            Assert.That(whole.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(whole.Value.WrappedValue.TryGetValue(out ArrayOf<int> actual), Is.True);
            Assert.That(actual.Count, Is.EqualTo(3));
            Assert.That(actual[0], Is.EqualTo(1));
            Assert.That(actual[1], Is.EqualTo(9));
            Assert.That(actual[2], Is.EqualTo(3));

            WotReadResult slice = await reader.ReadAsync(
                new WotReadRequest(context, NumericRange.Parse("1"))).ConfigureAwait(false);
            Assert.That(slice.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(slice.Value.WrappedValue.TryGetValue(out ArrayOf<int> sliced), Is.True);
            Assert.That(sliced.Count, Is.EqualTo(1));
            Assert.That(sliced[0], Is.EqualTo(9));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PropertyReadRetainsSourceNamespaceContext(bool qualifiedName)
        {
            string nodeName = qualifiedName ? "Scalar_Static_QualifiedName" : "Scalar_Static_NodeId";
            WotBindingPlan plan = CreatePropertyPlan(
                "nsu=" + ReferenceServerNamespace + ";s=" + nodeName, """{ "type": "string" }""");
            WotCompiledForm write = plan.CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm read = plan.CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.ReadProperty);
            await using IWotBindingChannel writer = await m_registry.OpenChannelAsync(write).ConfigureAwait(false);
            await using IWotBindingChannel reader = await m_registry.OpenChannelAsync(read).ConfigureAwait(false);
            ushort sourceIndex = checked((ushort)m_session.NamespaceUris.GetIndex(ReferenceServerNamespace));
            Variant sourceValue = qualifiedName
                ? new Variant(new QualifiedName("Sensor", sourceIndex))
                : new Variant(new NodeId("Sensor", sourceIndex));
            WotWriteResult initialized = await writer.WriteAsync(new DataValue(sourceValue)).ConfigureAwait(false);
            Assert.That(initialized.Status, Is.EqualTo(StatusCodes.Good));

            WotReadResult result = await reader.ReadAsync().ConfigureAwait(false);

            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Context, Is.Not.Null);
            Assert.That(result.Context!.NamespaceUris.GetString(sourceIndex), Is.EqualTo(ReferenceServerNamespace));
            Assert.That(result.Value.WrappedValue, Is.EqualTo(sourceValue));
            Assert.That(result.Context.NamespaceUris, Is.Not.SameAs(m_session.NamespaceUris));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PropertyWriteTranslatesCallerNamespaceBeforeWriting(bool qualifiedName)
        {
            string nodeName = qualifiedName ? "Scalar_Static_QualifiedName" : "Scalar_Static_NodeId";
            WotBindingPlan plan = CreatePropertyPlan(
                "nsu=" + ReferenceServerNamespace + ";s=" + nodeName, """{ "type": "string" }""");
            WotCompiledForm write = plan.CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.WriteProperty);
            WotCompiledForm read = plan.CompiledForms.Single(
                form => form.Operation == WoTBindingCapabilityEnum.ReadProperty);
            await using IWotBindingChannel writeChannel = await m_registry.OpenChannelAsync(write)
                .ConfigureAwait(false);
            await using IWotBindingChannel reader = await m_registry.OpenChannelAsync(read).ConfigureAwait(false);
            Assert.That(writeChannel, Is.InstanceOf<IWotPropertyBindingChannel>());
            var writer = (IWotPropertyBindingChannel)writeChannel;
            ushort sourceIndex = checked((ushort)m_session.NamespaceUris.GetIndex(ReferenceServerNamespace));
            int namespaceCount = m_session.NamespaceUris.Count;
            ServiceMessageContext caller = CreatePropertyContext();
            caller.NamespaceUris = new NamespaceTable();
            for (int i = 0; i <= sourceIndex; i++)
            {
                caller.NamespaceUris.Append("urn:aggregate-only:" + i.ToString(CultureInfo.InvariantCulture));
            }
            ushort callerIndex = caller.NamespaceUris.GetIndexOrAppend(ReferenceServerNamespace);
            Variant callerValue = qualifiedName
                ? new Variant(new QualifiedName("Sensor", callerIndex))
                : new Variant(new NodeId("Sensor", callerIndex));

            WotWriteResult written = await writer.WriteAsync(
                new WotWriteRequest(new DataValue(callerValue), caller)).ConfigureAwait(false);

            Assert.That(written.Status, Is.EqualTo(StatusCodes.Good));
            WotReadResult result = await reader.ReadAsync().ConfigureAwait(false);
            Assert.That(result.Status, Is.EqualTo(StatusCodes.Good));
            if (qualifiedName)
            {
                Assert.That(result.Value.WrappedValue.TryGetValue(out QualifiedName name), Is.True);
                Assert.That(name, Is.EqualTo(new QualifiedName("Sensor", sourceIndex)));
            }
            else
            {
                Assert.That(result.Value.WrappedValue.TryGetValue(out NodeId node), Is.True);
                Assert.That(node, Is.EqualTo(new NodeId("Sensor", sourceIndex)));
            }
            Assert.That(m_session.NamespaceUris.Count, Is.EqualTo(namespaceCount));
        }

        private ServiceMessageContext CreatePropertyContext()
        {
            return new ServiceMessageContext(NUnitTelemetryContext.Create(), m_session.Factory)
            {
                NamespaceUris = new NamespaceTable(m_session.NamespaceUris),
                ServerUris = new StringTable(m_session.ServerUris)
            };
        }

        private WotBindingPlan CreatePropertyPlan(string nodeId, string schema)
        {
            string href = new UriBuilder("opc.tcp", "localhost", m_serverFixture.Port).Uri.AbsoluteUri;
            string document = $$"""
                {
                  "@context": "https://www.w3.org/2022/wot/td/v1.1",
                  "title": "Native property operations",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": [ "nosec_sc" ],
                  "properties": {
                    "value": {
                      {{schema.Trim()[1..^1]}},
                      "forms": [
                        { "href": "{{href}}?id={{Uri.EscapeDataString(nodeId)}}",
                          "op": [ "readproperty", "writeproperty" ] }
                      ]
                    }
                  }
                }
                """;
            WotBindingPlan plan = m_registry.Prepare(WotBindingPlanRequest.FromDocument(
                "native-property", WoTDocumentKindEnum.ThingDescription, Encoding.UTF8.GetBytes(document)));
            Assert.That(plan.Diagnostics.Where(diagnostic => diagnostic.IsError), Is.Empty);
            return plan;
        }
    }
}
