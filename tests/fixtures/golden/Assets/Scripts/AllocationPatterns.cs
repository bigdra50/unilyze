using System;

namespace GoldenFixture;

public class AllocationPatterns
{
    public int MethodBoxing()
    {
        object boxed = 42;
        return (int)boxed;
    }

    public object PropertyBoxing
    {
        get
        {
            object boxed = 7;
            return boxed;
        }
    }

    public Action MethodClosure()
    {
        int captured = 0;
        return () => captured++;
    }

    public Action PropertyClosure
    {
        get
        {
            int captured = 0;
            return () => captured++;
        }
    }

    public void MethodParams()
    {
        LogParams(1, 2, 3);
    }

    public int PropertyParams
    {
        get
        {
            SumParams(4, 5, 6);
            return 0;
        }
    }

    static void LogParams(params int[] args) { }

    static int SumParams(params int[] args) => args.Length;
}
