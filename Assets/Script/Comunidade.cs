using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Moradores da vila (NPCs) e "pressão econômica" automática.
/// • Conversar com um morador (segurar E) dá um APOIO. Nas tarefas coletivas, cada apoio conta como uma pessoa,
///   então dá para jogar sozinho ou com pouca gente sem perder a ideia de que é preciso organizar a comunidade.
/// • Enquanto a sala não tem um sabotador de verdade (menos de 3 jogadores), a pressão econômica desfaz
///   conquistas sozinha de tempos em tempos, com os mesmos avisos políticos.
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class Comunidade : MonoBehaviour
{
    [Header("Apoios")]
    [SerializeField] private int maximoDeApoios = 3;
    [Tooltip("Depois de apoiar, o morador precisa de um tempo antes de apoiar de novo.")]
    [SerializeField] private float recargaDoMorador = 25f;

    [Header("Pressão econômica (sem sabotador na sala)")]
    [SerializeField] private float primeiraPressao = 75f;
    [SerializeField] private Vector2 intervaloPressao = new Vector2(45f, 70f);

    public int Apoios { get; private set; }
    public int MaximoDeApoios => maximoDeApoios;
    public float RecargaDoMorador => recargaDoMorador;

    // (nome, quem é, posição no mundo, desenho em Resources/Animacoes/Moradores)
    private static readonly (string nome, string papel, Vector2 pos, string desenho)[] Moradores =
    {
        ("Dona Cida", "catadora da cooperativa", new Vector2(92f, 45f), "cida"),
        ("Seu Zé", "agricultor", new Vector2(10f, 30f), "ze"),
        ("Lia", "professora", new Vector2(26f, 50f), "lia"),
        ("Tonho", "pescador do rio", new Vector2(84f, 50f), "tonho"),
        ("Marta", "vizinha do bairro", new Vector2(20f, -20f), "marta"),
        ("Joca", "estudante", new Vector2(70f, -20f), "joca"),
    };

    private GerenciadorAmbiental gm;

    private void Start()
    {
        gm = GetComponent<GerenciadorAmbiental>();
        var raiz = new GameObject("Moradores").transform;
        raiz.SetParent(transform, false);
        foreach (var m in Moradores)
        {
            var go = new GameObject("Morador - " + m.nome);
            go.transform.SetParent(raiz, false);
            go.transform.position = m.pos;
            go.AddComponent<Morador>().Configurar(m.nome, m.papel, m.desenho);
        }
        StartCoroutine(PressaoEconomica());
    }

    public bool Convidar(Morador m)
    {
        if (!m.Disponivel || Apoios >= maximoDeApoios) return false;
        Apoios++;
        m.Usar(recargaDoMorador);
        return true;
    }

    public void Gastar(int n) => Apoios = Mathf.Max(0, Apoios - n);

    private IEnumerator PressaoEconomica()
    {
        yield return new WaitForSeconds(primeiraPressao);
        while (true)
        {
            bool souQuemDecide = !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;   // só um computador sabota
            if (souQuemDecide && gm != null && !gm.TemSabotadorNaSala && !gm.PartidaVencida)
                gm.SabotarAleatoria();
            yield return new WaitForSeconds(Random.Range(intervaloPressao.x, intervaloPressao.y));
        }
    }
}

/// <summary>Um morador parado num ponto da vila, com um balão de conversa quando pode apoiar.</summary>
public class Morador : MonoBehaviour
{
    public string Nome { get; private set; }
    public string Papel { get; private set; }
    public bool Disponivel => Time.time >= livreEm;

    private float livreEm;
    private SpriteRenderer corpo, balao;
    private Sprite[] parado;
    private static Sprite spriteBalao;

    public void Configurar(string nome, string papel, string desenho)
    {
        Nome = nome; Papel = papel;
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
        balao.enabled = Disponivel;
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
        return pv == null || pv.IsMine;
    }

    // balão de conversa branco com "..."
    private static Sprite SpriteBalao()
    {
        if (spriteBalao != null) return spriteBalao;
        const int W = 64, H = 48;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color tinta = new Color32(42, 30, 26, 255), branco = Color.white;
        var px = new Color[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = (x - 32f) / 29f, dy = (y - 28f) / 18f;
                float d = dx * dx + dy * dy;
                bool rabo = y < 12 && x > 14 && x < 26 && (x - 14) > (12 - y);
                Color c = d <= 0.78f ? branco : d <= 1f ? tinta : Color.clear;
                if (rabo && d > 0.7f) c = (x - 14) > (12 - y) + 3 ? branco : tinta;
                foreach (int cx in new[] { 20, 32, 44 })
                    if ((x - cx) * (x - cx) + (y - 28) * (y - 28) <= 16) c = tinta;
                px[y * W + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        spriteBalao = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), 64f);
        return spriteBalao;
    }
}
