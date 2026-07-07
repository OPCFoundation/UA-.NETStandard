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
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.DependencyInjection
{
    /// <summary>
    /// Unit tests for
    /// <see cref="PubSubSecurityServiceCollectionExtensions"/>.
    /// </summary>
    [TestFixture]
    public class PubSubSecurityServiceCollectionExtensionsTests
    {
        [Test]
        public void AddPubSubSecurityKeyServiceClient_BindsOptions()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceClient();
            ServiceProvider sp = services.BuildServiceProvider();
            PullSecurityKeyProviderOptions options =
                sp.GetRequiredService<IOptions<PullSecurityKeyProviderOptions>>().Value;
            Assert.That(options, Is.Not.Null);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceServer_RegistersSingleton()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITelemetryContext>(NUnitTelemetryContext.Create());
            IOpcUaBuilder builder = services.AddOpcUa();
            builder.AddPubSubSecurityKeyServiceServer();
            ServiceProvider sp = services.BuildServiceProvider();
            InMemoryPubSubKeyServiceServer? server =
                sp.GetService<InMemoryPubSubKeyServiceServer>();
            Assert.That(server, Is.Not.Null);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceClient_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSubSecurityKeyServiceClient(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddPubSubSecurityKeyServiceServer_NullBuilder_Throws()
        {
            IOpcUaBuilder? builder = null;
            Assert.That(
                () => builder!.AddPubSubSecurityKeyServiceServer(),
                Throws.ArgumentNullException);
        }
    }
}
