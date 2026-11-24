@echo off
cd /d "C:\DSH NEXT\develop\RayScreen"
call install.cmd < nul > "C:\DSH NEXT\develop\RayScreen\tests\install-fresh.log" 2>&1
echo exit=%errorlevel% >> "C:\DSH NEXT\develop\RayScreen\tests\install-fresh.log"
