using System.Collections.Generic;
using System.Linq;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Ações extras do infiltrado da empresa (além de SABOTAR), todas sem violência, ligadas ao tema:
///
/// • COMPRAR um morador (perto dele): a empresa pressiona o morador e ele fica calado por 60 s
///   (não conta a pista nem apoia ninguém). O balão dele fica cinza. Recarga: 60 s.
/// • BOATO na rádio (de qualquer lugar): a Rádio Progresso espalha uma notícia falsa e o MAPA some da tela
///   dos moradores por 20 s (como "apagar as luzes" no Among Us). Recarga: 90 s.
/// • INTERDITAR uma tarefa ainda não feita (perto dela): a empresa põe uma "obra" no local e a tarefa fica
///   bloqueada até DUAS pessoas juntas segurarem EMBARGAR ali. No máximo 2 interdições ao mesmo tempo. Recarga: 60 s.
/// • BUEIROS: perto de um bueiro, o infiltrado entra e sai no próximo (5 bueiros pelas ruas). Recarga: 15 s.
///   Os bueiros aparecem para todo mundo; quem vir alguém sumir num bueiro já sabe de quem desconfiar.
///
/// Teclas do infiltrado: F = ação do lugar (comprar / interditar / bueiro), B = boato. Também há botões na tela.
/// Tudo vai pelas propriedades da sala, então todos veem o mesmo efeito (e quem entra no meio também).
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class PoderesInfiltrado : MonoBehaviourPunCallbacks
{
    public const string PREFIXO_COMPRADO = "CMP|";   // + nome do morador → double: até quando fica calado (PhotonNetwork.Time)
    public const string PREFIXO_INTERDITADA = "INT|"; // + id da tarefa → true
    public const string CH_BOATO = "BOA";             // double: até quando dura o boato

    [SerializeField] private float duracaoCompra = 60f, recargaCompra = 60f;
    [SerializeField] private float duracaoBoato = 20f, recargaBoato = 90f;
    [SerializeField] private float recargaInterdicao = 60f;
    [SerializeField] private int maxInterdicoes = 2;
    [SerializeField] private float recargaBueiro = 15f;
    [SerializeField] private float raioBueiro = 1.3f;

    /// Bueiros nas ruas (posições do mundo, em cima da terra batida).
    private static readonly (string nome, Vector2 pos)[] Bueiros =
    {
        ("rua de cima, oeste", new Vector2(8f, 50f)),
        ("rua de cima, leste", new Vector2(68f, 50f)),
        ("rua do meio, perto da ponte", new Vector2(50f, 27f)),
        ("rua de baixo, oeste", new Vector2(20f, -21f)),
        ("rua de baixo, leste", new Vector2(78f, -21f)),
    };

    private static readonly Dictionary<string, object> estadoOffline = new Dictionary<string, object>();
    private static PoderesInfiltrado instancia;

    private GerenciadorAmbiental gm;
    private HudAmbiental hud;
    private float prontoCompra, prontoBoato, prontoInterdicao, prontoBueiro;
    private double boatoAvisado;

    // marcas no mundo
    private readonly Dictionary<string, GameObject> obras = new Dictionary<string, GameObject>();
    private static Sprite spriteObra, spriteBueiro;

    // botões na tela (só o infiltrado vê)
    private CanvasScaler escala;
    private GameObject btBoato, btLugar;
    private Image imgBoato, imgLugar;
    private Text txtBoato, txtLugar;
    private bool clicouBoato, clicouLugar;

    private static readonly Color Vermelho = new Color32(205, 70, 55, 255);
    private static readonly Color Apagado = new Color(0.45f, 0.2f, 0.18f, 0.6f);

    // ------------------------------------------------------------------ consultas (usadas por outros scripts)
    private static double Agora => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;

    private static object Ler(string chave)
    {
        if (PhotonNetwork.InRoom)
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(chave, out object v) ? v : null;
        return estadoOffline.TryGetValue(chave, out object o) ? o : null;
    }

    private static void Gravar(string chave, object valor)
    {
        if (PhotonNetwork.InRoom) { PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { chave, valor } }); return; }
        if (valor == null) estadoOffline.Remove(chave); else estadoOffline[chave] = valor;
    }

    /// O morador foi "comprado" pela empresa e está calado? (segundos que faltam)
    public static bool MoradorComprado(string nome, out int segundos)
    {
        segundos = 0;
        if (!(Ler(PREFIXO_COMPRADO + nome) is double ate)) return false;
        double falta = ate - Agora;
        if (falta <= 0) return false;
        segundos = Mathf.CeilToInt((float)falta);
        return true;
    }

    public static bool Interditada(string idTarefa) => Ler(PREFIXO_INTERDITADA + idTarefa) is bool b && b;

    /// Duas pessoas embargaram a obra: a tarefa volta a ficar livre.
    public static void Desinterditar(string idTarefa) => Gravar(PREFIXO_INTERDITADA + idTarefa, null);

    public static bool BoatoAtivo => Ler(CH_BOATO) is double ate && ate > Agora;

    /// O boato tira o mapa da tela dos moradores (o infiltrado continua vendo).
    public static bool BoatoBloqueiaMapa
    {
        get
        {
            if (!BoatoAtivo) return false;
            var gm = GerenciadorAmbiental.Instancia;
            return gm == null || !gm.SouSabotador;
        }
    }

    /// Chamado no reinício da partida (dono da sala): apaga compras, interdições e boato.
    public static void LimparPara(Hashtable limpar)
    {
        if (PhotonNetwork.InRoom)
            foreach (var k in PhotonNetwork.CurrentRoom.CustomProperties.Keys.OfType<string>())
                if (k.StartsWith(PREFIXO_COMPRADO) || k.StartsWith(PREFIXO_INTERDITADA)) limpar[k] = null;
        limpar[CH_BOATO] = null;
    }

    public static void LimparOffline() => estadoOffline.Clear();

    // ------------------------------------------------------------------ ciclo
    private void Awake() { instancia = this; estadoOffline.Clear(); }
    private void OnDestroy() { if (instancia == this) instancia = null; }

    private void Start()
    {
        gm = GetComponent<GerenciadorAmbiental>();
        hud = GetComponent<HudAmbiental>();
        CriarBueiros();
        ConstruirBotoes();
    }

    private void Update()
    {
        AtualizarObras();
        AvisarBoato();
        if (escala != null) escala.matchWidthOrHeight = (float)Screen.width / Mathf.Max(1, Screen.height) < 16f / 9f ? 0f : 1f;

        bool posso = gm != null && gm.SouSabotador && gm.PartidaIniciada && !gm.PartidaEncerrada && !Assembleia.LocalDispensado
                     && !MenuPartida.Aberto && !MinigameTarefa.Aberto && !HudAmbiental.PainelAberto && !Assembleia.Aberto;
        if (!posso) { btBoato.SetActive(false); btLugar.SetActive(false); clicouBoato = clicouLugar = false; return; }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        bool apertouB = (kb != null && kb.bKey.wasPressedThisFrame) || clicouBoato;
        bool apertouF = (kb != null && kb.fKey.wasPressedThisFrame) || clicouLugar;
        clicouBoato = clicouLugar = false;

        // BOATO: de qualquer lugar
        float fBoato = prontoBoato - Time.time;
        btBoato.SetActive(true);
        imgBoato.color = fBoato <= 0 ? Vermelho : Apagado;
        txtBoato.text = fBoato > 0 ? Mathf.CeilToInt(fBoato) + "s" : "BOATO\n(B)";
        if (apertouB && fBoato <= 0) EspalharBoato();

        // ação do lugar: bueiro > morador > tarefa
        string rotulo = null; float falta = 0f; System.Action acao = null;
        int bueiro = BueiroPerto();
        var morador = gm.MoradorPerto;
        var ponto = gm.PontoPerto;
        if (bueiro >= 0)
        {
            rotulo = "BUEIRO"; falta = prontoBueiro - Time.time;
            acao = () => UsarBueiro(bueiro);
        }
        else if (morador != null)
        {
            rotulo = "COMPRAR"; falta = prontoCompra - Time.time;
            if (MoradorComprado(morador.Nome, out _)) { rotulo = null; }
            else acao = () => Comprar(morador);
        }
        else if (ponto != null && !ponto.Feita && !ponto.EhReuniao && !Interditada(ponto.Id))
        {
            rotulo = "INTERDITAR"; falta = prontoInterdicao - Time.time;
            acao = () => Interditar(ponto);
        }

        btLugar.SetActive(rotulo != null);
        if (rotulo == null) return;
        imgLugar.color = falta <= 0 ? Vermelho : Apagado;
        txtLugar.text = falta > 0 ? Mathf.CeilToInt(falta) + "s" : rotulo + "\n(F)";
        if (apertouF && falta <= 0) acao?.Invoke();
    }

    // ------------------------------------------------------------------ ações
    private void Comprar(Morador m)
    {
        Gravar(PREFIXO_COMPRADO + m.Nome, Agora + duracaoCompra);
        prontoCompra = Time.time + recargaCompra;
        hud.Aviso($"Você pressionou {m.Nome}: ela/ele vai ficar calado(a) por {Mathf.RoundToInt(duracaoCompra)} s.", HudAmbiental.Vermelho, 4f);
    }

    private void EspalharBoato()
    {
        Gravar(CH_BOATO, Agora + duracaoBoato);
        prontoBoato = Time.time + recargaBoato;
    }

    private void Interditar(PontoDeTarefa p)
    {
        int ativas = gm.Pontos.Count(x => Interditada(x.Id));
        if (ativas >= maxInterdicoes)
        {
            hud.Aviso($"Já há {maxInterdicoes} obras da empresa na vila. Espere a comunidade embargar uma.", HudAmbiental.Cinza, 4f);
            return;
        }
        Gravar(PREFIXO_INTERDITADA + p.Id, true);
        prontoInterdicao = Time.time + recargaInterdicao;
    }

    private void UsarBueiro(int i)
    {
        int destino = (i + 1) % Bueiros.Length;
        var eu = JogadorLocal();
        if (eu == null) return;
        Vector2 alvo = Bueiros[destino].pos + new Vector2(0f, -0.6f);
        var rb = eu.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.position = alvo; rb.linearVelocity = Vector2.zero; }
        eu.transform.position = new Vector3(alvo.x, alvo.y, eu.transform.position.z);
        prontoBueiro = Time.time + recargaBueiro;
        hud.Aviso($"Você saiu pelo bueiro da {Bueiros[destino].nome}.", HudAmbiental.Cinza, 3f);
    }

    // ------------------------------------------------------------------ efeitos para todos
    private void AvisarBoato()
    {
        if (!(Ler(CH_BOATO) is double ate) || ate <= Agora || ate == boatoAvisado) return;
        boatoAvisado = ate;
        if (gm == null || !gm.PartidaIniciada) return;
        hud.Fala("RÁDIO PROGRESSO (da empresa)",
                 "Atenção, moradores: a associação está espalhando mentiras! Não acreditem em mapas nem em reuniões.",
                 HudAmbiental.Vermelho, 7f);
        if (!gm.SouSabotador)
            hud.Aviso($"Boato na rádio! O mapa da vila saiu do ar por {Mathf.RoundToInt(duracaoBoato)} s.", HudAmbiental.Vermelho, 5f);
    }

    private void AtualizarObras()
    {
        if (gm == null || gm.Pontos == null) return;
        foreach (var p in gm.Pontos)
        {
            bool tem = Interditada(p.Id) && !p.Feita;
            obras.TryGetValue(p.Id, out var go);
            if (tem && go == null)
            {
                go = new GameObject("Obra da empresa - " + p.nome);
                go.transform.position = p.transform.position + new Vector3(0f, -0.2f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteObra(); sr.sortingOrder = 10; sr.spriteSortPoint = SpriteSortPoint.Pivot;
                obras[p.Id] = go;
            }
            else if (!tem && go != null) { Destroy(go); obras.Remove(p.Id); }
        }
    }

    // ------------------------------------------------------------------ bueiros
    private void CriarBueiros()
    {
        var raiz = new GameObject("Bueiros").transform;
        raiz.SetParent(transform, false);
        foreach (var b in Bueiros)
        {
            var go = new GameObject("Bueiro - " + b.nome);
            go.transform.SetParent(raiz, false);
            go.transform.position = b.pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteBueiro(); sr.sortingOrder = 1;          // no chão, embaixo de tudo
        }
    }

    private int BueiroPerto()
    {
        var eu = JogadorLocal();
        if (eu == null) return -1;
        for (int i = 0; i < Bueiros.Length; i++)
            if (Vector2.Distance(eu.position, Bueiros[i].pos) <= raioBueiro) return i;
        return -1;
    }

    private static Transform JogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!PhotonNetwork.InRoom) return pm.transform;
            var pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.AmOwner) return pm.transform;
        }
        return null;
    }

    // ------------------------------------------------------------------ interface
    private void ConstruirBotoes()
    {
        var cv = new GameObject("Poderes do infiltrado", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cv.transform.SetParent(transform, false);
        var canvas = cv.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 101;
        escala = cv.GetComponent<CanvasScaler>();
        escala.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; escala.referenceResolution = new Vector2(1920, 1080);
        var raiz = (RectTransform)cv.transform;
        // ao lado do SABOTAR (que fica em -250, 40 com 150 px)
        btLugar = Botao(raiz, "Lugar", new Vector2(-260, 210), 130, out imgLugar, out txtLugar, () => clicouLugar = true);
        btBoato = Botao(raiz, "Boato", new Vector2(-420, 50), 130, out imgBoato, out txtBoato, () => clicouBoato = true);
        btLugar.SetActive(false); btBoato.SetActive(false);
    }

    private static GameObject Botao(RectTransform pai, string nome, Vector2 pos, float tam, out Image img, out Text txt, UnityEngine.Events.UnityAction clique)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform; rt.SetParent(pai, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(tam, tam);
        img = go.GetComponent<Image>(); img.sprite = HudAmbiental.SpriteCirculo(); img.color = Vermelho;
        go.GetComponent<Button>().onClick.AddListener(clique);
        var t = new GameObject("Texto", typeof(RectTransform), typeof(Text));
        var trt = (RectTransform)t.transform; trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        txt = t.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 20; txt.fontStyle = FontStyle.Bold; txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter; txt.raycastTarget = false;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap; txt.verticalOverflow = VerticalWrapMode.Overflow;
        return go;
    }

    // ------------------------------------------------------------------ desenhos (feitos no código)
    /// Cavalete listrado laranja e branco com placa "OBRA" (pivô embaixo).
    private static Sprite SpriteObra()
    {
        if (spriteObra != null) return spriteObra;
        const int W = 96, H = 80;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        Color tinta = new Color32(42, 30, 26, 255), laranja = new Color32(240, 120, 40, 255), branco = new Color32(250, 245, 235, 255);
        Color cinza = new Color32(110, 110, 110, 255), sombra = new Color(0, 0, 0, 0.25f);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Color c = Color.clear;
                // sombra no chão
                float sx = (x - 48f) / 44f, sy = (y - 5f) / 5f;
                if (sx * sx + sy * sy <= 1f) c = sombra;
                // pés do cavalete
                if ((Mathf.Abs(x - 18) <= 3 || Mathf.Abs(x - 78) <= 3) && y >= 4 && y <= 46) c = (Mathf.Abs(x - 18) == 3 || Mathf.Abs(x - 78) == 3) ? tinta : cinza;
                // tábua listrada
                if (x >= 6 && x <= 90 && y >= 30 && y <= 50)
                {
                    bool borda = x == 6 || x == 90 || y == 30 || y == 50;
                    c = borda ? tinta : (((x + y) / 10) % 2 == 0 ? laranja : branco);
                }
                // placa em cima
                if (x >= 26 && x <= 70 && y >= 54 && y <= 76)
                {
                    bool borda = x == 26 || x == 70 || y == 54 || y == 76;
                    c = borda ? tinta : new Color32(250, 205, 60, 255);
                }
                px[y * W + x] = c;
            }
        // "OBRA" em blocos de pixel
        string[] letras =
        {
            "0110 1110 1110 0110",
            "1001 1001 1001 1001",
            "1001 1110 1110 1111",
            "1001 1001 1010 1001",
            "0110 1110 1001 1001",
        };
        for (int l = 0; l < letras.Length; l++)
        {
            string linha = letras[l].Replace(" ", "0");
            for (int i = 0; i < linha.Length; i++)
                if (linha[i] == '1')
                    for (int dy = 0; dy < 3; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int xx = 29 + i * 2 + dx, yy = 72 - l * 3 - dy;
                            if (xx < W && yy >= 0) px[yy * W + xx] = tinta;
                        }
        }
        tex.SetPixels(px); tex.Apply();
        spriteObra = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0f), 64f);
        return spriteObra;
    }

    /// Tampa de bueiro redonda com grade (pivô no centro).
    private static Sprite SpriteBueiro()
    {
        if (spriteBueiro != null) return spriteBueiro;
        const int W = 64; const int SS = 3;
        var tex = new Texture2D(W, W, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * W];
        Color tinta = new Color32(42, 30, 26, 255), ferro = new Color32(92, 96, 100, 255), fundo = new Color32(30, 32, 34, 255);
        for (int y = 0; y < W; y++)
            for (int x = 0; x < W; x++)
            {
                float cob = 0f, borda = 0f, buraco = 0f;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float fx = x + (sx + .5f) / SS - 32f, fy = y + (sy + .5f) / SS - 32f;
                        float r = Mathf.Sqrt(fx * fx + fy * fy);
                        if (r <= 30f) cob++;
                        if (r <= 30f && r > 26f) borda++;
                        bool grade = r <= 22f && (Mathf.Abs(fx) % 8f < 3f) && Mathf.Abs(fy) < Mathf.Sqrt(Mathf.Max(0, 22f * 22f - fx * fx)) - 2f;
                        if (grade) buraco++;
                    }
                float n = SS * SS; cob /= n; borda /= n; buraco /= n;
                Color c = Color.Lerp(ferro, tinta, borda);
                c = Color.Lerp(c, fundo, buraco);
                c.a = cob;
                px[y * W + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        spriteBueiro = Sprite.Create(tex, new Rect(0, 0, W, W), new Vector2(0.5f, 0.5f), 48f);
        return spriteBueiro;
    }
}
