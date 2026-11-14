using UnityEngine;
using UnityEngine.SceneManagement;

public static class BootstrapAutoLoader
{
    private const string BootstrapScene = "Bootstrap";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == BootstrapScene)
                return; // já está aberto

        SceneManager.LoadScene(BootstrapScene, LoadSceneMode.Additive);
    }
}