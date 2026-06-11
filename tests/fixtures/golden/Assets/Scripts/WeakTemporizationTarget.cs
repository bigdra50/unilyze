using UnityEngine;

namespace GoldenFixture;

public class WeakTemporizationTarget : MonoBehaviour
{
    Vector3 velocity = Vector3.zero;

    void Update()
    {
        transform.position += velocity;
    }

    void ScaledUpdate()
    {
        transform.position += velocity * Time.deltaTime;
    }
}
