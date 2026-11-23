using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// A comunidade do território (NPCs), o discurso da empresa e a "pressão econômica".
///
/// Pedagogia dos afetados (Stortti &amp; Sanchez, 2017): quem sabe o que acontece no território é quem vive nele.
/// • Cada morador guarda um SABER (o que viu, o que os mais velhos contam, o que a maré mostra).
///   Escutar o morador (segurar FALAR) mostra o depoimento e libera esse saber para TODOS os jogadores.
///   Algumas tarefas só abrem com o saber certo: o laudo popular precisa da marisqueira, a demarcação precisa da liderança quilombola...
/// • Escutar também dá um APOIO: nas tarefas coletivas cada apoio conta como uma pessoa (dá para jogar sozinho).
/// • A "Rádio Progresso" da empresa repete o discurso do Desenvolvimento e um morador responde.
/// • Os outdoors da empresa ganham a faixa da comunidade quando a área ligada a eles é defendida.
/// • A sabotagem automática (pressão econômica) existe, mas fica desligada: só o infiltrado desfaz conquistas.
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class Comunidade : MonoBehaviour
{
    [Header("Apoios")]
    [SerializeField] private int maximoDeApoios = 3;
    [Tooltip("Depois de apoiar, o morador precisa de um tempo antes de apoiar de novo.")]
    [SerializeField] private float recargaDoMorador = 25f;

    [Header("Pressão econômica (sem sabotador na sala)")]
    [Tooltip("Sabotagem automática quando não há infiltrado. Desligada por padrão.")]
    [SerializeField] private bool pressaoAutomatica = false;
    [SerializeField] private float primeiraPressao = 75f;
    [SerializeField] private Vector2 intervaloPressao = new Vector2(45f, 70f);

    [Header("Rádio Progresso (discurso da empresa)")]
    [SerializeField] private float primeiraRadio = 40f;
    [SerializeField] private Vector2 intervaloRadio = new Vector2(55f, 80f);

    public int Apoios { get; private set; }
    public int ApoiosMobilizados { get; private set; }
    public int MaximoDeApoios => maximoDeApoios;
    public int TotalDeSaberes => Moradores.Length;

    // (nome, quem é, posição no mundo, desenho em Resources/Animacoes/Moradores, saber, depoimento)
    private static readonly (string nome, string papel, Vector2 pos, string desenho, string saber, string fala)[] Moradores =
    {
        ("Dona Cida", "catadora da cooperativa", new Vector2(92f, 45f), "cida", "a rota do lixo da empresa",
         "Os caminhões da empresa descarregam aqui de madrugada. Depois dizem que o lixão é culpa da comunidade."),
        ("Seu Zé", "agricultor, guardião de sementes", new Vector2(10f, 30f), "ze", "as sementes crioulas",
         "Essa semente veio da minha avó e nunca precisou de veneno. A empresa quer vender semente que não nasce de novo."),
        ("Dona Lia", "marisqueira", new Vector2(26f, 50f), "lia", "onde o marisco morreu",
         "Eu sei pelo cheiro da maré quando a lama desce. Nenhum laudo da empresa veio perguntar pra quem cata marisco."),
        ("Seu Tonho", "pescador", new Vector2(84f, 50f), "tonho", "a memória do rio",
         "A gente bebia dessa água e o peixe era o pão de cada dia. Desde que a empresa chegou, no verão a gente compra água."),
        ("Dona Marta", "moradora mais antiga da vila", new Vector2(20f, -20f), "marta", "os limites das terras da comunidade",
         "Meus avós contavam onde termina a nossa terra. Está na memória da gente, não no mapa da empresa."),
        ("Joca", "jovem comunicador da rádio comunitária", new Vector2(70f, -20f), "joca", "as gravações da comunidade",
         "Gravei as famílias contando o que mudou. Se a TV só mostra a empresa, a nossa rádio devolve a palavra pra comunidade."),
    };

    /// Tarefa → morador cujo saber é necessário para fazê-la.
    private static readonly Dictionary<string, string> SaberDaTarefa = new Dictionary<string, string>
    {
        { "Laudo da comunidade", "Dona Lia" },
        { "Rastrear o lixo industrial da empresa", "Dona Cida" },
        { "Denúncia ao Ministério Público", "Seu Tonho" },
        { "Demarcar as terras da comunidade", "Dona Marta" },
        { "Banco de sementes crioulas", "Seu Zé" },
        { "Rádio comunitária", "Joca" },
    };

    /// O discurso hegemônico do "Desenvolvimento" e a resposta de quem vive no território.
    private static readonly (string empresa, string quem, string resposta)[] Radio =
    {
        ("A empresa trouxe progresso e emprego para toda a região!", "Seu Tonho",
         "Progresso pra quem? O emprego veio de fora e o peixe sumiu daqui."),
        ("Nossos laudos técnicos garantem: a água está dentro do padrão.", "Dona Lia",
         "A maré mostra o que o laudo esconde. Ninguém perguntou pra quem vive dela."),
        ("Quem é contra a mineração é contra o desenvolvimento do país.", "Dona Marta",
         "A gente mora aqui antes da empresa chegar. Defender a vila não é atraso, é vida."),
        ("A empresa investe em projetos sociais na comunidade.", "Dona Cida",
         "Dão uma quadra e levam o rio. Isso não é presente, é troca desigual."),
        ("Áreas sem uso produtivo serão incorporadas à expansão.", "Seu Zé",
         "Sem uso? Ali é a roça, a mata e o caminho dos nossos avós."),
        ("Os problemas de saúde da região não têm relação com a indústria.", "Joca",
         "Gravamos as crianças com tosse e as manchas na pele. A rádio comunitária vai mostrar."),
    };

    // outdoors da empresa: (desenho, posição, área que, defendida, faz a comunidade contestar o outdoor)
    private static readonly (string desenho, Vector2 pos, string area)[] Outdoors =
    {
        ("outdoor0", new Vector2(61f, 53f), "Bacia de rejeitos"),
        ("outdoor1", new Vector2(57f, 14f), "Rio"),
        ("outdoor2", new Vector2(76f, 58f), "Vila"),
    };

    private GerenciadorAmbiental gm;
    private HudAmbiental hud;
    private readonly List<Morador> lista = new List<Morador>();
    private readonly List<(SpriteRenderer sr, Sprite normal, Sprite contestado, string area)> outdoors = new List<(SpriteRenderer, Sprite, Sprite, string)>();

    private void Start()
    {
        gm = GetComponent<GerenciadorAmbiental>();
        hud = GetComponent<HudAmbiental>();
        var raiz = new GameObject("Moradores").transform;
        raiz.SetParent(transform, false);
        foreach (var m in Moradores)
        {
            var go = new GameObject("Morador - " + m.nome);
            go.transform.SetParent(raiz, false);
            go.transform.position = m.pos;
            var mor = go.AddComponent<Morador>();
            mor.Configurar(m.nome, m.papel, m.desenho, m.saber, m.fala);
            lista.Add(mor);
        }
        CriarOutdoors();
        if (pressaoAutomatica) StartCoroutine(PressaoEconomica());   // desligada: só o infiltrado sabota
        StartCoroutine(RadioProgresso());
    }

    // ------------------------------------------------------------------ saberes
    /// A tarefa precisa do saber de algum morador? Devolve o morador.
    public Morador SaberNecessario(string tarefa)
    {
        if (tarefa == null || !SaberDaTarefa.TryGetValue(tarefa, out var nome)) return null;
        return lista.Find(m => m.Nome == nome);
    }

    public IEnumerable<string> TarefasQueUsam(string morador)
    {
        foreach (var kv in SaberDaTarefa) if (kv.Value == morador) yield return kv.Key;
    }

    // ------------------------------------------------------------------ apoios
    public bool Convidar(Morador m)
    {
        if (!m.Disponivel || Apoios >= maximoDeApoios) return false;
        Apoios++;
        m.Usar(recargaDoMorador);
        return true;
    }

    public void Gastar(int n)
    {
        int g = Mathf.Min(n, Apoios);
        Apoios -= g; ApoiosMobilizados += g;
    }

    // ------------------------------------------------------------------ outdoors
    private void CriarOutdoors()
    {
        var dict = new Dictionary<string, Sprite>();
        foreach (var s in Resources.LoadAll<Sprite>("Animacoes/Outdoors")) dict[s.name] = s;
        if (dict.Count == 0) { Debug.LogWarning("[Comunidade] Outdoors não encontrados. Rode Tools → Vila → Preparar animações."); return; }
        var raiz = new GameObject("Outdoors da empresa").transform;
        raiz.SetParent(transform, false);
        foreach (var o in Outdoors)
        {
            dict.TryGetValue(o.desenho + "_0", out var a); dict.TryGetValue(o.desenho + "_1", out var b);
            if (a == null) continue;
            var go = new GameObject("Outdoor - " + o.area);
            go.transform.SetParent(raiz, false);
            go.transform.position = o.pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = a; sr.sortingOrder = 10; sr.spriteSortPoint = SpriteSortPoint.Pivot;
            outdoors.Add((sr, a, b ?? a, o.area));
        }
    }

    /// Chamado pelo GerenciadorAmbiental quando uma área muda.
    public void AtualizarOutdoors(System.Func<string, bool> areaDefendida)
    {
        foreach (var o in outdoors)
            if (o.sr != null) o.sr.sprite = areaDefendida(o.area) ? o.contestado : o.normal;
    }

    // ------------------------------------------------------------------ discurso e pressão
    private IEnumerator RadioProgresso()
    {
        yield return new WaitForSeconds(primeiraRadio);
        int i = Random.Range(0, Radio.Length);
        while (true)
        {
            if (hud != null && gm != null && gm.PartidaIniciada && !gm.PartidaEncerrada)
            {
                var r = Radio[i % Radio.Length]; i++;
                hud.Fala("RÁDIO PROGRESSO (da empresa)", r.empresa, HudAmbiental.Vermelho, 6f);
                yield return new WaitForSeconds(6.5f);
                var m = lista.Find(x => x.Nome == r.quem);
                hud.Fala(r.quem + (m != null ? ", " + m.Papel : ""), r.resposta, HudAmbiental.Verde, 7f);
            }
            yield return new WaitForSeconds(Random.Range(intervaloRadio.x, intervaloRadio.y));
        }
    }

    private IEnumerator PressaoEconomica()
    {
        yield return new WaitForSeconds(primeiraPressao);
        while (true)
        {
            bool souQuemDecide = !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;   // só um computador sabota
            if (souQuemDecide && gm != null && gm.PartidaIniciada && !gm.TemSabotadorNaSala && !gm.PartidaEncerrada)
                gm.SabotarAleatoria();
            yield return new WaitForSeconds(Random.Range(intervaloPressao.x, intervaloPressao.y));
        }
    }
}

/// <summary>Um morador parado num ponto do território, com um balão quando tem algo a contar ou pode apoiar.</summary>
public class Morador : MonoBehaviour
{
    public string Nome { get; private set; }
    public string Papel { get; private set; }
    public string Saber { get; private set; }
    public string Fala { get; private set; }
    public bool Disponivel => Time.time >= livreEm;

    private float livreEm;
    private SpriteRenderer corpo, balao;
    private Sprite[] parado;
    private static Sprite spriteBalao;

    public void Configurar(string nome, string papel, string desenho, string saber, string fala)
    {
        Nome = nome; Papel = papel; Saber = saber; Fala = fala;
        corpo = gameObject.AddComponent<SpriteRenderer>();
        corpo.sortingOrder = 10;
        corpo.spriteSortPoint = SpriteSortPoint.Pivot;
        var dict = new Dictionary<string, Sprite>();
        foreach (var s in Resources.LoadAll<Sprite>("Animacoes/Moradores")) dict[s.name] = s;
        dict.TryGetValue(desenho + "_0", out var a); dict.TryGetValue(desenho + "_1", out var b);
        if (a == null) Debug.LogWarning($"[Morador] Desenho '{desenho}' não encontrado. Rode Tools → Vila → Preparar animações.");
        parado = new[] { a, b ?? a };
        corpo.sprite = a;

        var col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true; col.radius = 1.3f; col.offset = new Vector2(0, 0.5f);

        var gb = new GameObject("Balao");
        gb.transform.SetParent(transform, false);
        gb.transform.localPosition = new Vector3(0.9f, 2.2f, 0);
        balao = gb.AddComponent<SpriteRenderer>();
        balao.sprite = SpriteBalao();
        balao.sortingOrder = 50;
    }

    public void Usar(float recarga) => livreEm = Time.time + recarga;

    private void Update()
    {
        if (parado != null && parado[0] != null)
            corpo.sprite = parado[(int)(Time.time * 2f + transform.position.x) % 2] ?? parado[0];
        var gm = GerenciadorAmbiental.Instancia;
        bool temHistoria = gm != null && !gm.SaberConhecido(Nome);
        balao.enabled = temHistoria || Disponivel;
        balao.color = temHistoria ? new Color(1f, 0.85f, 0.35f) : Color.white;     // amarelo = ainda tem um saber para contar
        if (balao.enabled) balao.transform.localPosition = new Vector3(0.9f, 2.2f + Mathf.Sin(Time.time * 3f) * 0.08f, 0);
    }

    private void OnTriggerEnter2D(Collider2D c)
    {
        if (EhJogadorLocal(c) && GerenciadorAmbiental.Instancia != null) GerenciadorAmbiental.Instancia.Entrou(this);
    }

    private void OnTriggerExit2D(Collider2D c)
    {
        if (EhJogadorLocal(c) && GerenciadorAmbiental.Instancia != null) GerenciadorAmbiental.Instancia.Saiu(this);
    }

    private static bool EhJogadorLocal(Collider2D c)
    {
        var pm = c.GetComponentInParent<PlayerMovement>();
        if (pm == null) return false;
        if (!PhotonNetwork.InRoom) return true;
        var pv = pm.GetComponent<PhotonView>();
        return pv == null || pv.AmOwner;
    }

    // balão de conversa branco com "..." e uma setinha apontando para a cabeça do morador (desenhado com antisserrilhado)
    private static Sprite SpriteBalao()
    {
        if (spriteBalao != null) return spriteBalao;
        const int W = 128, H = 96, SS = 4;            // 4 x 4 amostras por pixel
        const float BORDA = 6f;
        Color tinta = new Color32(42, 30, 26, 255);
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[W * H];
        // setinha: triângulo saindo da parte de baixo, à esquerda, apontando para baixo e para a esquerda
        Vector2 t0 = new Vector2(38, 36), t1 = new Vector2(62, 30), t2 = new Vector2(18, 10);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float fora = 0f, dentro = 0f, ponto = 0f;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        var p = new Vector2(x + (sx + .5f) / SS, y + (sy + .5f) / SS);
                        float de = DistElipse(p, new Vector2(64, 56), 54, 30);
                        float dt = DistTriangulo(p, t0, t1, t2);
                        float d = Mathf.Max(de, dt);                 // > 0 = dentro da forma
                        if (d > -BORDA) fora++;
                        if (d > 0) dentro++;
                        foreach (int cx in new[] { 40, 64, 88 })
                            if ((p - new Vector2(cx, 56)).sqrMagnitude <= 49f) ponto++;
                    }
                float n = SS * SS; fora /= n; dentro /= n; ponto /= n;
                Color c = fora > 0 ? Color.Lerp(tinta, Color.white, dentro / fora) : Color.clear;
                c = Color.Lerp(c, tinta, ponto);
                c.a = fora;
                px[y * W + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        spriteBalao = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 128f);
        return spriteBalao;
    }

    // distância aproximada até a borda da elipse (positiva dentro)
    private static float DistElipse(Vector2 p, Vector2 c, float rx, float ry)
    {
        float dx = (p.x - c.x) / rx, dy = (p.y - c.y) / ry;
        return (1f - Mathf.Sqrt(dx * dx + dy * dy)) * Mathf.Min(rx, ry);
    }

    // distância até a borda do triângulo (positiva dentro): o menor afastamento entre as três arestas
    private static float DistTriangulo(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s = Mathf.Sign((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        return Mathf.Min(Aresta(p, a, b, s), Mathf.Min(Aresta(p, b, c, s), Aresta(p, c, a, s)));
    }

    private static float Aresta(Vector2 p, Vector2 a, Vector2 b, float s)
    {
        Vector2 e = b - a;
        return s * (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / e.magnitude;
    }
}
