using System;
using UnityEngine;

public static class ConfigManager
{
    public const bool DEBUG = true;

    public const bool ONLY_VIDEO = false;
    
    public static bool IsRunningOnOculus()
    {
        return SystemInfo.deviceModel.Contains("Quest");
    }

    public static bool isRunningOnAndroid()
    {
        return Application.platform == RuntimePlatform.Android;
    }
}
