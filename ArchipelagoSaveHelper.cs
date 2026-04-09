using System;
using System.IO;

namespace FakutoriArchipelago;

public static class ArchipelagoSaveHelper
{
    public static int ReadLastAppliedIndex(string saveFilePath)
    {
        string indexPath = saveFilePath + ".apindex";

        if (!File.Exists(indexPath))
            return 0; // New game or first AP session

        try
        {
            string content = File.ReadAllText(indexPath).Trim();
            return int.Parse(content);
        }
        catch
        {
            // Corrupted file or parse error, treat as new
            return 0;
        }
    }

    public static void WriteLastAppliedIndex(string saveFilePath, int index)
    {
        string indexPath = saveFilePath + ".apindex";

        try
        {
            File.WriteAllText(indexPath, index.ToString());
        }
        catch
        {
            // Failed to write, but don't crash the game
            // Next load will re-apply everything (safe fallback)
        }
    }
}
