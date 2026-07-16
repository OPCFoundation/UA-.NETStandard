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

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Runtime tests for the obsolete static <c>DataValue.IsGood</c> /
    /// <c>IsBad</c> shim helpers defined in <c>DataValueObsolete</c>. Regression
    /// guard for the Phase 11.A fix that corrected the extension receiver type
    /// from <c>ExtensionObject</c> to <c>DataValue</c>; the wrong receiver type
    /// would have made these statics unreachable via the <c>DataValue.</c>
    /// qualifier (which is exactly how callers invoke them).
    /// </summary>
    [TestFixture]
    [Category("Shim")]
    public class DataValueObsoleteShimTests
    {
        [Test]
        public Task IsGoodOnDefaultDataValueReturnsTrueAsync()
        {
#pragma warning disable CS0618 // Intentional shim call.
            bool isGood = DataValue.IsGood(default);
#pragma warning restore CS0618

            Assert.That(isGood, Is.True);
            return Task.CompletedTask;
        }

        [Test]
        public Task IsBadOnBadStatusCodeReturnsTrueAsync()
        {
            // Construct via FromStatusCode to avoid the obsolete numeric overload
            // ambiguity flagged on the DataValue(StatusCode) constructor.
            StatusCode bad = Types.StatusCodes.Bad;
            var dv = DataValue.FromStatusCode(bad);

#pragma warning disable CS0618 // Intentional shim call.
            bool isBad = DataValue.IsBad(dv);
#pragma warning restore CS0618

            Assert.That(isBad, Is.True);
            return Task.CompletedTask;
        }
    }
}
