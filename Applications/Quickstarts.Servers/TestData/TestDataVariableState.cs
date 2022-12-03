/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua;

namespace TestData
{
    public partial class TestDataVariableState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Initialzies the variable as a counter.
        /// </summary>
        protected void InitializeVariable(ISystemContext context, BaseVariableState variable)
        {
            // set a valid initial value.
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            // allow writes 
            variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
        }
        #endregion
    }
}
