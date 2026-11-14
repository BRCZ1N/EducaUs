using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    [SerializeField] private string initialScene = "MainMenu";

    private string currentScene;
    private bool changingScene;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        StartCoroutine(LoadInitialScene());
    }

    private IEnumerator LoadInitialScene()
    {
        Scene menu = SceneManager.GetSceneByName(initialScene);

        if (menu.isLoaded)
        {
            // O menu já está aberto (no editor): ele é a cena atual e o resto que estiver aberto sai,
            // para nunca ficarem duas câmeras/EventSystems.
            currentScene = initialScene;
            yield return UnloadOthers(initialScene);
        }
        else
        {
            Scene alreadyOpen = FindOpenGameplayScene();
            if (alreadyOpen.IsValid())
            {
                // Ex.: Bootstrap + Game abertas para testar a Game direto
                currentScene = alreadyOpen.name;
                yield return UnloadOthers(currentScene);
            }
            else
            {
                yield return SceneManager.LoadSceneAsync(initialScene, LoadSceneMode.Additive);
                currentScene = initialScene;
            }
        }

        SceneManager.SetActiveScene(SceneManager.GetSceneByName(currentScene));
    }

    // Primeira cena carregada que não seja a do próprio SceneLoader (Bootstrap)
    private Scene FindOpenGameplayScene()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isLoaded && s != gameObject.scene)
                return s;
        }
        return default;
    }

    // Descarrega todas as cenas abertas, menos o Bootstrap e a cena indicada
    private IEnumerator UnloadOthers(string keep)
    {
        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.isLoaded && s != gameObject.scene && s.name != keep)
                yield return SceneManager.UnloadSceneAsync(s);
        }
    }

    public void ChangeScene(string sceneName)
    {
        if (changingScene || sceneName == currentScene)
            return;

        if (!SceneManager.GetSceneByName(sceneName).isLoaded && !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[SceneLoader] A cena '{sceneName}' não está em File → Build Profiles → Scene List.");
            return;
        }

        StartCoroutine(ChangeSceneCoroutine(sceneName));
    }

    private IEnumerator ChangeSceneCoroutine(string sceneName)
    {
        changingScene = true;

        bool jaAberta = SceneManager.GetSceneByName(sceneName).isLoaded;

        // Descarrega a antiga ANTES de carregar a nova: nunca há dois EventSystems/câmeras juntos
        if (!string.IsNullOrEmpty(currentScene) && SceneManager.GetSceneByName(currentScene).isLoaded)
            yield return SceneManager.UnloadSceneAsync(currentScene);

        // Se já estava aberta (ex.: deixada aberta no editor), só ativa; não carrega outra cópia
        if (!jaAberta)
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));

        currentScene = sceneName;
        changingScene = false;
    }
}