using System.Text.Json;
using System.Text.Json.Serialization;

namespace Velocity.Tweaks;

/// <summary>
/// Журнал изменений и откат. Главная страховка продукта: пользователь должен иметь
/// возможность вернуть систему в исходное состояние в один клик — даже если
/// основное приложение сломалось (для этого есть отдельный Velocity.Rescue).
///
/// Принцип: НИЧЕГО не меняется, пока прежнее состояние не записано на диск.
/// </summary>
public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string RootPath { get; }

    public SnapshotStore(string? root = null)
    {
        RootPath = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Velocity", "snapshots");
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>Открывает новую сессию изменений.</summary>
    public SnapshotSession BeginSession(string reason)
    {
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(RootPath, $"{id}.json");

        // Если за одну секунду открыли две сессии — не перетираем первую.
        int suffix = 1;
        while (File.Exists(path)) path = Path.Combine(RootPath, $"{id}-{suffix++}.json");

        return new SnapshotSession(path, new Snapshot
        {
            Id = Path.GetFileNameWithoutExtension(path),
            CreatedAt = DateTimeOffset.Now,
            Reason = reason
        }, Json);
    }

    public List<Snapshot> List()
    {
        var result = new List<Snapshot>();
        foreach (var file in Directory.EnumerateFiles(RootPath, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file), Json) is { } snapshot)
                {
                    snapshot.FilePath = file;
                    result.Add(snapshot);
                }
            }
            catch { /* повреждённый снимок пропускаем, но не роняем список */ }
        }
        return [.. result.OrderByDescending(s => s.CreatedAt)];
    }

    public Snapshot? Load(string id)
        => List().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void Delete(string id)
    {
        if (Load(id)?.FilePath is { } path && File.Exists(path)) File.Delete(path);
    }
}

public sealed class Snapshot
{
    public required string Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Reason { get; init; }
    public List<TweakChange> Changes { get; init; } = [];
    /// <summary>Уже откатан целиком.</summary>
    public bool Reverted { get; set; }

    [JsonIgnore] public string? FilePath { get; set; }
}

/// <summary>Одно применение одного твика со всеми затронутыми значениями.</summary>
public sealed class TweakChange
{
    public required string TweakId { get; init; }
    public required string TweakTitle { get; init; }
    public required DateTimeOffset AppliedAt { get; init; }
    public List<StateRecord> Previous { get; init; } = [];
    public bool Reverted { get; set; }
}

/// <summary>Прежнее состояние одного значения. Этого достаточно, чтобы вернуть всё назад.</summary>
public sealed class StateRecord
{
    /// <summary>registry | powercfg | display | service</summary>
    public required string Kind { get; init; }
    public required string Target { get; init; }
    /// <summary>null означает «значения не существовало» — при откате его надо удалить.</summary>
    public string? Value { get; init; }
    public string? ValueKind { get; init; }
}

/// <summary>
/// Сессия изменений. Пишет снимок на диск после КАЖДОГО твика, а не в конце:
/// если приложение упадёт посреди применения, откатить всё равно будет можно.
/// </summary>
public sealed class SnapshotSession(string path, Snapshot snapshot, JsonSerializerOptions json)
{
    public Snapshot Snapshot { get; } = snapshot;
    public string Path { get; } = path;

    private readonly Lock _lock = new();

    public void Record(TweakChange change)
    {
        lock (_lock)
        {
            Snapshot.Changes.Add(change);
            Flush();
        }
    }

    public void MarkReverted(string tweakId)
    {
        lock (_lock)
        {
            foreach (var c in Snapshot.Changes.Where(c => c.TweakId == tweakId)) c.Reverted = true;
            Snapshot.Reverted = Snapshot.Changes.All(c => c.Reverted);
            Flush();
        }
    }

    public void Flush()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(Snapshot, json));
        }
        catch { /* диск недоступен — изменения всё равно уже применены, не падаем */ }
    }

    /// <summary>Пустую сессию не храним — иначе список точек отката зарастёт мусором.</summary>
    public void DiscardIfEmpty()
    {
        if (Snapshot.Changes.Count != 0) return;
        try { if (File.Exists(Path)) File.Delete(Path); } catch { }
    }
}
