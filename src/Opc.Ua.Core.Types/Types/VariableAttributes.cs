/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Variable attributes.
    /// </summary>
    public partial class VariableAttributes
    {
        /// <summary>
        /// Initializes the object with value and access level.
        /// </summary>
        public VariableAttributes(Variant value, byte accessLevel, NamespaceTable namespaceUris)
        {
            Initialize();

            Value = value;
            AccessLevel = accessLevel;
            UserAccessLevel = accessLevel;
            MinimumSamplingInterval = MinimumSamplingIntervals.Indeterminate;
            Historizing = false;

            if (value.IsNull)
            {
                DataType = DataTypeIds.BaseDataType;
                ValueRank = ValueRanks.Any;
            }
            else
            {
                DataType = TypeInfo.GetDataTypeId(value, namespaceUris);
                ValueRank = value.TypeInfo.ValueRank;
            }
        }
    }
}
