# Awagaman ERP Release Steps

## 1) Version + installer naming convention
- App version tag format: `vMAJOR.MINOR.PATCH` (example: `v1.0.0`)
- Installer file format: `AwagamanERP-Setup-vMAJOR.MINOR.PATCH.exe`
- Example installer file: `AwagamanERP-Setup-v1.0.0.exe`

## 2) First-time GitHub setup
Run in PowerShell from repository root:

```powershell
cd "c:\amit sagu\awagaman project\ATL ERP"
git init
git branch -M main
git add .
git commit -m "Initial commit"
git remote add origin https://github.com/saguamit/awagamanerp.git
git push -u origin main
```

## 3) Install GitHub CLI and login
- Install: https://cli.github.com/
- Then:

```powershell
gh auth login
```

## 4) Build your installer
Generate:
- `AwagamanERP-Setup-v1.0.0.exe` (example)

## 5) Create release + upload installer

```powershell
gh release create v1.0.0 "C:\path\to\AwagamanERP-Setup-v1.0.0.exe" --title "Awagaman ERP v1.0.0" --notes "Release v1.0.0"
```

## 6) User download link
For installer named `AwagamanERP-Setup-v1.0.0.exe`, your stable latest link is:

```text
https://github.com/saguamit/awagamanerp/releases/latest/download/AwagamanERP-Setup-v1.0.0.exe
```

## 7) For next update
Example `v1.0.1`:
- Build `AwagamanERP-Setup-v1.0.1.exe`
- Run:

```powershell
gh release create v1.0.1 "C:\path\to\AwagamanERP-Setup-v1.0.1.exe" --title "Awagaman ERP v1.0.1" --notes "Release v1.0.1"
```

