using Photon.Pun;
using UnityEngine;

/// <summary>
/// Nome do jogador flutuando acima do personagem (o seu em amarelo, os outros em branco).
/// O PlayerMovement adiciona este componente sozinho.
/// </summary>
public class NomeAcima : MonoBehaviour
{
    [SerializeField] private float altura = 2.35f;
    [SerializeField] private float tamanho = 0.09f;

    private PhotonView pv;
    private TextMesh texto, sombra;
    private string ultimo;

    private void Start()
    {
        pv = GetComponentInParent<PhotonView>();
        var fonte = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        sombra = Criar("Nome (sombra)", fonte, new Color(0, 0, 0, 0.75f), new Vector3(0.03f, altura - 0.03f, 0), 199);
        texto = Criar("Nome", fonte, Color.white, new Vector3(0, altura, 0), 200);
    }

    private TextMesh Criar(string nome, Font fonte, Color cor, Vector3 pos, int ordem)
    {
        var go = new GameObject(nome);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = fonte; tm.fontSize = 64; tm.characterSize = tamanho;
        tm.anchor = TextAnchor.LowerCenter; tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold; tm.color = cor;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = fonte.material;
        mr.sortingOrder = ordem;                 // sempre por cima do cenário
        return tm;
    }

    private void LateUpdate()
    {
        if (texto == null) return;
        bool meu = !PhotonNetwork.InRoom || pv == null || pv.AmOwner;
        string nome = PhotonNetwork.InRoom && pv != null && pv.Owner != null ? pv.Owner.NickName : PhotonNetwork.NickName;
        if (string.IsNullOrEmpty(nome)) nome = "Jogador";
        if (PhotonNetwork.InRoom && pv != null && pv.Owner != null && pv.Owner.IsInactive) nome += " (caiu)";
        if (nome != ultimo) { texto.text = sombra.text = nome; ultimo = nome; }

        bool dispensado = pv != null && Assembleia.Dispensado(pv.OwnerActorNr);
        bool caiu = PhotonNetwork.InRoom && pv != null && pv.Owner != null && pv.Owner.IsInactive;
        float a = caiu ? 0.35f : dispensado ? 0.45f : 1f;
        texto.color = meu ? new Color(1f, 0.85f, 0.35f, a) : new Color(1f, 1f, 1f, a);
        sombra.color = new Color(0, 0, 0, 0.75f * a);

        // não espelha junto com o personagem
        var s = transform.lossyScale;
        texto.transform.localScale = sombra.transform.localScale = new Vector3(Mathf.Sign(s.x), 1, 1);
    }
}
