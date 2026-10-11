/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Text.Json;
using NUnit.Framework;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// Known defects of a peer's stack, read from "expected-differences.json"
    /// next to the peer: { "&lt;check or test name&gt;": { "reason": "...",
    /// "issue": "&lt;upstream link&gt;" } }. A listed failure is reported as
    /// inconclusive with its reason instead of failing the 2.0 tests; a
    /// listed check that passes is reported with a warning so the list is
    /// pruned once the stack is fixed.
    /// </summary>
    public static class ExpectedDifferences
    {
        public const string FileName = "expected-differences.json";

        private static readonly Lazy<Dictionary<string, string>> s_differences = new(Load);

        /// <summary>
        /// The reason a check or test is expected to fail with this peer, or
        /// null when it is expected to pass.
        /// </summary>
        public static string Reason(string name)
        {
            if (s_differences.Value.TryGetValue(name, out string? reason))
            {
                return reason;
            }
            // A key ending with '*' lists every test whose name starts with
            // the rest, e.g. all checks of one security configuration.
            foreach (KeyValuePair<string, string> entry in s_differences.Value)
            {
                if (entry.Key.EndsWith('*') &&
                    name.StartsWith(entry.Key.Substring(0, entry.Key.Length - 1), StringComparison.Ordinal))
                {
                    return entry.Value;
                }
            }
            return null!;
        }

        /// <summary>
        /// Ends a test whose check failed as expected, or warns when a listed
        /// check passed.
        /// </summary>
        public static void Apply(string name, bool passed, string failure)
        {
            string reason = Reason(name);
            if (reason == null)
            {
                return;
            }
            if (passed)
            {
                Assert.Warn($"'{name}' is listed in {FileName} ({reason}) but passed; remove the entry.");
                return;
            }
            Assert.Inconclusive($"Expected difference of this peer: {reason}{Environment.NewLine}{failure}");
        }

        /// <summary>
        /// The reason a test of this stack against the peer is expected to
        /// fail, looked up by "Fixture.Test" (e.g.
        /// "LegacyServerInteropTests.ConnectWithWrongPasswordIsRejected").
        /// </summary>
        public static string ReasonForTest(string fixture, string testName)
        {
            return Reason(fixture + "." + testName);
        }

        private static Dictionary<string, string> Load()
        {
            var differences = new Dictionary<string, string>(StringComparer.Ordinal);
            // Next to the peer, or up to two levels up for peers run from a
            // build output folder (milo/target/milo-peer.jar,
            // async-opcua/target/release/async-opcua-peer).
            string folder = LegacyPeerProcess.PeerDirectory;
            string file = Path.Combine(folder, FileName);
            for (int level = 0; level < 2 && !File.Exists(file) && Path.GetDirectoryName(folder) != null; level++)
            {
                folder = Path.GetDirectoryName(folder)!;
                file = Path.Combine(folder!, FileName);
            }
            if (!File.Exists(file))
            {
                return differences;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                string reason = (entry.Value.TryGetProperty("reason", out JsonElement r) ? r.GetString() : string.Empty)!;
                if (entry.Value.TryGetProperty("issue", out JsonElement issue))
                {
                    reason += " (" + issue.GetString() + ")";
                }
                differences[entry.Name] = reason!;
            }
            return differences;
        }
    }
}
