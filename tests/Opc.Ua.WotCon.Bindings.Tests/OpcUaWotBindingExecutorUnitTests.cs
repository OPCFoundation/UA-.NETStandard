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
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.WotCon.Bindings.OpcUa;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// Unit tests for endpoint construction in <see cref="OpcUaWotBindingExecutor"/>.
    /// </summary>
    [TestFixture]
    public sealed class OpcUaWotBindingExecutorUnitTests
    {
        [TestCase("opc.tcp", 4841, "opc.tcp://example.test:4841")]
        [TestCase("opc.tcp", 4840, "opc.tcp://example.test")]
        [TestCase("opc.https", 443, "opc.https://example.test")]
        public async Task ActivateAsyncBuildsEndpointFromHostAndPortWhenBaseUriIsEmptyAsync(
            string scheme, int port, string expected)
        {
            string? capturedEndpoint = null;
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                SessionFactory = (endpoint, ct) =>
                {
                    capturedEndpoint = endpoint;
                    return ValueTask.FromException<ISession>(new InvalidOperationException("stop"));
                }
            });

            WotCompiledForm form = BuildForm(scheme, port, baseUri: string.Empty);

            Assert.That(
                async () => await executor.ActivateAsync(form, new WotExecutorContext()).ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(capturedEndpoint, Is.EqualTo(expected));
        }

        [Test]
        public async Task ActivateAsyncPreservesExplicitBaseUriAsync()
        {
            string? capturedEndpoint = null;
            var executor = new OpcUaWotBindingExecutor(new OpcUaWotBindingOptions
            {
                SessionFactory = (endpoint, ct) =>
                {
                    capturedEndpoint = endpoint;
                    return ValueTask.FromException<ISession>(new InvalidOperationException("stop"));
                }
            });

            WotCompiledForm form = BuildForm("opc.tcp", 4841, "opc.tcp://actual.example:1111");

            Assert.That(
                async () => await executor.ActivateAsync(form, new WotExecutorContext()).ConfigureAwait(false),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(capturedEndpoint, Is.EqualTo("opc.tcp://actual.example:1111"));
        }

        private static WotCompiledForm BuildForm(string scheme, int port, string baseUri)
        {
            return new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", OpcUaBindingPlanner.BindingUri),
                WotAffordanceKind.Property,
                "p",
                "/properties/p/forms/0",
                WoTBindingCapabilityEnum.ReadProperty,
                "readproperty",
                new WotEndpointDescriptor(scheme, "example.test", port, baseUri),
                new WotAddressingDescriptor("i=2258"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.ReadProperty, "readproperty", "Read"),
                new WotPayloadDescriptor("application/json", "json"),
                ImmutableArray<WotCredentialReference>.Empty,
                isExecutable: true);
        }
    }
}
