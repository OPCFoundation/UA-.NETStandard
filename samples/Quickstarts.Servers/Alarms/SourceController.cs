/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua;

namespace Alarms
{
    public class SourceController
    {
        public SourceController(BaseDataVariableState source, AlarmController controller)
        {
            Source = source;
            Controller = controller;
        }

        public AlarmController Controller { get; set; }

        public BaseDataVariableState Source { get; set; }
    }
}
