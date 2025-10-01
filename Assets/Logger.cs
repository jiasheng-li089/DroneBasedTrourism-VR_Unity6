using System;
using System.IO;
using UnityEngine;

public class Logger {

    public static Logger Instance { get; private set; }
    
    private string _filePath;

    public void Start()
    {
        _filePath = Path.Combine(Application.persistentDataPath, DateTime.Now.ToString("yyyy-MM-dd-HH-mm") + ".log");
        Instance = this;
    }


    public void Log(string tag, string msg)
    {
        try
        {
            File.AppendAllTextAsync(_filePath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\t" + tag + "\t" + msg + "\n");
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
        }
    }
}
