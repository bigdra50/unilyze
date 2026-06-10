namespace GoldenFixture.Cycles;

public class CycleA
{
    public CycleB Partner = new();
}

public class CycleB
{
    public CycleA Partner = new();
}
