@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\DSH NEXT\develop\RayScreen\uninstall.ps1" > "C:\DSH NEXT\develop\RayScreen\tests\uninstall-full.log" 2>&1
echo exit=%errorlevel% >> "C:\DSH NEXT\develop\RayScreen\tests\uninstall-full.log"
