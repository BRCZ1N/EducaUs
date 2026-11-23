using Photon.Pun;
using UnityEngine;

/// <summary>
/// Ponto de tarefa no mapa ("Tarefa - Regar a horta"). O MontadorLocal já coloca este componente;
/// nos locais montados antes, o GerenciadorAmbiental adiciona sozinho ao iniciar.
/// Mostra um marcador amarelo enquanto a tarefa está pendente e avisa o gerenciador quando
/// o SEU personagem entra ou sai do círculo.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PontoDeTarefa : MonoBehaviour
{
    public string nome;
    [Tooltip("Quantas pessoas precisam estar no ponto ao mesmo tempo (tarefa coletiva).")]
    public int jogadores = 1;

    public string Id { get; private set; }
    public bool Feita { get; private set; }
    public AreaAmbiental Area { get; set; }
    public bool EhReuniao => nome != null && nome.StartsWith("Reunião de emergência");
    public bool Coletiva => jogadores > 1;

    /// Locais montados com os nomes antigos das tarefas continuam funcionando: o nome é trocado ao iniciar
    /// (assim não é preciso remontar os locais no Unity).
    public static readonly System.Collections.Generic.Dictionary<string, string> NomesNovos = new System.Collections.Generic.Dictionary<string, string>
    {
        { "Mapear os problemas com os moradores", "Roda de conversa com os moradores" },
        { "Coletar amostra da lama para o laudo", "Laudo da comunidade" },
        { "Protesto na entrada da mineradora", "Ato cultural na portaria da mineradora" },
        { "Separar recicláveis", "Rastrear o lixo industrial da empresa" },
        { "Organizar a cooperativa de catadores", "Cooperativa de catadores" },
        { "Denunciar o esgoto despejado no rio", "Denúncia ao Ministério Público" },
        { "Mutirão de limpeza do rio", "Monitoramento comunitário da água" },
        { "Plantar mudas nativas", "Demarcar as terras da comunidade" },
        { "Formar a brigada comunitária", "Brigada comunitária" },
        { "Trocar o agrotóxico por adubo orgânico", "Banco de sementes da comunidade" },
        { "Banco de sementes crioulas", "Banco de sementes da comunidade" },
        { "Criar a cooperativa de agricultores", "Cooperativa agroecológica" },
        { "Regar a horta comunitária", "Rádio comunitária" },
        { "Reunião da associação de moradores", "Assembleia da associação de moradores" },
        { "Fiscalizar a obra pelo conselho popular", "Fiscalizar a obra de saneamento" },
        { "Cobrar saneamento na Câmara de Vereadores", "Audiência pública com as comunidades vizinhas" },
        { "Roda de educação popular", "Roda de conversa com os moradores" },
        { "Laudo popular da lama", "Laudo da comunidade" },
        { "Assembleia da associação quilombola", "Assembleia da associação de moradores" },
        { "Demarcar o território tradicional", "Demarcar as terras da comunidade" },
        { "Fiscalização popular da obra", "Fiscalizar a obra de saneamento" },
        { "Audiência pública com a rede de atingidos", "Audiência pública com as comunidades vizinhas" },
    };

    private SpriteRenderer marca;
    private static Sprite spriteMarca;

    private void Awake()
    {
        if (string.IsNullOrEmpty(nome))
            nome = name.StartsWith("Tarefa - ") ? name.Substring("Tarefa - ".Length) : name;
        if (NomesNovos.TryGetValue(nome, out var novo)) { nome = novo; name = "Tarefa - " + novo; }
        Id = RaizDoLocal(transform).name + "/" + nome;
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
        if (Coletiva && col is CircleCollider2D cc) cc.radius = Mathf.Max(cc.radius, RaioColetivo);   // cabe o grupo
        if (!EhReuniao) CriarMarca();
    }

    private const float RaioColetivo = 2.2f;

    /// Quantos personagens (de qualquer jogador, inclusive o sabotador) estão dentro do ponto agora.
    public int ContarJogadores()
    {
        float raio = GetComponent<CircleCollider2D>() is CircleCollider2D cc ? cc.radius * transform.lossyScale.x : RaioColetivo;
        int n = 0;
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
            if (Vector2.Distance(pm.transform.position, transform.position) <= raio + 0.3f &&
                !(pm.GetComponent<Photon.Pun.PhotonView>() is Photon.Pun.PhotonView pv && Assembleia.Dispensado(pv.OwnerActorNr))) n++;   // dispensado não conta
        return n;
    }

    public static Transform RaizDoLocal(Transform t)
    {
        while (t.parent != null && !t.name.StartsWith("Local_")) t = t.parent;
        return t;
    }

    public void SetFeita(bool feita)
    {
        Feita = feita;
        if (marca != null) marca.enabled = !feita;
    }

    private void Update()
    {
        if (marca != null && marca.enabled)
            marca.transform.localPosition = new Vector3(0f, 2.1f + Mathf.Sin(Time.time * 4f) * 0.12f, 0f);
    }

    private void OnTriggerEnter2D(Collider2D outro)
    {
        if (!EhReuniao && EhJogadorLocal(outro) && GerenciadorAmbiental.Instancia != null)
            GerenciadorAmbiental.Instancia.Entrou(this);
    }

    private void OnTriggerExit2D(Collider2D outro)
    {
        if (EhJogadorLocal(outro) && GerenciadorAmbiental.Instancia != null)
            GerenciadorAmbiental.Instancia.Saiu(this);
    }

    private static bool EhJogadorLocal(Collider2D c)
    {
        var pm = c.GetComponentInParent<PlayerMovement>();
        if (pm == null) return false;
        if (!PhotonNetwork.InRoom) return true;
        var pv = pm.GetComponent<PhotonView>();
        return pv == null || pv.AmOwner;
    }

    // ------------------------------------------------------------------ marcador "!"
    private void CriarMarca()
    {
        var go = new GameObject("Marca");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 2.1f, 0f);
        marca = go.AddComponent<SpriteRenderer>();
        marca.sprite = SpriteMarca();
        marca.sortingOrder = 50;
        if (Coletiva) { marca.color = new Color(0.75f, 0.85f, 1f); go.transform.localScale = Vector3.one * 1.3f; }   // azulada e maior
    }

    private static Sprite SpriteMarca()
    {
        if (spriteMarca != null) return spriteMarca;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Color tinta = new Color32(42, 30, 26, 255), amarelo = new Color32(255, 210, 90, 255);
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(N / 2f, N / 2f));
                Color c = d < 25 ? amarelo : d < 31 ? tinta : Color.clear;
                bool barra = x >= 28 && x < 36 && y >= 26 && y < 50;     // "!" (y cresce para cima)
                bool ponto = x >= 28 && x < 36 && y >= 14 && y < 22;
                if (barra || ponto) c = tinta;
                px[y * N + x] = c;
            }
        tex.SetPixels(px); tex.Apply();
        spriteMarca = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(.5f, .5f), 64f);
        return spriteMarca;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Feita ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, jogadores > 1 ? RaioColetivo : 1.2f);
    }
}
