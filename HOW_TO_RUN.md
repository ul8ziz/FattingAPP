# كيفية تشغيل المشروع

## الطريقة 1: Visual Studio 2022 (مُوصى بها)

1. **افتح Visual Studio 2022**
2. **افتح Solution:**
   - File → Open → Project/Solution
   - اختر `Ul8ziz.FittingApp.sln` من المجلد الرئيسي
3. **انتظر حتى يتم تحميل المشروع**
4. **Build المشروع:**
   - اضغط `Ctrl+Shift+B` 
   - أو Build → Build Solution
5. **شغّل التطبيق:**
   - اضغط `F5` (Debug)
   - أو `Ctrl+F5` (Run without debugging)

## الطريقة 2: Command Line

```powershell
# انتقل إلى مجلد المشروع
cd src/App

# Build المشروع
dotnet build

# شغّل التطبيق
dotnet run
```

## ما الذي سيظهر؟

عند تشغيل التطبيق، ستظهر نافذة تحتوي على:

- **TopBar (شريط علوي):**
  - Logo + "Hearing Aid Fitting System"
  - Unsaved Changes warning (أصفر)
  - Active Session indicator (أخضر)
  - End Session button
  - Connection Status: "HI-PRO 2" (أخضر)
  - User Info: "Dr. Sarah Johnson"

- **Sidebar (شريط جانبي):**
  - Navigation header
  - Connect Devices
  - Patient Management
  - Audiogram
  - Fitting
  - Session Summary
  - System Info footer

- **Content Area (منطقة المحتوى):**
  - رسالة ترحيب افتراضية

## ملاحظات

- التطبيق يستخدم بيانات تجريبية (Sample Data) لعرض الواجهة
- لإضافة وظائف حقيقية، ستحتاج إلى إضافة ViewModels و Commands
- جميع الأنماط موجودة في `src/App/Styles/Styles.xaml`

## استكشاف الأخطاء

إذا لم يعمل المشروع:

1. **تأكد من تثبيت .NET 7 SDK:**
   ```powershell
   dotnet --version
   ```
   يجب أن يظهر `7.x.x` أو أعلى

2. **تأكد من تثبيت Visual Studio 2022** مع Desktop development workload

3. **تحقق من الأخطاء في Output window** في Visual Studio

4. **نظّف وأعد البناء:**
   - Build → Clean Solution
   - Build → Rebuild Solution
