using UnityEngine;

/// <summary>
/// Parede invisível em volta do mapa inteiro: ninguém sai da vila pela borda.
/// Cria um único EdgeCollider2D fechado (retângulo) nos limites do mundo.
/// O GerenciadorAmbiental adiciona este componente sozinho; os limites são os mesmos da câmera.
/// </summary>
public class LimiteDoMapa : MonoBehaviour
{
    [Header("Limites do mapa (unidades do mundo)")]
    [SerializeField] private Vector2 minimo = new Vector2(0f, -40f);     // canto de baixo à esquerda
    [SerializeField] private Vector2 maximo = new Vector2(112f, 90f);    // canto de cima à direita
    [Tooltip("Espessura da parede (metade para cada lado da linha).")]
    [SerializeField] private float espessura = 1f;

    private void Start()
    {
        var go = new GameObject("Limite do Mapa");
        go.transform.SetParent(transform, false);
        go.transform.position = Vector3.zero;           // pontos em coordenadas do mundo
        go.isStatic = true;

        var borda = go.AddComponent<EdgeCollider2D>();
        borda.edgeRadius = espessura / 2f;
        // um pouco para fora, para a parede não "comer" a primeira fileira de tiles
        float m = espessura / 2f;
        borda.points = new[]
        {
            new Vector2(minimo.x - m, minimo.y - m),
            new Vector2(maximo.x + m, minimo.y - m),
            new Vector2(maximo.x + m, maximo.y + m),
            new Vector2(minimo.x - m, maximo.y + m),
            new Vector2(minimo.x - m, minimo.y - m),     // fecha o retângulo
        };
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
        Vector3 a = new Vector3(minimo.x, minimo.y), b = new Vector3(maximo.x, minimo.y);
        Vector3 c = new Vector3(maximo.x, maximo.y), d = new Vector3(minimo.x, maximo.y);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);
    }
}
