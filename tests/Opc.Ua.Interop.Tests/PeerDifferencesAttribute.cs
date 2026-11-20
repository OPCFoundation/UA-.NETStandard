/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// Reports a failed test of a fixture as inconclusive when the peer's
    /// expected-differences.json lists it as "Fixture.Test" (the test name
    /// includes the case arguments, e.g. ValueAboveServerLimitIsRejectedAsync("Int32Array")),
    /// and warns when a listed test passes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PeerDifferencesAttribute : Attribute, ITestAction
    {
        public ActionTargets Targets => ActionTargets.Test;

        public void BeforeTest(ITest test)
        {
        }

        public void AfterTest(ITest test)
        {
            string reason = ExpectedDifferences.ReasonForTest((test.TypeInfo?.Name)!, test.Name);
            if (reason == null)
            {
                return;
            }
            TestResult result = TestExecutionContext.CurrentContext.CurrentResult;
            if (result.ResultState.Status == TestStatus.Failed)
            {
                result.SetResult(
                    ResultState.Inconclusive,
                    $"Expected difference of this peer: {reason}{Environment.NewLine}{result.Message}");
            }
            else if (result.ResultState.Status == TestStatus.Passed)
            {
                result.SetResult(
                    ResultState.Warning,
                    $"'{test.Name}' is listed in {ExpectedDifferences.FileName} ({reason}) but passed; remove the entry.");
            }
        }
    }
}
