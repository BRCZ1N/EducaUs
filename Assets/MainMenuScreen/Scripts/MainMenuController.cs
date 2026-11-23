using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Menu principal: nome do jogador, estado da conexão, criar partida, entrar com código e sair.
/// O VilaMenuBuilder (Tools → Menu principal) cria a tela e liga todos os campos.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Seleciona o primeiro botão ao abrir")]
    public bool selectFirstOnStart = false;

    [Tooltip("Tela 'Juntar-se a partida'")]
    [SerializeField] private JoinRoomScreen joinRoomScreen;

    [Header("Ligados pelo VilaMenuBuilder")]
    [SerializeField] private TMP_InputField nomeInput;
    [SerializeField] private TMP_Text statusConexao;
    [Tooltip("Botões que só funcionam conectado (ex.: Juntar-se a partida).")]
    [SerializeField] private Button botaoEntrar;

    private const string ChaveNome = "NomeJogador";
    private bool criandoSala;

    private void Start()
    {
        // nome salvo da última vez (ou um padrão)
        string nome = PlayerPrefs.GetString(ChaveNome, "");
        if (string.IsNullOrWhiteSpace(nome)) nome = "Voluntário " + Random.Range(10, 100);
        AplicarNome(nome);
        if (nomeInput != null)
        {
            nomeInput.text = nome;
            nomeInput.onEndEdit.AddListener(AplicarNome);
        }

        // voltou de uma partida por queda de conexão ou expulsão? avisa
        if (!string.IsNullOrEmpty(MenuPartida.MotivoSaida) && statusConexao != null)
        {
            statusConexao.text = MenuPartida.MotivoSaida;
            MenuPartida.MotivoSaida = null;
            Invoke(nameof(LiberarStatus), 4f);
        }
        else LiberarStatus();

        if (selectFirstOnStart && EventSystem.current != null)
        {
            var first = GetComponentInChildren<Button>();
            if (first) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }
    }

    private bool statusLivre;
    private void LiberarStatus() => statusLivre = true;

    private void Update()
    {
        bool pronto = OnlineManager.Instance != null && OnlineManager.Instance.IsReady;
        if (botaoEntrar != null) botaoEntrar.interactable = pronto && !criandoSala;

        if (statusConexao == null || !statusLivre) return;
        if (criandoSala) statusConexao.text = "Criando sala...";
        else if (pronto) statusConexao.text = $"Conectado  |  {Mathf.Max(1, PhotonNetwork.CountOfPlayers)} jogador(es) online  |  {PhotonNetwork.CountOfRooms} sala(s)";
        else if (PhotonNetwork.IsConnected) statusConexao.text = "Conectando ao servidor...";
        else statusConexao.text = "Sem conexão: o jogo abre offline";
    }

    private void AplicarNome(string nome)
    {
        nome = string.IsNullOrWhiteSpace(nome) ? "Voluntário" : nome.Trim();
        if (nome.Length > 16) nome = nome.Substring(0, 16);
        PhotonNetwork.NickName = nome;
        PlayerPrefs.SetString(ChaveNome, nome);
        if (nomeInput != null && nomeInput.text != nome) nomeInput.text = nome;
    }

    // ------------------------------------------------------------------ botões
    public void newGame()
    {
        if (criandoSala) return;
        if (nomeInput != null) AplicarNome(nomeInput.text);

        var online = OnlineManager.Instance;
        if (online == null || !online.IsReady)
        {
            Debug.LogWarning("[Menu] Photon não está pronto: abrindo a Game offline.");
            SceneLoader.Instance.ChangeScene("Game");
            return;
        }

        criandoSala = true;
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
        criandoSala = false;
        if (statusConexao != null) statusConexao.text = "Não deu para criar a sala. Tente de novo.";
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
        if (nomeInput != null) AplicarNome(nomeInput.text);
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
