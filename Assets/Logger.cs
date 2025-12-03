using System;
using System.IO;
using UnityEngine;

public class Logger {

    public static Logger Instance { get; private set; }
    
    private string _filePath;

    private StreamWriter _fileWriter;

    public void Start()
    {
        _filePath = Path.Combine(Application.persistentDataPath, DateTime.Now.ToString("yyyy-MM-dd-HH-mm") + ".log");
        _fileWriter = new StreamWriter(_filePath);
        Instance = this;
    }


    public void Log(string tag, string msg)
    {
        try
        {
            _fileWriter.WriteLineAsync(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\t" + tag + "\t" + msg);
        }
        catch (System.Exception e)
        {
            Debug.LogError(e);
        }
    }

    public void Stop()
    {
        _fileWriter.Close();
    }
}
