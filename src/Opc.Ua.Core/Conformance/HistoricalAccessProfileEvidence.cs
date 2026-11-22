// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Repository evidence for one Historical Access profile.
    /// </summary>
    /// <param name="ProfileUri">The profile URI.</param>
    /// <param name="ProductionModules">Production modules implementing it.</param>
    /// <param name="AutomatedTests">Automated test modules covering it.</param>
    /// <param name="Samples">Shippable samples demonstrating it.</param>
    public sealed record HistoricalAccessProfileEvidence(
        string ProfileUri,
        ArrayOf<string> ProductionModules,
        ArrayOf<string> AutomatedTests,
        ArrayOf<string> Samples);

    /// <summary>
    /// Evidence map for all released UACore 1.05 Historical Access
    /// Server and Client facets.
    /// </summary>
    public static class HistoricalAccessProfileEvidenceCatalog
    {
        /// <summary>
        /// One evidence record for every entry in
        /// <see cref="HistoricalAccessProfileCatalog.AllProfiles"/>.
        /// </summary>
        public static ArrayOf<HistoricalAccessProfileEvidence> All { get; } =
            CreateEvidence();

        /// <summary>
        /// Looks up evidence by profile URI.
        /// </summary>
        public static bool TryGet(
            string profileUri,
            out HistoricalAccessProfileEvidence? evidence)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(
                    All[i].ProfileUri,
                    profileUri,
                    StringComparison.Ordinal))
                {
                    evidence = All[i];
                    return true;
                }
            }
            evidence = null;
            return false;
        }

        private static ArrayOf<HistoricalAccessProfileEvidence>
            CreateEvidence()
        {
            var evidence =
                new List<HistoricalAccessProfileEvidence>(
                    HistoricalAccessProfileCatalog.AllProfiles.Count);
            foreach (HistoricalAccessProfileDescriptor profile in
                HistoricalAccessProfileCatalog.AllProfiles)
            {
                evidence.Add(new HistoricalAccessProfileEvidence(
                    profile.ProfileUri,
                    GetProductionModules(profile),
                    GetAutomatedTests(profile),
                    GetSamples(profile)));
            }
            return evidence.ToArrayOf();
        }

        private static ArrayOf<string> GetProductionModules(
            HistoricalAccessProfileDescriptor profile)
        {
            if (profile.Side == HistoricalAccessProfileSide.Client)
            {
                return profile.Family switch
                {
                    HistoricalAccessProfileFamily.Events =>
                    [
                        "src/Opc.Ua.Client/Historian/HistoryClient.Events.cs",
                        "src/Opc.Ua.Client/Historian/HistoryClient.Paging.cs"
                    ],
                    HistoricalAccessProfileFamily.Structured =>
                    [
                        "src/Opc.Ua.Client/Historian/HistoryClient.Updates.cs",
                        "src/Opc.Ua.Client/Historian/HistoryClient.cs",
                        "src/Opc.Ua.Client/Historian/HistoryClient.Extras.cs"
                    ],
                    _ =>
                    [
                        "src/Opc.Ua.Client/Historian/HistoryClient.cs",
                        "src/Opc.Ua.Client/Historian/HistoryClient.Extras.cs",
                        "src/Opc.Ua.Client/Historian/HistoryClient.Paging.cs"
                    ]
                };
            }
            return profile.Family switch
            {
                HistoricalAccessProfileFamily.Events =>
                [
                    "src/Opc.Ua.Server/Historian/HistorianDispatcher.cs",
                    "src/Opc.Ua.Server/Historian/HistorianEventUpdateValidator.cs",
                    "src/Opc.Ua.Server/Historian/HistorianEventCapture.cs"
                ],
                HistoricalAccessProfileFamily.Structured =>
                [
                    "src/Opc.Ua.Server/Historian/HistorianDispatcher.cs",
                    "src/Opc.Ua.Server/Historian/IHistorianStructuredDataProvider.cs",
                    "src/Opc.Ua.Server/Historian/HistoricalValueKey.cs"
                ],
                _ =>
                [
                    "src/Opc.Ua.Server/Historian/HistorianDispatcher.cs",
                    "src/Opc.Ua.Server/Historian/InMemory/InMemoryHistorianProvider.cs"
                ]
            };
        }

        private static ArrayOf<string> GetAutomatedTests(
            HistoricalAccessProfileDescriptor profile)
        {
            if (profile.Side == HistoricalAccessProfileSide.Client)
            {
                return
                [
                    "tests/Opc.Ua.Client.Tests/Historian/HistoryClientPart11Tests.cs",
                    "tests/Opc.Ua.Client.Tests/Historian/HistoryClientUnitTests.cs",
                    "tests/Opc.Ua.History.Tests/HistoryClientIntegrationTests.cs"
                ];
            }
            return profile.Family switch
            {
                HistoricalAccessProfileFamily.Events =>
                [
                    "tests/Opc.Ua.Server.Tests/Historian/HistorianEventUpdateValidatorTests.cs",
                    "tests/Opc.Ua.History.Tests/HistoryClientEventIntegrationTests.cs"
                ],
                HistoricalAccessProfileFamily.Structured =>
                [
                    "tests/Opc.Ua.Server.Tests/Historian/InMemoryHistorianStructuredDataTests.cs",
                    "tests/Opc.Ua.Server.Tests/Historian/HistorianStructuredDispatcherTests.cs"
                ],
                _ =>
                [
                    "tests/Opc.Ua.Server.Tests/Historian",
                    "tests/Opc.Ua.History.Tests/HistoricalAccessDepthTests.cs"
                ]
            };
        }

        private static ArrayOf<string> GetSamples(
            HistoricalAccessProfileDescriptor profile)
        {
            return profile.Side == HistoricalAccessProfileSide.Server
                ?
                [
                    "samples/Quickstarts.Servers/ReferenceServer/ReferenceNodeManager.cs",
                    "samples/Reference/ConsoleReferenceServer/README.md"
                ]
                :
                [
                    "samples/Reference/ConsoleReferenceClient/HistorianClientSample.cs",
                    "samples/Reference/ConsoleReferenceClient/README.md"
                ];
        }
    }
}
