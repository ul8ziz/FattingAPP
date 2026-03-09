# Building the Windows installer

1. **Publish and build** (from repo root; Inno Setup is auto-downloaded to `tools\InnoSetup` if missing):
   ```powershell
   .\scripts\publish-release.ps1 -BuildInstaller
   ```
   This creates `publish\FittingApp\` and `publish\FittingApp-Setup-1.0.0.exe`. Optional: `-CreateZip` for `publish\FittingApp-Release.zip`.

2. **Or compile the installer manually** (after running `publish-release.ps1` once):
   - Open `FittingApp.iss` in Inno Setup → Build → Compile.
   - Or: `& "tools\InnoSetup\ISCC.exe" "installer\FittingApp.iss"`

3. **App icon:** The installed .exe uses the project icon (from `AppIcon.ico`). To regenerate the icon from `AppIcon.png`, run `.\scripts\create-app-icon.ps1`. For a custom icon on the setup.exe itself, install [ImageMagick](https://imagemagick.org/script/download.php#windows), run the script again (it will create a multi-size .ico), then in `FittingApp.iss` uncomment the `SetupIconFile` line.

4. Output: `publish\FittingApp-Setup-1.0.0.exe`.
