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
            string reason = ExpectedDifferences.ReasonForTest(test.TypeInfo?.Name, test.Name);
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
