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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Tests.Observe;

internal sealed class HistoryProtocolTestDriver
{
    public HistoryProtocolTestDriver()
    {
        Namespaces.Append("urn:history:test:unused");
        Namespaces.Append("urn:history:test:plant");
        Session.SetupGet(s => s.NamespaceUris).Returns(Namespaces);
        Session.Setup(s => s.HistoryReadAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ExtensionObject>(), It.IsAny<TimestampsToReturn>(),
            It.IsAny<bool>(), It.IsAny<ArrayOf<HistoryReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? header, ExtensionObject details, TimestampsToReturn timestamps,
                bool release, ArrayOf<HistoryReadValueId> nodes, CancellationToken token) =>
            {
                var request = new ReadCall(header, details, timestamps, release,
                    nodes.ToArray() ?? throw new AssertionException("HistoryRead nodes must not be null."), token);
                Reads.Add(request);
                return Read(request);
            });
        Session.Setup(s => s.HistoryUpdateAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<ExtensionObject>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? header, ArrayOf<ExtensionObject> details, CancellationToken token) =>
            {
                var request = new UpdateCall(header,
                    details.ToArray() ?? throw new AssertionException("HistoryUpdate details must not be null."),
                    token);
                Updates.Add(request);
                return Update(request);
            });
        Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? header, ArrayOf<BrowsePath> paths, CancellationToken token) =>
            {
                var request = new TranslateCall(header,
                    paths.ToArray() ?? throw new AssertionException("Browse paths must not be null."), token);
                Translations.Add(request);
                return Translate(request);
            });
    }

    public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
    public NamespaceTable Namespaces { get; } = new();
    public List<ReadCall> Reads { get; } = [];
    public List<UpdateCall> Updates { get; } = [];
    public List<TranslateCall> Translations { get; } = [];

    public Func<ReadCall, ValueTask<HistoryReadResponse>> Read { get; set; } =
        _ => throw new InvalidOperationException("Unexpected history read.");
    public Func<UpdateCall, ValueTask<HistoryUpdateResponse>> Update { get; set; } =
        _ => throw new InvalidOperationException("Unexpected history update.");
    public Func<TranslateCall, ValueTask<TranslateBrowsePathsToNodeIdsResponse>> Translate { get; set; } =
        _ => throw new InvalidOperationException("Unexpected history translation.");

    public static HistoryReadResponse Page(DataValue[] values, ByteString continuation = default, bool modified = false)
    {
        ExtensionObject body = modified
            ? new ExtensionObject(new HistoryModifiedData { DataValues = values })
            : new ExtensionObject(new HistoryData { DataValues = values });
        return new HistoryReadResponse
        {
            Results = [new HistoryReadResult
            {
                StatusCode = StatusCodes.Good,
                HistoryData = body,
                ContinuationPoint = continuation
            }]
        };
    }

    public static TranslateBrowsePathsToNodeIdsResponse Property(NodeId id)
    {
        return new TranslateBrowsePathsToNodeIdsResponse
        {
            Results = [new BrowsePathResult
            {
                StatusCode = StatusCodes.Good,
                Targets = [new BrowsePathTarget
                {
                    TargetId = id,
                    RemainingPathIndex = uint.MaxValue
                }]
            }]
        };
    }

    public static HistoryUpdateResponse Outcome(StatusCode status, params StatusCode[] operations)
    {
        return new HistoryUpdateResponse
        {
            Results = [new HistoryUpdateResult { StatusCode = status, OperationResults = operations }]
        };
    }

    internal sealed record ReadCall(
        RequestHeader? Header,
        ExtensionObject Details,
        TimestampsToReturn Timestamps,
        bool Release,
        HistoryReadValueId[] Nodes,
        CancellationToken Token);

    internal sealed record UpdateCall(RequestHeader? Header, ExtensionObject[] Details, CancellationToken Token);
    internal sealed record TranslateCall(RequestHeader? Header, BrowsePath[] Paths, CancellationToken Token);
}
