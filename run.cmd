@echo off
rem Runs run.ps1 regardless of the session's execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run.ps1" %*
