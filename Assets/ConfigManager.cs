using System;
using UnityEngine;

public static class ConfigManager
{
    public const bool DEBUG = true;

    public const bool ONLY_VIDEO = false;

    public const long SAMPLING_INTERVAL_IN_MILLISECONDS = 40L;

    public const long PERIOD_FOR_USER_TO_PREPARE = 1000L;
    
    // sample the tensor data in coroutine or in `Update()` method directly?
    // true -> in coroutine
    // false -> in `Update()` method
    public const bool SAMPLE_IN_COROUTINE = true;
    
    public static bool IsRunningOnOculus()
    {
        return SystemInfo.deviceModel.Contains("Quest");
    }

    public static bool isRunningOnAndroid()
    {
        return Application.platform == RuntimePlatform.Android;
    }
}
