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
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Samples.Tests
{
    /// <summary>
    /// Role validation fails explicitly before any scenario or identity file is opened.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingCommandLineTests
    {
        /// <summary>
        /// The shipped sparse JSON recipes must retain the same effective defaults as their named counterparts.
        /// </summary>
        [TestCase("healthy")]
        [TestCase("sorter-drift")]
        [TestCase("utility-disturbance")]
        [TestCase("equipment-faults")]
        [TestCase("combined")]
        [TestCase("recovery")]
        public async Task ShippedScenarioFilesMatchNamedDefaultsAsync(string scenario)
        {
            string json = await File.ReadAllTextAsync(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "Scenarios", scenario + ".json")).ConfigureAwait(false);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            RecyclingConfiguration actual = await RecyclingCommandLine.LoadScenarioAsync(stream).ConfigureAwait(false);
            RecyclingConfiguration expected = RecyclingConfiguration.ForScenario(scenario);

            actual.Validate();
            Assert.That(JsonSerializer.Serialize(actual, RecyclingJsonContext.Default.RecyclingConfiguration),
                Is.EqualTo(JsonSerializer.Serialize(expected, RecyclingJsonContext.Default.RecyclingConfiguration)));
        }

        /// <summary>
        /// Explicit zero settings are preserved and invalid values are not replaced by valid defaults.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ScenarioOverridesPreserveExplicitValuesAsync(bool invalid)
        {
            string json = invalid ? "{\"tickSeconds\":0}" : "{\"plannedSourceBatches\":0,\"thermalSupplyWatts\":0}";
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            RecyclingConfiguration configuration = await RecyclingCommandLine.LoadScenarioAsync(stream)
                .ConfigureAwait(false);
            if (invalid)
            {
                Assert.That(configuration.TickSeconds, Is.Zero);
                Assert.Throws<ArgumentException>(configuration.Validate);
            }
            else
            {
                configuration.Validate();
                Assert.That(configuration.PlannedSourceBatches, Is.Zero);
                Assert.That(configuration.ThermalSupplyWatts, Is.Zero);
                Assert.That(configuration.SourceBatch.Total,
                    Is.EqualTo(new RecyclingConfiguration().SourceBatch.Total));
            }
        }

        /// <summary>
        /// A sparse scenario still rejects unknown members and malformed or null collections.
        /// </summary>
        [TestCase("{\"typo\":1}")]
        [TestCase("{\"stages\":null}")]
        [TestCase("null")]
        [TestCase("[]")]
        public async Task ScenarioOverridesRejectInvalidDocumentsAsync(string json)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            Assert.ThrowsAsync<JsonException>(() => RecyclingCommandLine.LoadScenarioAsync(stream).AsTask());
        }

        /// <summary>
        /// An absent selector is an argument error, with or without unrelated valid options.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MissingSelectorProducesTheRoleValidationError(bool includeOtherOption)
        {
            ArrayOf<string> arguments = includeOtherOption ? ["--line-id", "line-1"] : [];
            ArgumentException failure = Assert.ThrowsAsync<ArgumentException>(() =>
                RecyclingCommandLine.LoadAsync(arguments).AsTask())!;

            Assert.That(failure.Message, Does.Contain("Select exactly --role coordinator or --process"));
        }

        /// <summary>
        /// Both selectors and unsupported role names are rejected by the same explicit contract.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InvalidSelectorCombinationProducesTheRoleValidationError(bool both)
        {
            ArrayOf<string> arguments = both
                ? ["--role", "coordinator", "--process", "collection"]
                : ["--role", "worker"];
            ArgumentException failure = Assert.ThrowsAsync<ArgumentException>(() =>
                RecyclingCommandLine.LoadAsync(arguments).AsTask())!;

            Assert.That(failure.Message, Does.Contain("Select exactly --role coordinator or --process"));
        }
    }
}
