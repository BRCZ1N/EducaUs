using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Menu dentro da partida (Esc). Não é outra cena: abre por cima do jogo, que continua rodando para os outros.
/// Mostra o código da sala e os jogadores, e tem Continuar, Copiar código, Sair da partida e Sair do jogo.
/// Também leva o jogador de volta ao menu principal se a conexão cair.
/// O GerenciadorAmbiental adiciona este componente sozinho; não precisa colocar à mão.
/// </summary>
public class MenuPartida : MonoBehaviourPunCallbacks
{
    public static bool Aberto { get; private set; }

    /// Mensagem que o menu principal mostra ao voltar (queda de conexão etc.).
    public static string MotivoSaida;

    [SerializeField] private string cenaMenu = "MainMenu";
    [SerializeField] private int maxJogadores = 6;

    private static readonly Color Fundo = new Color(0.08f, 0.06f, 0.05f, 0.92f);
    private static readonly Color Ouro = new Color32(243, 166, 74, 255);
    private static readonly Color Cal = new Color32(241, 227, 200, 255);
    private static readonly Color BotaoCor = new Color32(70, 52, 40, 255);
    private static readonly Color BotaoPerigo = new Color32(150, 52, 40, 255);

    private Font fonte;
    private GameObject painel;
    private Text txtCodigo, txtJogadores, txtCanto;
    private bool saindo;

    // ------------------------------------------------------------------ ciclo de vida
    private void Awake()
    {
        Aberto = false;
        Construir();
        GarantirEventSystem();
        Fechar();
    }

    private void OnDestroy() => Aberto = false;

    private void Update()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame))
        {
            if (Aberto) Fechar(); else Abrir();
        }
        txtCanto.text = PhotonNetwork.InRoom ? $"Sala {PhotonNetwork.CurrentRoom.Name}" : "Offline";
    }

    public void Abrir()
    {
        Aberto = true;
        painel.SetActive(true);
        Atualizar();
    }

    public void Fechar()
    {
        Aberto = false;
        painel.SetActive(false);
    }

    private void Atualizar()
    {
        if (!PhotonNetwork.InRoom)
        {
            txtCodigo.text = "Partida offline";
            txtJogadores.text = "Você está jogando sem conexão.";
            return;
        }
        txtCodigo.text = $"Código da sala:  {PhotonNetwork.CurrentRoom.Name}";
        var nomes = PhotonNetwork.PlayerList
            .OrderBy(p => p.ActorNumber)
            .Select(p => (string.IsNullOrEmpty(p.NickName) ? $"Jogador {p.ActorNumber}" : p.NickName)
                         + (p.IsMasterClient ? "  (anfitrião)" : "") + (p.IsLocal ? "  (você)" : ""));
        txtJogadores.text = $"Jogadores ({PhotonNetwork.PlayerList.Length}/{maxJogadores}):\n" + string.Join("\n", nomes);
    }

    // ------------------------------------------------------------------ botões
    private void CopiarCodigo()
    {
        if (!PhotonNetwork.InRoom) return;
        GUIUtility.systemCopyBuffer = PhotonNetwork.CurrentRoom.Name;
        txtCodigo.text = $"Código da sala:  {PhotonNetwork.CurrentRoom.Name}   (copiado!)";
    }

    private void SairDaPartida()
    {
        if (saindo) return;
        saindo = true;
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();   // OnLeftRoom volta para o menu
        else VoltarAoMenu();
    }

    private void SairDoJogo()
    {
        if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void VoltarAoMenu()
    {
        Aberto = false;
        PhotonNetwork.IsMessageQueueRunning = true;
        if (SceneLoader.Instance != null) SceneLoader.Instance.ChangeScene(cenaMenu);
    }

    // ------------------------------------------------------------------ Photon
    public override void OnLeftRoom() => VoltarAoMenu();

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (saindo || cause == DisconnectCause.ApplicationQuit || cause == DisconnectCause.DisconnectByClientLogic) return;
        MotivoSaida = "A conexão caiu e você saiu da partida.";
        saindo = true;
        VoltarAoMenu();
    }

    public override void OnPlayerEnteredRoom(Player novo)
    {
        if (Aberto) Atualizar();
        AvisarHud($"{Nome(novo)} entrou na partida.");
    }

    public override void OnPlayerLeftRoom(Player saiu)
    {
        if (Aberto) Atualizar();
        AvisarHud($"{Nome(saiu)} saiu da partida.");
    }

    public override void OnMasterClientSwitched(Player novoAnfitriao)
    {
        if (Aberto) Atualizar();
    }

    private static string Nome(Player p) => string.IsNullOrEmpty(p.NickName) ? $"Jogador {p.ActorNumber}" : p.NickName;

    private void AvisarHud(string texto)
    {
        var hud = GetComponent<HudAmbiental>();
        if (hud != null) hud.Aviso(texto, Cal);
    }

    // ------------------------------------------------------------------ construção da tela
    private void Construir()
    {
        fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("Menu da Partida", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var raiz = (RectTransform)canvasGo.transform;

        // canto de cima à direita: código da sala sempre visível
        txtCanto = Texto(raiz, "Canto", 22, FontStyle.Bold, Cal, TextAnchor.UpperRight);
        Ancorar(txtCanto.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -20), new Vector2(700, 34));
        txtCanto.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.85f);

        // fundo escurecido que bloqueia cliques no jogo
        painel = new GameObject("Painel", typeof(RectTransform), typeof(Image));
        painel.transform.SetParent(raiz, false);
        var prt = (RectTransform)painel.transform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
        painel.GetComponent<Image>().color = new Color(0, 0, 0, 0.55f);

        var caixa = new GameObject("Caixa", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        caixa.transform.SetParent(painel.transform, false);
        Ancorar((RectTransform)caixa.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(620, 760));
        caixa.GetComponent<Image>().color = Fundo;
        var vl = caixa.GetComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(48, 48, 40, 40); vl.spacing = 16;
        vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;

        Linha(caixa.transform, Texto(null, "Titulo", 52, FontStyle.Bold, Ouro, TextAnchor.MiddleCenter, "EducaUs"), 70);
        txtCodigo = Texto(null, "Codigo", 28, FontStyle.Bold, Cal, TextAnchor.MiddleCenter);
        Linha(caixa.transform, txtCodigo, 40);
        txtJogadores = Texto(null, "Jogadores", 22, FontStyle.Normal, Cal, TextAnchor.UpperCenter);
        Linha(caixa.transform, txtJogadores, 200);

        Botao(caixa.transform, "Continuar", BotaoCor, Fechar);
        Botao(caixa.transform, "Copiar código da sala", BotaoCor, CopiarCodigo);
        Botao(caixa.transform, "Sair da partida", BotaoPerigo, SairDaPartida);
        Botao(caixa.transform, "Sair do jogo", BotaoPerigo, SairDoJogo);
    }

    private static void GarantirEventSystem()
    {
        if (EventSystem.current != null || FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.transform.SetParent(null);
    }

    private Text Texto(RectTransform pai, string nome, int tamanho, FontStyle estilo, Color cor, TextAnchor alinhamento, string texto = "")
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Text));
        if (pai != null) go.transform.SetParent(pai, false);
        var t = go.GetComponent<Text>();
        t.font = fonte; t.fontSize = tamanho; t.fontStyle = estilo; t.color = cor;
        t.alignment = alinhamento; t.text = texto; t.raycastTarget = false;
        return t;
    }

    private static void Linha(Transform pai, Graphic g, float altura)
    {
        g.transform.SetParent(pai, false);
        var le = g.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = altura;
    }

    private void Botao(Transform pai, string rotulo, Color cor, UnityEngine.Events.UnityAction acao)
    {
        var go = new GameObject(rotulo, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(pai, false);
        go.GetComponent<LayoutElement>().preferredHeight = 72;
        var img = go.GetComponent<Image>(); img.color = cor;
        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        var cores = b.colors;
        cores.normalColor = Color.white;
        cores.highlightedColor = new Color(1f, 0.85f, 0.65f);   // fica alaranjado ao passar o mouse
        cores.selectedColor = new Color(1f, 0.85f, 0.65f);
        cores.pressedColor = new Color(0.7f, 0.65f, 0.6f);
        b.colors = cores;
        b.onClick.AddListener(acao);
        var t = Texto((RectTransform)go.transform, "Texto", 30, FontStyle.Bold, Cal, TextAnchor.MiddleCenter, rotulo);
        var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Ancorar(RectTransform rt, Vector2 ancora, Vector2 pivo, Vector2 pos, Vector2 tam)
    {
        rt.anchorMin = rt.anchorMax = ancora; rt.pivot = pivo; rt.anchoredPosition = pos; rt.sizeDelta = tam;
    }
}
