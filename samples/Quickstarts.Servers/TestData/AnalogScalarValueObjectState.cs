// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using Opc.Ua;

namespace TestData
{
    public partial class AnalogScalarValueObjectState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            InitializeVariable(
                context,
                SByteValue!,
                Variables.AnalogScalarValueObjectType_SByteValue);
            InitializeVariable(context, ByteValue!, Variables.AnalogScalarValueObjectType_ByteValue);
            InitializeVariable(
                context,
                Int16Value!,
                Variables.AnalogScalarValueObjectType_Int16Value);
            InitializeVariable(
                context,
                UInt16Value!,
                Variables.AnalogScalarValueObjectType_UInt16Value);
            InitializeVariable(
                context,
                Int32Value!,
                Variables.AnalogScalarValueObjectType_Int32Value);
            InitializeVariable(
                context,
                UInt32Value!,
                Variables.AnalogScalarValueObjectType_UInt32Value);
            InitializeVariable(
                context,
                Int64Value!,
                Variables.AnalogScalarValueObjectType_Int64Value);
            InitializeVariable(
                context,
                UInt64Value!,
                Variables.AnalogScalarValueObjectType_UInt64Value);
            InitializeVariable(
                context,
                FloatValue!,
                Variables.AnalogScalarValueObjectType_FloatValue);
            InitializeVariable(
                context,
                DoubleValue!,
                Variables.AnalogScalarValueObjectType_DoubleValue);
            InitializeVariable(
                context,
                NumberValue!,
                Variables.AnalogScalarValueObjectType_NumberValue);
            InitializeVariable(
                context,
                IntegerValue!,
                Variables.AnalogScalarValueObjectType_IntegerValue);
            InitializeVariable(
                context,
                UIntegerValue!,
                Variables.AnalogScalarValueObjectType_UIntegerValue);
        }

        /// <summary>
        /// Handles the generate values method.
        /// </summary>
        protected override ServiceResult OnGenerateValues(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            uint count)
        {
            if (context.SystemHandle is not TestDataSystem system)
            {
                return StatusCodes.BadOutOfService;
            }

            GenerateValue(system, SByteValue!);
            GenerateValue(system, ByteValue!);
            GenerateValue(system, Int16Value!);
            GenerateValue(system, UInt16Value!);
            GenerateValue(system, Int32Value!);
            GenerateValue(system, UInt32Value!);
            GenerateValue(system, Int64Value!);
            GenerateValue(system, UInt64Value!);
            GenerateValue(system, FloatValue!);
            GenerateValue(system, DoubleValue!);
            GenerateValue(system, NumberValue!);
            GenerateValue(system, IntegerValue!);
            GenerateValue(system, UIntegerValue!);

            return base.OnGenerateValues(context, method, objectId, count);
        }
    }
}
