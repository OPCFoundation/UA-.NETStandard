#!/bin/bash
set echo off
echo Run the local docker container of the console reference server
echo By default, the certificate store of the ref server is mapped to './OPC Foundation/pki'
echo A log file is created at './OPC Foundation/Logs/Quickstarts.ReferenceServer.log.txt'
echo A shadow configuration file for customization is created in './OPC Foundation/Quickstarts.ReferenceServer.Config.xml'
sudo docker pull ghcr.io/opcfoundation/uanetstandard/refserver:latest
sudo docker run -it -p 62541:62541 -h $HOSTNAME -v "$(pwd)/OPC Foundation:/root/.local/share/OPC Foundation" ghcr.io/opcfoundation/uanetstandard/refserver:latest -c -s
