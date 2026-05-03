using System.IO;

namespace LauncherServer;

public class MythLoginServiceConfigManager
{
    public string Content { get; }

    public MythLoginServiceConfigManager(string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists)
            throw new FileNotFoundException("Config file missing!", filePath);

        Content = file.OpenText().ReadToEnd();
    }
}
