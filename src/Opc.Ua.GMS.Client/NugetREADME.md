# OPC UA GMS result client

Typed, bounded result workflows for the released OPC 40210 model. Clients use
generated GMS/Machinery proxies and the shared OPC UA temporary-file client.
There is no server-runtime dependency.

Result metadata handles and download streams belong to the calling session and
are released on success, failure and cancellation. Domain result codes remain
distinct from UA service failures. Missing optional variables are not replaced
with successful zero-valued measurements; raw data quality and acquisition
timestamps are retained.

These workflows cover the results slice, not complete GMS equipment, production
or profile conformance.

## Result workflows

```csharp
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Result;

var results = new GmsResultClient(session, gmsNodeId, telemetry,
    new GmsResultLimits
    {
        MaximumResults = 32,
        MaximumMetadataBytes = 256 * 1024,
        MaximumFileBytes = 8 * 1024 * 1024
    });

ResultDataType latest = await results.GetLatestAsync(cancellationToken);
ArrayOf<string> identifiers = await results.QueryAsync(
    maximumResults: 16, cancellationToken: cancellationToken);
await results.DownloadAsync(latest.ResultMetaData!.ResultId!, destination,
    cancellationToken);
ArrayOf<GmsCorrectionValues> corrections =
    await results.ReadCorrectionsAsync(cancellationToken);
```

`QueryAsync` accepts the standard Machinery `ContentFilter` and ordered
`RelativePath` list. It does not introduce another filtering language.
`GetLatestAsync`, `GetByIdAsync`, `QueryAsync`, and both download overloads
release their own metadata handles before completing. Downloading uses the
shared `TemporaryFileTransferClient` and asynchronously disposes its file stream
on success, overflow, destination failure or cancellation. The caller still owns
the destination stream; on failure it may contain a partial prefix.

`GmsResultDomainException` preserves a nonzero method domain code separately
from `ServiceResultException` (UA service/operation failure) and
`OperationCanceledException`. An error in both the operation and metadata
cleanup is reported as an `AggregateException` containing both failures.

`ReadPublishedResultsAsync` and `ReadCorrectionsAsync` retain raw `DataValue`
quality and timestamps. A null `ArrayOf<T>` means an optional folder is absent;
an empty non-null array means the folder exists but has no entries. An absent
optional property is `DataValue.Null`; bad or uncertain acquired data remains a
present sample with its original quality.

## Intermediate results and dependency injection

`CreateIntermediateResultFilter` extends the generated event filter with
explicit, server-qualified characteristic placeholder names.
`DecodeIntermediateResultAsync` verifies the concrete event subtype and decodes
the generated standard record plus those characteristic fields.
`ObserveIntermediateResultsAsync` composes an existing `IStreamingSubscription`
with bounded event count, queue size and response bytes. Its enumerator removes
only its monitored item; the caller retains ownership of the shared subscription.

Event fields are `Variant` values, not acquisition `DataValue` samples. The
client does not manufacture per-field quality or timestamps that the event
protocol did not transmit. For raw acquired result quality, read the result
variables.

Use `AddGmsResultsClient()` on the existing `IOpcUaClientBuilder` to register
`IGmsResultClientFactory`. Its `CreateAsync(gmsNodeId, cancellationToken)` method
uses the already-configured managed session. Direct construction and an
injectable `GmsResultClientFactory` session accessor remain available.
