// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Certificate stores of the lifecycle tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lifecycle tests run in parallel, so every test owns a private store root;
    /// trust-list changes, rejected certificates and deletions stay private. Every
    /// server and client fixture a lifecycle test creates sets
    /// <c>ReuseCachedCertificates</c>, so <see cref="TestFramework.TestCertificateCache"/>
    /// seeds the empty store with the certificates the first fixture of the same
    /// identity created in this process, and no other test creates key pairs.
    /// </para>
    /// <para>
    /// Servers of one type, and the clients, therefore share key pairs within a test
    /// process. No lifecycle test compares the identity of two applications.
    /// </para>
    /// </remarks>
    public sealed partial class NodeManagerLifecycleTests
    {
        /// <summary>
        /// Returns a new store root for the current test.
        /// </summary>
        private static string CreatePkiRoot()
        {
            return Path.Combine(Path.GetTempPath(), "nl-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Deletes a test's store root, ignoring files a stopped server still holds open.
        /// </summary>
        private static void DeletePkiRoot(string pkiRoot)
        {
            if (string.IsNullOrEmpty(pkiRoot) || !Directory.Exists(pkiRoot))
            {
                return;
            }
            try
            {
                Directory.Delete(pkiRoot, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder left behind is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // A temporary folder left behind is harmless.
            }
        }

        /// <summary>
        /// Marks a test that hosts its own server, or none, so the fixture does not start
        /// the shared per-test server and session for it.
        /// </summary>
        [AttributeUsage(AttributeTargets.Method)]
        private sealed class NoFixtureServerAttribute : Attribute
        {
        }
    }
}
