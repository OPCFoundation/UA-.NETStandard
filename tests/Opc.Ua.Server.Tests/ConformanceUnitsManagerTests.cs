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
                names.Add(unit.Name!);
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

        [Test]
        public void ConstructorRejectsNullServer()
        {
            Assert.That(
                () => new ConformanceUnitsManager(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RegisterRejectsNullContributor()
        {
            var server = new Mock<IServerInternal>();
            var manager = new ConformanceUnitsManager(server.Object);
            Assert.That(
                () => manager.Register(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var server = new Mock<IServerInternal>();
            var manager = new ConformanceUnitsManager(server.Object);
            Assert.That(() =>
            {
                manager.Dispose();
                manager.Dispose();
            }, Throws.Nothing);
        }

        [Test]
        public void RegisterIgnoresNullUnitsAndEmptyProfiles()
        {
            var server = new Mock<IServerInternal>();
            var manager = new ConformanceUnitsManager(server.Object);

            manager.Register(new FakeContributor(
                units: [QualifiedName.Null, new("Real Unit")],
                profiles: [string.Empty, "urn:profile:x"]));

            Assert.That(manager.IsSupported(new QualifiedName("Real Unit")), Is.True);
            Assert.That(manager.IsSupported(QualifiedName.Null), Is.False);
        }

        [Test]
        public async Task PublishWithdrawsWhatAContributorNoLongerReportsAsync()
        {
            var published = new List<(ArrayOf<QualifiedName> Units, ArrayOf<string> Profiles)>();
            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics
                .Setup(d => d.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (units, profiles, _) => published.Add((units, profiles)))
                .Returns(default(ValueTask));
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
            using var manager = new ConformanceUnitsManager(server.Object);

            var contributor = new MutableContributor
            {
                ConformanceUnits = [new("Kept Unit"), new("Withdrawn Unit")],
                ServerProfiles = ["urn:profile:kept", "urn:profile:withdrawn"]
            };
            manager.Register(contributor);
            await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

            contributor.ConformanceUnits = [new("Kept Unit")];
            contributor.ServerProfiles = ["urn:profile:kept"];
            await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(published, Has.Count.EqualTo(2));
            Assert.That(NamesOf(published[0].Units), Is.EqualTo(s_keptAndWithdrawnUnitNames));
            Assert.That(NamesOf(published[1].Units), Is.EqualTo(s_keptUnitNames));
            Assert.That(published[1].Profiles.ToArray(), Is.EqualTo(s_keptProfiles));
            Assert.That(manager.IsSupported(new QualifiedName("Withdrawn Unit")), Is.False);
            Assert.That(manager.IsSupported(new QualifiedName("Kept Unit")), Is.True);
        }

        [Test]
        public async Task RegisteringAgainReadsTheContributorOnceAsync()
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
            using var manager = new ConformanceUnitsManager(server.Object);

            var contributor = new MutableContributor { ConformanceUnits = [new("Unit")] };
            manager.Register(contributor);
            manager.Register(contributor);
            contributor.Reads = 0;
            await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(contributor.Reads, Is.EqualTo(1));
        }

        [Test]
        public void RegisteringAgainNeitherReadsNorChangesTheSupportedUnits()
        {
            var server = new Mock<IServerInternal>();
            using var manager = new ConformanceUnitsManager(server.Object);

            var contributor = new MutableContributor { ConformanceUnits = [new("Registered Unit")] };
            manager.Register(contributor);
            contributor.ConformanceUnits = [new("Changed Unit")];
            contributor.Reads = 0;
            manager.Register(contributor);

            Assert.That(contributor.Reads, Is.Zero);
            Assert.That(manager.IsSupported(new QualifiedName("Registered Unit")), Is.True);
            Assert.That(manager.IsSupported(new QualifiedName("Changed Unit")), Is.False);
        }

        [Test]
        public async Task UnregisterDuringAPublishIsNotUndoneByTheStaleReadAsync()
        {
            ArrayOf<QualifiedName> publishedUnits = default;
            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics
                .Setup(d => d.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (units, _, _) => publishedUnits = units)
                .Returns(default(ValueTask));
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
            using var manager = new ConformanceUnitsManager(server.Object);
            using var withdrawn = new BlockingContributor([new("Withdrawn Unit")]);
            manager.Register(withdrawn);
            manager.Register(new FakeContributor(units: [new("Kept Unit")], profiles: []));

            // The publish snapshots both contributors and blocks while reading
            // the first; it is unregistered before the read completes.
            withdrawn.BlockNextRead();
            Task publish = Task.Run(async () => await manager.PublishAsync().ConfigureAwait(false));
            Assert.That(withdrawn.WaitUntilReading(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(manager.Unregister(withdrawn), Is.True);
            Assert.That(manager.IsSupported(new QualifiedName("Withdrawn Unit")), Is.False);
            withdrawn.CompleteRead();
            await publish.ConfigureAwait(false);

            Assert.That(manager.IsSupported(new QualifiedName("Withdrawn Unit")), Is.False);
            Assert.That(manager.IsSupported(new QualifiedName("Kept Unit")), Is.True);
            Assert.That(NamesOf(publishedUnits), Is.EqualTo(s_keptUnitNames));
        }

        [Test]
        public async Task UnregisterWithdrawsTheUnitsOfTheContributorAsync()
        {
            ArrayOf<QualifiedName> publishedUnits = default;
            var diagnostics = new Mock<IDiagnosticsNodeManager>();
            diagnostics
                .Setup(d => d.PublishConformanceUnitsAsync(
                    It.IsAny<ArrayOf<QualifiedName>>(),
                    It.IsAny<ArrayOf<string>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<QualifiedName>, ArrayOf<string>, CancellationToken>(
                    (units, _, _) => publishedUnits = units)
                .Returns(default(ValueTask));
            var server = new Mock<IServerInternal>();
            server.Setup(s => s.DiagnosticsNodeManager).Returns(diagnostics.Object);
            using var manager = new ConformanceUnitsManager(server.Object);

            var first = new FakeContributor(units: [new("First Unit")], profiles: []);
            var second = new FakeContributor(units: [new("Second Unit")], profiles: []);
            manager.Register(first);
            manager.Register(second);

            Assert.That(manager.Unregister(first), Is.True);
            Assert.That(manager.Unregister(first), Is.False);
            await manager.PublishAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(NamesOf(publishedUnits), Is.EqualTo(s_secondUnitNames));
            Assert.That(manager.IsSupported(new QualifiedName("First Unit")), Is.False);
            Assert.That(() => manager.Unregister(null!), Throws.ArgumentNullException);
        }

        private static List<string> NamesOf(ArrayOf<QualifiedName> units)
        {
            var names = new List<string>();
            foreach (QualifiedName unit in units)
            {
                names.Add(unit.Name!);
            }
            return names;
        }

        private static readonly string[] s_keptAndWithdrawnUnitNames = ["Kept Unit", "Withdrawn Unit"];
        private static readonly string[] s_keptUnitNames = ["Kept Unit"];
        private static readonly string[] s_keptProfiles = ["urn:profile:kept"];
        private static readonly string[] s_secondUnitNames = ["Second Unit"];

        private static readonly string[] s_expectedSortedUnitNames =
            ["Alpha Unit", "Mid Unit", "Zeta Unit"];

        private sealed class MutableContributor : IConformanceContributor
        {
            public int Reads { get; set; }

            public ArrayOf<QualifiedName> ConformanceUnits
            {
                get
                {
                    Reads++;
                    return m_units;
                }
                set => m_units = value;
            }

            public ArrayOf<string> ServerProfiles { get; set; }

            private ArrayOf<QualifiedName> m_units;
        }

        /// <summary>
        /// A contributor whose next read of its units blocks until the test
        /// completes it, so a registration change can happen during the read.
        /// </summary>
        private sealed class BlockingContributor : IConformanceContributor, IDisposable
        {
            public BlockingContributor(ArrayOf<QualifiedName> units)
            {
                m_units = units;
            }

            public ArrayOf<QualifiedName> ConformanceUnits
            {
                get
                {
                    if (Interlocked.Exchange(ref m_blockNextRead, 0) == 1)
                    {
                        m_reading.Set();
                        m_completeRead.Wait(TimeSpan.FromSeconds(30));
                    }
                    return m_units;
                }
            }

            public ArrayOf<string> ServerProfiles => [];

            public void BlockNextRead()
            {
                Interlocked.Exchange(ref m_blockNextRead, 1);
            }

            public bool WaitUntilReading(TimeSpan timeout)
            {
                return m_reading.Wait(timeout);
            }

            public void CompleteRead()
            {
                m_completeRead.Set();
            }

            public void Dispose()
            {
                m_reading.Dispose();
                m_completeRead.Dispose();
            }

            private readonly ArrayOf<QualifiedName> m_units;
            private readonly ManualResetEventSlim m_reading = new();
            private readonly ManualResetEventSlim m_completeRead = new();
            private int m_blockNextRead;
        }

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
