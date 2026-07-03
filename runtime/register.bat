@echo off
REM CasScale.ocx'i Windows'a kaydeder. YONETICI olarak calistirin (sag tik > Yonetici olarak calistir).
echo CasScale.ocx kaydediliyor...
regsvr32 "%~dp0CasScale.ocx"
echo.
echo Bitti. Hata almadiysaniz OCX kayitli demektir.
pause
