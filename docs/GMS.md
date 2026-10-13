# Geometric Measurement Systems

| Feature | Support |
| --- | --- |
| OPC 40210 reference 1.0.1 / released model 1.0.0 | Supported |
| Sensors/multi-sensors, qualification, workspaces, loading, tools and accessories | Supported |
| Calibration observations and additional environmental sensors | Supported; no physical calibration is performed |
| Native GMS jobs/parts and inherited production statistics | Supported |
| Results, characteristics/tolerances, corrections, queries, downloads and events | Supported; corrections are observations, not actuation |
| SensorWarningAlarmType | Supported for additional sensors not directly involved in geometrical measurement |
| Shared/standalone hosting, typed clients, bounded MCP and NativeAOT | Supported |
| Complete GMS Production facet | Not supported: §12.3.2.7 requires undefined `GMS_ProductionStatisticsType`; [Annex A.1's normative NodeSet](https://reference.opcfoundation.org/specs/OPC-40210/annex-a) defines no such type/ID |
| Invented measurement-start, calibration-execution or GMS job/correction-control verbs | Not supported |
| External OPC Foundation certification of a deployment | Not claimed |
