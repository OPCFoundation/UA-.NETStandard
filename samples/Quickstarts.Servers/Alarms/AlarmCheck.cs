// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua;

namespace Alarms
{
    /// <summary>
    /// Alarm check
    /// </summary>
    public class AlarmCheck
    {
        /// <summary>
        /// Alarm name
        /// </summary>
        public string AlarmName { get; set; } = null!;

        /// <summary>
        /// MethodName
        /// </summary>
        public string MethodName { get; set; } = null!;

        /// <summary>
        /// ModellingRule
        /// </summary>
        public NodeId MethodDeclarationId { get; set; } = NodeId.Null;

        /// <summary>
        /// ModellingRule Exists
        /// </summary>
        public bool Exists { get; set; }
    }
}
