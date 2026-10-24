# Wire codec regression fixtures

These NUnit tests exercise the internal common-record, CONTROL JSON, incremental
stream and carrier-payload codecs. They do not implement or test transport
handshakes, cryptography, grant redemption, pipeline admission or flow lifecycle.

## Independent public fixtures

`WireGoldenData.cs` contains newly authored, redistributable specimens covered by
its source license. The content uses neutral example identifiers and test values.
It contains no specification prose, third-party reference implementation, private
fixture catalog, absolute file path or environment-dependent fixture lookup.

Expected bytes are assembled only from **fixed hexadecimal headers** and literal
JSON or binary bodies. Header lengths are frozen literals, not calculated by the
codec or generated at test time. Typed expected controls are constructed
independently of those bytes. Encoder tests compare these independent inputs
directly with the golden bytes; decoder tests check every typed member.

The catalog covers:

- All 15 CONTROL operations, their exact member sets and canonical key order.
- HELLO for dedicated/shared UASC, QUIC and WebTransport; configured and session
  authorization **syntax**, without asserting that a grant is authorized.
- Opaque, UADP and JSON DATA on both UASC payload layouts, both native stream
  layouts, and both native datagram payload layouts.
- Separate malformed literals and per-member mutations with precise expected
  errors. The detailed tests also cover bounds, UTF-8, numeric and enum values,
  member ordering, stream segmentation/EOF and body ownership.

The opaque sample is `k9!`. The UADP-shaped sample uses publisher `0x3412`, writer
group `0x2244`, group version `0x01020304`, network-message number `0x0506`,
sequence `0x0708`, writer `0x0910` and one Int32 field `-19`. The JSON-shaped
sample uses publisher `bench-7`, writer `57` and value `-19`, with intentional
outside whitespace. These are byte-preservation specimens, **not** Part 14
validation or authentication oracles. A separate all-byte-values case checks
that no inner framing, decoding or normalization is introduced.

UASC specimens contain only the plaintext codec input. They do not contain fake
security headers, signatures or ciphertext. RequestId and finality are supplied
separately, as required by the codec API. The caller must already have validated
and removed the actual transport/security framing.

## Project integration

All fixtures are compiled C# literals. The existing SDK default compile glob and
Core friend-assembly access are sufficient. No package, embedded resource,
copy-to-output item, test skip or private fixture dependency is required.

From the repository root, run the suite after the surrounding DataChannels
runtime/test migration is integrated:

```powershell
dotnet test tests\Opc.Ua.Core.DataChannels.Tests\Opc.Ua.Core.DataChannels.Tests.csproj `
  -c Release -f net10.0 --filter "FullyQualifiedName~Opc.Ua.Core.DataChannels.Tests.Wire"

dotnet test tests\Opc.Ua.Core.DataChannels.Tests\Opc.Ua.Core.DataChannels.Tests.csproj `
  -c Release -f net48 --filter "FullyQualifiedName~Opc.Ua.Core.DataChannels.Tests.Wire"
```
