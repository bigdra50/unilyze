namespace Unity.Entities;

public interface ISystem
{
    void OnCreate(ref SystemState state);
    void OnUpdate(ref SystemState state);
    void OnDestroy(ref SystemState state);
}

public class SystemBase { }

public interface IJobEntity { }

public interface IJobChunk { }

public interface IComponentData { }

public struct SystemState { }

public struct Entity
{
    public int Index;
}
