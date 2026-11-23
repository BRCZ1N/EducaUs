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
/// Se a conexão cair no meio de uma partida, tenta voltar para a MESMA sala sozinho (até 3 vezes):
/// a sala guarda o jogador por 30 s (PlayerTtl), então ele volta com o mesmo personagem e o mesmo papel.
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

    public event Action Reconectou;          // voltou para a sala depois de cair
    public event Action ReconexaoFalhou;     // não deu para voltar (a partida segue sem você)

    /// Tentando voltar para a sala depois de uma queda.
    public bool Reconectando { get; private set; }
    /// Entrar na próxima sala sem trocar de cena (sala aberta de dentro da partida offline).
    public bool FicarNaCena { get; set; }

    [Header("Reconexão")]
    [SerializeField] private int tentativasDeVolta = 3;
    [Tooltip("Segundos que a sala guarda o lugar de quem caiu.")]
    [SerializeField] private int segundosParaVoltar = 30;
    private bool estavaEmSala;
    private int tentativas;
    private string ultimaSala;

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
        string salvo = PlayerPrefs.GetString("NomeJogador", "");
        PhotonNetwork.NickName = string.IsNullOrWhiteSpace(salvo) ? "Voluntário " + UnityEngine.Random.Range(10, 100) : salvo;
        if (usarPorta443)
            PhotonNetwork.PhotonServerSettings.AppSettings.Protocol = ExitGames.Client.Photon.ConnectionProtocol.WebSocketSecure;
        PhotonNetwork.ConnectUsingSettings();
    }

    // ------------------------------------------------------------------ conexão

    public override void OnConnectedToMaster()
    {
        Debug.Log("Conectado");
        if (Reconectando && !string.IsNullOrEmpty(ultimaSala))
        {   // reconectou ao servidor mas ainda não à sala: tenta voltar para ela
            if (PhotonNetwork.RejoinRoom(ultimaSala)) return;
        }
        Connected?.Invoke();
        PedirListaDeSalas();                    // entra no lobby para receber a lista de salas
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log("Desconectado: " + cause);
        salas.Clear();
        SalasAtualizadas?.Invoke(Salas);
        bool porQueda = cause != DisconnectCause.ApplicationQuit && cause != DisconnectCause.DisconnectByClientLogic;
        if (porQueda && estavaEmSala && tentativas < tentativasDeVolta)
        {
            Reconectando = true;
            tentativas++;
            Disconnected?.Invoke(cause);
            Invoke(nameof(TentarVoltar), 2f);    // tenta voltar para a mesma sala em 2 segundos
            return;
        }
        if (Reconectando) DesistirDeVoltar();
        Disconnected?.Invoke(cause);
        if (cause != DisconnectCause.ApplicationQuit)
            Invoke(nameof(Reconectar), 2f);     // tenta de novo em 2 segundos
    }

    private void Reconectar() => PhotonNetwork.ConnectUsingSettings();

    private void TentarVoltar()
    {
        if (!PhotonNetwork.ReconnectAndRejoin()) PhotonNetwork.ConnectUsingSettings();   // sem dados para voltar direto: conecta e usa RejoinRoom
    }

    private void DesistirDeVoltar()
    {
        Reconectando = false; estavaEmSala = false; tentativas = 0;
        ReconexaoFalhou?.Invoke();
    }

    public override void OnLeftRoom()
    {
        // saiu de propósito (continua conectado): não tenta voltar. Se foi queda, quem cuida é o OnDisconnected.
        if (PhotonNetwork.IsConnected && !Reconectando) estavaEmSala = false;
    }

    /// Sair da sala de vez (sem guardar o lugar para voltar).
    public void SairDaSala()
    {
        estavaEmSala = false; Reconectando = false;
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom(false);
    }

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
            PlayerTtl = segundosParaVoltar * 1000,                // quem cai tem esse tempo para voltar à sala
            EmptyRoomTtl = segundosParaVoltar * 1000,
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
        ultimaSala = PhotonNetwork.CurrentRoom.Name;
        estavaEmSala = true;
        if (Reconectando)
        {   // voltou para a sala depois de cair: a cena Game já está aberta
            Reconectando = false; tentativas = 0;
            Reconectou?.Invoke();
            return;
        }
        tentativas = 0;
        if (FicarNaCena) { FicarNaCena = false; }                     // sala aberta de dentro da partida: não troca de cena
        else PhotonNetwork.IsMessageQueueRunning = false;   // segura as mensagens até a Game abrir (o PlayerSpawner religa)
        JoinSucceeded?.Invoke(PhotonNetwork.CurrentRoom.Name);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"Falha ao entrar na sala ({returnCode}): {message}");
        if (Reconectando) { DesistirDeVoltar(); return; }
        JoinFailed?.Invoke(returnCode, message);
    }
}
