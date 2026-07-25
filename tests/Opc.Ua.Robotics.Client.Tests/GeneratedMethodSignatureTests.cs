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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Robotics;

namespace Opc.Ua.Robotics.Client.Tests
{
    /// <summary>
    /// Verifies the public method-state APIs generated from the Robotics NodeSet.
    /// </summary>
    [TestFixture]
    [Category("Robotics")]
    public sealed class GeneratedMethodSignatureTests
    {
        [Test]
        public void LoadByNameCarriesNameAndStatus()
        {
            MethodInfo syncInvoke = GetInvoke("LoadByNameMethodStateMethodCallHandler");
            MethodInfo asyncInvoke = GetInvoke("LoadByNameMethodStateMethodAsyncCallHandler");
            Type resultType = GetRoboticsType("LoadByNameMethodStateResult");

            Assert.Multiple(() =>
            {
                Assert.That(syncInvoke.GetParameters(), Has.Length.EqualTo(5));
                Assert.That(syncInvoke.GetParameters()[3].ParameterType, Is.EqualTo(typeof(string)));
                Assert.That(syncInvoke.GetParameters()[3].Name, Is.EqualTo("name"));
                Assert.That(
                    syncInvoke.GetParameters()[4].ParameterType,
                    Is.EqualTo(typeof(int).MakeByRefType()));
                Assert.That(asyncInvoke.GetParameters(), Has.Length.EqualTo(5));
                Assert.That(asyncInvoke.GetParameters()[3].ParameterType, Is.EqualTo(typeof(string)));
                Assert.That(
                    asyncInvoke.GetParameters()[4].ParameterType,
                    Is.EqualTo(typeof(CancellationToken)));
                AssertAsyncResult(asyncInvoke.ReturnType, resultType);
                Assert.That(resultType.GetProperty("ServiceResult"), Is.Not.Null);
                Assert.That(resultType.GetProperty("Status")?.PropertyType, Is.EqualTo(typeof(int)));
            });
        }

        [Test]
        public void StopCarriesModeAndStatus()
        {
            MethodInfo syncInvoke = GetInvoke("StopMethodStateMethodCallHandler");
            MethodInfo asyncInvoke = GetInvoke("StopMethodStateMethodAsyncCallHandler");
            Type resultType = GetRoboticsType("StopMethodStateResult");

            Assert.Multiple(() =>
            {
                Assert.That(syncInvoke.GetParameters(), Has.Length.EqualTo(5));
                Assert.That(syncInvoke.GetParameters()[3].ParameterType, Is.EqualTo(typeof(long)));
                Assert.That(syncInvoke.GetParameters()[3].Name, Is.EqualTo("stopMode"));
                Assert.That(
                    syncInvoke.GetParameters()[4].ParameterType,
                    Is.EqualTo(typeof(int).MakeByRefType()));
                Assert.That(asyncInvoke.GetParameters(), Has.Length.EqualTo(5));
                Assert.That(asyncInvoke.GetParameters()[3].ParameterType, Is.EqualTo(typeof(long)));
                Assert.That(
                    asyncInvoke.GetParameters()[4].ParameterType,
                    Is.EqualTo(typeof(CancellationToken)));
                AssertAsyncResult(asyncInvoke.ReturnType, resultType);
                Assert.That(resultType.GetProperty("Status")?.PropertyType, Is.EqualTo(typeof(int)));
            });
        }

        [Test]
        public void GetReadyCarriesStatus()
        {
            MethodInfo syncInvoke = GetInvoke("GetReadyMethodStateMethodCallHandler");
            MethodInfo asyncInvoke = GetInvoke("GetReadyMethodStateMethodAsyncCallHandler");
            Type resultType = GetRoboticsType("GetReadyMethodStateResult");

            Assert.Multiple(() =>
            {
                Assert.That(syncInvoke.GetParameters(), Has.Length.EqualTo(4));
                Assert.That(
                    syncInvoke.GetParameters()[3].ParameterType,
                    Is.EqualTo(typeof(int).MakeByRefType()));
                Assert.That(asyncInvoke.GetParameters(), Has.Length.EqualTo(4));
                Assert.That(
                    asyncInvoke.GetParameters()[3].ParameterType,
                    Is.EqualTo(typeof(CancellationToken)));
                AssertAsyncResult(asyncInvoke.ReturnType, resultType);
                Assert.That(resultType.GetProperty("Status")?.PropertyType, Is.EqualTo(typeof(int)));
            });
        }

        [Test]
        public void DiGetUpdateBehaviorOverloadsRemainDistinct()
        {
            MethodInfo cachedInvoke = GetDiInvoke(
                "GetUpdateBehaviorCachedLoadingMethodStateMethodCallHandler");
            MethodInfo fileSystemInvoke = GetDiInvoke(
                "GetUpdateBehaviorFileSystemMethodStateMethodCallHandler");
            Type cachedState = GetDiType("CachedLoadingState");
            Type fileSystemState = GetDiType("FileSystemLoadingState");

            ParameterInfo[] cachedParameters = cachedInvoke.GetParameters();
            ParameterInfo[] fileSystemParameters = fileSystemInvoke.GetParameters();
            Assert.Multiple(() =>
            {
                Assert.That(cachedParameters, Has.Length.EqualTo(7));
                Assert.That(cachedParameters[3].ParameterType, Is.EqualTo(typeof(string)));
                Assert.That(cachedParameters[4].ParameterType, Is.EqualTo(typeof(string)));
                Assert.That(
                    cachedParameters[5].ParameterType.GetGenericArguments()[0],
                    Is.EqualTo(typeof(string)));
                Assert.That(
                    cachedParameters[6].ParameterType,
                    Is.EqualTo(typeof(uint).MakeByRefType()));
                Assert.That(fileSystemParameters, Has.Length.EqualTo(5));
                Assert.That(
                    fileSystemParameters[3].ParameterType.GetGenericArguments()[0].FullName,
                    Is.EqualTo("Opc.Ua.NodeId"));
                Assert.That(
                    fileSystemParameters[4].ParameterType,
                    Is.EqualTo(typeof(uint).MakeByRefType()));
                Assert.That(
                    cachedState.GetProperty("GetUpdateBehavior")?.PropertyType.Name,
                    Is.EqualTo("GetUpdateBehaviorCachedLoadingMethodState"));
                Assert.That(
                    fileSystemState.GetProperty("GetUpdateBehavior")?.PropertyType.Name,
                    Is.EqualTo("GetUpdateBehaviorFileSystemMethodState"));
            });
        }

        private static Type GetRoboticsType(string name)
        {
            return typeof(RoboticsModel).Assembly.GetType(
                "Opc.Ua.Robotics." + name,
                throwOnError: true)!;
        }

        private static Type GetDiType(string name)
        {
            return typeof(Opc.Ua.Di.Namespaces).Assembly.GetType(
                "Opc.Ua.Di." + name,
                throwOnError: true)!;
        }

        private static MethodInfo GetInvoke(string delegateName)
        {
            return GetRoboticsType(delegateName).GetMethod("Invoke")!;
        }

        private static MethodInfo GetDiInvoke(string delegateName)
        {
            return GetDiType(delegateName).GetMethod("Invoke")!;
        }

        private static void AssertAsyncResult(Type returnType, Type resultType)
        {
            Assert.Multiple(() =>
            {
                Assert.That(returnType.IsGenericType, Is.True);
                Assert.That(
                    returnType.GetGenericTypeDefinition(),
                    Is.EqualTo(typeof(ValueTask<>)));
                Assert.That(returnType.GetGenericArguments()[0], Is.EqualTo(resultType));
            });
        }
    }
}
