using System.Collections;
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
/// Jogando offline: o botão "Abrir sala online" conecta, cria uma sala levando o progresso e chama outros jogadores.
/// Se a conexão cair numa sala, o OnlineManager tenta voltar sozinho; só se não der o jogador volta ao menu.
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
    private bool abrindoSala;
    private bool fechandoJogo;
    private GameObject btAbrirSala, btCopiar;
    private Text txtAbrirSala;
    private bool inscrito;

    // ------------------------------------------------------------------ ciclo de vida
    private void Awake()
    {
        Aberto = false;
        Construir();
        GarantirEventSystem();
        Fechar();
    }

    private void OnDestroy()
    {
        Aberto = false;
        if (inscrito && OnlineManager.Instance != null)
        {
            OnlineManager.Instance.Reconectou -= AoReconectar;
            OnlineManager.Instance.ReconexaoFalhou -= AoFalharReconexao;
        }
    }

    private void Start()
    {
        if (OnlineManager.Instance == null) return;
        OnlineManager.Instance.Reconectou += AoReconectar;
        OnlineManager.Instance.ReconexaoFalhou += AoFalharReconexao;
        inscrito = true;
    }

    private void Update()
    {
        var kb = Keyboard.current; var gp = Gamepad.current;
        if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame))
        {
            if (Aberto) Fechar(); else Abrir();
        }
        bool voltando = OnlineManager.Instance != null && OnlineManager.Instance.Reconectando;
        txtCanto.text = voltando ? "Reconectando..." : PhotonNetwork.InRoom ? $"Sala {PhotonNetwork.CurrentRoom.Name}" : "Offline";
        if (Aberto && !abrindoSala) AtualizarBotoes();
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
        AtualizarBotoes();
        if (!PhotonNetwork.InRoom)
        {
            txtCodigo.text = $"Partida offline  ·  {PhotonNetwork.NickName}";
            bool pronto = OnlineManager.Instance != null && OnlineManager.Instance.IsReady;
            txtJogadores.text = "Você está jogando sozinho.\n\n" + (pronto
                ? $"Há conexão com o servidor ({PhotonNetwork.CountOfPlayers} jogador(es) online): abra uma sala para chamar outros jogadores. O progresso continua."
                : "Sem conexão com o servidor agora. Toque em \"Abrir sala online\" para tentar conectar.");
            return;
        }
        txtCodigo.text = $"Código da sala:  {PhotonNetwork.CurrentRoom.Name}";
        var nomes = PhotonNetwork.PlayerList
            .OrderBy(p => p.ActorNumber)
            .Select(p => (string.IsNullOrEmpty(p.NickName) ? $"Jogador {p.ActorNumber}" : p.NickName)
                         + (p.IsMasterClient ? "  (anfitrião)" : "") + (p.IsLocal ? "  (você)" : "") + (p.IsInactive ? "  (caiu, pode voltar)" : ""));
        txtJogadores.text = $"Jogadores na sala ({PhotonNetwork.PlayerList.Count(p => !p.IsInactive)}/{maxJogadores}):\n" + string.Join("\n", nomes) +
                            $"\n\n<size=18>Online no servidor: {PhotonNetwork.CountOfPlayers} jogador(es) em {PhotonNetwork.CountOfRooms} sala(s)</size>";
    }

    private void AtualizarBotoes()
    {
        if (btAbrirSala == null) return;
        btAbrirSala.SetActive(!PhotonNetwork.InRoom);
        btCopiar.SetActive(PhotonNetwork.InRoom);
    }

    // ------------------------------------------------------------------ abrir sala dentro da partida
    private void AbrirSalaOnline()
    {
        if (abrindoSala || PhotonNetwork.InRoom) return;
        StartCoroutine(AbrirSalaRotina());
    }

    private IEnumerator AbrirSalaRotina()
    {
        var online = OnlineManager.Instance;
        if (online == null) { txtJogadores.text = "O OnlineManager não está na cena (abra o jogo pelo Bootstrap)."; yield break; }
        abrindoSala = true;
        txtAbrirSala.text = "Conectando...";
        if (!PhotonNetwork.IsConnected) PhotonNetwork.ConnectUsingSettings();
        float t = 0f;
        while (!online.IsReady && t < 15f) { t += Time.unscaledDeltaTime; yield return null; }
        if (!online.IsReady)
        {
            txtJogadores.text = "Não foi possível conectar ao servidor. Confira a internet e tente de novo.";
            txtAbrirSala.text = "Abrir sala online"; abrindoSala = false; yield break;
        }

        txtAbrirSala.text = "Criando a sala...";
        bool criou = false, falhou = false;
        System.Action<string> okCb = _ => criou = true;
        System.Action<short, string> erroCb = (c, m) => falhou = true;
        online.RoomCreated += okCb; online.CreateFailed += erroCb;
        online.FicarNaCena = true;                       // continua nesta cena, sem recarregar a Game
        online.createRoom();
        t = 0f;
        while (!criou && !falhou && t < 15f) { t += Time.unscaledDeltaTime; yield return null; }
        online.RoomCreated -= okCb; online.CreateFailed -= erroCb;
        if (!criou)
        {
            online.FicarNaCena = false;
            txtJogadores.text = "Não deu para criar a sala. Tente de novo.";
            txtAbrirSala.text = "Abrir sala online"; abrindoSala = false; yield break;
        }
        t = 0f;
        while (!PhotonNetwork.InRoom && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
        PhotonNetwork.IsMessageQueueRunning = true;

        var gm = GetComponent<GerenciadorAmbiental>();
        if (gm != null) gm.LevarEstadoParaSala();        // tarefas, pistas e relógio vão para a sala
        TrocarJogadorLocal();
        txtAbrirSala.text = "Abrir sala online"; abrindoSala = false;
        Atualizar();
        AvisarHud($"Sala aberta! Código {PhotonNetwork.CurrentRoom.Name}: os outros entram pela lista de salas.");
    }

    /// O personagem offline vira um personagem de rede (os outros jogadores passam a vê-lo).
    private static void TrocarJogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            var pv = pm.GetComponent<PhotonView>();
            if (pv != null && pv.ViewID != 0) continue;              // já é de rede
            string prefab = pm.gameObject.name.Replace("(Clone)", "").Trim();
            try
            {
                var novo = PhotonNetwork.Instantiate(prefab, pm.transform.position, Quaternion.identity);
                if (novo != null) Destroy(pm.gameObject);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MenuPartida] Não deu para criar o personagem de rede '{prefab}' (ele precisa estar numa pasta Resources): {e.Message}");
            }
            return;
        }
    }

    // ------------------------------------------------------------------ reconexão
    private void AoReconectar()
    {
        saindo = false;
        AvisarHud("Conexão de volta! Você voltou para a sala.");
        if (Aberto) Atualizar();
    }

    private void AoFalharReconexao()
    {
        if (saindo) return;
        MotivoSaida = "A conexão caiu e não foi possível voltar para a sala.";
        saindo = true;
        VoltarAoMenu();
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
        if (PhotonNetwork.InRoom)
        {   // sai de vez (sem guardar o lugar para voltar); OnLeftRoom volta para o menu
            if (OnlineManager.Instance != null) OnlineManager.Instance.SairDaSala(); else PhotonNetwork.LeaveRoom(false);
        }
        else VoltarAoMenu();
    }

    private void SairDoJogo()
    {
        saindo = true; fechandoJogo = true;
        StartCoroutine(SairDoJogoRotina());
    }

    // sai da sala de vez ANTES de fechar o jogo (fechando direto, o boneco ficava 30 s parado na sala dos outros)
    private IEnumerator SairDoJogoRotina()
    {
        if (PhotonNetwork.InRoom)
        {
            if (OnlineManager.Instance != null) OnlineManager.Instance.SairDaSala(); else PhotonNetwork.LeaveRoom(false);
            float t = 0f;
            while (PhotonNetwork.InRoom && t < 2f) { t += Time.unscaledDeltaTime; yield return null; }
        }
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
    public override void OnLeftRoom()
    {
        if (saindo && !fechandoJogo) VoltarAoMenu();   // só quando o jogador escolheu sair (queda é tratada pela reconexão)
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (saindo || cause == DisconnectCause.ApplicationQuit || cause == DisconnectCause.DisconnectByClientLogic) return;
        StartCoroutine(VerQueda());
    }

    private IEnumerator VerQueda()
    {
        yield return null;                  // deixa o OnlineManager decidir se vai tentar voltar
        var online = OnlineManager.Instance;
        if (online != null && online.Reconectando)
        {
            AvisarHud("A conexão caiu. Tentando voltar para a sala...");
            yield break;
        }
        if (saindo) yield break;
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
        txtJogadores.horizontalOverflow = HorizontalWrapMode.Wrap; txtJogadores.supportRichText = true;

        Botao(caixa.transform, "Continuar", BotaoCor, Fechar);
        btCopiar = Botao(caixa.transform, "Copiar código da sala", BotaoCor, CopiarCodigo);
        btAbrirSala = Botao(caixa.transform, "Abrir sala online", new Color32(47, 107, 85, 255), AbrirSalaOnline);
        txtAbrirSala = btAbrirSala.GetComponentInChildren<Text>();
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

    private GameObject Botao(Transform pai, string rotulo, Color cor, UnityEngine.Events.UnityAction acao)
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
        return go;
    }

    private static void Ancorar(RectTransform rt, Vector2 ancora, Vector2 pivo, Vector2 pos, Vector2 tam)
    {
        rt.anchorMin = rt.anchorMax = ancora; rt.pivot = pivo; rt.anchoredPosition = pos; rt.sizeDelta = tam;
    }
}
