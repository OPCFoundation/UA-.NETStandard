// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Tests
{
    internal sealed class TestIntentExecutor : IIntentExecutor
    {
        public ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken)
        {
            return new ValueTask<IntentOutcome>(IntentOutcome.Success);
        }

        public bool CanCancel(IntentExecution execution)
        {
            return true;
        }
    }
}
