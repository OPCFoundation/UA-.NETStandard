/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using Opc.Ua;

namespace TestData
{
    public partial class StructureValueObjectState
    {
        /// <summary>
        /// Initializes the object with structures.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            InitializeVariable(
                context,
                ScalarStructure!,
                Variables.StructureValueObjectType_ScalarStructure);
            InitializeVariable(
                context,
                VectorStructure!,
                Variables.StructureValueObjectType_VectorStructure);
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
            if (context.SystemHandle is not TestDataSystem)
            {
                return StatusCodes.BadOutOfService;
            }

            ScalarStructure!.OnGenerateValues(context);
            VectorStructure!.OnGenerateValues(context);

            return base.OnGenerateValues(context, method, objectId, count);
        }
    }
}
