// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Channels;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// Injectable transport boundary for the real coordinator feed and deterministic host tests.
/// </summary>
internal interface IOperatorPlantFeed
{
    /// <summary>
    /// Latest authoritative image, if any.
    /// </summary>
    CommittedSnapshot? Snapshot { get; }

    /// <summary>
    /// Current display state and transport health.
    /// </summary>
    ConsoleState State { get; }

    /// <summary>
    /// Attaches a bounded display consumer.
    /// </summary>
    Channel<ConsoleState>? Subscribe();

    /// <summary>
    /// Releases a bounded display consumer.
    /// </summary>
    void Unsubscribe(Channel<ConsoleState> channel);

    /// <summary>
    /// Publishes bounded workstation receipts.
    /// </summary>
    void SetCommands(CommandResult[] commands);

    /// <summary>
    /// Sends exactly one explicit coordinator request.
    /// </summary>
    ValueTask<RecyclingCommandOutcome> ExecuteAsync(RecyclingCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// Reads typed bounded genealogy only while direct coordinator reads confirm the expected run.
    /// </summary>
    ValueTask<ArrayOf<MaterialExecution>> ReadHistoryAsync(
        string expectedRunId, long after, int limit, CancellationToken cancellationToken);
}
