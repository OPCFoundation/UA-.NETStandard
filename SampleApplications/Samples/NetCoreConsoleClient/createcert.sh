#!/bin/bash
# Create a UA Sample Client cert for OPC UA client
#
openssl req -newkey rsa:1024 -out cert.crt -keyout cert.key -subj "/DC=$HOSTNAME/CN=UA Sample Client" -nodes
openssl x509 -days 365 -req -in cert.crt -signkey cert.key -out cert.pem -sha1 -extfile sampleclient.ext 
openssl pkcs12 -export -in cert.pem -inkey cert.key -out $HOSTNAME.pfx -nodes 
openssl x509 -in cert.pem -out $HOSTNAME.der -outform DER 
openssl x509 -in $HOSTNAME.der -inform DER -text
rm cert.*
mkdir -p "./OPC Foundation/CertificateStores/MachineDefault/certs"
mkdir -p "./OPC Foundation/CertificateStores/MachineDefault/private"
mv $HOSTNAME.der "./OPC Foundation/CertificateStores/MachineDefault/certs"
mv $HOSTNAME.pfx "./OPC Foundation/CertificateStores/MachineDefault/private"

