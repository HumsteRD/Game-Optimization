// Самопроверка перед выпуском.
//
// Это не модульные тесты: здесь проверяется, что каталог непротиворечив,
// сканер отрабатывает на настоящем железе, состояние твиков определяется,
// а применение и откат действительно возвращают систему в исходное состояние.
// Запускать на реальной машине: dotnet run --project tests/Velocity.SelfCheck
//
// Меняются только ключи HKCU, и каждый из них откатывается в конце проверки.

using Microsoft.Win32;
using Velocity.Hardware;
using Velocity.Tweaks;
using Velocity.Tweaks.Games;

Console.OutputEncoding = System.Text.Encoding.UTF8;

int failures = 0;
void Check(string what, bool ok, string? detail = null)
{
    Console.WriteLine($"  {(ok ? "OK  " : "СБОЙ")}  {what}{(detail is null ? "" : "  — " + detail)}");
    if (!ok) failures++;
}

Console.WriteLine("=== КАТАЛОГ ===");
var catalog = TweakCatalog.Load();
Check($"загружено твиков: {catalog.All.Count}", catalog.All.Count >= 30);
Check("нет ошибок разбора каталога", catalog.LoadErrors.Count == 0,
    string.Join("; ", catalog.LoadErrors));

var dupes = catalog.All.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
Check("нет повторяющихся идентификаторов", dupes.Count == 0, string.Join(", ", dupes));

var noPreset = catalog.All.Where(t => t.Presets.Count == 0).Select(t => t.Id).ToList();
Check("каждый твик входит хотя бы в один набор", noPreset.Count == 0, string.Join(", ", noPreset));

var noGain = catalog.All.Where(t => t.ExpectedGain is null).Select(t => t.Id).ToList();
Check("у каждого твика заявлен ожидаемый эффект", noGain.Count == 0, string.Join(", ", noGain));

// Необратимое применять нельзя: команда без описанного отката — ошибка каталога.
var irreversible = catalog.All
    .Where(t => t.Apply.OfType<CommandAction>().Any() && t.Revert.Count == 0)
    .Select(t => t.Id).ToList();
Check("у всех командных твиков описан откат", irreversible.Count == 0, string.Join(", ", irreversible));

Console.WriteLine("\n=== ПРОФИЛИ ИГР ===");
var profiles = GameProfileEngine.Load();
Check($"загружено профилей: {profiles.Profiles.Count}", profiles.Profiles.Count >= 8);
Check("нет ошибок разбора профилей", profiles.LoadErrors.Count == 0,
    string.Join("; ", profiles.LoadErrors));

Console.WriteLine("\n=== СКАН ===");
var report = new HardwareScanner().Scan();
Check($"скан за {report.ScanDurationMs} мс", report.ScanDurationMs < 2000);
Check("нет ошибок сканеров", report.ScanErrors.Count == 0, string.Join("; ", report.ScanErrors));
Check("процессор определён", report.Cpu.Name is not null);
Check("видеокарта определена", report.Gpus.Count > 0);
Check("мониторы определены", report.Displays.Count > 0);

var facts = SystemFacts.From(report);
var engine = new TweakEngine(facts);

Console.WriteLine("\n=== ОПРЕДЕЛЕНИЕ СОСТОЯНИЯ ===");
var statuses = engine.GetStatuses(catalog.All);
var unknown = statuses.Where(s => s.State == TweakState.Unknown && s.Tweak.Detect is not null).ToList();
Check("состояние определяется у всех твиков с детектом", unknown.Count == 0,
    string.Join(", ", unknown.Select(u => u.Tweak.Id)));

foreach (var group in statuses.GroupBy(s => s.State).OrderBy(g => g.Key))
    Console.WriteLine($"        {group.Key,-15} {group.Count()}");

Console.WriteLine("\n=== ПРИМЕНЕНИЕ И ОТКАТ (только безопасное, без прав админа) ===");

string Peek(RegistryHive hive, string path, string name)
{
    using var k = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
    return k?.GetValue(name)?.ToString() ?? "<нет>";
}

// Берём только твики, которые пишут в HKCU: их можно применить и откатить без админа.
var subject = catalog.All
    .Where(t => !t.RequiresElevation
             && t.Apply.All(a => a is RegistryAction r && r.Hive == "HKCU")
             && facts.MatchesAll(t.Requires))
    .ToList();

Console.WriteLine($"  выбрано для проверки: {subject.Count}");

var targets = subject.SelectMany(t => t.Apply.OfType<RegistryAction>())
    .Select(a => (a.Path, a.Name)).Distinct().ToList();
var before = targets.ToDictionary(t => t, t => Peek(RegistryHive.CurrentUser, t.Path, t.Name));

var session = engine.Apply(subject, new TweakEngine.ApplyOptions { Reason = "Автопроверка" });
Check("применение не прервано", session.AbortReason is null, session.AbortReason);
Check($"применено {session.Results.Count(r => r.Outcome == ApplyOutcome.Applied)} из {subject.Count}",
    session.Results.All(r => r.Outcome is ApplyOutcome.Applied or ApplyOutcome.Skipped),
    string.Join("; ", session.Results.Where(r => r.Outcome is ApplyOutcome.Failed or ApplyOutcome.VerifyFailed)
        .Select(r => $"{r.TweakId}: {r.Message}")));

int changed = targets.Count(t => Peek(RegistryHive.CurrentUser, t.Path, t.Name) != before[t]);
Check($"значений реально изменилось: {changed}", changed > 0);

if (session.SnapshotId is { } id)
{
    var reverted = engine.Revert(id);
    Check("откат без ошибок", reverted.All(r => r.Outcome == ApplyOutcome.Applied),
        string.Join("; ", reverted.Where(r => r.Outcome != ApplyOutcome.Applied).Select(r => r.Message)));

    var mismatched = targets
        .Where(t => Peek(RegistryHive.CurrentUser, t.Path, t.Name) != before[t])
        .Select(t => $"{t.Path}\\{t.Name}")
        .ToList();
    Check("система вернулась в исходное состояние", mismatched.Count == 0,
        string.Join(", ", mismatched));

    new SnapshotStore().Delete(id);
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? "ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ"
    : $"ПРОВАЛЕНО ПРОВЕРОК: {failures}");
return failures == 0 ? 0 : 1;
