#!/bin/bash
rm Procedurline.zip Procedurline.dll
dotnet build Code/Procedurline/Procedurline.csproj
zip Procedurline.zip -r LICENSE.txt everest.yaml Procedurline.dll