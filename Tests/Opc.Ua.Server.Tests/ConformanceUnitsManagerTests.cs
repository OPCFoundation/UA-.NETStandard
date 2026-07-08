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
    /// <summary>
    /// Unit tests for <see cref="ConformanceUnitsManager"/>.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable]
    public class ConformanceUnitsManagerTests
    {
        [Test]
        public async Task PublishAggregatesDeduplicatesAndSortsContributorsAsync()
        {
            ArrayOf<QualifiedName> publishedUnits = default;
            ArrayOf<string> publishedProfiles = default;

            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics
                .Setup(d => d.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (units, profiles, _) =>
                    {
                        publishedUnits = units;
                        publishedProfiles = profiles;
                    })
                .Returns(default(ValueTask));

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);

            var manager = new ConformanceUnitsManager(server.Object);

            manager.Register(new FakeContributor(
                units: [new("Zeta Unit"), new("Alpha Unit")],
                profiles: ["urn:profile:b"]));
            // A second contributor overlaps on one unit and one profile.
            manager.Register(new FakeContributor(
                units: [new("Alpha Unit"), new("Mid Unit")],
                profiles: ["urn:profile:b", "urn:profile:a"]));

            await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

            var names = new List<string>();
            foreach (QualifiedName unit in publishedUnits)
            {
                names.Add(unit.Name);
            }
            Assert.That(names, Is.EqualTo(s_expectedSortedUnitNames));

            var profileList = new List<string>();
            foreach (string profile in publishedProfiles)
            {
                profileList.Add(profile);
            }
            Assert.That(profileList, Has.Count.EqualTo(2));
            Assert.That(profileList, Does.Contain("urn:profile:a"));
            Assert.That(profileList, Does.Contain("urn:profile:b"));
        }

        [Test]
        public void IsSupportedReflectsRegisteredUnits()
        {
            var server = new Mock<IServerInternal>();
            var manager = new ConformanceUnitsManager(server.Object);

            Assert.That(manager.IsSupported(new QualifiedName("Address Space Base")), Is.False);
            Assert.That(manager.IsSupported(QualifiedName.Null), Is.False);

            manager.Register(new FakeContributor(
                units: [new("Address Space Base")],
                profiles: []));

            Assert.That(manager.IsSupported(new QualifiedName("Address Space Base")), Is.True);
            Assert.That(manager.IsSupported(new QualifiedName("Not Registered")), Is.False);
        }

        [Test]
        public async Task PublishWithoutContributorsPublishesEmptyAsync()
        {
            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics
                .Setup(d => d.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);

            var manager = new ConformanceUnitsManager(server.Object);
            await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

            diagnostics.Verify(
                d => d.PublishConformanceUnitsAsync(
                    It.Is<ArrayOf<QualifiedName>>(u => u.Count == 0),
                    It.Is<ArrayOf<string>>(p => p.Count == 0),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static readonly string[] s_expectedSortedUnitNames =
            ["Alpha Unit", "Mid Unit", "Zeta Unit"];

        private sealed class FakeContributor : IConformanceContributor
        {
            public FakeContributor(ArrayOf<QualifiedName> units, ArrayOf<string> profiles)
            {
                ConformanceUnits = units;
                ServerProfiles = profiles;
            }

            public ArrayOf<QualifiedName> ConformanceUnits { get; }

            public ArrayOf<string> ServerProfiles { get; }
        }
    }
}
