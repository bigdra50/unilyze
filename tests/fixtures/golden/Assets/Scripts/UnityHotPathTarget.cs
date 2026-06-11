using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class MonoBehaviour { }
}

namespace GoldenFixture;

public class UnityHotPathTarget : UnityEngine.MonoBehaviour
{
    void Update()
    {
        GetComponent<object>();
        var filtered = new[] { 1, 2, 3 }.Where(x => x > 0).ToList();
        var list = new List<int>();
        var message = "hello" + "world";
    }
}
