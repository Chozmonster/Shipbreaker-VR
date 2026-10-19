using BepInEx.Logging;

namespace ShipbreakerVr;

/// <summary>BepInEx logger — shows up in LogOutput.log as [Info : ShipbreakerVr].</summary>
public static class ModLog
{
    public static ManualLogSource Log { get; private set; }

    public static void Init(ManualLogSource log) => Log = log;

    public static void Info(string msg) => (Log ?? Logger.CreateLogSource("ShipbreakerVr")).LogInfo(msg);

    public static void Warn(string msg) => (Log ?? Logger.CreateLogSource("ShipbreakerVr")).LogWarning(msg);

    public static void Error(string msg) => (Log ?? Logger.CreateLogSource("ShipbreakerVr")).LogError(msg);
}
