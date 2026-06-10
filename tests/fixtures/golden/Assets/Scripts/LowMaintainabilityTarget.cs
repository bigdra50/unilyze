namespace GoldenFixture;

public class LowMaintainabilityTarget
{
    public int Opaque(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j)
    {
        var x = a + b * c - d / (e + 1) + f % (g + 2) + h & i | j;
        var y = (x << 2) ^ (x >> 1) + (a * b) - (c * d) + (e * f);
        var z = y + (a ^ b) + (c & d) + (e | f) + (g << 1) + (h >> 2);
        if (x > 0 && y > 0 && z > 0) x++;
        if (x < 0 || y < 0 || z < 0) y--;
        if ((x & 1) == 1 && (y & 1) == 1) z++;
        if ((x & 2) == 2 || (y & 2) == 2) z--;
        for (var n = 0; n < 8; n++)
        {
            x += n * a;
            y -= n * b;
            z ^= n * c;
            if (n % 2 == 0) x++;
            else y--;
        }
        switch (x % 7)
        {
            case 0: return x + y;
            case 1: return x - y;
            case 2: return x * y;
            case 3: return x ^ y;
            case 4: return x & y;
            case 5: return x | y;
            default: return z;
        }
    }
}
