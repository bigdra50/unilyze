namespace UnityEngine;

public class MonoBehaviour
{
    public Transform transform = new();
}

public struct Vector3
{
    public static Vector3 zero;
    public static Vector3 operator +(Vector3 a, Vector3 b) => default;
}

public class Transform
{
    public Vector3 position;
    public void Translate(Vector3 translation) { }
    public void Rotate(Vector3 eulers) { }
    public void RotateAround(Vector3 point, Vector3 axis, float angle) { }
}

public static class Time
{
    public static float deltaTime;
    public static float time;
}

public static class Mathf
{
    public static float Sin(float f) => f;
}
