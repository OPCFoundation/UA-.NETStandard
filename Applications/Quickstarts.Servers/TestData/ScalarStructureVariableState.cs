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

using System;
using Opc.Ua;

namespace TestData
{
    public partial class ScalarStructureVariableState : ITestDataSystemValuesGenerator
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            InitializeVariable(context, BooleanValue);
            InitializeVariable(context, SByteValue);
            InitializeVariable(context, ByteValue);
            InitializeVariable(context, Int16Value);
            InitializeVariable(context, UInt16Value);
            InitializeVariable(context, Int32Value);
            InitializeVariable(context, UInt32Value);
            InitializeVariable(context, Int64Value);
            InitializeVariable(context, UInt64Value);
            InitializeVariable(context, FloatValue);
            InitializeVariable(context, DoubleValue);
            InitializeVariable(context, StringValue);
            InitializeVariable(context, DateTimeValue);
            InitializeVariable(context, GuidValue);
            InitializeVariable(context, ByteStringValue);
            InitializeVariable(context, XmlElementValue);
            InitializeVariable(context, NodeIdValue);
            InitializeVariable(context, ExpandedNodeIdValue);
            InitializeVariable(context, QualifiedNameValue);
            InitializeVariable(context, LocalizedTextValue);
            InitializeVariable(context, StatusCodeValue);
            InitializeVariable(context, VariantValue);
            InitializeVariable(context, EnumerationValue);
            InitializeVariable(context, StructureValue);
            InitializeVariable(context, NumberValue);
            InitializeVariable(context, IntegerValue);
            InitializeVariable(context, UIntegerValue);
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Initializes the variable.
        /// </summary>
        protected void InitializeVariable(ISystemContext context, BaseVariableState variable)
        {
            // set a valid initial value.
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            // copy access level to childs
            variable.AccessLevel = AccessLevel;
            variable.UserAccessLevel = UserAccessLevel;
        }
        #endregion

        #region Public Methods
        public virtual StatusCode OnGenerateValues(ISystemContext context)
        {
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system == null)
            {
                return StatusCodes.BadOutOfService;
            }

            var accessLevel = AccessLevel;
            var userAccessLevel = UserAccessLevel;
            AccessLevel = UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            // generate structure values here
            ServiceResult result = WriteValueAttribute(context, NumericRange.Empty, system.ReadValue(this), StatusCodes.Good, DateTime.UtcNow);

            AccessLevel = accessLevel;
            UserAccessLevel = userAccessLevel;

            ClearChangeMasks(context, true);

            return result.StatusCode;
        }
        #endregion
    }
}
