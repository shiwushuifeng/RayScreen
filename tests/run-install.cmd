@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\DSH NEXT\develop\RayScreen\install.ps1" > "C:\DSH NEXT\develop\RayScreen\tests\install-test.log" 2>&1
echo exit=%errorlevel% >> "C:\DSH NEXT\develop\RayScreen\tests\install-test.log"
