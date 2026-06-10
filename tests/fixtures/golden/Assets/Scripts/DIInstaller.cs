using VContainer;

namespace GoldenFixture.DI;

public interface IGameService { }

public class GameService : IGameService { }

public class GameInstaller
{
    public void Configure(IContainerBuilder builder)
    {
        builder.Register<IGameService, GameService>();
    }
}
