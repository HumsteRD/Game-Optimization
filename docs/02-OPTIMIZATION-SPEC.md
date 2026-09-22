# VELOCITY — Спецификация оптимизации (ядро проекта)

Это главный документ. Всё остальное — обёртка вокруг него.

---

## 1. Схема твика

Каждый твик — **данные**, а не код. Файл JSON, загружается с сервера, движок исполняет. Это позволяет чинить и добавлять твики без переустановки приложения.

```jsonc
{
  "id": "gpu.nvidia.low_latency_mode",
  "version": 3,
  "category": "gpu",
  "title": { "ru": "Режим низкой задержки NVIDIA", "en": "NVIDIA Low Latency Mode" },
  "description": {
    "ru": "Ограничивает очередь кадров, подготовленных CPU для GPU. Снижает input lag на 5–20 мс при GPU-bound нагрузке. Аналог NVIDIA Reflex для игр без его поддержки.",
    "en": "..."
  },

  // Оценки — честные, с источником
  "impact": {
    "fps_avg": "none",           // none | low | medium | high
    "fps_1percent": "low",
    "input_latency": "high",
    "stutter": "none",
    "confidence": "measured"     // measured | documented | community | experimental
  },
  "risk": "safe",                // safe | moderate | advanced | expert
  "reversible": true,
  "requires_reboot": false,
  "requires_game_closed": true,
  "anticheat_safe": true,

  // Когда твик вообще применим
  "requires": {
    "gpu.vendor": "nvidia",
    "gpu.driver_version": ">=456.38",
    "os.build": ">=19041"
  },
  "conflicts": ["gpu.nvidia.max_prerendered_frames"],

  // Определить текущее состояние
  "detect": {
    "type": "nvapi_profile",
    "setting": "OGL_CPL_PREFER_DXPRESENT_MODE",
    "profile": "_GLOBAL_DRIVER_PROFILE"
  },

  // Что делаем
  "apply": [
    { "type": "nvapi_profile", "setting": "PRERENDERLIMIT", "value": 1 },
    { "type": "nvapi_profile", "setting": "VSYNCSMOOTHAFR", "value": 0 }
  ],
  "revert": [
    { "type": "nvapi_profile_reset", "setting": "PRERENDERLIMIT" }
  ],
  "verify": { "type": "nvapi_profile", "setting": "PRERENDERLIMIT", "equals": 1 },

  "sources": [
    "https://developer.nvidia.com/blog/reflex-low-latency-platform/"
  ],
  "telemetry_key": "nv_lowlat"
}
```

### Типы действий (`apply` / `revert`)

| Тип | Описание |
|---|---|
| `registry` | path, name, kind (DWORD/QWORD/SZ/MULTI_SZ/BINARY), value |
| `registry_delete` | удаление значения (revert умеет восстановить) |
| `powercfg` | scheme/subgroup/setting GUID + AC/DC value |
| `service` | имя службы + StartMode (авто/ручной/отключено) — **никогда не удаляем службы** |
| `scheduled_task` | включить/выключить задачу планировщика |
| `nvapi_profile` | настройка драйверного профиля NVIDIA (глобального или per-app) |
| `adl` | настройка AMD через ADL |
| `display_mode` | ChangeDisplaySettingsEx — частота/разрешение/битность |
| `device_property` | setupapi: параметры устройства (MSI mode, USB power) |
| `file_config` | правка .ini/.cfg/.json игры (с бэкапом оригинала) |
| `command` | вызов из **whitelist** (powercfg.exe, bcdedit.exe, netsh.exe, dism.exe) — произвольные команды запрещены |
| `defender_exclusion` | добавить/убрать исключение папки |
| `mmcss` | параметры Multimedia Class Scheduler |
| `process_rule` | правило приоритета/affinity для exe (применяется агентом при запуске игры) |

### Уровни риска

| Уровень | Смысл | Поведение UI |
|---|---|---|
| `safe` | Обратимо, ничего не ломает, не влияет на безопасность | Включено в пресете «Безопасно», применяется без вопросов |
| `moderate` | Обратимо, но может изменить поведение системы (звук, сон, экономия батареи) | Требует галочки, есть предупреждение |
| `advanced` | Требует перезагрузки, меняет системные подсистемы | Отдельная вкладка, явное согласие |
| `expert` | Компромисс безопасности или стабильности (HVCI, VBS, Spectre-митигации) | Модальное окно с полным объяснением последствий, чекбокс «я понимаю» |

### Уровни уверенности (`confidence`)

- `measured` — мы сами замерили эффект на ≥3 конфигурациях
- `documented` — задокументировано вендором (Microsoft/NVIDIA/AMD/Intel)
- `community` — воспроизводимо по отзывам, но без строгих замеров
- `experimental` — гипотеза, доступно только в Advanced, помечено в UI

**Правило продукта:** в дефолтный пресет попадают только `measured` и `documented`.

---

## 2. Каталог твиков

### 2.1 Дисплей и вывод изображения (максимальный ROI, минимальный риск)

| ID | Что делаем | Импакт | Риск |
|---|---|---|---|
| `display.max_refresh` | Выставить максимальную поддерживаемую частоту обновления (`ChangeDisplaySettingsEx` с `DM_DISPLAYFREQUENCY`). **У ~15% людей 144/165/240 Гц монитор работает на 60 Гц.** | 🔥 огромный | safe |
| `display.color_depth` | 8bpc + Full RGB вместо Limited/YCbCr (частая проблема при HDMI-подключении к телевизору) | визуал | safe |
| `display.vrr_check` | Проверка G-Sync/FreeSync: включён ли, и правильная ли связка (G-Sync ON + V-Sync ON в панели драйвера + внутриигровой лимит FPS на 3 ниже макс. Гц + Reflex) | 🔥 стоттеры | safe |
| `display.multi_monitor_sync` | Мониторы с разной частотой обновления вызывают микрофризы на части GPU. Детект + рекомендация. | средний | safe |
| `display.hdr_advisory` | Auto HDR + не-HDR монитор = потери. Детект неверной конфигурации. | низкий | safe |
| `display.dsr_off` | Отключить DSR/VSR-факторы, если не используются (влияют на список разрешений) | нет | safe |
| `display.cable_bandwidth` | Детект: 165 Гц монитор на HDMI 1.4 → максимум 60–75 Гц. Совет сменить кабель/порт. | 🔥 огромный | info |

> **Топ-1 находка приложения.** Это то, ради чего человек скажет «эта штука окупилась за первые 10 секунд».

---

### 2.2 Питание и CPU

| ID | Что делаем | Импакт | Риск |
|---|---|---|---|
| `power.ultimate_plan` | `powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61` (Ultimate Performance), либо создать собственную схему «Velocity Gaming» | средний | safe |
| `power.core_parking_off` | `SUB_PROCESSOR` → `0cc5b647-c1df-4637-891a-dec35c318583` (CPMINCORES) = 100 | средний (заметный на старых Intel/Ryzen 1000–3000) | safe |
| `power.pcie_aspm_off` | `SUB_PCIEXPRESS` → ASPM = Off. Убирает микро-задержки при переходах линка. | низкий-средний | safe |
| `power.usb_selective_suspend_off` | `SUB_USB` → USB selective suspend = Disabled. **Важно для мыши/клавиатуры.** | средний (input) | safe |
| `power.hdd_never_sleep` | Диски не засыпают — убирает фризы при подгрузке уровней | средний | safe |
| `power.min_processor_state` | ⚠️ **НЕ ставим 100% на современных CPU.** На Ryzen 5000+/Intel 12+ это ломает boost-алгоритмы и повышает температуры → ниже реальные частоты. Ставим 100% только для CPU до Ryzen 3000 / Intel 10th. Правило в `requires`. | зависит | moderate |
| `cpu.hybrid_pcore_affinity` | **Intel 12/13/14/Core Ultra:** старые игры и тайтлы с устаревшим DRM (Denuvo до 2021) улетают на E-cores → −30% FPS. Правило affinity на P-cores для конкретных exe. | 🔥 высокий (для затронутых игр) | moderate |
| `cpu.x3d_ccd_pin` | **Ryzen 7950X3D/7900X3D/9950X3D:** пиннинг игры на CCD с 3D V-Cache. Проверка, что установлен AMD Chipset Driver + Xbox Game Bar (они делают это автоматически), иначе — свой affinity-профиль. | 🔥 очень высокий (до +25%) | moderate |
| `cpu.smt_advisory` | Только совет. SMT/HT отключать почти всегда вредно на современных CPU. Помечаем как **миф**. | — | info |
| `cpu.priority_profile` | Приоритет `High` (не `Realtime`!) для игрового процесса. `Realtime` = фризы ввода и звука. | низкий | safe |

---

### 2.3 GPU и драйвер

#### NVIDIA (через NVAPI, аналогично NVIDIA Profile Inspector)

| ID | Настройка | Значение | Импакт |
|---|---|---|---|
| `gpu.nv.power_mode` | Power Management Mode | Prefer Maximum Performance | низкий на десктопе, **вредит ноутам** (нагрев, троттлинг). Условие: `system.type != laptop` |
| `gpu.nv.low_latency` | Low Latency Mode | On / Ultra | 🔥 input lag −5..20 мс при GPU-bound |
| `gpu.nv.texture_quality` | Texture Filtering Quality | High Performance | +1..4% FPS, минимальная потеря качества |
| `gpu.nv.trilinear_opt` | Trilinear Optimization | On | +1..2% |
| `gpu.nv.aniso_sample_opt` | Anisotropic Sample Optimization | On | +1..3% |
| `gpu.nv.shader_cache` | Shader Cache Size | 10 GB / Unlimited | 🔥 сильно снижает шейдерные стоттеры (UE4/UE5) |
| `gpu.nv.threaded_opt` | Threaded Optimization | Auto (не On! On ломает часть игр) | зависит |
| `gpu.nv.vsync_off` | Vertical Sync | Off (глобально; включаем только в связке с G-Sync) | latency |
| `gpu.nv.rebar_force` | Resizable BAR — принудительно для игр вне вайтлиста NVIDIA (`rBarFeature`/`rBarSizeLimit`/`rBarOptions` в профиле) | до +10% в отдельных играх, **иногда минус** → только с бенчмарком до/после | advanced |
| `gpu.nv.dlss_override` | Подмена версии DLSS DLL на актуальную (NVIDIA App это уже умеет) | качество картинки | moderate |

#### AMD (через ADL / реестр драйвера)

| ID | Настройка | Значение |
|---|---|---|
| `gpu.amd.anti_lag` | Radeon Anti-Lag | Enabled (⚠️ Anti-Lag+ в 2023 вызывал баны в CS2 — **только штатный Anti-Lag**) |
| `gpu.amd.texture_quality` | Texture Filtering Quality | Performance |
| `gpu.amd.surface_opt` | Surface Format Optimization | On |
| `gpu.amd.tessellation` | Tessellation Mode | Override, x8 (для старых игр с оверкиллом) |
| `gpu.amd.shader_cache` | Shader Cache | AMD Optimized |
| `gpu.amd.smart_access_memory` | Проверка SAM (детект, включается в BIOS) | info |

#### Intel Arc / iGPU

- Проверка версии драйвера (Arc-драйверы дают огромные приросты между версиями)
- ReBAR **обязателен** для Arc — если выключен, отчёт кричит об этом
- iGPU: проверка выделенной памяти, гибридная графика на ноутах — какой GPU реально рендерит игру

#### Общее

| ID | Что делаем | Импакт | Риск |
|---|---|---|---|
| `gpu.hags` | Hardware-Accelerated GPU Scheduling: `HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers` → `HwSchMode` = 2 (вкл) / 1 (выкл) | зависит от системы → **обязательный A/B-бенчмарк** | advanced (reboot) |
| `gpu.driver_version` | Детект устаревшего драйвера + прямая ссылка (сами не качаем) | может быть 🔥 | info |
| `gpu.clean_install_advisory` | Совет по DDU при переходе AMD↔NVIDIA или при артефактах | — | info |
| `gpu.pcie_link_check` | Детект: GPU работает на x8/x4 или Gen2 вместо Gen4 (плохо вставлена, занят M.2-слот, райзер) | 🔥 огромный | info |
| `gpu.msi_mode` | Message Signaled Interrupts для GPU: `HKLM\SYSTEM\CurrentControlSet\Enum\PCI\<...>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties\MSISupported` = 1 | средний (DPC latency, frametime) | advanced (reboot) |

---

### 2.4 Windows: игровые подсистемы

| ID | Ключ | Значение | Комментарий |
|---|---|---|---|
| `win.gamedvr_off` | `HKCU\System\GameConfigStore\GameDVR_Enabled` | 0 | 🔥 Фоновая запись Game Bar реально ест кадры |
| `win.gamedvr_policy_off` | `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR\AllowGameDVR` | 0 | Тот же эффект на уровне политики |
| `win.gamebar_off` | `HKCU\Software\Microsoft\GameBar\UseNexusForGameBarEnabled` | 0 | Оверлей Xbox. ⚠️ **Не отключать на Ryzen X3D** — Game Bar нужен для CCD-детекта |
| `win.game_mode_on` | `HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled` | 1 | В Win11 Game Mode стал полезен (приоритизация, отсрочка обновлений) |
| `win.background_apps_off` | `HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\GlobalUserDisabled` | 1 | Отключает фоновые UWP |
| `win.mmcss_games` | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games` → `GPU Priority`=8, `Priority`=6, `Scheduling Category`="High", `SFIO Priority`="High" | — | Классика, документировано Microsoft |
| `win.system_responsiveness` | `...\Multimedia\SystemProfile\SystemResponsiveness` | 10 (не 0!) | 0 может задушить аудио и вызвать треск |
| `win.network_throttling_off` | `...\Multimedia\SystemProfile\NetworkThrottlingIndex` | 0xFFFFFFFF | Снимает лимит 10 пакетов/мс при активном мультимедиа |
| `win.fso_per_game` | Fullscreen Optimizations для конкретного exe через `HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers` → `DISABLEDXMAXIMIZEDWINDOWEDMODE` | зависит | ⚠️ Нюанс: в Win11 FSO часто **лучше** эксклюзивного фуллскрина (flip model, Auto HDR, VRR в окне). Не отключаем вслепую — только по профилю игры или после бенчмарка |
| `win.visual_effects` | Отключить анимации окон, тени, Aero Peek | косметика + чуть отзывчивее | safe |
| `win.transparency_off` | `HKCU\...\Themes\Personalize\EnableTransparency` = 0 | микро | safe |
| `win.notifications_focus` | Focus Assist в режиме «игра» | убирает фризы от всплывашек | safe |
| `win.storage_sense_off` | Storage Sense не запускается во время игры | стоттеры | safe |
| `win.au_active_hours` | Расширить «часы активности» Windows Update | предотвращает фоновые загрузки | safe |
| `win.p2p_updates_off` | `DeliveryOptimization\DODownloadMode` = 0 | сеть/пинг | safe |

---

### 2.5 Безопасность vs производительность (уровень `expert`)

Это самый большой источник реального прироста на новых системах — **и самый ответственный раздел**. Работаем строго с отдельным согласием и подробным объяснением.

| ID | Что | Реальный эффект | Что теряем |
|---|---|---|---|
| `sec.hvci_off` | Memory Integrity / Core Isolation: `HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity\Enabled` = 0 | **5–15% CPU-bound FPS**, особенно на Ryzen. Это самый крупный одиночный твик на Win11 | Защита от подмены драйверов в памяти. Для обычного домашнего ПК риск умеренный, но он есть |
| `sec.vbs_off` | Virtualization Based Security: `...\DeviceGuard\EnableVirtualizationBasedSecurity` = 0 | 3–10% | То же + ломает WSL2/Hyper-V/Docker/песочницу Windows. **Проверяем, не использует ли человек WSL/VM — если да, блокируем твик** |
| `sec.defender_exclusions` | Добавить папки игр/лаунчеров в исключения Defender | 🔥 Реально убирает стоттеры при загрузке ассетов | Практически ничего. **Это правильный способ, в отличие от отключения Defender** |
| `sec.spectre_meltdown` | Митигации Spectre/Meltdown (`FeatureSettingsOverride`) | 5–25% на CPU до 2019 года, ~0–3% на новых | Реальные уязвимости. **Только для CPU до Intel 9th / Ryzen 2000, только вручную, с большим предупреждением** |

**Правило UI:** каждый твик этого раздела показывает модальное окно с двумя колонками — «что получишь» и «что потеряешь» — и чекбоксом «Я понимаю и беру ответственность». Без галочки кнопка неактивна.

---

### 2.6 Мышь, клавиатура, ввод

| ID | Что делаем | Импакт |
|---|---|---|
| `input.mouse_accel_off` | `HKCU\Control Panel\Mouse`: `MouseSpeed`=0, `MouseThreshold1`=0, `MouseThreshold2`=0 + снять «Enhance pointer precision» | 🔥 Для соревновательных игр критично — мышечная память |
| `input.mouse_sensitivity_1to1` | `MouseSensitivity` = 10 (6/11 = 1:1 без масштабирования) | 🔥 То же |
| `input.mouse_queue_size` | `HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters\MouseDataQueueSize` = 50 (дефолт 100) | низкий, `community` |
| `input.kbd_queue_size` | `kbdclass\Parameters\KeyboardDataQueueSize` = 50 | низкий, `community` |
| `input.filter_keys_off` | Отключить залипание/фильтрацию клавиш (`HKCU\Control Panel\Accessibility\...`) | средний (пропуски нажатий) |
| `input.polling_measure` | **Реально измеряем polling rate**: подписка на Raw Input, гистограмма интервалов между сообщениями за 5 сек → «Заявлено 1000 Гц, фактически 512 Гц» | 🔥 Диагностика |
| `input.usb_port_advisory` | Детект: мышь на USB 2.0-хабе / порт разделён с гарнитурой / подключена через хаб монитора → потеря Hz. Совет: прямой порт на задней панели, желательно на чипсете, не на контроллере ASMedia | 🔥 |
| `input.usb_power_mgmt_off` | Снять «Разрешить отключение этого устройства для экономии энергии» со всех USB-хабов и HID (setupapi, per-device) | средний |
| `input.vendor_sdk_hint` | Обнаружить мышь по VID/PID (Logitech/Razer/Zowie/Glorious/Pulsar/Endgame) и подсказать: где выставить 1000/4000/8000 Гц, где выключить сглаживание/угловую привязку, оптимальный DPI (нативный шаг сенсора) | средний |
| `input.hz_warning` | ⚠️ Честное предупреждение: 4000/8000 Гц polling нагружает CPU (больше прерываний) и на слабых системах **снижает** FPS. Показываем замер FPS на 1000 vs 4000 Гц | честность |

> **Установку polling rate «программно» для любой мыши сделать нельзя** — это прошивка/вендорское ПО. Мы: измеряем фактическое, находим бутылочные горлышки (USB-хаб, энергосбережение, версия порта) и ведём по шагам. Это честнее и полезнее фейковой кнопки «Set 1000Hz».

---

### 2.7 Оперативная память и накопители

| ID | Что делаем | Импакт |
|---|---|---|
| `ram.xmp_advisory` | Эвристика: если текущая частота = базовой JEDEC (DDR4: 2133/2400/2666; DDR5: 4800/5600) и модули по SPD/парт-номеру поддерживают больше → **«XMP/EXPO выключен в BIOS»**. Инструкция под конкретного производителя матплаты со скриншотами | 🔥🔥 До +20% FPS. Встречается у ~35% сборок. Топ-находка |
| `ram.channel_check` | Детект одноканала при 2+ занятых слотах или 2 планок в неправильных слотах (A1/B1 вместо A2/B2) | 🔥 До +30% на iGPU, +5–15% на дискретке |
| `ram.pagefile` | Файл подкачки фиксированного размера на самом быстром SSD; не отключать полностью (краши в UE5); не держать на HDD | средний (стоттеры) |
| `ram.mem_compression` | Детект Memory Compression: при ≥32 ГБ можно отключить (`Disable-MMAgent -mc`), при ≤16 ГБ — **не трогать** | низкий, `moderate` |
| `ram.sysmain_advisory` | SysMain/Superfetch: на SSD эффект ~0. **Помечаем как миф**, не отключаем по умолчанию | — |
| `storage.game_drive` | Детект: игра на HDD, при этом на SSD есть место → предложить перенос (Steam умеет через «Переместить папку установки») | 🔥 время загрузок, стоттеры стриминга текстур |
| `storage.free_space` | <10% свободного на SSD → деградация записи и SLC-кэша | средний |
| `storage.nvme_driver` | Стандартный `stornvme` vs вендорский (Samsung NVMe Driver) — детект + совет | низкий |
| `storage.trim_check` | TRIM включён? `fsutil behavior query DisableDeleteNotify` | средний (долгосрочно) |
| `storage.directstorage` | Наличие рантайма DirectStorage + поддержка игрой | инфо |
| `storage.defrag_hdd` | Только для HDD, только по расписанию вне игры. На SSD — **никогда** | — |

---

### 2.8 Сеть (для онлайн-игр)

Важно: **это про пинг и стабильность, не про FPS.** Так и пишем в UI, не обманываем.

| ID | Что делаем | Импакт |
|---|---|---|
| `net.nagle_off` | Per-interface `HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}`: `TcpAckFrequency`=1, `TCPNoDelay`=1 | Средний для TCP-игр (WoW, старые MMO). **Ноль для игр на UDP (CS2, Valorant, Apex)** — честно указываем |
| `net.throttling_off` | `NetworkThrottlingIndex` = 0xFFFFFFFF (см. 2.4) | низкий-средний |
| `net.dns` | Замер латентности популярных DNS (Cloudflare 1.1.1.1, Google, Яндекс, провайдерский) → предложить лучший | загрузка сайтов, не пинг в игре |
| `net.adapter_power_off` | Снять энергосбережение с сетевого адаптера + отключить Green Ethernet / EEE | средний (спайки пинга) |
| `net.wifi_advisory` | Детект Wi-Fi → жёсткая рекомендация Ethernet, с замером jitter | 🔥 |
| `net.qos_gaming` | Политика QoS для игрового exe | низкий |
| `net.killer_advisory` | Killer/Rivet-сеть: их «оптимизирующий» софт часто вредит. Совет — оставить только драйвер | средний |
| `net.mtu_check` | Проверка MTU и наличия фрагментации | низкий |

---

### 2.9 Фоновые процессы и оверлеи

| ID | Что делаем | Импакт |
|---|---|---|
| `bg.overlay_stack` | **Недооценённый убийца frametime.** Детект одновременно активных оверлеев: Steam + Discord + GeForce/NVIDIA App + MSI Afterburner/RTSS + Xbox Game Bar + Wallpaper Engine + Razer/Corsair/iCUE. Каждый — хук в DXGI. 5 хуков = регулярные спайки. Рекомендуем оставить максимум 1–2 | 🔥 высокий по 1% low |
| `bg.discord_hwaccel` | Discord: аппаратное ускорение + оверлей — частая причина просадок на слабых GPU | средний |
| `bg.browser_advisory` | Chrome/Edge с 30 вкладками и аппаратным ускорением жрёт VRAM и GPU. Предложение закрывать/приостанавливать на время игры | средний-высокий |
| `bg.rgb_software` | iCUE/Synapse/Armoury Crate/GHUB — известные пожиратели CPU и источники DPC-спайков. Мягкая пауза службы на время игры (с восстановлением) | средний |
| `bg.startup_manager` | Менеджер автозагрузки с **оценкой влияния** (замер времени старта, а не «вкл/выкл» вслепую) | загрузка ОС |
| `bg.gaming_mode` | Одна кнопка «Игровой режим»: агент приостанавливает выбранные процессы/службы, включает Focus Assist, применяет power plan; при выходе из игры **всё возвращает** | 🔥 удобство + реальный эффект |
| `bg.telemetry_tasks` | Отключение части телеметрических задач планировщика. **Честно:** влияние на FPS ~0, влияние на случайные фоновые нагрузки — небольшое. Помечаем `low` | низкий |

---

### 2.10 Таймеры и планировщик (уровень `advanced`)

| ID | Что | Комментарий |
|---|---|---|
| `timer.resolution` | Запрос 0.5 мс через `NtSetTimerResolution`. **В Win11 22H2+ поведение изменено**: высокое разрешение действует только на запросивший процесс. Для глобального — `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\GlobalTimerResolutionRequests` = 1 | Влияет на плавность frametime в части игр. Эффект **нужно измерять**, а не заявлять. На ноутбуках повышает расход батареи |
| `timer.bcdedit_myths` | ❌ **НЕ делаем:** `useplatformclock true`, `disabledynamictick yes`, `tscsyncpolicy`, `bcdedit /set numproc` | Это карго-культ. На современных системах в лучшем случае нейтрально, часто **вредно** (`useplatformclock true` = принудительный HPET = хуже). Показываем в разделе «Мифы» и **умеем откатывать**, если человек уже применил это из чужого скрипта |

> **Фича «Детокс»:** сканируем систему на следы вредных твиков из популярных .bat-паков (`useplatformclock`, удалённые службы, `SystemResponsiveness=0`, отключённый Defender, `Priority=Realtime`, поломанный Windows Update) и предлагаем вернуть в норму. Отдельная сильная продающая фича — «почини то, что тебе сломал предыдущий "оптимизатор"».

---

### 2.11 Профили игр (движковые твики — здесь живут самые крупные приросты)

Формат `data/games/<slug>.json`. Стартовый набор — топ-30 тайтлов.

| Игра | Что крутим |
|---|---|
| **CS2** | `autoexec.cfg`: `fps_max`, `cl_disablehtmlmotd`, `engine_no_focus_sleep`, параметры запуска `-high -novid -nojoy`, `cl_updaterate/cmdrate`, Video.txt (тени, MSAA, шейдеры) |
| **Valorant** | `GameUserSettings.ini`: качество текстур/деталей/эффектов, отключение виньетки, `bUseVSync=False`, Raw Input Buffer вкл |
| **Fortnite** | `GameUserSettings.ini`: Performance Mode, DX12 vs DX11 по железу, отключение motion blur, `bDisableSSR` |
| **Apex Legends** | `videoconfig.txt` + `autoexec.cfg`: `fps_max`, `cl_gib_allow 0`, `mat_letterbox_aspect_goal 0`, параметры запуска `+exec autoexec -high -preload` |
| **UE4/UE5-игры (общее)** | `Engine.ini`: `r.Streaming.PoolSize` под VRAM, `r.ShaderPipelineCache.Enabled=1`, `r.VolumetricFog=0`, `r.MotionBlurQuality=0`, `r.Streaming.HLODStrategy`, отключение TAA-размытия через `r.TemporalAACurrentFrameWeight` |
| **Unity-игры** | `boot.config`, `gfx-enable-gfx-jobs`, ключи `-force-d3d11` / `-force-vulkan` для конкретных тайтлов |
| **Dota 2 / Source 2** | Аналогично CS2 |
| **GTA V / RDR2** | `settings.xml`, отключение MSAA-оверкилла, `Extended Distance Scaling` |
| **Minecraft** | JVM-аргументы (`-XX:+UseG1GC`, размер хипа под RAM), рекомендация Sodium/Fabric |
| **PUBG / Rust / Tarkov** | Свои .ini + приоритеты процессов + предзагрузка |

**Универсальные механики профиля:**
- Выбор API рендеринга по железу (DX11 vs DX12 vs Vulkan — на старых GPU DX11 часто быстрее)
- Подбор FPS-лимита: при G-Sync → `refresh − 3`; без VRR → `refresh` или `refresh × 2`
- Рекомендация апскейлера: DLSS (NVIDIA RTX) / FSR (AMD/универсально) / XeSS (Arc) с подбором пресета под целевой FPS
- **Всегда бэкапим оригинальный конфиг** и умеем откатить в один клик

---

### 2.12 Диагностика (отдельная ценность, продаёт сама себя)

| Модуль | Что делает |
|---|---|
| **DPC Latency Analyzer** | ETW-трассировка DPC/ISR по драйверам. Результат: «Драйвер `Netwtw10.sys` (Intel Wi-Fi) — максимальная задержка 3 100 мкс, 47 событий за 60 сек. Это причина твоих микрофризов.» + ссылка на актуальный драйвер. **Это то, за что люди платят отдельно.** |
| **Thermal / Power Throttle Detector** | Детект троттлинга: температуры, PL1/PL2, TDP-лимит на ноуте, деградация boost-частот под нагрузкой. Вывод: «CPU упирается в 95 °C через 4 мин, частота падает с 4.9 до 3.6 ГГц → чистка/термопаста/андервольт» |
| **Stutter Hunter** | Корреляция спайков frametime с событиями: сборка мусора, page fault, вход/выход процесса, DPC, шейдерная компиляция. Тайм-лайн «что произошло в момент фриза» |
| **Bottleneck Analyzer** | По логам загрузки CPU/GPU/VRAM/RAM за бенчмарк: «GPU 99%, CPU 40% → упор в видеокарту, снижай разрешение или включи DLSS» / «CPU 100% на одном ядре → упор в однопоток» |
| **Health Report** | Здоровье SSD (SMART), возраст драйверов, целостность системных файлов (`sfc`), ошибки WHEA в журнале, битые сектора, нестабильный разгон (WHEA-Logger 18/19) |
| **Baseline Compare** | Сравнение с облачной базой: «Ryzen 5 5600 + RTX 4060 в CS2 медиана 312 FPS. У тебя 198. Топ-3 отличия от типовой конфигурации: …» |

---

## 3. Раздел «Мифы» (обязательная фича)

Публикуем прямо в приложении и на сайте. Это одновременно маркетинг, обучение и защита от негатива.

| Миф | Реальность |
|---|---|
| «Очистка RAM даёт FPS» | Вредно. Выгружает нужные страницы, следующий доступ = page fault = фриз |
| «Отключи SysMain/Superfetch» | На SSD эффект ~0. Может замедлить запуск приложений |
| `bcdedit /set useplatformclock true` | Принудительный HPET. На современном железе **хуже**. Дефолт (invariant TSC) правильный |
| `disabledynamictick yes` | Мешает энергосбережению, эффекта на FPS нет |
| «Отключи Defender ради FPS» | Вместо этого — исключения папок игр. Тот же эффект, без дыры в безопасности |
| «Realtime-приоритет игре» | Душит ввод и аудио-поток → треск звука и лаги мыши. Максимум `High` |
| «Отключи страничный файл» | Краши в UE5 и играх с большим виртуальным адресным пространством |
| «Отключи HT/SMT» | Почти всегда −FPS на современных CPU |
| «Дефрагментация SSD» | Изнашивает накопитель, эффекта ноль |
| «Разгони GPU через наш софт» | Разгон — индивидуален и рискован. Мы даём **советы и профиль для Afterburner**, но не крутим сами |
| «MSI-режим для всех устройств» | Для GPU — полезно. Для всего подряд — можно получить неработающий USB/звук |
| «Отключи все службы Windows» | Часть из них нужна играм и лаунчерам. Мы работаем со списком безопасных, и **не удаляем службы, а переводим в ручной запуск** |

---

## 4. Пресеты

| Пресет | Состав | Для кого |
|---|---|---|
| 🟢 **Безопасный** (по умолчанию) | Только `safe`, без перезагрузки, `confidence` ≥ documented | Все. Это то, что применяется по кнопке «Оптимизировать» |
| 🔵 **Игровой** | + `moderate`, + профиль игры, + Gaming Mode | Основная масса игроков |
| 🟣 **Соревновательный** | + input-твики, timer resolution, FPS-лимиты под VRR, минимум оверлеев | CS2/Valorant/Apex |
| 🟠 **Максимум** | + `advanced` (HAGS, MSI mode, ReBAR) с обязательным A/B-бенчмарком | Энтузиасты |
| 🔴 **Экстремальный** | + `expert` (HVCI/VBS) с явным согласием по каждому пункту | Понимающие риск |
| ⚪ **Ноутбук** | Отдельная логика: без Prefer Max Performance, учёт AC/DC, термо-лимиты, гибридная графика | Ноутбучники |
| 🧹 **Детокс** | Откат вредных твиков из чужих .bat-паков, возврат к здоровому состоянию | Пострадавшие |

---

## 5. Протокол применения (безопасность превыше всего)

```
1. Проверить, что игра не запущена  →  иначе предложить закрыть
2. Проверить, что античит не активен (Vanguard/EAC/BE как служба)
3. Создать точку восстановления системы (не чаще 1 раза в 24 ч)
4. Экспортировать затрагиваемые ветки реестра в snapshot/<ts>/
5. Записать состояние «до» в SQLite (каждый ключ отдельно)
6. Применять твики ПО ОДНОМУ:
      apply → verify → если verify не прошёл → revert этого твика → лог
7. Если требуется перезагрузка — сообщить и запланировать
8. Предложить бенчмарк «после»
9. Если дельта отрицательная или в пределах шума → предложить откат
```

**Каждое действие пишется в журнал**, журнал виден пользователю, экспортируется в файл для саппорта.

---

## 6. Метрики честности (внутренний контроль качества)

Собираем анонимно (с согласия) по каждому твику:
- % применений, завершившихся откатом
- средняя дельта FPS / 1% low до и после
- корреляция с конфигурацией железа

**Правило:** твик, у которого доля откатов > 15% или медианная дельта ≈ 0 на определённом железе, **автоматически исключается** из пресетов для этого железа через облачное обновление каталога.

Это делает продукт умнее с каждым пользователем — и это то, что невозможно скопировать конкурентам с .bat-скриптами.
