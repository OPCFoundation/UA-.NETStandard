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

#nullable enable
using System;
using System.IO;

namespace Opc.Ua.Security.Pkcs11.Tests
{
    /// <summary>
    /// Locates a PKCS#11 module to test against.
    /// </summary>
    /// <remarks>
    /// The test suite runs on agents that may or may not have a token. Rather
    /// than failing where none is present, the token backed tests skip, so a
    /// missing module reduces coverage instead of breaking the build. CI installs
    /// SoftHSM2 and sets the environment below so the coverage is real there.
    /// <para>
    /// Configure with <c>OPCUA_PKCS11_MODULE</c>, <c>OPCUA_PKCS11_TOKEN</c> and
    /// <c>OPCUA_PKCS11_PIN</c>. When the module variable is unset, well known
    /// SoftHSM2 install locations are probed.
    /// </para>
    /// </remarks>
    internal static class Pkcs11TestEnvironment
    {
        /// <summary>
        /// The module path to test against, or <c>null</c> when none is available.
        /// </summary>
        public static string? ModulePath { get; } = FindModule();

        /// <summary>
        /// The label of the token to use.
        /// </summary>
        public static string TokenLabel { get; } =
            Environment.GetEnvironmentVariable("OPCUA_PKCS11_TOKEN") ?? "opcua-test";

        /// <summary>
        /// The user PIN to log in with.
        /// </summary>
        public static string Pin { get; } =
            Environment.GetEnvironmentVariable("OPCUA_PKCS11_PIN") ?? "1234";

        /// <summary>
        /// Whether the module was named explicitly rather than auto-discovered.
        /// </summary>
        /// <remarks>
        /// CI sets <c>OPCUA_PKCS11_MODULE</c>, so an explicitly configured module
        /// that then turns out to be unusable is a real failure there and must
        /// not be quietly skipped. A module found by probing well known paths is
        /// a convenience for developer machines, so an unusable one only costs
        /// coverage.
        /// </remarks>
        public static bool IsExplicitlyConfigured { get; } =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPCUA_PKCS11_MODULE"));

        /// <summary>
        /// Whether a module is available to test against.
        /// </summary>
        public static bool IsAvailable => ModulePath != null;

        /// <summary>
        /// The reason to report when skipping.
        /// </summary>
        public static string SkipReason =>
            "No PKCS#11 module is available. Set OPCUA_PKCS11_MODULE to the path of " +
            "one (for example SoftHSM2) to run the token backed tests.";

        /// <summary>
        /// Builds the options for the configured token.
        /// </summary>
        /// <returns>The options.</returns>
        public static Pkcs11TokenOptions CreateOptions()
        {
            return new Pkcs11TokenOptions
            {
                ModulePath = ModulePath,
                TokenLabel = TokenLabel,
                Pin = Pin
            };
        }

        /// <summary>
        /// Builds the RFC 7512 URI for the configured token.
        /// </summary>
        /// <returns>The store path.</returns>
        public static string CreateStorePath()
        {
            return $"pkcs11:token={TokenLabel}?module-path={ModulePath}&pin-value={Pin}";
        }

        private static string? FindModule()
        {
            string? configured = Environment.GetEnvironmentVariable("OPCUA_PKCS11_MODULE");

            if (!string.IsNullOrEmpty(configured))
            {
                return File.Exists(configured) ? configured : null;
            }

            // Declared here rather than in a static field so this cannot depend
            // on static initialization order, which the properties above force.
            string[] wellKnownModules =
            [
                "/usr/lib/softhsm/libsofthsm2.so",
                "/usr/lib/x86_64-linux-gnu/softhsm/libsofthsm2.so",
                "/usr/lib64/pkcs11/libsofthsm2.so",
                "/usr/local/lib/softhsm/libsofthsm2.so",
                "/opt/homebrew/lib/softhsm/libsofthsm2.so",
                "/usr/local/opt/softhsm/lib/softhsm/libsofthsm2.so"
            ];

            foreach (string candidate in wellKnownModules)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
