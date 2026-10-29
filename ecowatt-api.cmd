@echo off
REM Arranca la API de EcoWatt Casa desde la build de release.
REM
REM El puerto 5080 no es decorativo: el proxy del frontend apunta ahi.
REM El broker MQTT escucha aparte en 0.0.0.0:1883, asi que los equipos de la
REM LAN llegan igual aunque la API solo sirva HTTP en localhost.
set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://localhost:5080
cd /d "%~dp0publish\api"
title EcoWatt API
EcoWattCasa.API.exe
