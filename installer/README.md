# Building the Windows installer

1. **Publish the app** (from repo root):
   ```powershell
   .\scripts\publish-release.ps1
   ```
   This creates `publish\FittingApp\` with the runnable app.

2. **Install [Inno Setup](https://jrsoftware.org/isinfo.php)** if you have not already.

3. **Compile the installer**
   - Open `FittingApp.iss` in Inno Setup (or right-click → Compile).
   - Or from command line (if Inno Setup is in PATH):
     ```powershell
     & "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" "e:\GitHub\FattingAPP\installer\FittingApp.iss"
     ```

4. The setup executable is created as `publish\FittingApp-Setup-1.0.0.exe`.
