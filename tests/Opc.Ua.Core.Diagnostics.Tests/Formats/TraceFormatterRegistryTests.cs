/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class TraceFormatterRegistryTests
    {
        [Test]
        public void CreateDefaultAdvertisesEveryBuiltInFormat()
        {
            var registry = TraceFormatterRegistry.CreateDefault();

            Assert.That(registry.Available, Is.EquivalentTo(
            [
                FormatKind.Pcap,
                FormatKind.PcapNg,
                FormatKind.Json,
                FormatKind.Csv,
                FormatKind.Text,
                FormatKind.ServiceTimeline
            ]));
        }

        [TestCase(FormatKind.Pcap, typeof(PcapFormatter))]
        [TestCase(FormatKind.PcapNg, typeof(PcapNgFormatter))]
        [TestCase(FormatKind.Json, typeof(JsonFormatter))]
        [TestCase(FormatKind.Csv, typeof(CsvFormatter))]
        [TestCase(FormatKind.Text, typeof(TextFormatter))]
        [TestCase(FormatKind.ServiceTimeline, typeof(ServiceTimelineFormatter))]
        public void GetReturnsConcreteFormatterMatchingKind(FormatKind kind, Type expectedType)
        {
            var registry = TraceFormatterRegistry.CreateDefault();

            ITraceFormatter formatter = registry.Get(kind);

            Assert.That(formatter, Is.InstanceOf(expectedType));
            Assert.That(formatter.Kind, Is.EqualTo(kind));
        }

        [Test]
        public void GetThrowsPcapDiagnosticsExceptionWhenKindIsUnregistered()
        {
            // Build a registry that only knows about Json so we can prove
            // Get rejects every other kind with a diagnostic exception.
            TraceFormatterRegistry registry = new([new JsonFormatter()]);

            PcapDiagnosticsException? exception = Assert.Throws<PcapDiagnosticsException>(
                () => registry.Get(FormatKind.Pcap));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception!.Message, Does.Contain("Pcap"));
        }

        [Test]
        public void ConstructorRejectsNullFormatterEnumerable()
        {
            Assert.That(
                () => new TraceFormatterRegistry(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void AvailableMatchesGetForCustomRegistry()
        {
            TraceFormatterRegistry registry = new(
            [
                new TextFormatter(),
                new CsvFormatter()
            ]);

            Assert.That(registry.Available, Is.EquivalentTo([FormatKind.Text, FormatKind.Csv]));
            Assert.That(registry.Get(FormatKind.Text), Is.InstanceOf<TextFormatter>());
            Assert.That(registry.Get(FormatKind.Csv), Is.InstanceOf<CsvFormatter>());
        }
    }
}
