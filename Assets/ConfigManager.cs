using System;
using UnityEngine;

public static class ConfigManager
{
    // set false to get rid of all the debug information on the screen
    public const bool DEBUG = false;

    // mock to trigger the tracking headset movement rather than waiting for start command
    public const bool MOCK = false;

    // only show video. for condition #1, the user will use the combo of thumbsticks and computer to control the drone
    public const bool ONLY_VIDEO = false;

    // interval to send the movement info to the drone controller, the unit is milliseconds.
    // Sending data too frequently doesn't help, cause for the Dji SDK, the maximum frequency to send control data is 25Hz
    public const long SAMPLING_INTERVAL_IN_MILLISECONDS = 40L;

    // Once receive the start command, the period for the user to get ready. The unit is milliseconds.
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
