using System.IO;
using System.Xml.Serialization;
using AI_Discord_Bot.Models;

namespace AI_Discord_Bot.Services;

public class SettingsService
{
    private readonly string _filePath;

    public SettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        var serializer = new XmlSerializer(typeof(AppSettings));
        using var reader = new StreamReader(_filePath);
        return (AppSettings)serializer.Deserialize(reader)!;
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var serializer = new XmlSerializer(typeof(AppSettings));
        using var writer = new StreamWriter(_filePath);
        serializer.Serialize(writer, settings);
    }
}
