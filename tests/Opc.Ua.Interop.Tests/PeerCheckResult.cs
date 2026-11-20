// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// One "RESULT {json}" line of the legacy peer in client mode.
    /// </summary>
    public sealed class PeerCheckResult
    {
        public string Check { get; private set; } = null!;
        public bool Passed { get; private set; }
        public string Message { get; private set; } = null!;
        public long Milliseconds { get; private set; }

        public static PeerCheckResult Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return new PeerCheckResult
            {
                Check = root.GetProperty("check").GetString()!,
                Passed = root.GetProperty("outcome").GetString() == "Passed",
                Message = root.GetProperty("message").GetString()!,
                Milliseconds = root.GetProperty("milliseconds").GetInt64()
            };
        }

        /// <summary>
        /// Asserts that the named check ran and passed; the failure message
        /// carries the peer's reason and its full output.
        /// </summary>
        public static void AssertPassed(
            IReadOnlyList<PeerCheckResult> results,
            string check,
            string peerOutput)
        {
            PeerCheckResult? result = results.FirstOrDefault(r => r.Check == check);
            if (result == null)
            {
                Assert.Fail(
                    $"The 1.5 client did not run '{check}'." + Environment.NewLine + peerOutput);
                return;
            }
            ExpectedDifferences.Apply(check, result.Passed, result.Message);
            Assert.That(
                result.Passed,
                Is.True,
                $"1.5 client check '{check}' failed: {result.Message}" + Environment.NewLine + peerOutput);
        }
    }
}
