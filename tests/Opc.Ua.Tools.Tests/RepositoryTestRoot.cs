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

#if NET10_0_OR_GREATER
using System;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests
{
    /// <summary>
    /// Locates original repository assets when test outputs are outside the checkout.
    /// </summary>
    internal static class RepositoryTestRoot
    {
        /// <summary>
        /// Resolves the configured checkout or a test-assembly ancestor containing the required asset.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The configured root is invalid or no qualifying checkout can be found.
        /// </exception>
        internal static string Find(string requiredRelativePath)
        {
            string? configuredRoot = Environment.GetEnvironmentVariable("OPCUA_TEST_REPOSITORY_ROOT");
            if (configuredRoot is not null)
            {
                if (!string.IsNullOrWhiteSpace(configuredRoot))
                {
                    string root = Path.GetFullPath(configuredRoot);
                    if (ContainsRequiredFiles(root, requiredRelativePath))
                    {
                        return root;
                    }
                }

                throw new InvalidOperationException(
                    $"OPCUA_TEST_REPOSITORY_ROOT must contain UA.slnx and '{requiredRelativePath}'.");
            }

            string? current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (ContainsRequiredFiles(current, requiredRelativePath))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            throw new InvalidOperationException(
                $"Could not find a repository root containing UA.slnx and '{requiredRelativePath}'.");
        }

        /// <summary>
        /// Checks both the solution and the fixture-specific repository asset.
        /// </summary>
        private static bool ContainsRequiredFiles(string root, string requiredRelativePath)
        {
            return File.Exists(Path.Combine(root, "UA.slnx")) &&
                File.Exists(Path.Combine(root, requiredRelativePath));
        }
    }
}
#endif
