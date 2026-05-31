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

#nullable enable

using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.StateMachines
{
    /// <summary>
    /// Smoke tests for source-generator-emitted typed accessors for
    /// every Object child of an emitted ObjectType. Verifies the
    /// well-known standard UA cases (AlarmConditionType.ShelvingState,
    /// ExclusiveLimitAlarmType.LimitState) are reachable as typed
    /// async accessors with the correct return types.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("StateMachines")]
    [Parallelizable]
    public sealed class TypedObjectChildAccessorTests
    {
        [Test]
        public void AlarmConditionTypeClientExposesShelvingStateAccessor()
        {
            MethodInfo? method = typeof(AlarmConditionTypeClient)
                .GetMethod("GetShelvingStateAsync",
                    BindingFlags.Instance | BindingFlags.Public);

            Assert.That(method, Is.Not.Null,
                "AlarmConditionTypeClient should expose GetShelvingStateAsync");
        }

        [Test]
        public void GetShelvingStateAsyncReturnsTypedValueTask()
        {
            MethodInfo method = typeof(AlarmConditionTypeClient)
                .GetMethod("GetShelvingStateAsync",
                    BindingFlags.Instance | BindingFlags.Public)!;

            System.Type returnType = method.ReturnType;
            Assert.That(returnType.IsGenericType, Is.True);
            Assert.That(returnType.GetGenericTypeDefinition(),
                Is.EqualTo(typeof(System.Threading.Tasks.ValueTask<>)));

            System.Type inner = returnType.GetGenericArguments()[0];
            Assert.That(inner, Is.EqualTo(typeof(ShelvedStateMachineTypeClient)),
                "Return type should be ValueTask<ShelvedStateMachineTypeClient?>");
        }

        [Test]
        public void GetShelvingStateAsyncAcceptsTelemetryAndCancellationToken()
        {
            MethodInfo method = typeof(AlarmConditionTypeClient)
                .GetMethod("GetShelvingStateAsync",
                    BindingFlags.Instance | BindingFlags.Public)!;
            ParameterInfo[] parameters = method.GetParameters();

            Assert.That(parameters, Has.Length.EqualTo(2));
            Assert.That(parameters[0].ParameterType,
                Is.EqualTo(typeof(ITelemetryContext)));
            Assert.That(parameters[1].ParameterType,
                Is.EqualTo(typeof(System.Threading.CancellationToken)));
            Assert.That(parameters[1].HasDefaultValue, Is.True);
        }

        [Test]
        public void ExclusiveLimitAlarmTypeClientExposesLimitStateAccessor()
        {
            MethodInfo? method = typeof(ExclusiveLimitAlarmTypeClient)
                .GetMethod("GetLimitStateAsync",
                    BindingFlags.Instance | BindingFlags.Public);

            Assert.That(method, Is.Not.Null,
                "ExclusiveLimitAlarmTypeClient should expose GetLimitStateAsync");
            System.Type inner = method!.ReturnType.GetGenericArguments()[0];
            Assert.That(inner,
                Is.EqualTo(typeof(ExclusiveLimitStateMachineTypeClient)));
        }

        [Test]
        public void GeneratedAccessorIsAsyncMethod()
        {
            MethodInfo method = typeof(AlarmConditionTypeClient)
                .GetMethod("GetShelvingStateAsync",
                    BindingFlags.Instance | BindingFlags.Public)!;
            // The compiler marks async methods with the
            // AsyncStateMachineAttribute. This is the simplest way to
            // assert the body is `async`-compiled (the lazy + caching
            // logic relies on this).
            object[] attrs = method.GetCustomAttributes(
                typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute),
                inherit: false);
            Assert.That(attrs, Is.Not.Empty,
                "Generated accessor should be an async method");
        }
    }
}
