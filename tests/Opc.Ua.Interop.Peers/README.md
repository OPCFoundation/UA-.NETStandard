# Interop peers on other OPC UA stacks

The fixtures in [`tests/Opc.Ua.Interop.Tests`](../Opc.Ua.Interop.Tests) run
against a *peer*: a child process that hosts an OPC UA server or runs a set of
client checks. By default the peer is the 1.5.378 .NET stack
([`Opc.Ua.Interop.LegacyPeer`](../Opc.Ua.Interop.LegacyPeer)). This folder holds
peers built on other open source OPC UA stacks, so the same fixtures also test
the 2.0 stack against them.

| Folder | Stack | Version | Toolchain | Roles |
|---|---|---|---|---|
| [`node-opcua`](node-opcua) | node-opcua | 2.186.15 | Node.js 24 | server, client |
| [`milo`](milo) | Eclipse Milo | 1.1.7 | JDK 21 (release 17), Maven | server, client |
| [`asyncua`](asyncua) | opcua-asyncio | 2.0.1 | Python 3.12 | server, client |
| [`open62541`](open62541) | open62541 | 1.5.8 | C compiler, CMake 3.20+, OpenSSL 3.x | server, client, ECC |
| [`async-opcua`](async-opcua) | async-opcua (Rust) | 0.19.0 | Rust stable (cargo) | server, client |
| [`gopcua`](gopcua) | gopcua | 0.9.1 | Go (see `go.mod`) | client only |

The [Foreign stack interop](../../.github/workflows/interop-foreign.yml)
workflow builds and runs every peer on Linux, Windows and macOS (open62541 on
Linux only) nightly, on demand and for pull requests labelled `interop`. The
workflow is the reference for the build steps below.

## Peer contract

Every peer is a command line program with two modes.

- `server --port <n> --pki <dir> [--kind interop|reference] [--ecc] [--init-only]`
  prints `PEER-INFO {json}` (stack, version, applicationUri, security policies,
  user tokens, limits, features), then `PEER-SERVER-READY <url>`, and stops
  when it reads `stop` on stdin. The server exposes the `Interop` folder
  (scalar and array variables, methods, `RaiseEvent`) that the fixtures use.
- `client --url <url> --pki <dir> --policy <uri> --mode <mode> [--user <u> --password <p>] --checks <a,b,...> [--expect-connect-error] [--token-lifetime <ms>] [--token-test-seconds <s>] [--timeout-seconds <s>]`
  prints one `RESULT {"check","outcome","message","milliseconds"}` line per
  check and a final `SUMMARY passed=<n> failed=<n>` line.

The certificate stores use the .NET directory layout below `--pki`:
`own/certs/*.der`, `own/private`, `trusted/{certs,crl}`, `issuer/{certs,crl}`
and `rejected/certs`. The checks and their exact semantics are defined by the
1.5 peer ([`LegacyClient.cs`](../Opc.Ua.Interop.LegacyPeer/LegacyClient.cs),
[`LegacyClientFeatures.cs`](../Opc.Ua.Interop.LegacyPeer/LegacyClientFeatures.cs),
[`LegacyServer.cs`](../Opc.Ua.Interop.LegacyPeer/LegacyServer.cs)); a new peer
mirrors them.

## Building the peers

Run the commands from the repository root unless noted.

### node-opcua

```bash
cd tests/Opc.Ua.Interop.Peers/node-opcua
npm ci --no-audit --no-fund
```

Peer: `node tests/Opc.Ua.Interop.Peers/node-opcua/peer.mjs`.

### Eclipse Milo

Needs a JDK 17 or later (CI uses Temurin 21) and Maven.

```bash
mvn -B -q -f tests/Opc.Ua.Interop.Peers/milo/pom.xml package
```

Peer: `java -jar tests/Opc.Ua.Interop.Peers/milo/target/milo-peer.jar`.

### asyncua

Use a virtual environment. On Windows create it on a short path (for example
`C:\venv\asyncua`): the package's file names exceed `MAX_PATH` below a deep
repository path.

```bash
python -m venv .venv-asyncua
.venv-asyncua/bin/pip install -r tests/Opc.Ua.Interop.Peers/asyncua/requirements.txt
```

Peer: `<venv>/bin/python tests/Opc.Ua.Interop.Peers/asyncua/peer.py`
(`<venv>\Scripts\python.exe` on Windows).

### open62541

open62541 is built from source and installed, then the peer is built against
the installed package. Use the OpenSSL crypto backend: only with it does
open62541 1.5 implement `ECC_nistP256_AesGcm`, `ECC_nistP256_ChaChaPoly`,
`ECC_curve25519` and `ECC_curve448`. The options below are required:
`UA_ENABLE_DISCOVERY` builds the client sources the peer uses, and
`UA_ENABLE_XML_ENCODING` generates the argument definitions of the namespace 0
methods (without it `Server.GetMonitoredItems`, which the .NET client calls
after TransferSubscriptions, fails with `Bad_InternalError`).

Linux (`sudo apt-get install libssl-dev cmake`) or macOS (`brew install openssl@3 cmake`):

```bash
git clone --depth 1 --branch v1.5.8 https://github.com/open62541/open62541.git
git -C open62541 submodule update --init --depth 1 deps/ua-nodeset
cmake -S open62541 -B build-open62541 -DCMAKE_BUILD_TYPE=Release \
  -DUA_ENABLE_ENCRYPTION=OPENSSL -DUA_NAMESPACE_ZERO=FULL \
  -DUA_ENABLE_DISCOVERY=ON -DUA_ENABLE_XML_ENCODING=ON \
  -DUA_BUILD_EXAMPLES=OFF -DUA_BUILD_UNIT_TESTS=OFF -DUA_FORCE_WERROR=OFF \
  -DCMAKE_INSTALL_PREFIX="$PWD/open62541-install"
cmake --build build-open62541 --parallel
cmake --install build-open62541
cmake -S tests/Opc.Ua.Interop.Peers/open62541 -B tests/Opc.Ua.Interop.Peers/open62541/build \
  -DCMAKE_BUILD_TYPE=Release -DCMAKE_PREFIX_PATH="$PWD/open62541-install"
cmake --build tests/Opc.Ua.Interop.Peers/open62541/build
```

On macOS add `-DOPENSSL_ROOT_DIR="$(brew --prefix openssl@3)"` to the first
`cmake` call. The namespace 0 generator needs Python 3 on the `PATH` (or
`-DPython3_EXECUTABLE=<python>`).

Windows: without Visual Studio, a portable toolchain works: CMake, Ninja and
[Zig](https://ziglang.org) as the C compiler (`-G Ninja
-DCMAKE_C_COMPILER="zig;cc"`), with the MinGW OpenSSL 3 package from
[MSYS2](https://packages.msys2.org/base/mingw-w64-openssl) (`-DOPENSSL_ROOT_DIR=<extracted mingw64 folder>`).
With zig add `-DCMAKE_C_FLAGS="-Wno-date-time -fno-sanitize=undefined"` to the
open62541 configuration, link OpenSSL dynamically and copy
`libcrypto-3-x64.dll` and `libssl-3-x64.dll` next to `open62541-peer.exe`.
With Visual Studio, use vcpkg's `openssl` and the default generator.

open62541 also builds with `-DUA_ENABLE_ENCRYPTION=MBEDTLS` (mbedTLS 3.6); the
peer then offers the RSA policies and the four ECC policies of OPC UA 1.05 only
and reports that in its `PEER-INFO`, so the fixtures skip the others.

Peer: `tests/Opc.Ua.Interop.Peers/open62541/build/open62541-peer[.exe]`.

### async-opcua

```bash
cargo build --release --manifest-path tests/Opc.Ua.Interop.Peers/async-opcua/Cargo.toml
```

Peer: `tests/Opc.Ua.Interop.Peers/async-opcua/target/release/async-opcua-peer[.exe]`.
On Windows the default MSVC toolchain needs the Visual Studio C++ build tools.
Without them use the `x86_64-pc-windows-gnu` toolchain (`rustup toolchain
install stable-x86_64-pc-windows-gnu`) with the MinGW-w64 binutils (`dlltool`,
`ld`) on the `PATH`.

### gopcua

```bash
cd tests/Opc.Ua.Interop.Peers/gopcua
go build -o build/gopcua-peer .
```

Peer: `tests/Opc.Ua.Interop.Peers/gopcua/build/gopcua-peer[.exe]`. gopcua has
no server that can run the contract, so only `LegacyClientInteropTests` apply.

## Running the fixtures against a peer

Three environment variables select the peer. Native peers need no host.

| Variable | node-opcua | Milo | asyncua | open62541, async-opcua, gopcua |
|---|---|---|---|---|
| `OPCUA_INTEROP_PEER_HOST` | path of `node` | path of `java` | path of the venv `python` | (unset) |
| `OPCUA_INTEROP_PEER_HOST_ARGUMENTS` | (unset) | `-jar` | (unset) | (unset) |
| `OPCUA_INTEROP_LEGACY_PEER` | `.../node-opcua/peer.mjs` | `.../milo/target/milo-peer.jar` | `.../asyncua/peer.py` | path of the executable |

Use absolute paths. Then run the fixtures that apply to the peer:

```bash
dotnet test tests/Opc.Ua.Interop.Tests/Opc.Ua.Interop.Tests.csproj -c Release -f net10.0 -p:CustomTestTarget=net10.0 --filter "FullyQualifiedName~LegacyServerInteropTests|FullyQualifiedName~LegacyClientInteropTests|FullyQualifiedName~ServerFeatureInteropTests"
```

Add `|FullyQualifiedName~EccInteropTests` for open62541, and use only
`FullyQualifiedName~LegacyClientInteropTests` for gopcua. The ECC fixture runs
a policy against a foreign peer only when the peer lists it in the `policies`
of its `PEER-INFO` line and the 2.0 stack supports it on the platform.

## Expected differences

Known defects and missing features of a stack are listed in the
`expected-differences.json` next to its peer. A key is a client check name
(`"ComplexTypes"`), a test (`"Fixture.Test"`, with the test case arguments as
NUnit prints them), or a prefix ending in `*`. Each entry gives the `reason`,
with the source file or spec section that shows it, and the upstream `issue`.
The `[PeerDifferences]` attribute turns a listed failure into Inconclusive and
reports a listed test that passes as a Warning, so a fixed stack is noticed.
Add an entry only after confirming that the foreign stack, not the 2.0 stack,
deviates from the specification; a 2.0 defect is fixed instead.

## Adding a peer

1. Create `tests/Opc.Ua.Interop.Peers/<stack>` with the peer, its lock or
   version pin, and a `.gitignore` for the build output.
2. Implement the contract above and the checks of the 1.5 peer. Run every
   check against the 1.5 peer's server first: a check that fails there is a
   peer bug.
3. Run the fixtures, investigate each failure, and record confirmed stack
   defects in `expected-differences.json`.
4. Add the stack to the matrix and the `Select the peer` step of the workflow,
   and to the table at the top of this file.
