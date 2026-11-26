// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using Opc.Ua;

namespace TestData
{
    public partial class VectorVariableState : ITestDataSystemValuesGenerator
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            InitializeVariable(context, X!);
            InitializeVariable(context, Y!);
            InitializeVariable(context, Z!);
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
