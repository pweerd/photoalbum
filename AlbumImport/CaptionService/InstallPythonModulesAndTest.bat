echo Upgrading PIP
python.exe -m pip install --upgrade pip
if errorlevel 1 goto ERROR

echo Installing module quart
pip install quart
if errorlevel 1 goto ERROR

echo Installing module transformers
pip install transformers
if errorlevel 1 goto ERROR

echo Installing module torch
pip install torch
if errorlevel 1 goto ERROR

echo Installing module googletrans
pip install googletrans
if errorlevel 1 goto ERROR

echo Installing module Pillow
pip install Pillow
if errorlevel 1 goto ERROR

echo Installing AI models and testing
python test.py
if errorlevel 1 goto ERROR
goto OK

:ERROR
Pause
exit /B 1

:OK
exit /B 0
