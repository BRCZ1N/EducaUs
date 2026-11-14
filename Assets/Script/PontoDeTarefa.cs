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

    public string Id { get; private set; }
    public bool Feita { get; private set; }
    public AreaAmbiental Area { get; set; }
    public bool EhReuniao => nome != null && nome.StartsWith("Reuni");

    private SpriteRenderer marca;
    private static Sprite spriteMarca;

    private void Awake()
    {
        if (string.IsNullOrEmpty(nome))
            nome = name.StartsWith("Tarefa - ") ? name.Substring("Tarefa - ".Length) : name;
        Id = RaizDoLocal(transform).name + "/" + nome;
        GetComponent<Collider2D>().isTrigger = true;
        if (!EhReuniao) CriarMarca();
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
        return pv == null || pv.IsMine;
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
        Gizmos.DrawWireSphere(transform.position, 1.2f);
    }
}
