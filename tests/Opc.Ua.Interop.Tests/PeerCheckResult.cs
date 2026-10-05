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
        public string Check { get; private set; }
        public bool Passed { get; private set; }
        public string Message { get; private set; }
        public long Milliseconds { get; private set; }

        public static PeerCheckResult Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return new PeerCheckResult
            {
                Check = root.GetProperty("check").GetString(),
                Passed = root.GetProperty("outcome").GetString() == "Passed",
                Message = root.GetProperty("message").GetString(),
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
            PeerCheckResult result = results.FirstOrDefault(r => r.Check == check);
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
