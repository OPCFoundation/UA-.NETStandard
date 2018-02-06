#!/bin/bash
echo Test the .Net Core console server and console client
workdir=$(pwd)
testresult=0
testresulthttps=0
serverresult=0

cd SampleApplications/Samples/NetCoreConsoleServer
echo build server
rm -r obj
dotnet build NetCoreConsoleServer.csproj
echo start server
dotnet run --no-restore --no-build --project NetCoreConsoleServer.csproj -t 60 -a &
serverpid="$!"
cd $workdir

cd SampleApplications/Samples/NetCoreConsoleClient
echo build client
rm -r obj
dotnet build NetCoreConsoleClient.csproj
echo start client
dotnet run --no-restore --no-build --project NetCoreConsoleClient.csproj -t 10 -a &
clientpid="$!"
cd $workdir

echo wait for opc.tcp client
wait $clientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresult=$?
	echo "FAILED - Client test failed with a status of $testresult"
fi

cd SampleApplications/Samples/NetCoreConsoleClient
echo start client for https connection
dotnet run --no-restore --no-build --project NetCoreConsoleClient.csproj -t 10 -a https://localhost:51212 &
clientpid="$!"
cd $workdir

echo wait for opc.tcp client
wait $clientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresulthttps=$?
	echo "FAILED - Client test failed with a status of $testresulthttps"
fi

echo wait for server
wait $serverpid
serverresult=$?

if [ $? -eq 0 ]; then
	echo "SUCCESS - Server test passed"
else
	serverresult=$?
	echo "FAILED - Server test failed with a status of $serverresult"
fi

echo "Test results: Client:$testresult Server:$serverresult ClientHttps:$testresulthttps"
exit $((testresult + serverresult + testresulthttps))



