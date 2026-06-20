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
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Formats;
using Opc.Ua.Pcap.Models;

using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Formats
{
    [TestFixture]
    public sealed class TraceFormatterRegistryTests
    {
        [Test]
        public void CreateDefaultAdvertisesEveryBuiltInFormat()
        {
            var registry = TraceFormatterRegistry.CreateDefault();

            Assert.That(registry.Available, Is.EquivalentTo(new[]
            {
                FormatKind.Pcap,
                FormatKind.PcapNg,
                FormatKind.Json,
                FormatKind.Csv,
                FormatKind.Text,
                FormatKind.ServiceTimeline
            }));
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
            TraceFormatterRegistry registry = new(new ITraceFormatter[] { new JsonFormatter() });

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
            TraceFormatterRegistry registry = new(new ITraceFormatter[]
            {
                new TextFormatter(),
                new CsvFormatter()
            });

            Assert.That(registry.Available, Is.EquivalentTo(new[] { FormatKind.Text, FormatKind.Csv }));
            Assert.That(registry.Get(FormatKind.Text), Is.InstanceOf<TextFormatter>());
            Assert.That(registry.Get(FormatKind.Csv), Is.InstanceOf<CsvFormatter>());
        }
    }
}
