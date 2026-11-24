using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Assembleia com votação (só no multiplayer, com 3+ jogadores e um infiltrado na sala).
///
/// • Como chamar: sempre no SINO da associação de moradores (ponto "Reunião de emergência").
///   - DENUNCIAR: depois de uma sabotagem ainda não discutida, qualquer um pode ir até o sino e denunciar.
///     Cada sabotagem rende no máximo uma assembleia (a próxima denúncia só depois de uma nova sabotagem).
///   - ASSEMBLEIA de emergência: sem sabotagem pendente, cada jogador pode convocar UMA por partida.
/// • Na assembleia todo mundo para, conversa e vota em quem acha que é o infiltrado (ou pula o voto).
///   Acaba quando todos votam ou o tempo acaba. O mais votado é DISPENSADO da organização; empate ou "pular" = ninguém sai.
/// • Dispensado vira espectador: fica transparente, não faz tarefa, não vota, não conta nas ações coletivas.
/// • Se o dispensado era o infiltrado, a comunidade vence. Se sobrarem só o infiltrado e mais um, a empresa vence.
///
/// Tudo vai pelas propriedades da sala (quem entra no meio recebe o estado certo); quem apura é o dono da sala.
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class Assembleia : MonoBehaviourPunCallbacks
{
    public const string CH_ATIVA = "ASM";      // object[] { id, inicio (PhotonNetwork.Time), quem chamou, motivo }
    public const string CH_DISPENSADOS = "EXP"; // int[] atores dispensados
    public const string CH_USOU = "USO";        // int[] quem já usou a convocação de emergência
    public const string CH_RESULTADO = "RES";   // object[] { id, ator dispensado (0 = ninguém), era o infiltrado }
    public const string CH_FIM = "FIM";         // "comunidade" ou "empresa"
    public const string CH_SAB_CONTAGEM = "SABC"; // int: quantas sabotagens já aconteceram na partida
    public const string CH_SAB_AREA = "SABA";     // string: onde foi a última
    public const string CH_DENUNCIADAS = "DEN";   // int: até qual sabotagem já houve assembleia
    private const string CH_VOTO = "V";         // (jogador) object[] { id da assembleia, ator votado (0 = pulou) }

    [SerializeField] private float duracao = 75f;          // conversa + votação
    [SerializeField] private float raioAssociacao = 2.6f;

    public static bool Aberto { get; private set; }
    private static readonly HashSet<int> dispensados = new HashSet<int>();
    public static bool Dispensado(int ator) => ator > 0 && dispensados.Contains(ator);
    public static bool LocalDispensado => PhotonNetwork.InRoom && Dispensado(PhotonNetwork.LocalPlayer.ActorNumber);

    private static readonly Color[] CoresJogador =
    {
        new Color32(79, 154, 70, 255), new Color32(58, 127, 196, 255), new Color32(224, 123, 46, 255),
        new Color32(138, 91, 184, 255), new Color32(232, 195, 58, 255), new Color32(47, 179, 176, 255),
    };

    private GerenciadorAmbiental gm;
    private HudAmbiental hud;
    private Transform pontoReuniao;
    private bool avisouSolo;
    private int idAtivo; private double inicio; private string motivo = "";
    private int ultimoResultadoMostrado = -1;
    private float resultadoAte;

    // interface
    private Font fonte;
    private GameObject botao, painel, painelResultado;
    private Text txtBotao, txtTitulo, txtMotivo, txtTempo, txtMeuVoto, txtResultado;
    private readonly List<(GameObject go, Text nome, Text estado, Button votar, Image fundo)> cartoes = new List<(GameObject, Text, Text, Button, Image)>();
    private Button btPular;

    private void Awake() { Aberto = false; dispensados.Clear(); }
    private void OnDestroy() { Aberto = false; dispensados.Clear(); }

    private void Start()
    {
        gm = GetComponent<GerenciadorAmbiental>();
        hud = GetComponent<HudAmbiental>();
        var p = FindObjectsByType<PontoDeTarefa>(FindObjectsSortMode.None).FirstOrDefault(x => x.EhReuniao);
        if (p != null) pontoReuniao = p.transform;
        fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Construir();
        if (PhotonNetwork.InRoom) LerTudo(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    // ------------------------------------------------------------------ regras
    private bool MultiplayerValido =>
        PhotonNetwork.InRoom && Ativos.Count() >= 3 && gm != null && gm.TemSabotadorNaSala;

    /// Jogadores presentes (quem caiu fica guardado na sala por um tempo, mas não conta).
    private static IEnumerable<Player> Ativos => PhotonNetwork.PlayerList.Where(p => !p.IsInactive);

    private bool PertoDaAssociacao()
    {
        if (pontoReuniao == null) return false;
        var eu = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
                 .FirstOrDefault(pm => !PhotonNetwork.InRoom || (pm.GetComponent<PhotonView>() is PhotonView v && v.AmOwner));
        return eu != null && Vector2.Distance(eu.transform.position, pontoReuniao.position) <= raioAssociacao;
    }

    private static int ContagemSabotagens()
    {
        var r = PhotonNetwork.CurrentRoom;
        return r != null && r.CustomProperties.TryGetValue(CH_SAB_CONTAGEM, out object v) && v is int n ? n : 0;
    }

    /// Há sabotagem que ainda não foi discutida em assembleia?
    private static bool SabotagemPendente()
    {
        var r = PhotonNetwork.CurrentRoom;
        int den = r != null && r.CustomProperties.TryGetValue(CH_DENUNCIADAS, out object v) && v is int n ? n : 0;
        return ContagemSabotagens() > den;
    }

    private bool JaUsei()
    {
        var r = PhotonNetwork.CurrentRoom;
        return r != null && r.CustomProperties.TryGetValue(CH_USOU, out object v) && v is int[] a && a.Contains(PhotonNetwork.LocalPlayer.ActorNumber);
    }

    private void Update()
    {
        if (painelResultado.activeSelf && Time.time > resultadoAte) painelResultado.SetActive(false);

        // assembleia em andamento: atualiza o painel e o dono da sala apura (mesmo se alguém saiu no meio)
        if (Aberto && PhotonNetwork.InRoom)
        {
            AtualizarPainel();
            if (PhotonNetwork.IsMasterClient && (PhotonNetwork.Time - inicio >= duracao || TodosVotaram())) Apurar();
        }

        bool perto = PertoDaAssociacao();
        if (!MultiplayerValido)
        {
            botao.SetActive(false);
            if (perto && !avisouSolo && hud != null && (!PhotonNetwork.InRoom || Ativos.Count() < 3))
            {
                hud.Aviso("A assembleia com votação só existe no multiplayer (3 ou mais jogadores).", HudAmbiental.Cinza, 5f);
                avisouSolo = true;
            }
            if (!perto) avisouSolo = false;
            return;
        }

        // botão de convocar / denunciar
        bool podeAgir = !Aberto && !gm.PartidaEncerrada && !LocalDispensado && !MenuPartida.Aberto && !HudAmbiental.PainelAberto;
        bool denuncia = perto && SabotagemPendente();
        bool emergencia = perto && !JaUsei();
        botao.SetActive(podeAgir && (denuncia || emergencia));
        txtBotao.text = denuncia ? "DENUNCIAR" : "ASSEMBLEIA";
        var kb = UnityEngine.InputSystem.Keyboard.current;                 // atalho: R
        if (botao.activeSelf && kb != null && kb.rKey.wasPressedThisFrame) Convocar();
    }

    private void Convocar()
    {
        if (!MultiplayerValido || Aberto) return;
        if (!PertoDaAssociacao()) return;
        bool denuncia = SabotagemPendente();
        if (!denuncia && JaUsei()) return;
        var props = PhotonNetwork.CurrentRoom.CustomProperties;
        string area = props.TryGetValue(CH_SAB_AREA, out object ar) && ar is string sa ? sa : "algum lugar";
        string quem = PhotonNetwork.LocalPlayer.NickName;
        string mot = denuncia ? $"{quem} tocou o sino e denunciou a sabotagem em {area}" : $"{quem} convocou uma assembleia de emergência";
        int id = Random.Range(1, int.MaxValue);
        var h = new Hashtable { { CH_ATIVA, new object[] { id, PhotonNetwork.Time, PhotonNetwork.LocalPlayer.ActorNumber, mot } } };
        h[CH_DENUNCIADAS] = ContagemSabotagens();          // qualquer assembleia já discute as sabotagens até aqui
        if (!denuncia)
        {
            var usou = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(CH_USOU, out object v) && v is int[] a ? a.ToList() : new List<int>();
            usou.Add(PhotonNetwork.LocalPlayer.ActorNumber);
            h[CH_USOU] = usou.ToArray();
        }
        PhotonNetwork.CurrentRoom.SetCustomProperties(h);
    }

    private void Votar(int alvo)
    {
        if (!Aberto || LocalDispensado) return;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { CH_VOTO, new object[] { idAtivo, alvo } } });
    }

    private bool VotoDe(Player p, out int alvo)
    {
        alvo = 0;
        if (p.CustomProperties.TryGetValue(CH_VOTO, out object v) && v is object[] a && a.Length == 2 && a[0] is int id && id == idAtivo && a[1] is int t)
        { alvo = t; return true; }
        return false;
    }

    private IEnumerable<Player> Votantes => Ativos.Where(p => !Dispensado(p.ActorNumber));

    private bool TodosVotaram() => Votantes.All(p => VotoDe(p, out _));

    private void Apurar()
    {
        var contagem = new Dictionary<int, int>();
        foreach (var p in Votantes)
            if (VotoDe(p, out int alvo)) contagem[alvo] = (contagem.TryGetValue(alvo, out int c) ? c : 0) + 1;
        int pulos = contagem.TryGetValue(0, out int pz) ? pz : 0;
        var candidatos = contagem.Where(kv => kv.Key != 0).OrderByDescending(kv => kv.Value).ToList();
        int dispensar = 0;
        if (candidatos.Count > 0)
        {
            int topo = candidatos[0].Value;
            bool empate = candidatos.Count(kv => kv.Value == topo) > 1;
            if (!empate && topo > pulos) dispensar = candidatos[0].Key;
        }
        int sab = gm.AtorSabotador;
        bool eraSab = dispensar != 0 && dispensar == sab;

        var novos = dispensados.ToList();
        if (dispensar != 0) novos.Add(dispensar);
        var h = new Hashtable
        {
            { CH_ATIVA, null },
            { CH_DISPENSADOS, novos.ToArray() },
            { CH_RESULTADO, new object[] { idAtivo, dispensar, eraSab } },
        };
        if (eraSab) h[CH_FIM] = "comunidade";
        else if (sab > 0 && Ativos.Count(p => !novos.Contains(p.ActorNumber)) <= 2) h[CH_FIM] = "empresa";
        PhotonNetwork.CurrentRoom.SetCustomProperties(h);
    }

    // ------------------------------------------------------------------ Photon
    public override void OnRoomPropertiesUpdate(Hashtable mudou) => LerTudo(mudou);

    public override void OnPlayerLeftRoom(Player saiu)
    {
        // se a saída deixa só o infiltrado e mais um, a empresa vence (o dono da sala decide)
        if (!PhotonNetwork.IsMasterClient || gm == null || gm.PartidaEncerrada) return;
        int sab = gm.AtorSabotador;
        if (sab > 0 && sab != saiu.ActorNumber && Ativos.Count() >= 2 &&
            Ativos.Count(p => !Dispensado(p.ActorNumber)) <= 2 && dispensados.Count > 0)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { CH_FIM, "empresa" } });
    }

    private void LerTudo(Hashtable h)
    {
        if (h.ContainsKey(CH_DISPENSADOS))
        {
            dispensados.Clear();
            if (h[CH_DISPENSADOS] is int[] a) foreach (int x in a) dispensados.Add(x);
        }
        if (h.ContainsKey(CH_ATIVA))
        {
            if (h[CH_ATIVA] is object[] s && s.Length == 4)
            {
                idAtivo = (int)s[0]; inicio = (double)s[1]; motivo = s[3] as string ?? "";
                Abrir();
            }
            else Fechar();
        }
        if (h.ContainsKey(CH_RESULTADO) && h[CH_RESULTADO] is object[] r && r.Length == 3 && (int)r[0] != ultimoResultadoMostrado)
        {
            ultimoResultadoMostrado = (int)r[0];
            MostrarResultado((int)r[1], (bool)r[2]);
        }
        if (h.ContainsKey(CH_FIM) && h[CH_FIM] is string fim && gm != null)
        {
            if (fim == "comunidade") gm.EncerrarPorAssembleia(true, "O infiltrado foi descoberto! A comunidade venceu!");
            else gm.EncerrarPorAssembleia(false, "A comunidade se desarticulou: a empresa venceu.");
        }
        if (h.ContainsKey("REI")) { Fechar(); painelResultado.SetActive(false); ultimoResultadoMostrado = -1; }   // nova rodada
    }

    // ------------------------------------------------------------------ painel
    private void Abrir()
    {
        Aberto = true;
        painel.SetActive(true);
        var mg = GetComponent<MinigameTarefa>();
        if (MinigameTarefa.Aberto && mg != null) mg.Fechar();
        txtMotivo.text = motivo;
        MontarCartoes();
    }

    private void Fechar()
    {
        Aberto = false;
        if (painel != null) painel.SetActive(false);
    }

    private void MontarCartoes()
    {
        var jogadores = Ativos.OrderBy(p => p.ActorNumber).ToArray();
        for (int i = 0; i < cartoes.Count; i++)
        {
            var c = cartoes[i];
            c.go.SetActive(i < jogadores.Length);
            if (i >= jogadores.Length) continue;
            var p = jogadores[i];
            int ator = p.ActorNumber;
            c.nome.text = (string.IsNullOrEmpty(p.NickName) ? "Jogador " + ator : p.NickName) + (p.IsLocal ? " (você)" : "");
            c.fundo.color = CoresJogador[(ator - 1 + CoresJogador.Length) % CoresJogador.Length];
            c.votar.onClick.RemoveAllListeners();
            c.votar.onClick.AddListener(() => Votar(ator));
            c.go.name = "Cartao_" + ator;
        }
    }

    private void AtualizarPainel()
    {
        float resta = Mathf.Max(0f, duracao - (float)(PhotonNetwork.Time - inicio));
        txtTempo.text = $"{Mathf.CeilToInt(resta)}s";
        var jogadores = Ativos.OrderBy(p => p.ActorNumber).ToArray();
        bool euVotei = VotoDe(PhotonNetwork.LocalPlayer, out int meu);
        bool possoVotar = !LocalDispensado && !euVotei;
        for (int i = 0; i < cartoes.Count && i < jogadores.Length; i++)
        {
            var p = jogadores[i]; var c = cartoes[i];
            bool fora = Dispensado(p.ActorNumber);
            c.estado.text = fora ? "dispensado(a)" : VotoDe(p, out _) ? "já votou" : "pensando...";
            c.votar.gameObject.SetActive(possoVotar && !fora && !p.IsLocal);
            c.go.GetComponent<CanvasGroup>().alpha = fora ? 0.4f : 1f;
        }
        btPular.gameObject.SetActive(possoVotar);
        if (LocalDispensado) txtMeuVoto.text = "Você foi dispensado(a): só acompanha a assembleia.";
        else if (euVotei)
        {
            var alvo = meu == 0 ? null : PhotonNetwork.CurrentRoom.GetPlayer(meu);
            txtMeuVoto.text = meu == 0 ? "Você pulou o voto." : $"Seu voto: {alvo?.NickName ?? "?"}";
        }
        else txtMeuVoto.text = "Conversem: quem estava perto da sabotagem? Quem fingiu fazer tarefa?";
    }

    private void MostrarResultado(int ator, bool eraSab)
    {
        string texto;
        if (ator == 0) texto = "Ninguém foi dispensado (empate ou a maioria pulou o voto).";
        else
        {
            var p = PhotonNetwork.CurrentRoom?.GetPlayer(ator);
            string nome = p != null && !string.IsNullOrEmpty(p.NickName) ? p.NickName : "Jogador " + ator;
            texto = eraSab ? $"{nome} foi dispensado(a)... e ERA o infiltrado da empresa!"
                           : $"{nome} foi dispensado(a), mas era morador(a). A desconfiança também enfraquece a comunidade.";
        }
        txtResultado.text = texto;
        txtResultado.color = ator != 0 && eraSab ? HudAmbiental.Verde : ator == 0 ? Color.white : HudAmbiental.Vermelho;
        painelResultado.SetActive(true);
        resultadoAte = Time.time + 6f;
    }

    // ------------------------------------------------------------------ construção da interface
    private void Construir()
    {
        var cv = new GameObject("Assembleia UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cv.transform.SetParent(transform, false);
        var canvas = cv.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 60;
        var sc = cv.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        var raiz = (RectTransform)cv.transform;

        // botão redondo acima do USAR
        botao = Ret(raiz, "Botao", new Color32(217, 72, 59, 255), new Vector2(1, 0), new Vector2(-135, 330), new Vector2(150, 150)).gameObject;
        botao.GetComponent<Image>().sprite = Circulo(); botao.GetComponent<Image>().raycastTarget = true;
        txtBotao = Txt((RectTransform)botao.transform, 22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        botao.AddComponent<Button>().onClick.AddListener(Convocar);
        botao.SetActive(false);

        // painel da assembleia
        painel = Ret(raiz, "Painel", new Color(0, 0, 0, 0.75f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(4000, 3000)).gameObject;
        painel.GetComponent<Image>().raycastTarget = true;
        var cx = Ret((RectTransform)painel.transform, "Caixa", new Color32(26, 46, 40, 250), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1400, 820));
        txtTitulo = Txt(cx, 40, FontStyle.Bold, HudAmbiental.Amarelo, TextAnchor.UpperCenter, new Vector2(0, -30), new Vector2(1300, 56));
        txtTitulo.text = "ASSEMBLEIA DE EMERGÊNCIA";
        txtMotivo = Txt(cx, 24, FontStyle.Italic, Color.white, TextAnchor.UpperCenter, new Vector2(0, -92), new Vector2(1300, 36));
        txtTempo = Txt(cx, 34, FontStyle.Bold, Color.white, TextAnchor.UpperRight, new Vector2(-30, -30), new Vector2(160, 50), new Vector2(1, 1));
        for (int i = 0; i < 6; i++)
        {
            int col = i % 2, lin = i / 2;
            var c = Ret(cx, "Cartao" + i, new Color(1, 1, 1, 0.08f), new Vector2(.5f, 1), new Vector2(-330 + col * 660, -200 - lin * 150), new Vector2(620, 128));
            c.gameObject.AddComponent<CanvasGroup>();
            var bolinha = Ret(c, "Cor", Color.white, new Vector2(0, .5f), new Vector2(56, 0), new Vector2(72, 72));
            bolinha.GetComponent<Image>().sprite = Circulo();
            var meio = new Vector2(.5f, .5f);
            var nome = Txt(c, 28, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft, new Vector2(-30, 18), new Vector2(380, 40), meio);
            var estado = Txt(c, 20, FontStyle.Italic, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleLeft, new Vector2(-30, -22), new Vector2(380, 30), meio);
            var bv = Ret(c, "Votar", new Color32(232, 131, 58, 255), new Vector2(1, .5f), new Vector2(-90, 0), new Vector2(150, 64));
            bv.GetComponent<Image>().raycastTarget = true;
            Txt(bv, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).text = "VOTAR";
            cartoes.Add((c.gameObject, nome, estado, bv.gameObject.AddComponent<Button>(), bolinha.GetComponent<Image>()));
        }
        var bp = Ret(cx, "Pular", new Color(1, 1, 1, 0.18f), new Vector2(.5f, 0), new Vector2(0, 110), new Vector2(300, 64));
        bp.GetComponent<Image>().raycastTarget = true;
        Txt(bp, 24, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter).text = "PULAR VOTO";
        btPular = bp.gameObject.AddComponent<Button>(); btPular.onClick.AddListener(() => Votar(0));
        txtMeuVoto = Txt(cx, 22, FontStyle.Normal, HudAmbiental.Cal, TextAnchor.MiddleCenter, new Vector2(0, 40), new Vector2(1300, 40), new Vector2(.5f, 0));
        painel.SetActive(false);

        // resultado da votação
        painelResultado = Ret(raiz, "Resultado", new Color32(20, 16, 14, 235), new Vector2(.5f, .5f), new Vector2(0, 120), new Vector2(1300, 130)).gameObject;
        txtResultado = Txt((RectTransform)painelResultado.transform, 32, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        txtResultado.horizontalOverflow = HorizontalWrapMode.Wrap;
        painelResultado.SetActive(false);
    }

    private RectTransform Ret(RectTransform pai, string nome, Color cor, Vector2 ancora, Vector2 pos, Vector2 tam)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = ancora; rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = pos; rt.sizeDelta = tam;
        var img = go.GetComponent<Image>(); img.color = cor; img.raycastTarget = false;
        return rt;
    }

    /// pos/size = posição e tamanho; ancora (padrão: topo do pai) também vira o pivô. Sem pos, o texto ocupa o pai inteiro.
    private Text Txt(RectTransform pai, int tam, FontStyle estilo, Color cor, TextAnchor al, Vector2? pos = null, Vector2? size = null, Vector2? ancora = null)
    {
        var go = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        if (pos.HasValue)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = ancora ?? new Vector2(.5f, 1);
            rt.anchoredPosition = pos.Value; rt.sizeDelta = size ?? new Vector2(400, 40);
        }
        else { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        var t = go.GetComponent<Text>();
        t.font = fonte; t.fontSize = tam; t.fontStyle = estilo; t.color = cor; t.alignment = al; t.raycastTarget = false;
        return t;
    }

    private static Sprite circulo;
    private static Sprite Circulo()
    {
        if (circulo != null) return circulo;
        const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(N / 2f, N / 2f));
                px[y * N + x] = new Color(1, 1, 1, Mathf.Clamp01(N / 2f - d));
            }
        tex.SetPixels(px); tex.Apply();
        circulo = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 100f);
        return circulo;
    }
}
