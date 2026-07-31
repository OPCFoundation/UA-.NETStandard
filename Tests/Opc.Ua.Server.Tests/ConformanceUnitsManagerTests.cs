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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public class ConformanceUnitsManagerTests
    {
        [Test]
        public async Task PublishDeduplicatesSortsAndPreservesConfiguredProfilesAsync()
        {
            var fixture = new ServerFixture<StandardServer>();
            StandardServer standardServer = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                ServerCapabilitiesState capabilities = standardServer.CurrentInstance
                    .DiagnosticsNodeManager.FindPredefinedNode<ServerCapabilitiesState>(
                        ObjectIds.Server_ServerCapabilities);
                capabilities.ServerProfileArray.Value = ["urn:configured", "urn:profile:b"];
                using var manager = new ConformanceUnitsManager(standardServer.CurrentInstance);

                manager.Register(new FakeContributor(
                    [new QualifiedName("Zeta Unit"), new QualifiedName("Alpha Unit")],
                    ["urn:profile:b"]));
                manager.Register(new FakeContributor(
                    [new QualifiedName("Alpha Unit"), new QualifiedName("Mid Unit")],
                    ["urn:profile:a", string.Empty]));

                await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    capabilities.ConformanceUnits.Value,
                    Is.EqualTo(new[]
                    {
                        new QualifiedName("Alpha Unit"),
                        new QualifiedName("Mid Unit"),
                        new QualifiedName("Zeta Unit")
                    }));
                Assert.That(
                    capabilities.ServerProfileArray.Value,
                    Is.EqualTo(new[] { "urn:configured", "urn:profile:b", "urn:profile:a" }));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void IsSupportedReflectsRegisteredUnitsAndIgnoresNulls()
        {
            IServerInternal server = new Mock<IServerInternal>().Object;
            using var manager = new ConformanceUnitsManager(server);

            manager.Register(new FakeContributor(
                [QualifiedName.Null, new QualifiedName("Address Space Base")],
                [string.Empty]));

            Assert.That(manager.IsSupported(new QualifiedName("Address Space Base")), Is.True);
            Assert.That(manager.IsSupported(QualifiedName.Null), Is.False);
            Assert.That(manager.IsSupported(new QualifiedName("Not Registered")), Is.False);
        }

        [Test]
        public void NullArgumentsAreRejectedAndDisposeIsIdempotent()
        {
            Assert.That(() => new ConformanceUnitsManager(null), Throws.ArgumentNullException);

            IServerInternal server = new Mock<IServerInternal>().Object;
            var manager = new ConformanceUnitsManager(server);
            Assert.That(() => manager.Register(null), Throws.ArgumentNullException);
            Assert.That(() =>
            {
                manager.Dispose();
                manager.Dispose();
            }, Throws.Nothing);
        }

        private sealed class FakeContributor : IConformanceContributor
        {
            public FakeContributor(
                IReadOnlyList<QualifiedName> conformanceUnits,
                IReadOnlyList<string> serverProfiles)
            {
                ConformanceUnits = conformanceUnits;
                ServerProfiles = serverProfiles;
            }

            public IReadOnlyList<QualifiedName> ConformanceUnits { get; }

            public IReadOnlyList<string> ServerProfiles { get; }
        }
    }
}
