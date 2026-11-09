/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
