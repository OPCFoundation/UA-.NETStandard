// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using Opc.Ua;

namespace TestData
{
    public partial class AnalogArrayValueObjectState
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
                Variables.AnalogArrayValueObjectType_SByteValue);
            InitializeVariable(context, ByteValue!, Variables.AnalogArrayValueObjectType_ByteValue);
            InitializeVariable(
                context,
                Int16Value!,
                Variables.AnalogArrayValueObjectType_Int16Value);
            InitializeVariable(
                context,
                UInt16Value!,
                Variables.AnalogArrayValueObjectType_UInt16Value);
            InitializeVariable(
                context,
                Int32Value!,
                Variables.AnalogArrayValueObjectType_Int32Value);
            InitializeVariable(
                context,
                UInt32Value!,
                Variables.AnalogArrayValueObjectType_UInt32Value);
            InitializeVariable(
                context,
                Int64Value!,
                Variables.AnalogArrayValueObjectType_Int64Value);
            InitializeVariable(
                context,
                UInt64Value!,
                Variables.AnalogArrayValueObjectType_UInt64Value);
            InitializeVariable(
                context,
                FloatValue!,
                Variables.AnalogArrayValueObjectType_FloatValue);
            InitializeVariable(
                context,
                DoubleValue!,
                Variables.AnalogArrayValueObjectType_DoubleValue);
            InitializeVariable(
                context,
                NumberValue!,
                Variables.AnalogArrayValueObjectType_NumberValue);
            InitializeVariable(
                context,
                IntegerValue!,
                Variables.AnalogArrayValueObjectType_IntegerValue);
            InitializeVariable(
                context,
                UIntegerValue!,
                Variables.AnalogArrayValueObjectType_UIntegerValue);
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
