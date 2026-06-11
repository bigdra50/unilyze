using Unity.Entities;

namespace Golden.Ecs;

public partial struct MissingBurstSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }
}

[Burst.BurstCompile]
public partial struct AnnotatedBurstSystem : ISystem
{
    public void OnCreate(ref SystemState state) { }
    public void OnUpdate(ref SystemState state) { }
    public void OnDestroy(ref SystemState state) { }
}

public partial struct MissingBurstJob : IJobEntity { }

[Burst.BurstCompile]
public partial struct AnnotatedBurstJob : IJobEntity { }

public class ManagedSystem : SystemBase { }

public struct UnmanagedComponent : IComponentData
{
    public Entity Target;
    public float Value;
}

public struct ManagedComponent : IComponentData
{
    public string Label;
}

public class ManagedComponentClass : IComponentData
{
    public string Label;
}
