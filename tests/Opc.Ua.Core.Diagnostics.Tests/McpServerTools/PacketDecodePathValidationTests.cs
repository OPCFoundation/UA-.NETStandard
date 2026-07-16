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

// Opc.Ua.Mcp targets net10.0 only and is loaded reflectively from its net10.0
// build output, so these tests only build and run on net10.0.
#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

namespace Opc.Ua.Pcap.Tests.McpServerTools
{
    /// <summary>
    /// Tests path validation used by MCP packet-decode tools.
    /// </summary>
    [TestFixture]
    public sealed class PacketDecodePathValidationTests
    {
        [Test]
        public void ResolveAndValidateDecodePathRejectsParentTraversal()
        {
            string allowedRoot = CreateAllowedRoot();

            // Build the traversal path with Path.Combine so it works on
            // both Windows (separator = '\') and Linux (separator = '/').
            // A hard-coded "..\..\etc\passwd" is treated as a single
            // filename on Linux (backslash is a valid filename character
            // on POSIX) and therefore Path.GetFullPath does not actually
            // escape the allowed root, so the test would erroneously
            // pass through the validator without throwing.
            string traversalPath = Path.Combine("..", "..", "etc", "passwd");

            Assert.That(
                () => InvokeResolveAndValidateDecodePath(traversalPath, allowedRoot),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void ResolveAndValidateDecodePathRejectsAbsolutePathOutsideAllowedRoot()
        {
            string allowedRoot = CreateAllowedRoot();
            string pathRoot = Path.GetPathRoot(allowedRoot)!;
            string outsidePath = Path.Combine(pathRoot, "outside-opcua-pcap", "capture.pcap");

            Assert.That(
                () => InvokeResolveAndValidateDecodePath(outsidePath, allowedRoot),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void ResolveAndValidateDecodePathAcceptsRelativePathWithinRoot()
        {
            string allowedRoot = CreateAllowedRoot();
            string expectedPath = Path.Combine(allowedRoot, "capture.pcap");

            string actualPath = InvokeResolveAndValidateDecodePath("capture.pcap", allowedRoot);

            Assert.That(actualPath, Is.EqualTo(expectedPath));
        }

        [Test]
        public void ResolveAndValidateDecodePathAcceptsAbsolutePathInsideRoot()
        {
            string allowedRoot = CreateAllowedRoot();
            string expectedPath = Path.Combine(allowedRoot, "sub", "keys.uakeys.json");

            string actualPath = InvokeResolveAndValidateDecodePath(expectedPath, allowedRoot);

            Assert.That(actualPath, Is.EqualTo(expectedPath));
        }

        [Test]
        public void ResolveAndValidateDecodePathRejectsNullOrEmpty()
        {
            string allowedRoot = CreateAllowedRoot();

            foreach (string? invalidPath in new string?[] { null, string.Empty, "  " })
            {
                Assert.That(
                    () => InvokeResolveAndValidateDecodePath(invalidPath, allowedRoot),
                    Throws.InstanceOf<ArgumentException>());
            }
        }

        [Test]
        public void ResolveAndValidateDecodePathRejectsUncPath()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("UNC path validation is only meaningful on Windows.");
            }

            string allowedRoot = CreateAllowedRoot();

            Assert.That(
                () => InvokeResolveAndValidateDecodePath(@"\\server\share\capture.pcap", allowedRoot),
                Throws.InstanceOf<ArgumentException>());
        }

        private static string InvokeResolveAndValidateDecodePath(string? filePath, string allowedRoot)
        {
            MethodInfo method = GetResolveAndValidateDecodePathMethod();

            try
            {
                return (string)method.Invoke(null, [filePath, allowedRoot])!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static MethodInfo GetResolveAndValidateDecodePathMethod()
        {
            Assembly assembly = LoadMcpAssembly();
            Type? toolType = assembly.GetType("Opc.Ua.Mcp.Tools.PacketDecodeTools", throwOnError: false);

            Assert.That(toolType, Is.Not.Null);

            MethodInfo? method = toolType!.GetMethod(
                "ResolveAndValidateDecodePath",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            return method!;
        }

        private static Assembly LoadMcpAssembly()
        {
            string repoRoot = FindRepositoryRoot();
            string configuration = GetBuildConfiguration();
            string? assemblyPath = Path.Combine(
                repoRoot,
                "tools",
                "Opc.Ua.Mcp",
                "bin",
                configuration,
                "net10.0",
                "Opc.Ua.Mcp.dll");

            if (!File.Exists(assemblyPath))
            {
                string binPath = Path.Combine(repoRoot, "tools", "Opc.Ua.Mcp", "bin");
                assemblyPath = Directory.Exists(binPath)
                    ? Directory.EnumerateFiles(binPath, "Opc.Ua.Mcp.dll", SearchOption.AllDirectories)
                        .FirstOrDefault()
                    : null;
            }

            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            {
                Assert.Ignore(
                    "The net10.0 Opc.Ua.Mcp assembly is not built for this CI leg " +
                    "(the MCP server only targets net10.0); skipping the reflective MCP server test.");
            }

            return Assembly.LoadFrom(assemblyPath!);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            Assert.Fail("Unable to locate repository root.");
            throw new InvalidOperationException("Unable to locate repository root.");
        }

        private static string GetBuildConfiguration()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (string.Equals(directory.Name, "Debug", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directory.Name, "Release", StringComparison.OrdinalIgnoreCase))
                {
                    return directory.Name;
                }

                directory = directory.Parent;
            }

            return "Debug";
        }

        private static string CreateAllowedRoot()
        {
            return Path.GetFullPath(Path.Combine("test-artifacts", "opcua-pcap"));
        }
    }
}
#endif
