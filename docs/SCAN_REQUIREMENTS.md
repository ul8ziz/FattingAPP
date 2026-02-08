# متطلبات فحص المبرمجين (Programmer Scan Requirements)

هذا المستند يوضح جميع المتطلبات اللازمة لتشغيل فحص المبرمجين بشكل صحيح في تطبيق Ul8ziz.FittingApp.

---

## 1. متطلبات النظام الأساسية

### 1.1 نظام التشغيل
- **Windows 10/11** (64-bit موصى به)
- **.NET Runtime 10.0** أو أحدث

### 1.2 Visual C++ Redistributables

#### مطلوب للـ SDK (إجباري)
- **Microsoft Visual C++ 2022 Redistributable (x86)**
  - المسار: `SoundDesignerSDK/redistribution/MS VC++ 2022 Redist (x86)/VC_redist.x86.exe`
  - **ملاحظة:** هذا مطلوب لعمل الـ SDK بشكل صحيح.
  - **التحقق:** تأكد من تثبيته من خلال "Add or Remove Programs" في Windows.

#### مطلوب للمبرمجات السلكية (CTK) - إجباري لـ CAA, HI-PRO, Promira
- **CTK Runtime**
  - المسار: `SoundDesignerSDK/redistribution/CTK/CTKRuntime64.msi` (لـ 64-bit)
  - أو `CTKRuntime.msi` (لـ 32-bit)
  - **التحقق:** تأكد من وجود `C:\Program Files (x86)\CTK` أو `C:\Program Files\CTK`

- **Microsoft Visual C++ 2010 SP1 Redistributable (x86)**
  - المسار: `SoundDesignerSDK/redistribution/MS VC++ 2010 Redist (x86)/vcredist_x86.exe`
  - **ملاحظة:** هذا مطلوب كتبعية لـ CTK.

---

## 2. متطلبات ملفات الـ SDK

### 2.1 ملفات SDK الأساسية (في `src/Device/Libs/`)
يجب أن تكون الملفات التالية موجودة في مجلد الإخراج (`bin/Debug/net10.0-windows/`):

- `sd.config` - ملف إعدادات الـ SDK
- `sdnet.dll` - الربط .NET للـ SDK
- `SoundDesigner.Core.dll`
- `SoundDesigner.Framework.dll`
- `SoundDesigner.Manufacturing.dll`
- `SoundDesigner.Modeling.dll`
- `SoundDesigner.Modules.dll`
- `SDCOM.dll` + `SDCOM.dll.manifest`
- `E7160SL.library` - ملف المنتج
- `aplib.dll`, `noahlinklib.dll`, `rsl10lib.dll` - مكتبات المبرمجات

### 2.2 متغيرات البيئة المطلوبة

يتم إعدادها تلقائياً في `SdkConfiguration.SetupEnvironment()`:

- **SD_CONFIG_PATH**: يجب أن يشير إلى `sd.config` في مجلد الإخراج
- **PATH**: يتم إضافة المسارات التالية تلقائياً:
  - `C:\Program Files (x86)\HI-PRO` (إذا كان موجوداً)
  - `C:\Program Files (x86)\CTK` (إذا كان موجوداً)
  - `C:\Program Files\CTK` (إذا كان موجوداً)
  - مجلد الإخراج (للبحث عن DLLs الخاصة بالـ SDK)

---

## 3. متطلبات المبرمجات السلكية (Wired Programmers)

### 3.1 Communication Accelerator Adaptor (CAA)

**المتطلبات:**
- CTK Runtime مثبت (انظر القسم 1.2)
- VC++ 2010 Redistributable مثبت
- الجهاز متصل عبر USB
- الجهاز قيد التشغيل

**الحالة المتوقعة عند الفحص:**
- إذا كان التعريف مثبتاً والجهاز غير متصل: `Driver OK, hardware not connected`
- إذا كان التعريف غير مثبت: خطأ في `CreateCommunicationInterface`

### 3.2 HI-PRO

**المتطلبات:**

1. **تثبيت تعريف HI-PRO (v4.02 أو أحدث)**
   - متوفر من Otometrics®
   - المسار الافتراضي: `C:\Program Files (x86)\HI-PRO`
   - يجب أن يحتوي على:
     - `HiProWrapper.dll`
     - `ftd2xx.dll`
     - `HIP32402.dll`
     - `FTChipID.dll`
     - `SharedUtilities.dll`
     - `Swboxw32.dll`
     - مجلد `Usb Driver` مع تعريفات USB

2. **إضافة المسار إلى System PATH**
   - **مهم جداً:** SDK Readme يذكر صراحة: "add the following to your **system path** (found in the environment variables)"
   - يجب إضافة `C:\Program Files (x86)\HI-PRO` إلى **متغير بيئة النظام (System PATH)** وليس فقط User PATH
   - **ملاحظة:** التطبيق يضيف المسار إلى process PATH تلقائياً عند التشغيل، لكن SDK قد يحتاج System PATH عند تحميل الوحدات لأول مرة (خاصة عند استدعاء `GetProductManagerInstance()`)
   - **خطوات الإضافة:**
     1. افتح "System Properties" → "Environment Variables"
     2. في "System variables" (وليس User variables)، ابحث عن `Path`
     3. اضغط "Edit" → "New"
     4. أضف: `C:\Program Files (x86)\HI-PRO`
     5. اضغط "OK" في جميع النوافذ
     6. **أعد تشغيل التطبيق** (مهم! - التطبيق يقرأ PATH عند بدء التشغيل)

3. **CTK Runtime** (مطلوب للمبرمجات السلكية)

**الحالة المتوقعة عند الفحص:**
- إذا كان التعريف مثبتاً والجهاز غير متصل: `Driver OK, hardware not connected`
- إذا كان التعريف غير مثبت أو PATH غير مضاف: `E_UNKNOWN_NAME: The name specified is not recognized`

**استكشاف الأخطاء لـ E_UNKNOWN_NAME:**
1. تأكد من وجود المجلد `C:\Program Files (x86)\HI-PRO`
2. تأكد من وجود `HiProWrapper.dll` داخل المجلد
3. تأكد من إضافة المسار إلى **System PATH** (وليس User PATH فقط)
4. **أعد تشغيل التطبيق** بعد تعديل PATH
5. تأكد من تثبيت CTK Runtime
6. تأكد من تثبيت VC++ 2010 و 2022 Redistributables

**HI-PRO على منفذ COM5 أو أعلى (مثلاً COM6) ولا يُكتشف:**
وحدة HI-PRO في الـ SDK/CTK قد تمسح فقط **COM1–COM4**. إذا كان الجهاز يظهر في Device Manager على منفذ أعلى (مثلاً COM6):
1. تأكد من إغلاق أي برنامج آخر يستخدم HI-PRO (مثل HI-PRO Configuration أو برنامج الشركة).
2. افتح **Device Manager** → **Ports (COM & LPT)** → اختر **HI-PRO (COMx)** → **Properties** → **Port Settings** → **Advanced**.
3. عيّن **COM Port Number** إلى أحد المنافذ **COM1–COM4** إن كان متاحاً.
4. أعد تشغيل التطبيق وأعد تشغيل الفحص.
*ملاحظة: ليست كل تعريفات USB-COM تسمح بتغيير رقم المنفذ؛ إن لم يظهر الخيار فالتعريف لا يدعم ذلك.*

### 3.3 Promira

**المتطلبات:**
- CTK Runtime مثبت
- VC++ 2010 Redistributable مثبت
- تعريف Promira مثبت (من TotalPhase™)
- الجهاز متصل عبر USB
- الجهاز قيد التشغيل
- **ملاحظة:** عند توصيل Promira لأول مرة على منفذ USB معين، انتظر دقيقتين للسماح بالتسجيل في Device Manager

**الحالة المتوقعة عند الفحص:**
- إذا كان التعريف مثبتاً والجهاز غير متصل: `Driver OK, hardware not connected`
- إذا كان التعريف غير مثبت: خطأ في `CreateCommunicationInterface`

---

## 4. ترتيب التهيئة (Initialization Order)

التطبيق يفحص **HI-PRO أولاً** (قبل CAA و Promira) حتى تبقى حالة الـ CTK نظيفة، ثم يجرّب المسار الافتراضي (USB) ثم منافذ COM عند الحاجة. راجع أيضاً التوثيق الرسمي **HI-PRO Configuration Help** (متطلبات COM1–COM4 وعدم استخدام تطبيق آخر لـ HI-PRO).

التطبيق يتبع الترتيب التالي عند فحص المبرمجين:

```
1. InitializeSdkServices()
   ├─ SdkConfiguration.SetupEnvironment()
   │  ├─ SetDllDirectory(HI-PRO path)  // إضافة HI-PRO إلى DLL search path
   │  ├─ Set SD_CONFIG_PATH
   │  ├─ Add HI-PRO to PATH
   │  ├─ Add CTK to PATH (if exists)
   │  └─ Add app directory to PATH
   │
   ├─ SDLibMain.GetProductManagerInstance()
   │  └─ SDK يقرأ sd.config ويحمل الوحدات المتاحة
   │
   ├─ LoadLibraryFromFile(E7160SL.library)
   │
   └─ CreateProduct()

2. ProgrammerScanner.ScanForWiredProgrammersAsync()
   ├─ LogEnvironmentDiagnostics()  // تسجيل معلومات البيئة
   └─ لكل مبرمج (بالترتيب: HI-PRO ثم CAA ثم Promira):
      ├─ HI-PRO: default/USB أولاً ثم منافذ COM إن لزم
      ├─ CAA / Promira: CreateCommunicationInterface(programmerName, port, "")
      └─ CheckDevice()  // التحقق من وجود الجهاز
```

---

## 5. التحقق من المتطلبات قبل الفحص

### 5.1 فحص يدوي

قبل تشغيل الفحص، تأكد من:

- [ ] VC++ 2022 Redistributable (x86) مثبت
- [ ] CTK Runtime مثبت (`C:\Program Files (x86)\CTK` أو `C:\Program Files\CTK` موجود)
- [ ] VC++ 2010 Redistributable (x86) مثبت
- [ ] HI-PRO driver مثبت (`C:\Program Files (x86)\HI-PRO` موجود)
- [ ] HI-PRO في System PATH (تحقق من Environment Variables)
- [ ] ملفات SDK موجودة في `bin/Debug/net10.0-windows/`:
  - [ ] `sd.config`
  - [ ] `sdnet.dll`
  - [ ] `E7160SL.library`
  - [ ] جميع DLLs المطلوبة

### 5.2 فحص تلقائي (في نتائج الفحص)

التطبيق يعرض معلومات التشخيص التالية في نافذة "No Programmers Found":

- **Architecture:** 64-bit أو 32-bit
- **HI-PRO Path:** المسار المستخدم
- **HI-PRO Path Exists:** True/False
- **PATH has HI-PRO:** True/False
- **CTK Runtime Installed:** True/False (مطلوب للمبرمجات السلكية)
- **SD_CONFIG_PATH:** المسار المحدد
- **SD_CONFIG exists:** True/False

---

## 6. حل المشاكل الشائعة

### المشكلة: E_UNKNOWN_NAME لـ HI-PRO

**الأسباب المحتملة:**
1. HI-PRO غير مضاف إلى **System PATH** (SDK Readme يطلب System PATH صراحة)
2. CTK Runtime غير مثبت (مطلوب لجميع المبرمجات السلكية)
3. VC++ Redistributables غير مثبتة
4. SDK لم يتمكن من تحميل وحدة HI-PRO

**الحلول:**
1. أضف `C:\Program Files (x86)\HI-PRO` إلى **System PATH** (في System variables، وليس User variables)
2. أعد تشغيل التطبيق بعد تعديل PATH (مهم جداً)
3. تأكد من تثبيت CTK Runtime (`CTKRuntime64.msi` من `redistribution/CTK`)
4. تأكد من تثبيت VC++ 2010 SP1 (x86) و VC++ 2022 (x86) Redistributables
5. تحقق من وجود `HiProWrapper.dll` في مجلد HI-PRO
6. تحقق من أن HI-PRO driver (v4.02+) مثبت بشكل صحيح

### المشكلة: "Driver OK, hardware not connected"

**المعنى:** التعريف مثبت بشكل صحيح، لكن الجهاز غير متصل أو غير مكتشف.

**الحلول:**
1. تأكد من توصيل المبرمج عبر USB
2. تأكد من تشغيل المبرمج (مضاء)
3. تحقق من Device Manager للتأكد من اكتشاف الجهاز
4. جرب منفذ USB آخر
5. أعد تشغيل المبرمج

### المشكلة: فشل تحميل SDK

**الأسباب المحتملة:**
1. `sd.config` غير موجود
2. `E7160SL.library` غير موجود
3. DLLs مفقودة
4. VC++ 2022 Redistributable غير مثبت

**الحلول:**
1. تأكد من نسخ جميع ملفات SDK إلى مجلد الإخراج
2. تحقق من وجود `sd.config` و `E7160SL.library`
3. تأكد من تثبيت VC++ 2022 Redistributable (x86)

---

## 7. ملاحظات مهمة

1. **System PATH vs Process PATH:**
   - **SDK Readme يطلب System PATH صراحة:** "add the following to your **system path** (found in the environment variables)"
   - التطبيق يضيف HI-PRO إلى process PATH تلقائياً عند التشغيل (في `SdkConfiguration.SetupEnvironment()`)
   - لكن SDK قد يحتاج System PATH عند تحميل الوحدات لأول مرة (خاصة عند `GetProductManagerInstance()`)
   - **الحل الموصى به:** أضف HI-PRO إلى System PATH دائماً، حتى لو كان التطبيق يضيفه إلى process PATH
   - **السبب:** SDK قد يقرأ PATH من النظام عند التهيئة الأولى قبل أن يتم تعديل process PATH

2. **إعادة التشغيل:**
   - بعد تعديل System PATH، يجب إعادة تشغيل التطبيق
   - التطبيق يقرأ PATH عند بدء التشغيل

3. **CTK مطلوب:**
   - جميع المبرمجات السلكية (CAA, HI-PRO, Promira) تحتاج CTK Runtime
   - بدون CTK، قد تفشل عملية `CreateCommunicationInterface`

4. **ترتيب التهيئة:**
   - يجب تهيئة SDK قبل الفحص
   - التطبيق يقوم بذلك تلقائياً عند الضغط على "Search Programmers"

---

## 8. المراجع والوثائق

### وثائق SDK الأساسية

- **SDK Readme**: `SoundDesignerSDK/SDK Readme.html`
  - المتطلبات الأساسية
  - إعداد المبرمجات
  - معلومات التثبيت

### وثائق البرمجة والـ API

- **Programmers Guide**: `SoundDesignerSDK/documentation/sounddesigner_programmers_guide.pdf`
  - دليل شامل لاستخدام المبرمجات
  - تفاصيل حول كل نوع مبرمج
  - استكشاف الأخطاء وإصلاحها

- **API Reference**: `SoundDesignerSDK/documentation/sounddesigner_api_reference.pdf`
  - مرجع كامل لـ API
  - تفاصيل الدوال والواجهات
  - أمثلة الاستخدام

### وثائق المنتجات

- **Ezairo 7160 SL Firmware Bundle**: `SoundDesignerSDK/documentation/Ezairo_7160_SL_firmware_bundle_user_reference.pdf`
  - مرجع البرامج الثابتة لـ Ezairo 7160 SL
  - معلومات التحديث والإدارة

- **Ezairo 7111 V2 Firmware Bundle**: `SoundDesignerSDK/documentation/Ezairo_7111_V2_firmware_bundle_user_reference.pdf`
  - مرجع البرامج الثابتة لـ Ezairo 7111 V2
  - معلومات التحديث والإدارة

### وثائق التطبيقات المحمولة

- **Mobile Getting Started**: `SoundDesignerSDK/documentation/sound_designer_mobile_getting_started.pdf`
  - دليل البدء السريع للتطبيقات المحمولة
  - إعداد iOS و Android

---

**ملاحظة:** جميع ملفات PDF موجودة في `SoundDesignerSDK/documentation/`. لمراجعة تفاصيل محددة حول المبرمجات أو API، راجع `sounddesigner_programmers_guide.pdf` و `sounddesigner_api_reference.pdf`.

---

**آخر تحديث:** 2026-02-08
