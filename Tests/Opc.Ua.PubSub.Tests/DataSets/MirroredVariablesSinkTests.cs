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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.DataSets
{
    /// <summary>
    /// Validates the MirroredVariablesSink: cache updates, snapshot
    /// isolation and the ValuesChanged event payload.
    /// </summary>
    [TestFixture]
    [TestSpec("6.2.10", Summary = "MirroredVariablesSink cache + event")]
    public class MirroredVariablesSinkTests
    {
        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_UpdatesCacheKeyedByFieldNameAsync()
        {
            var sink = new MirroredVariablesSink();
            await sink.WriteAsync([
                new DataSetField { Name = "alpha", Value = new Variant(1) },
                new DataSetField { Name = "beta", Value = new Variant("two") }
            ]).ConfigureAwait(false);

            IReadOnlyDictionary<string, Variant> values = sink.CurrentValues;
            Assert.That(values, Has.Count.EqualTo(2));
            Assert.That(values["alpha"], Is.EqualTo(new Variant(1)));
            Assert.That(values["beta"], Is.EqualTo(new Variant("two")));
        }

        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_RaisesValuesChangedEventOnceAsync()
        {
            var sink = new MirroredVariablesSink();
            IReadOnlyList<string>? lastUpdate = null;
            sink.ValuesChanged += (_, names) => lastUpdate = names;

            await sink.WriteAsync([
                new DataSetField { Name = "f", Value = new Variant(42) }
            ]).ConfigureAwait(false);

            Assert.That(lastUpdate, Is.Not.Null);
            Assert.That(lastUpdate, Contains.Item("f"));
        }

        [Test]
        [TestSpec("6.2.10")]
        public async Task CurrentValues_SnapshotIsIsolatedAsync()
        {
            var sink = new MirroredVariablesSink();
            await sink.WriteAsync([
                new DataSetField { Name = "f", Value = new Variant(1) }
            ]).ConfigureAwait(false);
            IReadOnlyDictionary<string, Variant> snapshot1 = sink.CurrentValues;

            await sink.WriteAsync([
                new DataSetField { Name = "f", Value = new Variant(2) }
            ]).ConfigureAwait(false);

            Assert.That(snapshot1["f"], Is.EqualTo(new Variant(1)),
                "Previous snapshot must not see later writes.");
            Assert.That(sink.CurrentValues["f"], Is.EqualTo(new Variant(2)));
        }

        [Test]
        [TestSpec("6.2.10")]
        public async Task WriteAsync_SkipsAnonymousFieldsAsync()
        {
            var sink = new MirroredVariablesSink();
            await sink.WriteAsync([
                new DataSetField { Name = string.Empty, Value = new Variant(1) },
                new DataSetField { Name = "named", Value = new Variant(2) }
            ]).ConfigureAwait(false);

            Assert.That(sink.CurrentValues, Has.Count.EqualTo(1));
            Assert.That(sink.CurrentValues, Contains.Key("named"));
        }
    }
}
