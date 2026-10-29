using WolfSlop.Engine.Actors;

namespace WolfSlop.Engine.Graphics;

public interface ISceneRenderer
{
    Task PrepareSceneAsync(RenderMap map, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null, SceneResources? resources = null);
    void Render(RenderFrame frame);
    void ReleaseScene();
}
