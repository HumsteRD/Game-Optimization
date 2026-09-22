using Velocity.Core;

namespace Velocity.Hardware;

/// <summary>
/// Превращает снимок железа в список находок. Это то, что пользователь видит бесплатно,
/// и главный аргумент к покупке (docs/01-PROJECT-PLAN.md).
///
/// Два правила, которые нельзя нарушать:
///  1. Не выдумываем проблему. Нет данных — нет находки (молчим, а не гадаем).
///  2. Не обещаем цифру, которой не измеряли. «Обычно +5–12 FPS», а не «+40% FPS».
/// </summary>
public static class FindingsEngine
{
    public static List<Finding> Analyze(HardwareReport r)
    {
        var findings = new List<Finding>();

        AnalyzeDisplays(r, findings);
        AnalyzeMemory(r, findings);
        AnalyzeStorage(r, findings);
        AnalyzeGpu(r, findings);
        AnalyzeWindows(r, findings);
        AnalyzePower(r, findings);
        AnalyzeInput(r, findings);
        AnalyzeBackground(r, findings);
        AnalyzeNetwork(r, findings);
        AnalyzeGames(r, findings);
        AnalyzeStartup(r, findings);
        AnalyzeDetox(r, findings);

        return [.. findings.OrderByDescending(f => f.Severity).ThenByDescending(f => f.ScoreWeight)];
    }

    // ─────────────────────────── Дисплей ───────────────────────────

    private static void AnalyzeDisplays(HardwareReport r, List<Finding> f)
    {
        foreach (var d in r.Displays)
        {
            var label = d.FriendlyName ?? d.DeviceName ?? "Монитор";

            // Топ-находка продукта: монитор способен на больше, чем Windows ему выставила.
            if (d.MaxRefreshAtCurrentResolution > d.RefreshHz + 1)
            {
                double ratio = (double)d.MaxRefreshAtCurrentResolution / Math.Max(d.RefreshHz, 1);
                f.Add(new Finding
                {
                    Id = "display.refresh_not_max",
                    Category = FindingCategory.Display,
                    Severity = ratio >= 1.5 ? FindingSeverity.Critical : FindingSeverity.Warning,
                    Title = $"{label}: {d.RefreshHz} Гц вместо {d.MaxRefreshAtCurrentResolution} Гц",
                    Detail = $"Монитор поддерживает {d.MaxRefreshAtCurrentResolution} Гц на текущем разрешении " +
                             $"{d.Width}×{d.Height}, но Windows выставила {d.RefreshHz} Гц. " +
                             $"Ты видишь в {ratio:0.0} раза меньше кадров, чем способен показать экран — " +
                             "даже если игра выдаёт 200 FPS.",
                    Recommendation = $"Выставить {d.MaxRefreshAtCurrentResolution} Гц",
                    FixTweakId = "display.max_refresh",
                    ExpectedGain = "Плавность картинки и заметно ниже задержка ввода",
                    ScoreWeight = 20
                });
            }

            // Частота выше доступна, но на другом разрешении — тут решать пользователю.
            else if (d.MaxRefreshAnyResolution > d.RefreshHz + 1)
            {
                f.Add(new Finding
                {
                    Id = "display.higher_hz_other_resolution",
                    Category = FindingCategory.Display,
                    Severity = FindingSeverity.Info,
                    Title = $"{label}: {d.MaxRefreshAnyResolution} Гц доступны на другом разрешении",
                    Detail = $"Сейчас {d.Width}×{d.Height} @ {d.RefreshHz} Гц. " +
                             $"На пониженном разрешении монитор выдаёт до {d.MaxRefreshAnyResolution} Гц. " +
                             "Это компромисс между чёткостью и плавностью — решение за тобой.",
                    ScoreWeight = 0
                });
            }

            if (d.BitsPerPixel is > 0 and < 32)
            {
                f.Add(new Finding
                {
                    Id = "display.color_depth",
                    Category = FindingCategory.Display,
                    Severity = FindingSeverity.Warning,
                    Title = $"{label}: глубина цвета {d.BitsPerPixel} бит",
                    Detail = "Режим ниже 32 бит даёт полосы на градиентах. Обычно это следствие " +
                             "кривого режима вывода или ограничений кабеля.",
                    Recommendation = "Выставить 32 бита",
                    FixTweakId = "display.color_depth",
                    ScoreWeight = 4
                });
            }
        }

        // Разная частота у мониторов — известный источник микрофризов на части GPU.
        var refreshRates = r.Displays.Where(d => d.RefreshHz > 0).Select(d => d.RefreshHz).Distinct().ToList();
        if (refreshRates.Count > 1)
        {
            f.Add(new Finding
            {
                Id = "display.mixed_refresh",
                Category = FindingCategory.Display,
                Severity = FindingSeverity.Warning,
                Title = $"Мониторы работают на разной частоте ({string.Join(" / ", refreshRates.Order())} Гц)",
                Detail = "Смешанные частоты обновления — известная причина микрофризов в играх: " +
                         "композитор вынужден синхронизировать разные тайминги. " +
                         "Особенно заметно, если на втором мониторе идёт видео или стрим.",
                Recommendation = "Выровнять частоту или отключать второй монитор на время игры",
                ScoreWeight = 6
            });
        }
    }

    // ─────────────────────────── Память ───────────────────────────

    private static void AnalyzeMemory(HardwareReport r, List<Finding> f)
    {
        var mem = r.Memory;
        if (mem.Modules.Count == 0) return;

        // Эвристика XMP/EXPO: частота на уровне базовой JEDEC.
        // Точно определить профиль SPD без чтения по SMBus нельзя, поэтому формулировка осторожная,
        // а порог разделён на два уровня: ниже базовой JEDEC — почти наверняка профиль выключен,
        // ровно на верхней ступени JEDEC — возможен честный комплект без разгона.
        (int Certain, int Possible) jedec = mem.Kind switch
        {
            MemoryKind.Ddr5 => (4800, 5600),
            MemoryKind.Ddr4 => (2400, 2666),
            MemoryKind.Ddr3 => (1333, 1600),
            _ => (0, 0)
        };

        if (jedec.Possible > 0 && mem.ConfiguredSpeedMhz > 0 && mem.ConfiguredSpeedMhz <= jedec.Possible
            && r.Machine.Chassis != ChassisKind.Laptop)
        {
            bool certain = mem.ConfiguredSpeedMhz <= jedec.Certain;
            f.Add(new Finding
            {
                Id = "ram.xmp_likely_off",
                Category = FindingCategory.HardwareBios,
                Severity = certain ? FindingSeverity.Critical : FindingSeverity.Warning,
                Title = $"Память работает на {mem.ConfiguredSpeedMhz} МГц — похоже, XMP/EXPO выключен",
                Detail = $"{mem.Kind} на {mem.ConfiguredSpeedMhz} МГц — это базовая частота JEDEC, " +
                         "то есть модули работают на «безопасном» минимуме, а не на своей паспортной скорости. " +
                         "Профиль XMP (Intel) или EXPO (AMD) включается одной галочкой в BIOS и обычно " +
                         "поднимает частоту в 1.3–1.5 раза. Для игр это одна из самых крупных бесплатных прибавок.",
                Recommendation = $"Зайти в BIOS ({r.Machine.BoardManufacturer ?? "материнская плата"}) → " +
                                 "включить профиль XMP / EXPO / DOCP",
                ExpectedGain = "Обычно +5–15% FPS, на встроенной графике заметно больше",
                ScoreWeight = certain ? 20 : 10
            });
        }

        // Одноканал при нескольких модулях = планки в слотах одного канала.
        if (mem.Modules.Count >= 2 && mem.Channels == 1)
        {
            f.Add(new Finding
            {
                Id = "ram.single_channel",
                Category = FindingCategory.HardwareBios,
                Severity = FindingSeverity.Critical,
                Title = "Память работает в одноканальном режиме",
                Detail = $"Установлено модулей: {mem.Modules.Count}, но все они в слотах одного канала " +
                         $"({string.Join(", ", mem.Modules.Select(m => m.Slot).Where(s => s is not null))}). " +
                         "Пропускная способность вдвое ниже возможной.",
                Recommendation = "Переставить планки через слот (обычно A2 и B2 — второй и четвёртый от процессора)",
                ExpectedGain = "Обычно +5–15% FPS, на встроенной графике до +30%",
                ScoreWeight = 18
            });
        }

        if (mem.Modules.Count == 1 && mem.TotalGb >= 8 && r.Machine.Chassis != ChassisKind.Laptop)
        {
            f.Add(new Finding
            {
                Id = "ram.single_module",
                Category = FindingCategory.HardwareBios,
                Severity = FindingSeverity.Warning,
                Title = "Установлен только один модуль памяти",
                Detail = "Одна планка = одноканальный режим. Вторая планка того же объёма даёт " +
                         "двухканальный режим и заметный прирост в играх — часто больше, чем апгрейд процессора.",
                Recommendation = "Добавить второй модуль такого же объёма",
                ExpectedGain = "Обычно +5–15% FPS",
                ScoreWeight = 10
            });
        }

        if (mem.TotalGb is > 0 and < 12)
        {
            f.Add(new Finding
            {
                Id = "ram.low_capacity",
                Category = FindingCategory.Memory,
                Severity = mem.TotalGb < 8 ? FindingSeverity.Critical : FindingSeverity.Warning,
                Title = $"Всего {mem.TotalGb} ГБ оперативной памяти",
                Detail = "Современные игры уверенно занимают 8–12 ГБ. При нехватке система начинает " +
                         "выгружать данные в файл подкачки — это и есть те самые фризы при загрузке локаций.",
                Recommendation = "Апгрейд до 16 ГБ (в двухканале)",
                ScoreWeight = mem.TotalGb < 8 ? 12 : 6
            });
        }

        if (mem.Pagefile.Disabled)
        {
            f.Add(new Finding
            {
                Id = "ram.pagefile_disabled",
                Category = FindingCategory.Memory,
                Severity = FindingSeverity.Warning,
                Title = "Файл подкачки отключён",
                Detail = "Популярный «твик» из .bat-паков, который ничего не ускоряет, зато роняет игры " +
                         "на Unreal Engine 5 и всё, что резервирует много виртуальной памяти.",
                Recommendation = "Включить файл подкачки на самом быстром SSD",
                FixTweakId = "ram.pagefile",
                ScoreWeight = 8
            });
        }
    }

    // ─────────────────────────── Накопители ───────────────────────────

    private static void AnalyzeStorage(HardwareReport r, List<Finding> f)
    {
        var system = r.Storage.FirstOrDefault(s => s.IsSystemDrive);

        if (system is { Kind: StorageKind.Hdd })
        {
            f.Add(new Finding
            {
                Id = "storage.system_on_hdd",
                Category = FindingCategory.Storage,
                Severity = FindingSeverity.Critical,
                Title = "Windows установлена на жёсткий диск",
                Detail = $"Системный диск — {system.Model} (HDD). Это узкое место всей системы: " +
                         "долгая загрузка, фризы при подгрузке текстур, медленный отклик. " +
                         "Переезд на SSD даёт больше ощутимой разницы, чем любая программная оптимизация.",
                Recommendation = "Перенести систему на SSD",
                ScoreWeight = 15
            });
        }

        // Флешки и внешние диски исключаем: «мало места на флешке» — не проблема производительности.
        foreach (var s in r.Storage.Where(s => s is { SizeGb: > 0, FreeGb: > 0, IsRemovable: false }))
        {
            if (s.FreePercent < 10)
            {
                f.Add(new Finding
                {
                    Id = "storage.low_free_space",
                    Category = FindingCategory.Storage,
                    Severity = s.FreePercent < 5 ? FindingSeverity.Critical : FindingSeverity.Warning,
                    Title = $"{s.Model}: свободно {s.FreeGb} ГБ из {s.SizeGb} ГБ ({s.FreePercent:0}%)",
                    Detail = s.Kind is StorageKind.Ssd or StorageKind.Nvme
                        ? "У SSD при заполнении выше 90% резко падает скорость записи: " +
                          "заканчивается SLC-кэш и негде выполнять сборку мусора."
                        : "Заполненный HDD сильнее фрагментируется и медленнее отдаёт данные.",
                    Recommendation = "Освободить место (цель — минимум 15% свободного)",
                    ScoreWeight = 8
                });
            }

            if (s is { Kind: StorageKind.Hdd, IsSystemDrive: false, DriveLetters.Count: > 0 })
            {
                f.Add(new Finding
                {
                    Id = "storage.games_maybe_on_hdd",
                    Category = FindingCategory.Storage,
                    Severity = FindingSeverity.Info,
                    Title = $"Есть жёсткий диск: {s.Model} ({string.Join(", ", s.DriveLetters)})",
                    Detail = "Если игры установлены сюда — время загрузки уровней и подгрузка текстур " +
                             "будут заметно хуже, чем на SSD. Стоит проверить, где лежат библиотеки Steam/Epic.",
                    Recommendation = "Перенести активные игры на SSD",
                    ScoreWeight = 0
                });
            }

            if (s.HealthStatus is "Warning" or "Unhealthy")
            {
                f.Add(new Finding
                {
                    Id = "storage.health",
                    Category = FindingCategory.Storage,
                    Severity = FindingSeverity.Critical,
                    Title = $"{s.Model}: состояние «{s.HealthStatus}»",
                    Detail = "Windows сообщает о проблемах с накопителем. Это не про производительность — " +
                             "это про сохранность данных.",
                    Recommendation = "Сделать резервную копию и проверить SMART",
                    ScoreWeight = 10
                });
            }
        }
    }

    // ─────────────────────────── GPU ───────────────────────────

    private static void AnalyzeGpu(HardwareReport r, List<Finding> f)
    {
        var gpu = r.Gpus.FirstOrDefault(g => !g.IsIntegrated) ?? r.Gpus.FirstOrDefault();
        if (gpu is null) return;

        if (gpu.Vendor == GpuVendor.Microsoft)
        {
            f.Add(new Finding
            {
                Id = "gpu.no_driver",
                Category = FindingCategory.Gpu,
                Severity = FindingSeverity.Critical,
                Title = "Драйвер видеокарты не установлен",
                Detail = "Система использует «Базовый видеоадаптер (Майкрософт)» — это заглушка без " +
                         "3D-ускорения. Игры либо не запустятся, либо будут рендериться программно.",
                Recommendation = "Установить драйвер с сайта производителя видеокарты",
                ScoreWeight = 25
            });
            return;
        }

        if (gpu.DriverDate is { } date)
        {
            var age = DateTime.Now - date;
            if (age.TotalDays > 365)
            {
                f.Add(new Finding
                {
                    Id = "gpu.driver_outdated",
                    Category = FindingCategory.Gpu,
                    Severity = age.TotalDays > 730 ? FindingSeverity.Warning : FindingSeverity.Info,
                    Title = $"Драйвер видеокарты от {date:MM.yyyy} (больше {Plural.Months((int)(age.TotalDays / 30))} назад)",
                    Detail = $"Установлена версия {gpu.VendorDriverVersion ?? gpu.DriverVersion}. " +
                             "Свежие драйверы регулярно дают прирост в новых играх и чинят стоттеры — " +
                             "особенно это касается Intel Arc и новых поколений AMD.",
                    Recommendation = "Обновить драйвер с сайта производителя",
                    ScoreWeight = 5
                });
            }
        }

        if (r.Gpus.Count(g => !g.IsIntegrated) > 0 && r.Gpus.Any(g => g.IsIntegrated)
            && r.Machine.Chassis == ChassisKind.Laptop)
        {
            f.Add(new Finding
            {
                Id = "gpu.hybrid_laptop",
                Category = FindingCategory.Gpu,
                Severity = FindingSeverity.Info,
                Title = "Гибридная графика: встроенная + дискретная",
                Detail = "На ноутбуках игры регулярно запускаются на встроенном видеоядре вместо дискретной карты — " +
                         "это мгновенная потеря в разы. Стоит проверить, какой GPU реально рендерит игру, " +
                         "и при возможности включить MUX-переключатель на дискретную.",
                Recommendation = "Проверить назначение GPU для игр и настройки MUX",
                ScoreWeight = 0
            });
        }

        if (gpu.VramGb is > 0 and <= 4 && !gpu.IsIntegrated)
        {
            f.Add(new Finding
            {
                Id = "gpu.low_vram",
                Category = FindingCategory.Gpu,
                Severity = FindingSeverity.Info,
                Title = $"{gpu.VramGb} ГБ видеопамяти",
                Detail = "Современные игры на высоких текстурах выходят за 6–8 ГБ. При нехватке VRAM " +
                         "данные начинают ходить через системную память — это резкие просадки 1% low, " +
                         "а не плавное снижение FPS.",
                Recommendation = "Снизить качество текстур, включить апскейлер (DLSS/FSR/XeSS)",
                ScoreWeight = 0
            });
        }
    }

    // ─────────────────────────── Windows ───────────────────────────

    private static void AnalyzeWindows(HardwareReport r, List<Finding> f)
    {
        var t = r.Tuning;

        if (t.GameDvrEnabled == true)
        {
            f.Add(new Finding
            {
                Id = "win.gamedvr_on",
                Category = FindingCategory.Windows,
                Severity = FindingSeverity.Warning,
                Title = "Фоновая запись Game DVR включена",
                Detail = "Xbox Game Bar держит наготове буфер записи последних минут игры. " +
                         "Это стоит кадров даже когда ты ничего не записываешь.",
                Recommendation = "Отключить фоновую запись",
                FixTweakId = "win.gamedvr_off",
                ExpectedGain = "Обычно +2–6% FPS и меньше просадок 1% low",
                ScoreWeight = 8
            });
        }

        if (t.HvciEnabled == true)
        {
            f.Add(new Finding
            {
                Id = "sec.hvci_on",
                Category = FindingCategory.Security,
                Severity = FindingSeverity.Warning,
                Title = "Включена «Целостность памяти» (Memory Integrity / HVCI)",
                Detail = "Самый крупный одиночный источник потерь производительности на Windows 11: " +
                         "виртуализация проверяет драйверы в реальном времени. В играх, упирающихся в процессор, " +
                         "это обычно 5–15% FPS. Но это реальная защита от подмены драйверов — " +
                         "отключать стоит осознанно, а не «потому что так в гайде».",
                Recommendation = "Взвесить: отключение даёт FPS, но снижает защиту системы",
                FixTweakId = "sec.hvci_off",
                ExpectedGain = "Обычно +5–15% FPS при упоре в CPU",
                ScoreWeight = 10
            });
        }

        // VBS отдельно от HVCI: гипервизор поднимается и стоит производительности сам по себе,
        // даже когда «Целостность памяти» выключена.
        if (t is { VbsEnabled: true, HvciEnabled: not true })
        {
            f.Add(new Finding
            {
                Id = "sec.vbs_on",
                Category = FindingCategory.Security,
                Severity = FindingSeverity.Warning,
                Title = "Включена безопасность на основе виртуализации (VBS)",
                Detail = "Windows запускает систему поверх гипервизора. Даже без «Целостности памяти» " +
                         "это стоит нескольких процентов в играх, упирающихся в процессор. " +
                         "Важно: VBS нужен для WSL2, Hyper-V, Docker и «Песочницы Windows» — " +
                         "если ты ими пользуешься, отключать нельзя.",
                Recommendation = "Отключить, если не используешь WSL2 / Hyper-V / Docker",
                FixTweakId = "sec.vbs_off",
                ExpectedGain = "Обычно +3–10% FPS при упоре в CPU",
                ScoreWeight = 8
            });
        }

        if (t.HardwareAcceleratedGpuScheduling == false)
        {
            f.Add(new Finding
            {
                Id = "gpu.hags_off",
                Category = FindingCategory.Gpu,
                Severity = FindingSeverity.Info,
                Title = "Аппаратное ускорение планирования GPU выключено",
                Detail = "HAGS перекладывает управление очередью кадров с процессора на видеокарту. " +
                         "Эффект неоднозначный: на связке NVIDIA + Reflex обычно плюс, на части систем — минус. " +
                         "Это ровно тот случай, когда нужно замерить до и после, а не верить гайдам.",
                Recommendation = "Включить и сравнить замером",
                FixTweakId = "gpu.hags",
                ScoreWeight = 0
            });
        }

        if (t.GameModeEnabled == false && r.Os.IsWindows11)
        {
            f.Add(new Finding
            {
                Id = "win.game_mode_off",
                Category = FindingCategory.Windows,
                Severity = FindingSeverity.Info,
                Title = "Игровой режим Windows выключен",
                Detail = "В Windows 11 игровой режим стал полезным: он приоритизирует игру и откладывает " +
                         "установку обновлений и перезагрузку драйверов во время игры.",
                Recommendation = "Включить игровой режим",
                FixTweakId = "win.game_mode_on",
                ScoreWeight = 0
            });
        }

        if (t.SystemResponsiveness is > 10)
        {
            f.Add(new Finding
            {
                Id = "win.system_responsiveness",
                Category = FindingCategory.Windows,
                Severity = FindingSeverity.Info,
                Title = $"SystemResponsiveness = {t.SystemResponsiveness}",
                Detail = "Доля процессорного времени, резервируемая под фоновые задачи. " +
                         "Значение 10 оставляет играм чуть больше, чем стандартные 20.",
                Recommendation = "Выставить 10",
                FixTweakId = "win.system_responsiveness",
                ScoreWeight = 0
            });
        }

        if (r.Os.Build is > 0 and < 19041)
        {
            f.Add(new Finding
            {
                Id = "os.outdated",
                Category = FindingCategory.Windows,
                Severity = FindingSeverity.Warning,
                Title = $"Старая сборка Windows ({r.Os.FullBuild})",
                Detail = "Часть игровых оптимизаций (HAGS, современный flip-model вывод, DirectStorage) " +
                         "появилась в более поздних сборках и здесь просто недоступна.",
                Recommendation = "Обновить Windows",
                ScoreWeight = 6
            });
        }
    }

    // ─────────────────────────── Питание ───────────────────────────

    private static void AnalyzePower(HardwareReport r, List<Finding> f)
    {
        var plan = r.Tuning.PowerPlanName;
        if (plan is null) return;

        bool isPowerSaver = plan.Contains("Экономия", StringComparison.OrdinalIgnoreCase)
                            || plan.Contains("Power saver", StringComparison.OrdinalIgnoreCase);
        bool isBalanced = plan.Contains("Сбалансированная", StringComparison.OrdinalIgnoreCase)
                          || plan.Contains("Balanced", StringComparison.OrdinalIgnoreCase);

        if (isPowerSaver)
        {
            f.Add(new Finding
            {
                Id = "power.saver_plan",
                Category = FindingCategory.Power,
                Severity = FindingSeverity.Critical,
                Title = $"Активна схема питания «{plan}»",
                Detail = "Режим экономии энергии искусственно ограничивает частоты процессора " +
                         "и агрессивно паркует ядра. В играх это прямая потеря производительности.",
                Recommendation = "Переключиться на схему высокой производительности",
                FixTweakId = "power.ultimate_plan",
                ExpectedGain = "На затронутых системах +10–30% FPS",
                ScoreWeight = 15
            });
        }
        else if (isBalanced && r.Machine.Chassis != ChassisKind.Laptop)
        {
            f.Add(new Finding
            {
                Id = "power.balanced_plan",
                Category = FindingCategory.Power,
                Severity = FindingSeverity.Info,
                Title = "Активна сбалансированная схема питания",
                Detail = "Для настольного ПК это разумный компромисс, но парковка ядер и переходы " +
                         "энергосостояний PCIe дают лишние микрозадержки. Отдельная игровая схема их убирает.",
                Recommendation = "Создать игровую схему питания",
                FixTweakId = "power.ultimate_plan",
                ScoreWeight = 0
            });
        }
    }

    // ─────────────────────────── Ввод ───────────────────────────

    private static void AnalyzeInput(HardwareReport r, List<Finding> f)
    {
        if (r.Input.PointerPrecisionEnabled)
        {
            f.Add(new Finding
            {
                Id = "input.mouse_accel_on",
                Category = FindingCategory.Input,
                Severity = FindingSeverity.Warning,
                Title = "Включена акселерация мыши",
                Detail = "«Повышенная точность установки указателя» меняет пройденное курсором расстояние " +
                         "в зависимости от скорости движения руки. В шутерах это ломает мышечную память: " +
                         "один и тот же взмах даёт разный поворот.",
                Recommendation = "Отключить акселерацию",
                FixTweakId = "input.mouse_accel_off",
                ExpectedGain = "Стабильное прицеливание (на FPS не влияет)",
                ScoreWeight = 8
            });
        }

        // Замер частоты опроса выполняется только по явному запросу (нужно движение мышью).
        if (r.Input.MeasuredPollingHz > 0)
        {
            int hz = r.Input.MeasuredPollingHz;
            if (hz < 500)
            {
                f.Add(new Finding
                {
                    Id = "input.low_polling_rate",
                    Category = FindingCategory.Input,
                    Severity = hz <= 125 ? FindingSeverity.Warning : FindingSeverity.Info,
                    Title = $"Фактическая частота опроса мыши: {hz} Гц",
                    Detail = hz <= 125
                        ? "125 Гц — стандартное значение USB по умолчанию. Между отчётами проходит 8 мс: " +
                          "при быстрых движениях курсор идёт заметными ступеньками. " +
                          "Если мышь игровая, её частота почти наверняка сбита или ограничена подключением."
                        : "Ниже 500 Гц. Для игровой мыши это мало — стоит проверить, во что она воткнута.",
                    Recommendation = "Проверить: прямой порт на задней панели (не хаб и не разъём монитора), " +
                                     "энергосбережение USB, настройку частоты в софте мыши",
                    ExpectedGain = "Ровнее движение курсора и точнее прицеливание",
                    ScoreWeight = 6
                });
            }
        }

        if (r.Input.MouseSensitivity is > 0 and not 10)
        {
            f.Add(new Finding
            {
                Id = "input.mouse_sensitivity_scaled",
                Category = FindingCategory.Input,
                Severity = FindingSeverity.Warning,
                Title = $"Системная чувствительность мыши {r.Input.MouseSensitivity}/20 вместо 10/20",
                Detail = "Любое значение кроме 10 (это 6-я позиция из 11 в панели управления) заставляет " +
                         "Windows масштабировать отсчёты сенсора. Появляются пропуски и неравномерность движения.",
                Recommendation = "Выставить 6/11 (значение 10) и регулировать чувствительность через DPI",
                FixTweakId = "input.mouse_sensitivity_1to1",
                ScoreWeight = 6
            });
        }
    }

    // ─────────────────────────── Фон ───────────────────────────

    private static void AnalyzeBackground(HardwareReport r, List<Finding> f)
    {
        var overlays = r.Tuning.ActiveOverlays;
        if (overlays.Count >= 3)
        {
            f.Add(new Finding
            {
                Id = "bg.overlay_stack",
                Category = FindingCategory.Background,
                Severity = overlays.Count >= 5 ? FindingSeverity.Critical : FindingSeverity.Warning,
                Title = $"Одновременно активны {Plural.Overlays(overlays.Count)}",
                Detail = $"Обнаружены: {string.Join(", ", overlays)}. " +
                         "Каждый оверлей встраивается в цепочку вывода кадра. Три и больше хуков подряд — " +
                         "это уже не потеря средних FPS, а регулярные спайки frametime, те самые рывки.",
                Recommendation = "Оставить один-два нужных, остальные отключить",
                ExpectedGain = "Заметно ровнее 1% low",
                ScoreWeight = 10
            });
        }
    }

    // ─────────────────────────── Сеть ───────────────────────────

    private static void AnalyzeNetwork(HardwareReport r, List<Finding> f)
    {
        var active = r.Network.Where(n => n.Connected && n.Kind is NetworkKind.WiFi or NetworkKind.Ethernet).ToList();
        if (active.Count == 0) return;

        if (active.All(n => n.Kind == NetworkKind.WiFi))
        {
            f.Add(new Finding
            {
                Id = "net.wifi_only",
                Category = FindingCategory.Network,
                Severity = FindingSeverity.Warning,
                Title = "Подключение только по Wi-Fi",
                Detail = "Wi-Fi даёт не столько высокий пинг, сколько нестабильный: джиттер и редкие потери " +
                         "пакетов. В соревновательных играх это выглядит как «резина» и откаты позиции. " +
                         "Кабель убирает это полностью.",
                Recommendation = "Подключиться по Ethernet",
                ScoreWeight = 6
            });
        }

        foreach (var n in active.Where(n => n.Kind == NetworkKind.Ethernet && n.LinkSpeedMbps is > 0 and < 1000))
        {
            f.Add(new Finding
            {
                Id = "net.slow_link",
                Category = FindingCategory.Network,
                Severity = FindingSeverity.Info,
                Title = $"{n.ConnectionId ?? n.Name}: скорость линка {n.LinkSpeedMbps} Мбит/с",
                Detail = "Линк договорился на скорость ниже гигабита. Обычно это старый или повреждённый кабель " +
                         "(категории ниже 5e), либо порт роутера на 100 Мбит.",
                Recommendation = "Проверить кабель и порт",
                ScoreWeight = 0
            });
        }
    }

    // ─────────────────────────── Игры ───────────────────────────

    private static void AnalyzeGames(HardwareReport r, List<Finding> f)
    {
        if (r.Games.Count == 0) return;

        var onHdd = r.Games.Where(g => g.DriveKind == StorageKind.Hdd).ToList();
        bool hasFastDrive = r.Storage.Any(s => s.Kind is StorageKind.Ssd or StorageKind.Nvme && !s.IsRemovable);

        if (onHdd.Count > 0 && hasFastDrive)
        {
            var top = onHdd.OrderByDescending(g => g.SizeGb).Take(3).Select(g => g.Name);
            f.Add(new Finding
            {
                Id = "storage.games_on_hdd",
                Category = FindingCategory.Storage,
                Severity = FindingSeverity.Critical,
                Title = $"{Plural.Games(onHdd.Count)} на жёстком диске",
                Detail = $"Например: {string.Join(", ", top)}. " +
                         "На HDD это долгие загрузки уровней и подгрузка текстур прямо в бою — " +
                         "те самые фризы при забегании в новую локацию. При этом в системе есть SSD.",
                Recommendation = "Перенести активные игры на SSD (Steam умеет это через «Переместить папку установки»)",
                ExpectedGain = "Загрузки в 3–10 раз быстрее, заметно меньше фризов стриминга",
                ScoreWeight = 12
            });
        }

        // Игра рядом с системой на забитом диске — частая причина деградации SSD.
        var systemDrive = r.Storage.FirstOrDefault(s => s.IsSystemDrive);
        if (systemDrive is { FreePercent: < 15 } && systemDrive.DriveLetters.Count > 0)
        {
            var onSystem = r.Games.Count(g => g.DriveLetter is { } d
                && systemDrive.DriveLetters.Contains(d, StringComparer.OrdinalIgnoreCase));

            if (onSystem > 0)
            {
                f.Add(new Finding
                {
                    Id = "storage.games_crowding_system",
                    Category = FindingCategory.Storage,
                    Severity = FindingSeverity.Info,
                    Title = $"{Plural.Games(onSystem)} на системном диске, где осталось {systemDrive.FreePercent:0}%",
                    Detail = "Системному диску нужен запас: под файл подкачки, обновления Windows, " +
                             "шейдерные кэши и сборку мусора SSD. Игры проще перенести, чем чистить систему.",
                    Recommendation = "Перенести часть игр на другой накопитель",
                    ScoreWeight = 0
                });
            }
        }
    }

    // ─────────────────────────── Автозагрузка ───────────────────────────

    private static void AnalyzeStartup(HardwareReport r, List<Finding> f)
    {
        if (r.Startup.HeavyItems.Count >= 3)
        {
            f.Add(new Finding
            {
                Id = "bg.heavy_startup",
                Category = FindingCategory.Background,
                Severity = FindingSeverity.Warning,
                Title = $"В автозагрузке {Plural.With(r.Startup.HeavyItems.Count, "тяжёлая программа", "тяжёлых программы", "тяжёлых программ")}",
                Detail = $"Обнаружены: {string.Join(", ", r.Startup.HeavyItems)}. " +
                         "Софт для подсветки и лаунчеры держат фоновые службы, а некоторые " +
                         "(iCUE, Armoury Crate) известны как источники скачков задержки DPC — " +
                         "это микрофризы, которые не видно по среднему FPS.",
                Recommendation = "Убрать из автозагрузки то, что не нужно постоянно",
                ExpectedGain = "Ровнее frametime, быстрее загрузка системы",
                ScoreWeight = 6
            });
        }
        else if (r.Startup.ItemCount >= 15)
        {
            f.Add(new Finding
            {
                Id = "bg.many_startup",
                Category = FindingCategory.Background,
                Severity = FindingSeverity.Info,
                Title = $"В автозагрузке {Plural.Programs(r.Startup.ItemCount)}",
                Detail = "Само по себе это не бьёт по FPS, но удлиняет загрузку системы " +
                         "и оставляет в памяти процессы, которые могут проснуться посреди игры.",
                Recommendation = "Проредить автозагрузку",
                ScoreWeight = 0
            });
        }
    }

    // ─────────────────────────── Детокс ───────────────────────────

    private static void AnalyzeDetox(HardwareReport r, List<Finding> f)
    {
        foreach (var tweak in r.Tuning.HarmfulTweaksDetected)
        {
            f.Add(new Finding
            {
                Id = "detox.harmful_tweak",
                Category = FindingCategory.Windows,
                Severity = FindingSeverity.Warning,
                Title = "Найден вредный твик от стороннего «оптимизатора»",
                Detail = tweak,
                Recommendation = "Вернуть штатное значение",
                ScoreWeight = 5
            });
        }
    }
}
