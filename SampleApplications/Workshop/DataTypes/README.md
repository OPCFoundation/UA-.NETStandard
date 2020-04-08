# DataTypes Server

This server is build using the UA-.NETStandard stack as example of how to define and use custom data types.

## How to integrate new information model into OPC Server

This documentation explains how to add a custom information model to OPC Server based on UA-.NETStandard stack. It will use the DataTypes server example as reference but the general steps are the same for every UA-.NETStandard stack based OPC Server.

### Preparation

1. Clone [Opc.Ua.ModelCompiler Repository](https://github.com/OPCFoundation/UA-ModelCompiler)
2. Build Opc.Ua.ModelCompiler solution
3. Copy the build result of Opc.Ua.ModelCompiler solution to [UA-.NETStandard/SampleApplications/bin](./../../bin)

### Add own information model

1. Create a Folder to to UA-.NETStandard/SampleApplications/Workshop/DataTypes/Common e.g. MyInformationModel
   1. Create a sub-folder named "Output"
2. Copy the model itself into this folder e.g. MyInformationModel.xml into UA-.NETStandard/SampleApplications/Workshop/DataTypes/Common/MyInformationModel
3. Modify [BuildDesign.bat](./Common/BuildDesign.bat) and add the following lines

```cmd
echo Building MyInformationModel
Opc.Ua.ModelCompiler.exe -version v104 -d2 ".\MyInformationModel\MyInformationModel.xml" -cg ".\MyInformationModel\Output\MyInformationModel.csv" -o2 ".\MyInformationModel\Output"
echo Success!
```

4. Run [BuildDesign.bat](./Common/BuildDesign.bat)

```cmd
.\Common\BuildDesign.bat
```

In case of an issue the Opc.Ua.ModelCompiler will show and error dialog, otherwise you will have different files in your output folder, that need to be added into the project. Either as source code or as embedded resource.

### Use information model

Extend the [DataTypesServer](./Server/DataTypesServer.cs):

```csharp
// in CreateMasterNodeManager method
server.Factory.AddEncodeableTypes(typeof(MyNamespace.Data.Types.MyDataType).Assembly);
```

Extend the [DataTypesNodeManager](./Server/DataTypesNodeManager.cs):

```csharp
// in constructor - add additional namespaces

SetNamespaces(
   Quickstarts.DataTypes.Namespaces.DataTypes,
   Quickstarts.DataTypes.Types.Namespaces.DataTypes,
   Quickstarts.DataTypes.Instances.Namespaces.DataTypeInstances,
   MyNamespace.DataTypes.Namespaces.DataTypes,
   MyNamespace.DataTypes.Types.Namespaces.DataTypes,
   MyNamespace.DataTypes.Instances.Namespaces.DataTypeInstances);

// CreateAddressSpace - load from XML and binary (when generated for specific model)

BaseDataVariableState dictionary = (BaseDataVariableState)FindPredefinedNode(
   ExpandedNodeId.ToNodeId(MyNamespace.DataTypes.Types.VariableIds.DataTypes_BinarySchema, Server.NamespaceUris),
   typeof(BaseDataVariableState));

dictionary.Value = LoadSchemaFromResource("MyNamespace.DataTypes.Types.MyNamespace.DataTypes.Types.Types.bsd",typeof(MyNamespace.Data.Types.MyDataType).Assembly);

dictionary = (BaseDataVariableState)FindPredefinedNode(
   ExpandedNodeId.ToNodeId(MyNamespace.DataTypes.Types.VariableIds.DataTypes_XmlSchema, Server.NamespaceUris),
   typeof(BaseDataVariableState));

dictionary.Value = LoadSchemaFromResource("MyNamespace.DataTypes.Types.MyNamespace.DataTypes.Types.Types.xsd", typeof(MyNamespace.Data.Types.MyDataType).Assembly);

// in LoadPredefinedNodes
predefinedNodes.LoadFromBinaryResource(context, 
      "MyNamespace.DataTypes.Types.MyNamespace.DataTypes.Types.PredefinedNodes.uanodes",
      typeof(MyNamespace.DataTypes.Types.MyDataType).Assembly,
      true);
predefinedNodes.LoadFromBinaryResource(context,
      "MyNamespace.DataTypes.Instances.MyNamespace.DataTypes.Instances.PredefinedNodes.uanodes",
      typeof(MyNamespace.DataTypes.Types.MyDataType).GetTypeInfo().Assembly,
      true);
```

Compile and run the DataTypes server, you should be able to connect with any OPC UA client (e.g. DataTypes Client) and to browse your own data types.
