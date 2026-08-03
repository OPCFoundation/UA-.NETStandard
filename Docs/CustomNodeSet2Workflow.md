# General workflow for creating a custom NodeSet2

This document describes a practical workflow for creating and maintaining a custom OPC UA information model with the .NET Standard stack.

The short version is:

1. Author the model in **ModelDesign**.
2. Run **ModelCompiler** to generate the derived artifacts.
3. Treat the generated **NodeSet2**, CSV, and code as build outputs.
4. Instantiate your root objects in `CreateAddressSpace`.
5. Keep editing the **ModelDesign** file instead of hand-editing the generated NodeSet2.

## Recommended workflow

### 1. Start with ModelDesign

Use a ModelDesign file as the source of truth for your custom model.

That is the pattern used by the quickstart models in this repository. For example, the Boiler sample keeps the design in:

- `Applications/Quickstarts.Servers/Boiler/Generated/BoilerDesign.xml`

From there, ModelCompiler generates the rest of the model artifacts.

ModelDesign is usually the easiest place to:

- derive from a companion specification type,
- add your own properties and objects,
- change modelling rules from optional to mandatory for your subtype,
- provide default values and descriptions.

For example, if your type derives from `MachineTool:MachineToolType`, you can redefine children in your subtype and mark the nodes you always want to exist as `ModellingRule="Mandatory"`.

### 2. Generate the artifacts with ModelCompiler

Run ModelCompiler against the ModelDesign file and keep the generated outputs together.

Typical generated outputs are:

- `*.NodeSet2.xml`
- `*.NodeIds.csv`
- `*.Classes.cs`
- `*.Constants.cs`
- `*.DataTypes.cs`
- `*.PredefinedNodes.xml`
- `*.PredefinedNodes.uanodes`

The Boiler sample shows this generated set in:

- `Applications/Quickstarts.Servers/Boiler/Generated`

### 3. Treat NodeSet2 as generated output

The generated NodeSet2 can become much larger than the original ModelDesign file. That is expected.

The ModelDesign file is compact because it describes the model declaratively. The generated NodeSet2 is expanded and contains the fully materialized UA nodes, references, modelling rules, and metadata.

A good rule is:

- **edit `ModelDesign.xml`**
- **regenerate `NodeSet2.xml`, CSV, and code**
- **do not hand-maintain the generated NodeSet2 unless you have a very specific reason**

### 4. Load the generated predefined nodes in your server

At runtime, the server usually loads the generated predefined nodes from the `*.PredefinedNodes.uanodes` resource.

The quickstart servers do this in `LoadPredefinedNodes`, for example:

- `Applications/Quickstarts.Servers/Boiler/BoilerNodeManager.cs`
- `Applications/Quickstarts.Servers/MemoryBuffer/MemoryBufferNodeManager.cs`

The generated binary node set is embedded as a resource in:

- `Applications/Quickstarts.Servers/Quickstarts.Servers.csproj`

This is the part that brings the model metadata into the server in a form the node manager can use.

### 5. Create your runtime root objects in `CreateAddressSpace`

Use `CreateAddressSpace` to decide which concrete objects should exist in the running server.

This is the right place to:

- create one machine instance for the current device,
- choose between multiple machine variants,
- attach the instance to the `Objects` folder or another predefined root,
- initialize values and runtime behavior.

This pattern is used throughout the quickstart servers. For example:

- `Applications/Quickstarts.Servers/Boiler/BoilerNodeManager.cs`
- `Applications/Quickstarts.Servers/MemoryBuffer/MemoryBufferNodeManager.cs`

If you have three machine variants, define the reusable types in ModelDesign, then instantiate only the one that applies to the current machine in `CreateAddressSpace`.

If your NodeSet already contains concrete predefined objects and you need to attach custom runtime behavior to them, use `AddBehaviourToPredefinedNode`. If runtime configuration decides which root objects exist, `CreateAddressSpace` is usually the clearer workflow.

### 6. Prefer modelling optional children in your subtype when you always need them

If a companion specification defines a child as optional but your machine always exposes it, model it as mandatory in your derived type.

That avoids manually creating each optional child in code and gives you the generated metadata, references, and structure automatically.

This is often simpler than creating optional properties by hand with `new PropertyState(...)` and then trying to recreate the missing metadata yourself.

### 7. Let each instance get its own instance NodeIds

When child nodes are introduced through the type definition and then instantiated as part of an object instance, each instantiated child receives its own instance NodeId.

That is normally what you want.

The important distinction is:

- **type definition nodes** describe the structure,
- **instance nodes** are the concrete runtime nodes created from that structure.

If multiple tools appear to share one manually assigned NodeId, that usually means the node was modeled or created as a single explicit node instead of as part of the instantiated type structure.

Modeling the tool structure in ModelDesign and then instantiating the tool object avoids that problem.

## Practical guidance

For most custom server projects, the clean workflow is:

1. Define your types in ModelDesign.
2. Derive from companion specification types there.
3. Mark children as mandatory in your subtype when your server always needs them.
4. Run ModelCompiler.
5. Commit the generated artifacts required by your project.
6. Load the predefined nodes in the node manager.
7. Create the concrete root object for the current machine in `CreateAddressSpace`.
8. Update values and behavior in your runtime code.

## When to edit which file

### Edit the ModelDesign file when you want to change

- the type hierarchy,
- references,
- modelling rules,
- default values,
- browse names,
- custom types and properties.

### Edit C# server code when you want to change

- which machine instance is created at runtime,
- how values are updated,
- method implementations,
- eventing and runtime behavior,
- environment-specific configuration.

### Avoid editing the generated NodeSet2 directly when

- the same change can be expressed in ModelDesign,
- you plan to regenerate the model later,
- you need consistency between XML, CSV, and generated code.

## Related examples in this repository

- `Applications/Quickstarts.Servers/Boiler/Generated/BoilerDesign.xml`
- `Applications/Quickstarts.Servers/Boiler/Generated/Boiler.NodeSet2.xml`
- `Applications/Quickstarts.Servers/Boiler/BoilerNodeManager.cs`
- `Applications/Quickstarts.Servers/MemoryBuffer/MemoryBufferNodeManager.cs`

## Summary

For this stack, the most maintainable workflow is usually:

- **ModelDesign as the source of truth**
- **ModelCompiler for generated artifacts**
- **NodeSet2 as generated output**
- **`CreateAddressSpace` for choosing and creating runtime instances**

That keeps the model definition, generated artifacts, and runtime logic clearly separated.
