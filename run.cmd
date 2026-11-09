@echo off
rem Avvia run.ps1 senza dipendere dalla execution policy della sessione
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
