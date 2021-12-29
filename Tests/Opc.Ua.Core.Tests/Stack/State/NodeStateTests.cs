/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the NodeState classes.
    /// </summary>
    [TestFixture, Category("NodeStateTypes")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class StateTypesTests
    {
        #region DataPointSources
        [DatapointSource]
        public Type[] TypeArray = typeof(BaseObjectState).Assembly.GetExportedTypes().Where(type => IsNodeStateType(type)).ToArray();
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify activation of a NodeState type.
        /// </summary>
        [Theory]
        public void ActivateNodeStateType(
            Type systemType
            )
        {
            NodeState testObject = CreateDefaultNodeStateType(systemType) as NodeState;
            Assert.NotNull(testObject);
            Assert.False(testObject.Initialized);
            SystemContext context = new SystemContext();
            testObject.Initialize(context, "");
            testObject.Dispose();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Create an instance of a NodeState type with default values.
        /// </summary>
        /// <param name="systemType">The type to create</param>
        private static object CreateDefaultNodeStateType(Type systemType)
        {
            var systemTypeInfo = systemType.GetTypeInfo();
            object instance;
            try
            {
                if (typeof(BaseObjectState).GetTypeInfo().IsAssignableFrom(systemTypeInfo) ||
                    typeof(BaseVariableState).GetTypeInfo().IsAssignableFrom(systemTypeInfo) ||
                    typeof(MethodState).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
                {
                    instance = Activator.CreateInstance(systemType, (NodeState)null);
                }
                else
                {
                    instance = Activator.CreateInstance(systemType);
                }
            }
            catch
            {
                return null;
            }
            return instance;
        }

        /// <summary>
        /// Return true if system Type is IEncodeable.
        /// </summary>
        private static bool IsNodeStateType(System.Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            var systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract || systemTypeInfo.IsGenericType ||
                systemTypeInfo.IsGenericTypeDefinition ||
                !typeof(NodeState).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
            {
                return false;
            }

            var nodeState = CreateDefaultNodeStateType(systemType) as NodeState;

            if (nodeState == null)
            {
                return false;
            }

            return true;
        }
        #endregion
    }
}
