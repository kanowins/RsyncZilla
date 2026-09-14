@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\set-version.ps1" %*
