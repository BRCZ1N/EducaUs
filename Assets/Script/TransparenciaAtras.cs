using Photon.Pun;
using UnityEngine;

/// <summary>
/// Deixa uma árvore ou prédio alto semitransparente enquanto o SEU personagem está atrás dele,
/// para ninguém sumir atrás da copa. O GerenciadorAmbiental coloca este componente sozinho nos objetos altos.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TransparenciaAtras : MonoBehaviour
{
    [SerializeField, Range(0.1f, 1f)] private float alfaAtras = 0.45f;
    [SerializeField] private float velocidade = 6f;

    private SpriteRenderer sr;
    private static Transform jogador;
    private static float proximaBusca;

    private void Awake() => sr = GetComponent<SpriteRenderer>();

    private void Update()
    {
        if (jogador == null && Time.time >= proximaBusca) { jogador = AcharJogadorLocal(); proximaBusca = Time.time + 1f; }

        float alvo = 1f;
        if (jogador != null)
        {
            Vector3 p = jogador.position + Vector3.up * 0.6f;          // meio do corpo
            Bounds b = sr.bounds;
            bool dentro = p.x > b.min.x + 0.2f && p.x < b.max.x - 0.2f && p.y > b.min.y && p.y < b.max.y;
            bool atras = jogador.position.y > transform.position.y;     // pés acima da base = está atrás
            if (dentro && atras) alvo = alfaAtras;
        }
        var c = sr.color;
        c.a = Mathf.MoveTowards(c.a, alvo, velocidade * Time.deltaTime);
        sr.color = c;
    }

    private static Transform AcharJogadorLocal()
    {
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None))
        {
            if (!PhotonNetwork.InRoom) return pm.transform;
            var pv = pm.GetComponent<PhotonView>();
            if (pv == null || pv.IsMine) return pm.transform;
        }
        return null;
    }
}
