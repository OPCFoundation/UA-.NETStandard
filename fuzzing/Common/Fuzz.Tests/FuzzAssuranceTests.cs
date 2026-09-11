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
using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Verifies that replay requires a nonempty good-seed inventory while regression inputs remain optional.
    /// </summary>
    [TestFixture]
    public sealed class FuzzAssuranceTests
    {
        /// <summary>
        /// Verifies that missing required good seeds fail enumeration rather than imply successful replay.
        /// </summary>
        [Test]
        public void MissingGoodSeedsAreNotSuccessfulReplay()
        {
            Assert.Throws<InvalidOperationException>(() =>
                TestUtils.EnumerateTestAssets("missing-good-seeds-" + Guid.NewGuid().ToString("N"), "*", true));
        }

        /// <summary>
        /// Verifies that an empty directory fails required-seed enumeration but is valid for optional regressions.
        /// </summary>
        [Test]
        public void EmptyGoodSeedDirectoryIsNotSuccessfulReplay()
        {
            string directory = Path.Combine(TestContext.CurrentContext.TestDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                    TestUtils.EnumerateTestAssets(directory, "*", true));
                Assert.That(TestUtils.EnumerateTestAssets(directory, "crash*"), Is.Empty);
            }
            finally
            {
                Directory.Delete(directory);
            }
        }

        /// <summary>
        /// Verifies that a missing optional regression directory produces an empty inventory.
        /// </summary>
        [Test]
        public void MissingOptionalRegressionDirectoryHasAnEmptyInventory()
        {
            Assert.That(
                TestUtils.EnumerateTestAssets("missing-regressions-" + Guid.NewGuid().ToString("N"), "crash*"),
                Is.Empty);
        }
    }
}
