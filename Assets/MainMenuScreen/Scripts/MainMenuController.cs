using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Tooltip("Seleciona o primeiro botão ao abrir")]
    public bool selectFirstOnStart = false;

    [Tooltip("Tela 'Juntar-se a partida'")]
    [SerializeField] private JoinRoomScreen joinRoomScreen;

    void Start()
    {
        if (!selectFirstOnStart || EventSystem.current == null) return;

        var first = GetComponentInChildren<Button>();

        if (first)
            EventSystem.current.SetSelectedGameObject(first.gameObject);
    }

    public void newGame()
    {
        var online = OnlineManager.Instance;

        // Sem Photon conectado: abre a Game offline (bom para testar o mapa)
        if (online == null || !online.IsReady)
        {
            Debug.LogWarning("[Menu] Photon não está pronto: abrindo a Game offline.");
            SceneLoader.Instance.ChangeScene("Game");
            return;
        }

        Debug.Log("[Menu] Criando sala...");
        online.RoomCreated += OnRoomCreated;
        online.CreateFailed += OnCreateFailed;
        online.createRoom();          // a resposta chega depois, nos eventos abaixo
    }

    private void OnRoomCreated(string code)
    {
        Unsubscribe();
        Debug.Log("[Menu] Sala criada: " + code);
        SceneLoader.Instance.ChangeScene("Game");
    }

    private void OnCreateFailed(short error, string message)
    {
        Unsubscribe();
        Debug.LogError($"[Menu] Não deu para criar a sala ({error}): {message}");
    }

    private void Unsubscribe()
    {
        if (OnlineManager.Instance == null) return;
        OnlineManager.Instance.RoomCreated -= OnRoomCreated;
        OnlineManager.Instance.CreateFailed -= OnCreateFailed;
    }

    private void OnDestroy() => Unsubscribe();

    public void joinLobby()
    {
        if (joinRoomScreen != null)
            joinRoomScreen.Open();
        else
            Debug.LogError("[MainMenuController] Arraste o JoinRoomScreen no campo do Inspector.");
    }

    public void quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}