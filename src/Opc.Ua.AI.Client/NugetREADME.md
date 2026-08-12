# Client support for OPC UA AI Model Management

`Opc.Ua.AI.Client` provides a high-level client facade for the draft OPC UA AI Model Management and Inference companion model. The API is organised around the specification concepts: AI root, model catalogue, model cards and resources, datasets, deployments, model sources, inference jobs, learning jobs, evaluation runs, and inference transfers.

The root `AiClient` resolves the AI namespace and folders, enumerates typed instances, and opens focused clients such as `AiModelClient`, `AiDeploymentClient`, `AiModelSourceClient`, and `AiInferenceTransferClient`. These clients use the generated ObjectType proxies for method calls and return named snapshot records for reads. Artefact transfer is exposed through `ByteString` and stream helpers over the standard OPC UA `FileType` methods.

Part of the [OPC UA .NET Standard](https://github.com/OPCFoundation/UA-.NETStandard) stack.

> **Draft.** The *OPC UA - AI Model Management and Inference* companion
> specification is a working draft. Its namespace URI and every NodeId are
> provisional, and every ObjectType and BrowseName can change when the working
> group publishes.

## Documentation

See the [AI Model Management sample](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/samples/AI/README.md)
for the example: `ModelManagementServer` publishes a catalogue and
routes inference, and `ModelManagementClient` walks it with `AiClient`.

## License

MIT - see the [license](https://opcfoundation.org/license/mit.html).
