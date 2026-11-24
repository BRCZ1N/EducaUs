using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Nome do jogador flutuando logo acima da cabeça (o seu em amarelo, os outros em branco).
/// A altura vem do tamanho do quadro atual do personagem. Quando dois nomes ficariam um em cima
/// do outro (jogadores juntos), o de trás sobe uma linha, com movimento suave.
/// O PlayerMovement adiciona este componente sozinho.
/// </summary>
public class NomeAcima : MonoBehaviour
{
    [SerializeField] private float folga = 0.12f;     // espaço entre o topo da cabeça e o nome
    [SerializeField] private float tamanho = 0.05f;   // tamanho da letra

    private static readonly List<NomeAcima> todos = new List<NomeAcima>();
    /// F3 liga/desliga o diagnóstico de rede: mostra a posição de cada boneco e o atraso da última mensagem.
    public static bool Diagnostico;
    private static int quadroF3 = -1;
    private SincronizarPosicao sync;
    private static int quadroArrumado = -1;

    private PhotonView pv;
    private SpriteRenderer corpo;
    private TextMesh texto, sombra;
    private MeshRenderer mrTexto;
    private string ultimo;
    private Vector3 basePos;          // posição sem desvio
    private float desvioAlvo, desvio; // subida para não sobrepor

    private void OnEnable() { todos.Add(this); }
    private void OnDisable() { todos.Remove(this); }

    private void Start()
    {
        pv = GetComponentInParent<PhotonView>();
        corpo = GetComponentInChildren<SpriteRenderer>();
        var fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sombra = Criar("Nome (sombra)", fonte, new Color(0, 0, 0, 0.75f), 199);
        texto = Criar("Nome", fonte, Color.white, 200);
        mrTexto = texto.GetComponent<MeshRenderer>();
    }

    private TextMesh Criar(string nome, Font fonte, Color cor, int ordem)
    {
        var go = new GameObject(nome);
        go.transform.SetParent(transform, false);
        var tm = go.AddComponent<TextMesh>();
        tm.font = fonte; tm.fontSize = 64; tm.characterSize = tamanho;
        tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold; tm.color = cor;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = fonte.material;
        mr.sortingOrder = ordem;                 // sempre por cima do cenário
        return tm;
    }

    // Altura da cabeça pelo quadro atual (não pela maior altura já vista, que guardava o sprite antigo do prefab).
    private float AlturaCabeca()
    {
        if (corpo == null || corpo.sprite == null) return 1.6f;
        var t = corpo.transform;
        float topoLocal = corpo.sprite.bounds.max.y * Mathf.Abs(t.lossyScale.y);
        return (t.position.y - transform.position.y) + topoLocal;
    }

    private void LateUpdate()
    {
        if (texto == null) return;
        bool meu = !PhotonNetwork.InRoom || pv == null || pv.AmOwner;
        string nome = PhotonNetwork.InRoom && pv != null && pv.Owner != null ? pv.Owner.NickName : PhotonNetwork.NickName;
        if (string.IsNullOrEmpty(nome)) nome = "Jogador";
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.f3Key.wasPressedThisFrame && quadroF3 != Time.frameCount) { quadroF3 = Time.frameCount; Diagnostico = !Diagnostico; }
        if (Diagnostico)
        {
            if (sync == null) sync = GetComponentInParent<SincronizarPosicao>();
            Vector2 pp = transform.position;
            string info = $"({pp.x:0.0}, {pp.y:0.0})";
            if (sync != null && sync.EhRemoto)
            {
                float sem = sync.SegundosSemMensagem;
                info += sem < 0 ? "  SEM DADOS" : $"  atraso {sync.UltimoAtrasoMs}ms  última há {sem:0.0}s";
            }
            else if (sync == null && PhotonNetwork.InRoom) info += "  SEM SincronizarPosicao";
            nome += "\n" + info;
        }
        bool caiu = PhotonNetwork.InRoom && pv != null && pv.Owner != null && pv.Owner.IsInactive;
        if (caiu) nome += " (caiu)";
        if (nome != ultimo) { texto.text = sombra.text = nome; ultimo = nome; }

        bool dispensado = pv != null && Assembleia.Dispensado(pv.OwnerActorNr);
        float a = caiu ? 0.35f : dispensado ? 0.45f : 1f;
        texto.color = meu ? new Color(1f, 0.85f, 0.35f, a) : new Color(1f, 1f, 1f, a);
        sombra.color = new Color(0, 0, 0, 0.75f * a);

        // tamanho fixo no mundo, sem espelhar junto com o personagem
        var s = transform.lossyScale;
        texto.transform.localScale = sombra.transform.localScale = new Vector3(
            Mathf.Sign(s.x) / Mathf.Max(0.0001f, Mathf.Abs(s.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(s.y)), 1f);

        basePos = transform.position + new Vector3(0, AlturaCabeca() + folga, 0);

        if (quadroArrumado != Time.frameCount) { quadroArrumado = Time.frameCount; Arrumar(); }

        desvio = Mathf.MoveTowards(desvio, desvioAlvo, 3f * Time.deltaTime);
        Vector3 p = basePos + new Vector3(0, desvio, 0);
        texto.transform.position = p;
        sombra.transform.position = p + new Vector3(0.02f, -0.02f, 0);
    }

    // Uma vez por quadro: quem está mais embaixo na tela fica no lugar; quem colide com um nome já colocado sobe.
    private static void Arrumar()
    {
        var lista = new List<NomeAcima>();
        foreach (var n in todos) if (n != null && n.texto != null) { n.basePos = n.transform.position + new Vector3(0, n.AlturaCabeca() + n.folga, 0); lista.Add(n); }
        lista.Sort((x, y) => x.basePos.y.CompareTo(y.basePos.y));
        var colocados = new List<(float x, float y, float meiaLarg)>();
        foreach (var n in lista)
        {
            float meia = n.mrTexto != null ? Mathf.Max(0.4f, n.mrTexto.bounds.extents.x) : 0.8f;
            float linha = n.mrTexto != null ? Mathf.Clamp(n.mrTexto.bounds.size.y * 1.05f, 0.2f, 1.5f) : 0.4f;   // altura de uma linha
            float y = n.basePos.y;
            bool mexeu = true; int guarda = 0;
            while (mexeu && guarda++ < 6)
            {
                mexeu = false;
                foreach (var c in colocados)
                    if (Mathf.Abs(c.x - n.basePos.x) < c.meiaLarg + meia && Mathf.Abs(c.y - y) < linha)
                    { y = c.y + linha; mexeu = true; }
            }
            n.desvioAlvo = y - n.basePos.y;
            colocados.Add((n.basePos.x, y, meia));
        }
    }
}
