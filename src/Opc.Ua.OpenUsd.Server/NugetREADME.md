# Opc.Ua.OpenUsd.Server

Server-side reusable functionality for the draft OPC UA – OpenUSD Bindings companion model.

Built on **Opc.Ua.OpenUsd**, it helps an OPC UA server expose a live twin:

- author `OpenUsdRepresentation` live / alarm / history / command bindings and component
  compositions on a represented object;
- serve the artist-authored USD asset closure (root layer + references) through a read-only
  OPC UA Part 5 `FileType`, so a generic connector can fetch, verify and render the twin with
  no external asset resolver (`UsdAssetDelivery`, spec §5.15).

Pair it with **Opc.Ua.OpenUsd.Client** on the connector side.
