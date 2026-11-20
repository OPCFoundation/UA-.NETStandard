// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;

// Run different test fixtures in parallel. Tests within a fixture
// run sequentially because each fixture owns one in-process server and
// one client session. Fixtures themselves are independent — each starts
// its own ReferenceServer on a random port with a per-fixture PKI store
// (see TestFixture.OneTimeSetUp), so parallel fixtures do not contend.
[assembly: Parallelizable(ParallelScope.Fixtures)]

// Cap concurrency. Each in-process server holds significant memory and
// binds an ephemeral TCP port. Running too many fixtures concurrently
// can exhaust resources or saturate the dynamic-port range, which
// surfaces as session-connect failures. Four is conservative; raise
// later if the host has spare capacity.
[assembly: LevelOfParallelism(4)]
