using System;
using System.Collections;
using UnityEngine;

public interface ISchedulableAction<T>
{
    IEnumerator Start();

    void Stop();

    public void OnAction();
}

public abstract class SchedulableAction<T> : ISchedulableAction<T>
{
    protected readonly T Host;

    protected bool IsRunning;

    protected SchedulableAction(T host)
    {
        Host = host;
    }

    public virtual IEnumerator Start()
    {
        yield return null;
    }

    public void Stop()
    {
        IsRunning = false;
    }

    public abstract void OnAction();
}


public abstract class DelayAction<T> : SchedulableAction<T>
{
    private readonly long _period;

    protected DelayAction(T host, long period) : base(host)
    {
        _period = period;
    }

    public override IEnumerator Start()
    {
        IsRunning = true;
        yield return new WaitForSecondsRealtime(_period / 1000f);

        if (IsRunning) OnAction();
        
        IsRunning = false;
    }

    public abstract override void OnAction();
}


public sealed class SimpleDelayAction<T> : DelayAction<T>
{
    private readonly Action _action;
    
    public SimpleDelayAction(T host, long period, Action action) : base(host, period)
    {
        _action = action;
    }

    public override void OnAction()
    {
        _action?.Invoke();
    }
}

public abstract class PeriodicalAction<T> : SchedulableAction<T>
{
    private readonly long _interval;

    private readonly long _delay;

    protected PeriodicalAction(T host, long interval, long delay = 0) : base(host)
    {
        _interval = interval;
        _delay = delay;
    }

    public override IEnumerator Start()
    {
        IsRunning = true;
        if (_delay > 0)
        {
            yield return new WaitForSecondsRealtime(_delay / 1000f);
        }

        while (IsRunning)
        {
            OnAction();
            yield return new WaitForSecondsRealtime(_interval / 1000f);
        }

        IsRunning = false;
    }
}