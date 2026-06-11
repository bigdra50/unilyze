namespace GoldenFixture;

/// <summary>MonoBehaviour with 21 methods — GodClass under default profile, passes under unity MonoBehaviour thresholds.</summary>
public class MonoBehaviourGodClassTarget : UnityEngine.MonoBehaviour
{
    int _a; int _b; int _c; int _d; int _e;

    [UnityEngine.SerializeField] SerializedRefBase wiredTarget;

    public int M01() { _a++; return _a; }
    public int M02() { _b++; return _b; }
    public int M03() { _c++; return _c; }
    public int M04() { _d++; return _d; }
    public int M05() { _e++; return _e; }
    public int M06() { _a += 2; return _a; }
    public int M07() { _b += 2; return _b; }
    public int M08() { _c += 2; return _c; }
    public int M09() { _d += 2; return _d; }
    public int M10() { _e += 2; return _e; }
    public int M11() { _a += 3; return _a; }
    public int M12() { _b += 3; return _b; }
    public int M13() { _c += 3; return _c; }
    public int M14() { _d += 3; return _d; }
    public int M15() { _e += 3; return _e; }
    public int M16() { _a += 4; return _a; }
    public int M17() { _b += 4; return _b; }
    public int M18() { _c += 4; return _c; }
    public int M19() { _d += 4; return _d; }
    public int M20() { _e += 4; return _e; }
    public int M21() { _a += 5; return _a; }
}
