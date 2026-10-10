/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    public sealed partial class ValidatorContractTests
    {
        [Test]
        [Category("NegativeControl")]
        public async Task CallbackExceptionFailsValidationAndDoesNotPublishACompletionReceiptAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            PrepareNegativeControl(drop, "throw");

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            await AssertNegativeControlExitedAsync(drop, result, "throw").ConfigureAwait(false);
            AssertRejected(drop, result, beforeReplay: false,
                "Published callback " + ContractDrop.ControlId + " failed with exit");
            AssertSuccessfulReplayPrefix(drop);
            Assert.That(File.ReadAllText(Path.Combine(drop.Results, ContractDrop.ControlId + ".stderr.log")),
                Does.Contain("SYNTHETIC_ONEFUZZ_CALLBACK_EXCEPTION"));
        }

        [Test]
        [Category("NegativeControl")]
        public async Task ZeroExitWithoutChildCompletionReceiptIsNotSuccessfulValidationAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            PrepareNegativeControl(drop, "exit0");

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            await AssertNegativeControlExitedAsync(drop, result, "exit0").ConfigureAwait(false);
            AssertRejected(drop, result, beforeReplay: false,
                "Replay exited without a completion receipt", ContractDrop.ControlId, "zero skips are required");
            AssertSuccessfulReplayPrefix(drop);
            Assert.That(File.ReadAllText(Path.Combine(drop.Results, ContractDrop.ControlId + ".stderr.log")),
                Is.Empty);
        }

        [Test]
        [Category("NegativeControl")]
        public async Task HardWatchdogKillsNoncooperativeSynchronousCallbackInItsChildProcessAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            PrepareNegativeControl(drop, "spin");

            // The outer 45-second guard is intentionally much larger. A test-harness
            // kill is a failure, not evidence that the validator watchdog worked.
            ProcessResult result = await ValidateAsync(drop, "--timeout-seconds", "3").ConfigureAwait(false);

            await AssertNegativeControlExitedAsync(drop, result, "spin").ConfigureAwait(false);
            AssertRejected(drop, result, beforeReplay: false,
                "Hard process watchdog terminated " + ContractDrop.ControlId, "after 3 seconds");
            AssertSuccessfulReplayPrefix(drop);
            Assert.That(File.ReadAllText(Path.Combine(drop.Results, ContractDrop.ControlId + ".stderr.log")),
                Is.Empty);
        }

        [TestCase("target", "Incomplete or mismatched callback receipt")]
        [TestCase("type", "Incomplete or mismatched callback receipt")]
        [TestCase("method", "Incomplete or mismatched callback receipt")]
        [TestCase("count", "Incomplete or mismatched callback receipt")]
        [TestCase("extra", "Incomplete or mismatched callback receipt")]
        [TestCase("path", "Replay did not complete the exact input inventory")]
        [TestCase("hash", "Replay did not complete the exact input inventory")]
        [Category("NegativeControl")]
        public async Task ZeroExitWithIncorrectReceiptCannotClaimCallbackCompletionAsync(
            string field,
            string diagnostic)
        {
            using var drop = new ContractDrop(m_fixture);
            string control = "receipt-" + field;
            PrepareNegativeControl(drop, control);

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            await AssertNegativeControlExitedAsync(drop, result, control).ConfigureAwait(false);
            AssertRejected(drop, result, beforeReplay: false, diagnostic, ContractDrop.ControlId);
            AssertSuccessfulReplayPrefix(drop, expectControlReceipt: true);
            Assert.That(File.ReadAllText(Path.Combine(drop.Results, ContractDrop.ControlId + ".stderr.log")),
                Is.Empty);
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(drop.Results, ContractDrop.ControlId + ".json")));
            JsonElement receipt = document.RootElement;
            switch (field)
            {
                case "target":
                    Assert.That(receipt.GetProperty(field).GetString(), Is.EqualTo("other.synthetic.target"));
                    break;
                case "type":
                    Assert.That(receipt.GetProperty(field).GetString(), Is.EqualTo("Synthetic.UnexpectedType"));
                    break;
                case "method":
                    Assert.That(receipt.GetProperty(field).GetString(), Is.EqualTo("OtherMethod"));
                    break;
                case "count":
                    Assert.That(receipt.GetProperty("inputs").GetArrayLength(), Is.Zero);
                    break;
                case "extra":
                    Assert.That(receipt.GetProperty("inputs").GetArrayLength(), Is.EqualTo(2));
                    break;
                case "path":
                    Assert.That(receipt.GetProperty("inputs")[0].GetProperty("path").GetString(),
                        Is.EqualTo("fuzzing/ContractFixture/Corpus/NegativeControl/different.bin"));
                    break;
                case "hash":
                    Assert.That(receipt.GetProperty("inputs")[0].GetProperty("sha256").GetString(),
                        Is.EqualTo(new string('0', 64)));
                    break;
            }
        }

        private static void PrepareNegativeControl(ContractDrop drop, string control)
        {
            // Only this one synthetic callback interprets the reserved input.
            // It must never enter a service manifest or the shared fuzz harness.
            drop.AddInput(ContractDrop.ControlId, ContractDrop.ControlSeed,
                Encoding.UTF8.GetBytes("onefuzz-contract:" + control));
            drop.Save();
        }

        private static void AssertSuccessfulReplayPrefix(ContractDrop drop, bool expectControlReceipt = false)
        {
            AssertCallbackCompleted(drop, ContractDrop.InspectId, "Inspect");
            AssertCallbackCompleted(drop, ContractDrop.MeasureId, "Measure");
            Assert.That(File.Exists(Path.Combine(drop.Results, ContractDrop.ControlId + ".json")),
                Is.EqualTo(expectControlReceipt),
                "Only the explicit incorrect-receipt controls may leave a receipt after failure.");
        }

        private static async Task AssertNegativeControlExitedAsync(
            ContractDrop drop,
            ProcessResult result,
            string control)
        {
            string log = Path.Combine(drop.Results, ContractDrop.ControlId + ".stdout.log");
            Assert.That(File.Exists(log), Is.True, result.Diagnostics);
            string[] lines = File.ReadAllLines(log);
            string marker = lines.Single(line => line.StartsWith(
                "negative-control:" + control + ":", StringComparison.Ordinal));
            string[] parts = marker.Split(':');
            int processId = int.Parse(parts[2], CultureInfo.InvariantCulture);
            long startTicks = long.Parse(parts[3], CultureInfo.InvariantCulture);

            // Check the recorded process identity before any assertions that could
            // interrupt cleanup. A reused PID must never cause an unrelated kill.
            Process child;
            try
            {
                child = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                AssertChildIdentityAndLog();
                return;
            }

            using (child)
            {
                bool stillRunning = !child.HasExited &&
                    child.StartTime.ToUniversalTime().Ticks == startTicks;
                if (stillRunning)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().ConfigureAwait(false);
                }

                Assert.That(stillRunning, Is.False,
                    "Validator left the exact negative-control child alive; the test cleaned it up.");
            }

            AssertChildIdentityAndLog();

            void AssertChildIdentityAndLog()
            {
                Assert.That(processId, Is.Not.EqualTo(result.ProcessId).And.Not.EqualTo(Environment.ProcessId),
                    "The callback must run in _replay, not in the validator parent or the test host.");
                Assert.That(lines, Is.EqualTo(new[]
                {
                    ContractDrop.ControlId + ": " + ContractDrop.ControlSeed,
                    marker
                }));
            }
        }
    }
}
