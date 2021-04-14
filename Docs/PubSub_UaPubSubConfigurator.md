

# UaPubSubConfigurator Class

*UaPubSubConfigurator* class contains a simplified API that can be used to configure a PubSub application.

It is instantiated by default for any new instance of [UaPubSubApplication Class](PubSub_UaPubSubApplication.md) and afterwards can be used to alter that application's configuration.

The *UaPubSubConfigurator* class has the following methods:

 - **LoadConfiguration**()  
Loads the configuration from a file path or from a PubSubConfigurationDataType instance and raises the corresponding Added events for all the objects added from that configuration. It can entirely replace the configuration if the parameter replaceExisting is set on true, or it can append to existing configuration the contents of the new configuration.

 - **Enable**()  
Tries to set the [PubSubState](PubSubState.md) of the specified configuration object to Operational and if successful raises the PubSubStateChanged event for all configuration objects that changed their state because of this action.
If the configuration object that is specified does not have status = Disabled the method will return BadInvalidState status code without any effect.
 - **Disable**()  
Tries to set the [PubSubState](PubSubState.md) of the specified configuration object to Disabled and if successful raises the PubSubStateChanged event for all configuration objects that changed their state because of this action.
If the configuration object that is specified has status = Disabled the method will return BadInvalidState status code without any effect.

 - **AddPublishedDataSet**(PublishedDataSetDataType publishedDataSetDataType)  
Adds the specified publishedDataSetDataType object to current configuration and raises the PublishedDataSetAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided publishedDataSetDataType object has an already used name then BadBrowseNameDuplicated status code is returned and the dataset is not added to the configuration.

 - **RemovePublishedDataSet**()  
Removes the specified published data set object from configuration and raises the PublishedDataSetRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.

 - **AddExtensionField**(uint publishedDataSetConfigId, KeyValuePair extensionField)  
Adds the specified extensionField object to the specified publishedDataSet and raises the ExtensionFieldAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided extensionField object has an already used name then BadNodeIdExists status code is returned and the extension field is not added to the configuration.

 - **RemoveExtensionField**()  
Removes the specified extension field from parent published data set and raises the ExtensionFieldRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.

 - **AddConnection**(PubSubConnectionDataType pubSubConnectionDataType)  
Adds the specified pubSubConnectionDataType object to current configuration and raises the ConnectionAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided pubSubConnectionDataType object has an already used name then BadBrowseNameDuplicated status code is returned and the connection is not added to the configuration.

 - **RemoveConnection**()  
Removes the specified connection object from configuration and raises ConnectionRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.

 - **AddWriterGroup**(uint parentConnectionId, WriterGroupDataType writerGroupDataType)  
Adds the specified writerGroupDataType object to current configuration as a child of the connection specified by parentConnectionId and raises the WriterGroupAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided writerGroupDataType object has an already used name then BadBrowseNameDuplicated status code is returned and the writer group is not added to the configuration.

 - **RemoveWriterGroup**()  
Removes the specified writer group object from configuration and raises WriterGroupRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.

 - **AddDataSetWriter**(uint parentWriterGroupId, DataSetWriterDataType dataSetWriterDataType)  
Adds the specified dataSetWriterDataType object to current configuration as a child of the writer group specified by parentWriterGroupId and raises the DataSetWriterAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided dataSetWriterDataType object has an already used name then BadBrowseNameDuplicated status code is returned and the dataset writer is not added to the configuration.

 - **RemoveDataSetWriter**()  
Removes the specified dataset writer object from configuration and raises DataSetWriterRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.

 - **AddReaderGroup**(uint parentConnectionId, ReaderGroupDataType readerGroupDataType)  
Adds the specified readerGroupDataType object to current configuration as a child of the connection specified by parentConnectionId and raises the ReaderGroupAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided readerGroupDataType object has an already used name then BadBrowseNameDuplicated status code is returned and the reader group is not added to the configuration.

 - **RemoveReaderGroup**()  
Removes the specified reader group object from configuration and raises ReaderGroupRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.

 - **AddDataSetReader**(uint parentReaderGroupId, DataSetReaderDataType dataSetReaderDataType)  
Adds the specified dataSetReaderDataType object to current configuration as a child of the reader group specified by parentReaderGroupId and raises the DataSetReaderAdded event.
The UaPubSubConfigurator will assign a unique configuration id to this newly added configuration object that will be useful for finding it at a later point using the Find methods.
If the provided dataSetReaderDataType object has an already used name then BadBrowseNameDuplicated status code is returned and the dataset reader is not added to the configuration.

 - **RemoveDataSetReader**()  
Removes the specified dataset reader object from configuration and raises DataSetReaderRemoved event.
If the configuration cannot find the object to remove then it will return BadNodeIdUnknown status code.