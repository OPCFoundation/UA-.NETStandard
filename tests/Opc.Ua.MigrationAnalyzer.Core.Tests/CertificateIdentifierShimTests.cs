// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Runtime tests for <see cref="CertificateIdentifierShim"/>.
    /// </summary>
    [TestFixture]
    [Category("Shim")]
    public class CertificateIdentifierShimTests
    {
        /// <summary>
        /// Accessing the obsolete <c>Certificate</c> property must throw
        /// <see cref="NotSupportedException"/> with the migration-pointer
        /// message that names the async resolver replacement.
        /// </summary>
        [Test]
        public Task CertificateGetterThrowsNotSupportedAsync()
        {
            var id = new CertificateIdentifier();

#pragma warning disable CS0618 // CertificateIdentifier.Certificate is an intentional shim call.
            NotSupportedException ex = Assert.Throws<NotSupportedException>(
                () => _ = id.Certificate)!;
#pragma warning restore CS0618

            Assert.That(ex.Message, Does.Contain("CertificateIdentifierResolver.ResolveAsync"));
            return Task.CompletedTask;
        }
    }
}
