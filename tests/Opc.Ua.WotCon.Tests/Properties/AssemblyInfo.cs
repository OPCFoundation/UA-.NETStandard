// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using NUnit.Framework;

[assembly: CLSCompliant(false)]

// Every test owns its servers, clients, certificate stores and temporary
// folders, so tests run in parallel, each on its own fixture instance.
// Fixtures that change process-wide state opt out with [NonParallelizable].
// Each live test hosts one or two in-process servers; four workers keep the
// 3- and 4-core CI runners busy without starving the servers' own threads.
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[assembly: LevelOfParallelism(4)]

// [Category("Integration")] marks exactly the tests that start a server; the
// guard fails a test that breaks this. "--filter TestCategory!=Integration"
// is the fast, server-free local loop (see README.md).
[assembly: Opc.Ua.WotCon.Tests.IntegrationCategoryGuard]
