#if NET9_0_OR_GREATER
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
using System.Net.Quic;
using NUnit.Framework;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// Decides whether a QUIC fixture may skip itself.
    /// </summary>
    /// <remarks>
    /// .NET reaches QUIC through msquic, which not every platform carries, so
    /// a skip is the honest outcome where it is genuinely absent: a red run
    /// would claim the code is broken when the runtime simply cannot host it.
    /// A skip is not honest on a machine that is supposed to have msquic,
    /// though. The QUIC tests silently skipped on every CI agent for the whole
    /// life of this feature, which left the transport untested and its whole
    /// assembly reading as uncovered. Setting UA_REQUIRE_QUIC turns that
    /// silence into a failure.
    /// </remarks>
    internal static class QuicTestSupport
    {
        /// <summary>
        /// Skips the calling test when QUIC is unavailable, unless this
        /// machine is required to provide it.
        /// </summary>
        public static void SkipUnlessAvailable()
        {
#pragma warning disable CA2252 // QUIC is a preview API on the older TFMs.
            if (QuicListener.IsSupported && QuicConnection.IsSupported)
            {
                return;
            }
#pragma warning restore CA2252

            if (IsRequired)
            {
                Assert.Fail(
                    "QUIC is unavailable but UA_REQUIRE_QUIC is set, so this machine is " +
                        "expected to provide msquic. Skipping here would hide the QUIC " +
                        "transport from the test run and from coverage.");
            }

            Assert.Ignore("QUIC is unavailable on this platform (msquic missing).");
        }

        /// <summary>
        /// True when the environment declares that msquic has been provisioned.
        /// </summary>
        private static bool IsRequired
        {
            get
            {
                string? value = Environment.GetEnvironmentVariable("UA_REQUIRE_QUIC");

                return !string.IsNullOrEmpty(value) &&
                    !string.Equals(value, "0", StringComparison.Ordinal) &&
                    !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}

#endif
