namespace GoldenFixture;

public class DeepNestingTarget
{
    public int DeeplyNested(int a, int b, int c, int d, int e, int f)
    {
        if (a > 0)
        {
            if (b > 0)
            {
                if (c > 0)
                {
                    if (d > 0)
                    {
                        if (e > 0)
                        {
                            if (f > 0)
                                return a + b + c + d + e + f;
                        }
                    }
                }
            }
        }
        return 0;
    }
}
