#!/bin/bash
echo build a docker container of the .NET Core reference server, without https support
sudo docker build -f Dockerfile -t consolerefserver ./../..
