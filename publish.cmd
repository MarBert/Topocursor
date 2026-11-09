@echo off
rem Exe singolo piccolo (richiede .NET 8 Desktop Runtime installato)
dotnet publish src\Windows\TopoCursor.Windows.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
rem Variante senza prerequisiti (exe piu' grande):
rem dotnet publish src\Windows\TopoCursor.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
