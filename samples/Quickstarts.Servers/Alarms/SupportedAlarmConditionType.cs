// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua;

namespace Alarms
{
    public class SupportedAlarmConditionType
    {
        public SupportedAlarmConditionType(string name, string conditionName, NodeId nodeId)
        {
            Name = name;
            ConditionName = conditionName;
            Node = nodeId;
        }

        public string Name { get; }

        public string ConditionName { get; }

        public NodeId Node { get; }
    }
}
