using System;
using UnityEngine;

public static class ConfigManager
{
    public const bool DEBUG = false;

    public const bool ONLY_VIDEO = true;
    
    public static bool IsRunningOnOculus()
    {
        return SystemInfo.deviceModel.Contains("Quest");
    }

    public static bool isRunningOnAndroid()
    {
        return Application.platform == RuntimePlatform.Android;
    }
}
