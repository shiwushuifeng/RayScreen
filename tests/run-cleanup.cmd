@echo off
chcp 65001 >nul
pnputil /delete-driver oem42.inf /uninstall /force > "C:\DSH NEXT\develop\RayScreen\tests\cleanup.log" 2>&1
echo delete-driver exit=%errorlevel% >> "C:\DSH NEXT\develop\RayScreen\tests\cleanup.log"
pnputil /remove-device "DISPLAY\MTT1337\1&23323A80&0&UID256" >> "C:\DSH NEXT\develop\RayScreen\tests\cleanup.log" 2>&1
echo remove-monitor exit=%errorlevel% >> "C:\DSH NEXT\develop\RayScreen\tests\cleanup.log"
