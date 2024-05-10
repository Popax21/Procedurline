#!/bin/bash -e
CONFIG=$1
rm -f Procedurline.zip Procedurline.xml Procedurline.dll Procedurline.pdb
dotnet build -c ${CONFIG:=Release} Code/Procedurline/Procedurline.csproj
zip Procedurline.zip -r LICENSE.txt everest.yaml Procedurline.dll Procedurline.pdb Procedurline.xml