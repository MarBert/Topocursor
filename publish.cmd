@echo off
rem Small single exe (requires the .NET 8 Desktop Runtime to be installed)
dotnet publish src\Windows\TopoCursor.Windows.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
rem Variant with no prerequisites (larger exe):
rem dotnet publish src\Windows\TopoCursor.Windows.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
