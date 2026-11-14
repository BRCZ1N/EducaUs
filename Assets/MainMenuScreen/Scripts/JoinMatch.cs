#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Tela de partida (PUN 2): criar sala com código, entrar com código, iniciar e voltar.
public class JoinMatch : MonoBehaviourPunCallbacks
{
    public TMP_InputField roomInput;
    public TMP_Text status;
    public Button startButton;
    public string gameScene = "Game";
    public string mainMenuScene = "MainMenu";
    public byte maxPlayers = 4;

    const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const string Ready = "Digite o código da sala ou crie uma nova";

    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        SetStart(false);

        if (roomInput)
        {
            roomInput.characterLimit = 8;
            roomInput.onValueChanged.AddListener(v =>
            {
                var up = v.ToUpper();
                if (up != v) roomInput.text = up;
            });
        }

        if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InRoom) Say(Ready);
        else if (!PhotonNetwork.IsConnected) { Say("Conectando..."); PhotonNetwork.ConnectUsingSettings(); }
        else Say("Conectando...");
    }

    public void Join()
    {
        string code = roomInput ? roomInput.text.Trim().ToUpper() : "";
        if (code.Length == 0) { Say("Digite o código da sala"); return; }
        if (!PhotonNetwork.IsConnectedAndReady) { Say("Ainda conectando..."); return; }
        Say("Entrando na sala " + code + "...");
        PhotonNetwork.JoinRoom(code);
    }

    public void Create()
    {
        if (!PhotonNetwork.IsConnectedAndReady) { Say("Ainda conectando..."); return; }
        string code = "";
        for (int i = 0; i < 5; i++) code += Chars[Random.Range(0, Chars.Length)];
        Say("Criando sala " + code + "...");
        PhotonNetwork.CreateRoom(code, new RoomOptions { MaxPlayers = maxPlayers });
    }

    public void StartMatch()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.LoadLevel(gameScene);
    }

    public void Back()
    {
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        SceneManager.LoadScene(mainMenuScene);
    }

    public override void OnConnectedToMaster() { if (!PhotonNetwork.InRoom) Say(Ready); }
    public override void OnJoinedRoom() { ShowRoom(); }
    public override void OnPlayerEnteredRoom(Player p) { ShowRoom(); }
    public override void OnPlayerLeftRoom(Player p) { ShowRoom(); }
    public override void OnMasterClientSwitched(Player p) { ShowRoom(); }
    public override void OnJoinRoomFailed(short code, string msg) { Say("Sala não encontrada"); }
    public override void OnCreateRoomFailed(short code, string msg) { Say("Erro ao criar a sala"); }
    public override void OnDisconnected(DisconnectCause cause) { Say("Desconectado: " + cause); SetStart(false); }

    void ShowRoom()
    {
        var r = PhotonNetwork.CurrentRoom;
        if (r == null) return;
        string who = PhotonNetwork.IsMasterClient ? "Você inicia a partida" : "Aguardando o anfitrião";
        Say("Sala " + r.Name + "  (" + r.PlayerCount + "/" + r.MaxPlayers + ")  " + who);
        SetStart(PhotonNetwork.IsMasterClient);
    }

    void SetStart(bool on)
    {
        if (!startButton) return;
        startButton.interactable = on;
        var t = startButton.GetComponentInChildren<TMP_Text>();
        if (t) t.alpha = on ? 1f : 0.4f;
    }

    void Say(string s) { if (status) status.text = s; }
}
#endif
