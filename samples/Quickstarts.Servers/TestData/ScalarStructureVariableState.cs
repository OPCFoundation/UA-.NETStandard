// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Opc.Ua;

namespace TestData
{
    public partial class ScalarStructureVariableState : ITestDataSystemValuesGenerator
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            InitializeVariable(context, BooleanValue!);
            InitializeVariable(context, SByteValue!);
            InitializeVariable(context, ByteValue!);
            InitializeVariable(context, Int16Value!);
            InitializeVariable(context, UInt16Value!);
            InitializeVariable(context, Int32Value!);
            InitializeVariable(context, UInt32Value!);
            InitializeVariable(context, Int64Value!);
            InitializeVariable(context, UInt64Value!);
            InitializeVariable(context, FloatValue!);
            InitializeVariable(context, DoubleValue!);
            InitializeVariable(context, StringValue!);
            InitializeVariable(context, DateTimeValue!);
            InitializeVariable(context, GuidValue!);
            InitializeVariable(context, ByteStringValue!);
            InitializeVariable(context, XmlElementValue!);
            InitializeVariable(context, NodeIdValue!);
            InitializeVariable(context, ExpandedNodeIdValue!);
            InitializeVariable(context, QualifiedNameValue!);
            InitializeVariable(context, LocalizedTextValue!);
            InitializeVariable(context, StatusCodeValue!);
            InitializeVariable(context, VariantValue!);
            InitializeVariable(context, EnumerationValue!);
            InitializeVariable(context, StructureValue!);
            InitializeVariable(context, NumberValue!);
            InitializeVariable(context, IntegerValue!);
            InitializeVariable(context, UIntegerValue!);
        }

        /// <summary>
        /// Initializes the variable.
        /// </summary>
        protected void InitializeVariable(ISystemContext context, BaseVariableState variable)
        {
            // copy access level to childs
            variable.AccessLevel = AccessLevel;
            variable.UserAccessLevel = UserAccessLevel;
        }

        public virtual StatusCode OnGenerateValues(ISystemContext context)
        {
            if (context.SystemHandle is not TestDataSystem system)
            {
                return StatusCodes.BadOutOfService;
            }

            byte accessLevel = AccessLevel;
            byte userAccessLevel = UserAccessLevel;
            AccessLevel = UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            // generate structure values here
            ServiceResult result = WriteValueAttribute(
                context,
                default,
                system.ReadValue(this),
                StatusCodes.Good,
                DateTime.UtcNow);

            AccessLevel = accessLevel;
            UserAccessLevel = userAccessLevel;

            ClearChangeMasks(context, true);

            return result.StatusCode;
        }
    }
}
