/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            Type toolType = McpAssemblyProbe.GetRequiredType("Opc.Ua.Mcp.Tools.PacketDecodeTools");

            MethodInfo? method = toolType.GetMethod(
                "ResolveAndValidateDecodePath",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            return method!;
        }

        private static string CreateAllowedRoot()
        {
            return Path.GetFullPath(Path.Combine("test-artifacts", "opcua-pcap"));
        }
    }
}
#endif
