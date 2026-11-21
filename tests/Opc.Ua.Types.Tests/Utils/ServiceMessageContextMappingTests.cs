// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Utils
{
    /// <summary>
    /// The namespace and server tables of a message context are read without a
    /// lock; a reader that compares the table and the mapping version it read
    /// earlier with the current ones must still detect every replacement.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public sealed class ServiceMessageContextMappingTests
    {
        [Test]
        public void ReplacingATableAdvancesTheMappingVersion()
        {
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            long version = context.MappingVersion;
            NamespaceTable first = context.NamespaceUris;

            context.NamespaceUris = first;
            Assert.That(context.MappingVersion, Is.EqualTo(version), "setting the same table is no change.");

            var second = new NamespaceTable();
            context.NamespaceUris = second;
            Assert.That(context.NamespaceUris, Is.SameAs(second));
            Assert.That(context.MappingVersion, Is.EqualTo(version + 1));

            var servers = new StringTable();
            context.ServerUris = servers;
            Assert.That(context.ServerUris, Is.SameAs(servers));
            Assert.That(context.MappingVersion, Is.EqualTo(version + 2));

            // back to the first table: the reference matches again, the version does not.
            context.NamespaceUris = first;
            Assert.That(context.NamespaceUris, Is.SameAs(first));
            Assert.That(context.MappingVersion, Is.EqualTo(version + 3));
        }

        [Test]
        public async Task ReadersDetectEveryReplacementWhileTablesAreSwappedAsync()
        {
            const int swaps = 20000;
            var context = ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create());
            NamespaceTable a = context.NamespaceUris;
            var b = new NamespaceTable();
            long initial = context.MappingVersion;
            int done = 0;
            int violations = 0;

            Task[] readers = [.. Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                long lastVersion = initial;
                while (Volatile.Read(ref done) == 0)
                {
                    // version, table, version: when the version did not move, the
                    // table read in between is the one that version belongs to
                    // (the swaps alternate, so odd versions are b and even ones a).
                    long before = context.MappingVersion;
                    NamespaceTable table = context.NamespaceUris;
                    long after = context.MappingVersion;
                    if (before < lastVersion || after < before)
                    {
                        Interlocked.Increment(ref violations);
                    }
                    lastVersion = after;

                    if (before == after &&
                        !ReferenceEquals(table, (before - initial) % 2 == 1 ? b : a))
                    {
                        Interlocked.Increment(ref violations);
                    }
                }
            }))];

            for (int ii = 0; ii < swaps; ii++)
            {
                context.NamespaceUris = ii % 2 == 0 ? b : a;
            }
            Volatile.Write(ref done, 1);
            await Task.WhenAll(readers).ConfigureAwait(false);

            Assert.That(violations, Is.Zero);
            Assert.That(context.MappingVersion, Is.EqualTo(initial + swaps));
            Assert.That(context.NamespaceUris, Is.SameAs(a));
        }
    }
}
