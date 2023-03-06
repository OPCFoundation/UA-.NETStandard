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
    public partial class ScalarValueVariableState
    {
        #region Initialization
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node)
        {
            base.OnAfterCreate(context, node);

            AccessLevel = UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            if (!SimulationActive.Value)
            {
                AccessLevel = UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }

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

            TestDataSystem system = context.SystemHandle as TestDataSystem;
            this.WriteValueAttribute(context, NumericRange.Empty, system.ReadValue(this), StatusCodes.Good, DateTime.UtcNow);

        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Handles the generate values method.
        /// </summary>
        protected override ServiceResult OnGenerateValues(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint count)
        {
            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system == null)
            {
                return StatusCodes.BadOutOfService;
            }

            // generate structure values here
            this.WriteValueAttribute(context, NumericRange.Empty, system.ReadValue(this), StatusCodes.Good, DateTime.UtcNow);

            return base.OnGenerateValues(context, method, objectId, count);
        }
        #endregion

        #region Public Methods
        public override StatusCode OnGenerateValues(ISystemContext context)
        {
            if (!SimulationActive.Value)
            {
                return StatusCodes.BadInvalidState;
            }

            TestDataSystem system = context.SystemHandle as TestDataSystem;

            if (system == null)
            {
                return StatusCodes.BadOutOfService;
            }

            // generate structure values here
            this.WriteValueAttribute(context, NumericRange.Empty, system.ReadValue(this), StatusCodes.Good, DateTime.UtcNow);

            return base.OnGenerateValues(context);
        }
        #endregion
    }
}
