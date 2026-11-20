/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
