# Coverage After Report — httpwssbindingfollowups

## Summary

| Field | Value |
|-------|-------|
| Timestamp | 2026-06-20 00:46 |
| Branch SHA | 66355a61e8b5af9187b6152928192ec21748945b |
| Test projects run | 9 (targeted: Core, Bindings.Https.WebApi, Client, Sessions, Server, Client.ComplexTypes, Types, Bindings.Pcap, Lds) |
| Baseline run | Full solution (29 test projects), 2026-06-19 |
| **Weighted patch coverage (partial run)** | **74.77%** |
| Baseline weighted coverage | 75.13% |
| Goal | ≥ 80% |
| **Goal met?** | **⚠️ NO (partial-run measurement; 12 more files crossed 80%)** |
| Files ≥ 80% | **43** (baseline: 31, **+12**) |
| Files < 80% | **27** (baseline: 39, **-12**) |

## Local Commits (5 batches of test additions)

| Commit | Message |
|--------|---------|
| 5d74385b4 | test: cover DI builder extensions (batch 1) |
| 3713ec6d6 | test: cover WebApi client/server/auth/codec (batch 2) |
| d3b2b255f | test: cover HTTPS/WSS/JSON/Kestrel transports (batch 3) |
| 3bc2ec852 | test: cover core Tcp + transport binding registry (batch 4) |
| 66355a61e | test: cover fluent client + reverse-connect gaps (batch 5) |

## Note on Measurement Methodology

The baseline was collected from a **full solution run** (29 test projects).
This after-report uses a **targeted run** of 9 test projects covering the diff files.
For large infrastructure files (TcpTransportListener, StandardServer, etc.) that are also
exercised by non-targeted test projects, the per-file percentages in this report may be
slightly lower than if all 29 projects were merged. The **file-count improvement** (+12 files
crossing ≥80%) is the most reliable signal of the batch impact.

## Files That Newly Crossed ≥ 80% Threshold (12 files)

| File | Baseline % | New % | Delta |
|------|-----------|-------|-------|
| Libraries/Opc.Ua.Client/WebApi/OpcUaWebApiClientBuilderExtensions.cs | 0% | 100% | +100% |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaHttpsBuilderExtensions.cs | 0% | 100% | +100% |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaKestrelTcpBuilderExtensions.cs | 0% | 100% | +100% |
| Stack/Opc.Ua.Bindings.Https/WebApi/WebApiServer.cs | 59.8% | 100% | +40.2% |
| Stack/Opc.Ua.Bindings.Https/WebApi/WebApiTransportOptions.cs | 0% | 100% | +100% |
| Stack/Opc.Ua.Core/Stack/Client/ReverseConnectHost.cs | 75% | 94.44% | +19.44% |
| Stack/Opc.Ua.Core/Stack/Tcp/InProcessTransport.cs | 51.55% | 89.69% | +38.14% |
| Stack/Opc.Ua.Core/Stack/Bindings/DefaultTransportBindingRegistry.cs | 77.78% | 88.39% | +10.61% |
| Stack/Opc.Ua.Bindings.Https/Https/WebSocketByteTransport.cs | 79.1% | 83.21% | +4.11% |
| Stack/Opc.Ua.Core/Stack/WebApi/WebApiBodyCodec.cs | 66.06% | 83.03% | +16.97% |
| Libraries/Opc.Ua.Client/Fluent/ManagedSessionBuilder.cs | 68.48% | 82.88% | +14.4% |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaWebApiAuthenticationBuilderExtensions.cs | 66.67% | 80% | +13.33% |

## Top 10 Remaining Gaps (by uncovered-line count)

| File | Baseline % | New % | Uncovered Lines |
|------|-----------|-------|----------------|
| Libraries/Opc.Ua.Server/Server/StandardServer.cs | 76.66% | 70.7% | 753 |
| Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryClientChannel.cs | 55.77% | 53.3% | 510 |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpServerChannel.cs | 58.95% | 57.95% | 378 |
| Stack/Opc.Ua.Bindings.Https/Https/HttpsTransportListener.cs | 70.53% | 70.89% | 370 |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs | 71.62% | 61.56% | 311 |
| Stack/Opc.Ua.Core/Types/Utils/Utils.cs | 77.51% | 71.47% | 291 |
| Libraries/Opc.Ua.Client/Session/ManagedSession.cs | 70.66% | 69.13% | 280 |
| Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs | 73.16% | 70.9% | 245 |
| Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.cs | 72.35% | 66.56% | 203 |
| Stack/Opc.Ua.Core/Stack/Configuration/ConfiguredEndpoints.cs | 79.7% | 79.7% | 165 |

## Per-File Coverage Table

| File | Baseline % | New % | Delta | New Uncovered | Status |
|------|-----------|-------|-------|---------------|--------|
| Libraries/Opc.Ua.Client/Fluent/ClientReverseConnectOptions.cs | 100% | 100% | 0 | 0 | ✅ |
| Libraries/Opc.Ua.Client/Fluent/HttpsTransportChannelBindings.cs | 0% | 45% | 45 | 11 | ⚠️ |
| Libraries/Opc.Ua.Client/Fluent/ManagedSessionBuilder.cs | 68.48% | 82.88% | 14.4 | 63 | ✅ |
| Libraries/Opc.Ua.Client/Fluent/OpcUaClientBuilderExtensions.cs | 52.4% | 72.58% | 20.18 | 136 | ⚠️ |
| Libraries/Opc.Ua.Client/ReverseConnectManager.cs | 64.1% | 76.22% | 12.12 | 107 | ⚠️ |
| Libraries/Opc.Ua.Client/Session/ManagedSession.cs | 70.66% | 69.13% | -1.53 | 280 | ⚠️ |
| Libraries/Opc.Ua.Client/WebApi/OpcUaWebApiClientBuilderExtensions.cs | 0% | 100% | 100 | 0 | ✅ |
| Libraries/Opc.Ua.Client/WebApi/WebApiClient.cs | 56.94% | 79.17% | 22.23 | 45 | ⚠️ |
| Libraries/Opc.Ua.Client/WebApi/WebApiClientOptions.cs | 100% | 100% | 0 | 0 | ✅ |
| Libraries/Opc.Ua.Client/WebApi/WebApiTransportChannel.cs | 74.5% | 74.5% | 0 | 77 | ⚠️ |
| Libraries/Opc.Ua.Client/WebApi/WebApiTransportChannelFactory.cs | 94.12% | 100% | 5.88 | 0 | ✅ |
| Libraries/Opc.Ua.Client/WebApi/WebApiWssTransportChannel.cs | 77.46% | 77.46% | 0 | 78 | ⚠️ |
| Libraries/Opc.Ua.Client/WebApi/WebApiWssTransportChannelFactory.cs | 90.91% | 100% | 9.09 | 0 | ✅ |
| Libraries/Opc.Ua.Lds.Server/LdsServer.cs | 87.54% | 80.12% | -7.42 | 67 | ✅ |
| Libraries/Opc.Ua.Server/Server/StandardServer.cs | 76.66% | 70.7% | -5.96 | 753 | ⚠️ |
| Stack/Opc.Ua.Bindings.Https/Authentication/BasicAuthenticationHandler.cs | 87.5% | 87.5% | 0 | 7 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Authentication/JwtClaimSessionlessIdentityProvider.cs | 86.52% | 86.52% | 0 | 19 | ✅ |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaHttpsBuilderExtensions.cs | 0% | 100% | 100 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaKestrelTcpBuilderExtensions.cs | 0% | 100% | 100 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaWebApiAuthenticationBuilderExtensions.cs | 66.67% | 80% | 13.33 | 18 | ✅ |
| Stack/Opc.Ua.Bindings.Https/DependencyInjection/OpcUaWebApiBuilderExtensions.cs | 0% | 67.39% | 67.39 | 15 | ⚠️ |
| Stack/Opc.Ua.Bindings.Https/Https/HttpsServiceHost.cs | 91.55% | 91.55% | 0 | 12 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Https/HttpsTransportListener.cs | 70.53% | 70.89% | 0.36 | 370 | ⚠️ |
| Stack/Opc.Ua.Bindings.Https/Https/JsonRequestMapper.cs | 81.33% | 81.33% | 0 | 14 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Https/SharedKestrelHostRegistry.cs | 91.45% | 92.42% | 0.97 | 15 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Https/WebSocketByteTransport.cs | 79.1% | 83.21% | 4.11 | 45 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Https/WssJsonTransportChannel.cs | 64.86% | 77.93% | 13.07 | 49 | ⚠️ |
| Stack/Opc.Ua.Bindings.Https/Https/WssTransportChannel.cs | 91.67% | 91.67% | 0 | 3 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Tcp/KestrelTcpConnectionHandler.cs | 87.88% | 87.88% | 0 | 4 | ✅ |
| Stack/Opc.Ua.Bindings.Https/Tcp/KestrelTcpTransportListener.cs | 69.72% | 69.72% | 0 | 86 | ⚠️ |
| Stack/Opc.Ua.Bindings.Https/Tcp/PipeByteTransport.cs | 69.62% | 67.72% | -1.9 | 51 | ⚠️ |
| Stack/Opc.Ua.Bindings.Https/WebApi/Endpoints/WebApiEndpointDispatcher.cs | 97.92% | 97.92% | 0 | 2 | ✅ |
| Stack/Opc.Ua.Bindings.Https/WebApi/Endpoints/WebApiEndpointRouteBuilderExtensions.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Https/WebApi/ISessionlessIdentityProvider.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Https/WebApi/IWebApiServer.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Https/WebApi/WebApiHttpsStartupContributor.cs | 92% | 92% | 0 | 8 | ✅ |
| Stack/Opc.Ua.Bindings.Https/WebApi/WebApiServer.cs | 59.8% | 100% | 40.2 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Https/WebApi/WebApiTransportOptions.cs | 0% | 100% | 100 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Pcap/Bindings/CapturingByteTransport.cs | 57.69% | 57.69% | 0 | 33 | ⚠️ |
| Stack/Opc.Ua.Bindings.Pcap/Bindings/CapturingByteTransportFactory.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Pcap/Bindings/ChannelCaptureRegistry.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Pcap/Bindings/PcapBindings.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Bindings.Pcap/Bindings/PcapTransportChannelBinding.cs | 57.14% | 57.14% | 0 | 15 | ⚠️ |
| Stack/Opc.Ua.Bindings.Pcap/Capture/Sources/InProcessCaptureSource.cs | 70.11% | 70.19% | 0.08 | 79 | ⚠️ |
| Stack/Opc.Ua.Bindings.Pcap/DependencyInjection/PcapServiceCollectionExtensions.cs | 74.71% | 73.81% | -0.9 | 22 | ⚠️ |
| Stack/Opc.Ua.Core/Security/Constants/SecurityConstants.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Core/Stack/Bindings/DefaultTransportBindingRegistry.cs | 77.78% | 88.39% | 10.61 | 18 | ✅ |
| Stack/Opc.Ua.Core/Stack/Bindings/ITransportBindingConfigurator.cs | 80% | 80% | 0 | 2 | ✅ |
| Stack/Opc.Ua.Core/Stack/Bindings/OpcUaTransportBuilderExtensions.cs | 87.5% | 87.5% | 0 | 6 | ✅ |
| Stack/Opc.Ua.Core/Stack/Client/Channels/ClientChannelManager.cs | 87.43% | 86.18% | -1.25 | 93 | ✅ |
| Stack/Opc.Ua.Core/Stack/Client/ReverseConnectHost.cs | 75% | 94.44% | 19.44 | 4 | ✅ |
| Stack/Opc.Ua.Core/Stack/Configuration/ConfiguredEndpoints.cs | 79.7% | 79.7% | 0 | 165 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Https/HttpsTransportChannel.cs | 73.98% | 77.81% | 3.83 | 87 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Server/SecureChannelContext.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs | 73.16% | 70.9% | -2.26 | 245 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/InProcessTransport.cs | 51.55% | 89.69% | 38.14 | 10 | ✅ |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpByteTransport.cs | 85.21% | 85.6% | 0.39 | 35 | ✅ |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpListenerChannel.cs | 79.89% | 67.99% | -11.9 | 113 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpReverseConnectChannel.cs | 65.31% | 59.18% | -6.13 | 40 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpServerChannel.cs | 58.95% | 57.95% | -1 | 378 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportChannel.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs | 71.62% | 61.56% | -10.06 | 311 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.cs | 72.35% | 66.56% | -5.79 | 203 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryClientChannel.cs | 55.77% | 53.3% | -2.47 | 510 | ⚠️ |
| Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryTransportChannel.cs | 90.09% | 85.77% | -4.32 | 38 | ✅ |
| Stack/Opc.Ua.Core/Stack/WebApi/WebApiBodyCodec.cs | 66.06% | 83.03% | 16.97 | 28 | ✅ |
| Stack/Opc.Ua.Core/Stack/WebApi/WebApiMediaType.cs | 94.44% | 94.44% | 0 | 4 | ✅ |
| Stack/Opc.Ua.Core/Stack/WebApi/WebApiServiceRoutes.cs | 100% | 100% | 0 | 0 | ✅ |
| Stack/Opc.Ua.Core/Types/Utils/Utils.cs | 77.51% | 71.47% | -6.04 | 291 | ⚠️ |
| Stack/Opc.Ua.Types/Encoders/JsonDecoder.cs | 85.48% | 85.3% | -0.18 | 402 | ✅ |

*N/A entries = interfaces, generated code, deleted files, or fuzzing scaffolding — excluded from weighted calculation.*
