# OPC Foundation UA .NET Standard Reference Client

## Introduction

The console reference client can be configured using several console parameters.
Some of these parameters are explained in more detail below.

To see all available parameters call console reference client the with the parameter `-h`.

### How to specify User Identity
#### Username & Password
Specify as console parameters:
    `-un YourUsername`
    `-up YourPassword`

#### Certificate
Place your user certificate in the TrustedUserCertificatesStore (the path can be found in the client configuration XML). Make shure to include an accessible private key with the certificate.
Specify console parameters:
    `-uc Thumbprint` (of the user certificate to select)
    `-ucp Password` (of the user certificates private key (optional))