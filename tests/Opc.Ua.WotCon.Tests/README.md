<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# WoT connectivity tests

`tests/Opc.Ua.WotCon.Tests` covers the WoT connectivity server, registry,
materialization, bindings and samples. Tests run in parallel, one fixture
instance per test (see `Properties/AssemblyInfo.cs`).

## The `Integration` category

`[Category("Integration")]` means exactly one thing here: the test starts a
real OPC UA server in process (and usually opens client sessions against it).
Every other test is server-free.

`Support/IntegrationCategoryGuard.cs` keeps it that way. It watches every
server start and fails

* a test that starts a server without the category, and
* a passing test that carries the category but starts no server.

Put the category on the fixture when all of its tests start a server,
otherwise on the methods. A server-free test of an `Integration` fixture moves into a sibling
`...ServerFreeTests` class in the same file (for example
`WotBindingDescriptorServerFreeTests`). A nested class does not help: the
NUnit adapter files it under the outer fixture, so it inherits the category.

## Local fast loop

Run the server-free subset while you iterate (about 2,200 tests in about a
minute on a development machine):

```bash
dotnet test tests/Opc.Ua.WotCon.Tests -f net10.0 -p:CustomTestTarget=net10.0 \
  --filter "TestCategory!=Integration"
```

Run the whole project (about 3,300 tests, 10 to 12 minutes) before pushing:

```bash
dotnet test tests/Opc.Ua.WotCon.Tests -f net10.0 -p:CustomTestTarget=net10.0
```

CI does not filter on `Integration`; it shards this project by namespace
only.
