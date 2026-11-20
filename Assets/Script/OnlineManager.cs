using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

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
    [SerializeField] private int maxJogadores = 6;
    [Tooltip("WebSocket seguro na porta 443: costuma passar por redes que bloqueiam o Photon (ex.: faculdade).")]
    [SerializeField] private bool usarPorta443 = true;

    // Sem 0/O e 1/I, que se confundem quando alguém dita o código
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const string ChaveAnfitriao = "anf";
    public const string ChaveNomeSala = "nom";

    // nomes de sala mostrados na lista (o código continua existindo, mas não aparece)
    private static readonly string[] NomesDeSala =
    {
        "Mutirão do Rio", "Guardiões da Nascente", "Brigada da Mata", "Cooperativa Unida", "Raízes da Vila",
        "Bairro em Ação", "Rede Comunitária", "Vila Esperança", "Horta Coletiva", "Água Limpa Já",
    };

    // ------------------------------------------------------------------ eventos

    public event Action Connected;
    public event Action<DisconnectCause> Disconnected;

    public event Action<string> RoomCreated;          // código da sala criada
    public event Action<short, string> CreateFailed;  // código de erro, mensagem

    public event Action<string> JoinSucceeded;        // código da sala
    public event Action<short, string> JoinFailed;    // código de erro, mensagem

    public event Action<IReadOnlyList<RoomInfo>> SalasAtualizadas;   // lista de salas abertas mudou

    /// <summary>True quando dá pra criar/entrar em sala.</summary>
    public bool IsReady => PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer || PhotonNetwork.NetworkClientState == ClientState.JoinedLobby;

    private readonly Dictionary<string, RoomInfo> salas = new Dictionary<string, RoomInfo>();
    /// <summary>Salas abertas conhecidas (atualizadas enquanto o jogador está no lobby).</summary>
    public IReadOnlyList<RoomInfo> Salas => salas.Values.ToList();

    // ------------------------------------------------------------------ ciclo de vida

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        PhotonNetwork.SendRate = 30;            // pacotes por segundo
        PhotonNetwork.SerializationRate = 30;   // atualizações de posição por segundo
    }

    private void Start()
    {
        PhotonNetwork.NickName = PlayerPrefs.GetString("NomeJogador", "Voluntário");
        if (usarPorta443)
            PhotonNetwork.PhotonServerSettings.AppSettings.Protocol = ExitGames.Client.Photon.ConnectionProtocol.WebSocketSecure;
        PhotonNetwork.ConnectUsingSettings();
    }

    // ------------------------------------------------------------------ conexão

    public override void OnConnectedToMaster()
    {
        Debug.Log("Conectado");
        Connected?.Invoke();
        PedirListaDeSalas();                    // entra no lobby para receber a lista de salas
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log("Desconectado: " + cause);
        salas.Clear();
        SalasAtualizadas?.Invoke(Salas);
        Disconnected?.Invoke(cause);
        if (cause != DisconnectCause.ApplicationQuit)
            Invoke(nameof(Reconectar), 2f);     // tenta de novo em 2 segundos
    }

    private void Reconectar() => PhotonNetwork.ConnectUsingSettings();

    public void joinLobby() => PedirListaDeSalas();

    /// <summary>Entra no lobby padrão (se ainda não estiver) para receber a lista de salas.</summary>
    public void PedirListaDeSalas()
    {
        if (PhotonNetwork.NetworkClientState == ClientState.ConnectedToMasterServer && !PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby();
    }

    public override void OnRoomListUpdate(List<RoomInfo> lista)
    {
        foreach (var s in lista)
        {
            if (s.RemovedFromList || !s.IsOpen || !s.IsVisible) salas.Remove(s.Name);
            else salas[s.Name] = s;
        }
        SalasAtualizadas?.Invoke(Salas);
    }

    public override void OnLeftLobby()
    {
        salas.Clear();
        SalasAtualizadas?.Invoke(Salas);
    }

    // ------------------------------------------------------------------ criar sala

    public void createRoom()
    {
        var opcoes = new RoomOptions
        {
            MaxPlayers = (byte)maxJogadores,
            IsVisible = true,                                   // aparece na lista de salas
            IsOpen = true,
            CustomRoomProperties = new Hashtable
            {
                { ChaveAnfitriao, PhotonNetwork.NickName },
                { ChaveNomeSala, NomesDeSala[UnityEngine.Random.Range(0, NomesDeSala.Length)] },
            },
            CustomRoomPropertiesForLobby = new[] { ChaveAnfitriao, ChaveNomeSala },   // aparecem na lista de salas
        };
        PhotonNetwork.CreateRoom(GenerateRoomCode(), opcoes);
    }

    public override void OnCreatedRoom()
    {
        Debug.Log("Sala criada: " + PhotonNetwork.CurrentRoom.Name);
        RoomCreated?.Invoke(PhotonNetwork.CurrentRoom.Name);
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
