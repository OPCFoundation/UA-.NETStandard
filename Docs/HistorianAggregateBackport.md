# Additive Historian Aggregate Backport

This backport brings the aggregate corrections and aggregate-focused tests from
`master` into `master378`. It deliberately keeps the
`master378` historian architecture, public API, and wire behavior.

## Compatibility boundary

- `HistoryFile` remains the raw-history implementation of
  `IHistoryDataSource`.
- `IHistoryDataSource` still exposes only `FirstRaw` and `NextRaw`.
- `HistoryRecord` still contains only `RawData` and `Historizing`.
- `HistoryEntry` still contains only `Value` and `IsModified`.
- Existing Int32 archive generation remains oldest value `1000` through newest
  value `0`.
- No async historian provider, registry, client facade, or new public historian
  contract from `master` is introduced.
- Processed reads are added behind the existing synchronous NodeManager
  overrides. Existing clients continue to send the standard OPC UA
  `HistoryRead` request and do not need adapting.

The new `ProcessedHistoryAdapter` reads the existing `HistoryFile` through
`IHistoryDataSource`, feeds those values into the existing aggregate
calculators, and returns standard `HistoryData`. It does not rewrite or append
raw history. A compatibility test fingerprints the record fields and compares
all raw values, status codes, and timestamps before and after a processed read.

Annotations are stored in a separate in-memory timestamp dictionary. They are
never inserted into `HistoryRecord.RawData`, so the raw-history storage layout
and raw-read ordering remain unchanged.

## Processed-read behavior

- Forward and reverse ranges are supported.
- Leading and trailing bounds are supplied from the existing raw source.
- Aggregate configuration uses an explicit request when supplied; an implicit
  or requested server default is resolved through `AggregateManager`.
- Results are buffered as a stable request snapshot, limited to 100,000
  aggregate outputs, and returned in pages of 1,000.
- A non-empty continuation point is the only “more data” signal; the per-node
  result remains `Good`.
- Continuation points are tied to the session, node, and aggregate, and can be
  released through the existing HistoryRead release operation.
- Index range, data encoding, and `TimestampsToReturn` are applied to every
  result page.
- `AnnotationCount` reads only the annotation sidecar and preserves duplicate
  annotation timestamps.

## Seeded Reference Server data

The Reference Server adds deterministic historizing scalar nodes for Boolean,
Int32, Float, Double, and String. Selected pre-existing static scalar nodes are
also made historizing. Each record has 1,001 samples at ten-second intervals
with deterministic Good, Bad, and Uncertain status codes. Historical
configuration metadata is linked with `HasHistoricalConfiguration`.

## Source commit ledger in plain language

Times are the commits' recorded ISO timestamps.

| Source commit | Time | What it means for this backport |
| --- | --- | --- |
| `64ca85fce304` | 2026-04-14 13:32:10 +02:00 | Added more tests around existing behavior. Relevant aggregate tests were inventoried. |
| `14e995c1d061` | 2026-05-24 06:23:16 +02:00 | Moved tests between projects. This is mainly file organization, so tests are mapped to the older project layout. |
| `c89bc92e7cdd` | 2026-05-26 10:45:32 +02:00 | Added a new general historian framework. That framework is intentionally not copied; only matching observable HistoryRead behavior is adapted to the old historian. |
| `b2b4dbb13f4f` | 2026-06-10 08:34:09 +02:00 | Added the main Part 13 aggregate correctness fixes and broad tests. This is the central calculator backport. |
| `e1c734438458` | 2026-06-28 12:29:11 +02:00 | Changed general StatusCode equality rules. The broad API change is not copied; aggregate tests compare the intended code and aggregate bits explicitly. |
| `ed090304986f` | 2026-07-06 08:48:56 +02:00 | Strengthened and stabilized tests. Relevant aggregate cases are retained. |
| `b26be47a487a` | 2026-07-08 16:24:19 +02:00 | Added more server-library unit coverage, including aggregate edge paths. |
| `d7cb170b86e4` | 2026-07-09 09:08:27 +02:00 | Added CTT historian/aggregate conformance behavior and server facets. Relevant behavior is implemented through the legacy NodeManager path. |
| `7f409657ade9` | 2026-07-11 04:47:25 +02:00 | Included broad high-availability work. Only historian/aggregate tests found by the inventory are considered; unrelated HA code is excluded. |
| `70f2e0557a88` | 2026-07-11 20:43:06 +02:00 | Added coverage and fixes discovered by tests, including aggregate boundary cases. |
| `0da85b9d8988` | 2026-07-13 19:14:19 +02:00 | Added more CTT aggregate conformance cases and diagnostics checks. |
| `84fa8e8906c7` | 2026-07-18 05:01:03 +02:00 | Added CTT follow-up fixes for history and model validation. Relevant historian aggregate cases are included. |
