@echo off
REM Change to script directory
cd /d %~dp0

IF NOT EXIST node_modules (
  echo Installing npm dependencies...
  npm install
)

echo Starting Vite dev server...
npm run dev
