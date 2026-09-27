using System.Text.Json;
using PicRestore.Restoration.DamageDetection;

namespace PicRestore.Restoration.Training;

/// <summary>Metadata for one stored before/after pair.</summary>
public sealed record TrainingPairInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset AddedUtc { get; init; }
    public required string DamagedFileName { get; init; }
    public required string RestoredFileName { get; init; }
    public required double DamageShare { get; init; }
    public required double AlignmentScore { get; init; }
}

/// <summary>User-level training preferences.</summary>
public sealed record TrainingSettings
{
    /// <summary>Retrain automatically whenever a new pair is added (the "continuous" mode).</summary>
    public bool RetrainAutomatically { get; init; } = true;
}

/// <summary>
/// On-disk store for in-app training (by default %LOCALAPPDATA%\PicRestore\Training): the user's
/// before/after pairs (derived data only, see <see cref="TrainingPair"/>), the currently active trained
/// model, a history of every training run, and training settings. Deleting the folder - or
/// <see cref="ResetModel"/> - returns the app to its built-in detector.
/// </summary>
public sealed class TrainingLibrary
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public TrainingLibrary(string root)
    {
        Root = root;
        Directory.CreateDirectory(PairsDirectory);
    }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicRestore", "Training");

    public string Root { get; }

    private string PairsDirectory => Path.Combine(Root, "pairs");
    private string ModelPath => Path.Combine(Root, "model.json");
    private string HistoryPath => Path.Combine(Root, "history.log");
    private string SettingsPath => Path.Combine(Root, "settings.json");

    public string PairDirectory(string id) => Path.Combine(PairsDirectory, id);

    public IReadOnlyList<TrainingPairInfo> ListPairs()
    {
        var list = new List<TrainingPairInfo>();
        foreach (string dir in Directory.EnumerateDirectories(PairsDirectory))
        {
            string infoPath = Path.Combine(dir, "info.json");
            if (!File.Exists(infoPath) || !File.Exists(Path.Combine(dir, "pair.bin")))
            {
                continue;
            }

            try
            {
                TrainingPairInfo? info = JsonSerializer.Deserialize<TrainingPairInfo>(File.ReadAllText(infoPath), Json);
                if (info is not null)
                {
                    list.Add(info);
                }
            }
            catch (JsonException)
            {
                // A corrupt entry is skipped rather than breaking the whole library.
            }
        }

        return list.OrderBy(p => p.AddedUtc).ToList();
    }

    public TrainingPairInfo AddPair(TrainingPair pair, string damagedFileName, string restoredFileName, double alignmentScore)
    {
        string id = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];
        string dir = PairDirectory(id);
        Directory.CreateDirectory(dir);
        pair.Save(Path.Combine(dir, "pair.bin"));
        var info = new TrainingPairInfo
        {
            Id = id,
            Name = pair.Name,
            AddedUtc = DateTimeOffset.UtcNow,
            DamagedFileName = damagedFileName,
            RestoredFileName = restoredFileName,
            DamageShare = pair.DamageShare,
            AlignmentScore = alignmentScore,
        };
        File.WriteAllText(Path.Combine(dir, "info.json"), JsonSerializer.Serialize(info, Json));
        return info;
    }

    public void RemovePair(string id)
    {
        string dir = PairDirectory(id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public TrainingPair LoadPair(string id) => TrainingPair.Load(Path.Combine(PairDirectory(id), "pair.bin"));

    public IReadOnlyList<TrainingPair> LoadAllPairs() => ListPairs().Select(p => LoadPair(p.Id)).ToList();

    /// <summary>The user's trained model, or null to use the built-in one.</summary>
    public DamageModelWeights? LoadActiveModel()
    {
        if (!File.Exists(ModelPath))
        {
            return null;
        }

        try
        {
            return DamageModelWeights.FromJson(File.ReadAllText(ModelPath));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return null; // Unreadable model: fall back to built-in rather than failing to start.
        }
    }

    public void SaveActiveModel(DamageModelWeights model)
    {
        string temp = ModelPath + ".tmp";
        File.WriteAllText(temp, model.ToJson());
        File.Move(temp, ModelPath, overwrite: true);
    }

    public void ResetModel()
    {
        if (File.Exists(ModelPath))
        {
            File.Delete(ModelPath);
        }

        AppendHistory("Reset to the built-in model.");
    }

    public void AppendHistory(string line) =>
        File.AppendAllText(HistoryPath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm}  {line}{Environment.NewLine}");

    public IReadOnlyList<string> ReadHistory() =>
        File.Exists(HistoryPath) ? File.ReadAllLines(HistoryPath).Reverse().ToList() : new List<string>();

    public TrainingSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<TrainingSettings>(File.ReadAllText(SettingsPath), Json) ?? new TrainingSettings()
                : new TrainingSettings();
        }
        catch (JsonException)
        {
            return new TrainingSettings();
        }
    }

    public void SaveSettings(TrainingSettings settings) => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
}
