# Server support for OPC UA AI Model Management

A node manager that publishes the model catalogue, datasets, deployments and inference endpoints, routes `Invoke` to an `IInferenceBackend`, runs learning jobs, and streams model artefacts through the standard file-transfer types.

Part of the [OPC UA .NET Standard](https://github.com/OPCFoundation/UA-.NETStandard) stack.

> **Draft.** The *OPC UA - AI Model Management and Inference* companion
> specification is a working draft. Its namespace URI and every NodeId are
> provisional, and every ObjectType and BrowseName can change when the working
> group publishes.

## Documentation

See the [AI Model Management guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/samples/AI/README.md)
for the example: `ModelManagementServer` publishes a catalogue and
routes inference, and `ModelManagementClient` walks it.

## License

MIT - see the [license](https://opcfoundation.org/license/mit.html).
