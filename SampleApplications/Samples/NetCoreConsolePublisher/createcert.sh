#!/bin/bash
# Create a Opc.Ua.Publisher cert for OPC UA publisher
#
MYHOSTNAME=`echo $HOSTNAME | cut -f1 -d. `
echo Create certificate $MYHOSTNAME for Opc.Ua.Publisher
openssl req -newkey rsa:1024 -out cert.crt -keyout cert.key -subj "/DC=$MYHOSTNAME/CN=Opc.Ua.Publisher" -nodes
openssl x509 -days 365 -req -in cert.crt -signkey cert.key -out cert.pem -sha1 -extfile samplepublisher.ext 
openssl pkcs12 -export -in cert.pem -inkey cert.key -out $MYHOSTNAME.pfx -nodes 
openssl x509 -in cert.pem -out $MYHOSTNAME.der -outform DER 
openssl x509 -in $MYHOSTNAME.der -inform DER -text
rm cert.*
mkdir -p "./OPC Foundation/CertificateStores/MachineDefault/certs"
mkdir -p "./OPC Foundation/CertificateStores/MachineDefault/private"
mv $MYHOSTNAME.der "./OPC Foundation/CertificateStores/MachineDefault/certs"
mv $MYHOSTNAME.pfx "./OPC Foundation/CertificateStores/MachineDefault/private"

