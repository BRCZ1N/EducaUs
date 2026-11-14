using System;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Fala com o Photon e avisa o resultado por eventos.
/// Não conhece nenhuma tela: quem quiser saber o que aconteceu se inscreve nos eventos.
/// Fica no Bootstrap (nunca é descarregado).
/// </summary>
public class OnlineManager : MonoBehaviourPunCallbacks
{
    public static OnlineManager Instance { get; private set; }

    [Header("Código da sala")]
    [SerializeField] private int roomCodeLength = 5;

    // Sem 0/O e 1/I, que se confundem quando alguém dita o código
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // ------------------------------------------------------------------ eventos

    public event Action Connected;
    public event Action<DisconnectCause> Disconnected;

    public event Action<string> RoomCreated;          // código da sala criada
    public event Action<short, string> CreateFailed;  // código de erro, mensagem

    public event Action<string> JoinSucceeded;        // código da sala
    public event Action<short, string> JoinFailed;    // código de erro, mensagem

    /// <summary>True quando dá pra criar/entrar em sala.</summary>
    public bool IsReady => PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer || PhotonNetwork.NetworkClientState == ClientState.JoinedLobby;

    // ------------------------------------------------------------------ ciclo de vida

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
        PhotonNetwork.ConnectUsingSettings();
    }

    // ------------------------------------------------------------------ conexão

    public override void OnConnectedToMaster()
    {
        Debug.Log("Conectado");
        Connected?.Invoke();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log("Desconectado: " + cause);
        Disconnected?.Invoke(cause);
    }

    public void joinLobby()
    {
        PhotonNetwork.JoinLobby();
    }

    // ------------------------------------------------------------------ criar sala

    public void createRoom()
    {
        PhotonNetwork.CreateRoom(GenerateRoomCode());
    }

    public override void OnCreatedRoom()
    {
        RoomCreated?.Invoke(PhotonNetwork.CurrentRoom.Name);
        Debug.Log(PhotonNetwork.CurrentRoom.Name);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        if (returnCode == ErrorCode.GameIdAlreadyExists)
        {
            createRoom(); // código repetido: tenta outro
            return;
        }

        Debug.LogError($"Falha ao criar sala ({returnCode}): {message}");
        CreateFailed?.Invoke(returnCode, message);
    }

    private string GenerateRoomCode()
    {
        var chars = new char[roomCodeLength];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = CodeChars[UnityEngine.Random.Range(0, CodeChars.Length)];
        return new string(chars);
    }

    public string getRoomId()
    {
        if (PhotonNetwork.CurrentRoom == null)
            return null;

        return PhotonNetwork.CurrentRoom.Name;
    }

    // ------------------------------------------------------------------ entrar em sala

    /// <summary>
    /// Pede para entrar na sala. Retorna false se nem deu pra enviar o pedido
    /// (sem conexão, já está numa sala...). O resultado chega por JoinSucceeded / JoinFailed.
    /// </summary>
    public bool JoinRoom(string code)
    {
        if (!IsReady) return false;
        return PhotonNetwork.JoinRoom(code.Trim().ToUpperInvariant());
    }

    public override void OnJoinedRoom()
    {
        Debug.Log("Entrou na sala: " + PhotonNetwork.CurrentRoom.Name);
        PhotonNetwork.IsMessageQueueRunning = false;   // segura as mensagens até a Game abrir (o PlayerSpawner religa)
        JoinSucceeded?.Invoke(PhotonNetwork.CurrentRoom.Name);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"Falha ao entrar na sala ({returnCode}): {message}");
        JoinFailed?.Invoke(returnCode, message);
    }
}