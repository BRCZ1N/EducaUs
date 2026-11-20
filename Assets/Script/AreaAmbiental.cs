using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Uma área do mapa que pode estar poluída ou recuperada.
/// Ao recuperar, troca (só dentro da área) cada tile poluído pelo seu par limpo
/// e guarda o tile original de cada célula; ao poluir de novo, restaura exatamente o original.
/// Ex.: agua_poluida_N -> agua_N,  lama_iNE -> terra_iNE,  queimada_N -> grama_1
/// </summary>
public class AreaAmbiental : MonoBehaviour
{
    [Serializable]
    public struct ParDeTiles
    {
        public TileBase poluido;
        public TileBase recuperado;
    }

    [Tooltip("Tilemap onde ficam os tiles desta área (normalmente a de Chão).")]
    [SerializeField] private Tilemap tilemap;

    [Tooltip("Retângulo da área, em células da Tilemap.")]
    [SerializeField] private BoundsInt area = new BoundsInt(0, 0, 0, 10, 8, 1);

    [Tooltip("Pares poluído → recuperado.")]
    [SerializeField] private List<ParDeTiles> pares = new List<ParDeTiles>();

    [Tooltip("Objetos de lixo desta área: somem quando ela é recuperada e voltam se for sabotada.")]
    [SerializeField] private List<GameObject> objetosPoluicao = new List<GameObject>();

    [Tooltip("Objetos de recompensa (garças, peixes, flores): só aparecem quando a área está recuperada.")]
    [SerializeField] private List<GameObject> objetosRecuperacao = new List<GameObject>();

    [SerializeField] private bool comecaPoluida = true;

    public bool Recuperada { get; private set; }

    /// Nome da área (o objeto se chama "Area_Rio" → "Rio").
    public string Nome => name.StartsWith("Area_") ? name.Substring(5) : name;

    /// Retângulo da área em coordenadas do mundo.
    public Rect RetanguloMundo
    {
        get
        {
            if (tilemap == null) return new Rect(transform.position, Vector2.zero);
            Vector3 min = tilemap.CellToWorld(area.min), max = tilemap.CellToWorld(area.max);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }
    }

    public bool Contem(Vector3 p) => RetanguloMundo.Contains((Vector2)p);

    public float Distancia(Vector3 p)
    {
        Rect r = RetanguloMundo;
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private Dictionary<TileBase, TileBase> paraLimpo;
    private readonly Dictionary<Vector3Int, TileBase> originais = new Dictionary<Vector3Int, TileBase>();

    private void Awake()
    {
        paraLimpo = new Dictionary<TileBase, TileBase>();
        foreach (var p in pares)
            if (p.poluido != null && p.recuperado != null)
                paraLimpo[p.poluido] = p.recuperado;

        Recuperada = !comecaPoluida;
        AtualizarObjetos();
    }

    private void AtualizarObjetos()
    {
        foreach (var o in objetosPoluicao)
            if (o != null) o.SetActive(!Recuperada);
        foreach (var o in objetosRecuperacao)
            if (o != null) o.SetActive(Recuperada);
    }

    /// <summary>Chame quando a tarefa da área for concluída (true) ou sabotada (false).</summary>
    public void SetRecuperada(bool recuperada)
    {
        if (recuperada == Recuperada) return;
        Recuperada = recuperada;
        AtualizarObjetos();

        if (recuperada)
        {
            originais.Clear();
            foreach (var pos in area.allPositionsWithin)
            {
                TileBase atual = AnimarAgua.Original(tilemap.GetTile(pos));   // a água pode estar mostrando um quadro da animação
                if (atual != null && paraLimpo.TryGetValue(atual, out TileBase limpo))
                {
                    originais[pos] = atual;
                    tilemap.SetTile(pos, limpo);
                }
            }
        }
        else
        {
            foreach (var kv in originais)
                tilemap.SetTile(kv.Key, kv.Value);
            originais.Clear();
        }
    }

    // Retângulo da área na Scene view
    private void OnDrawGizmosSelected()
    {
        if (tilemap == null) return;
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
        Vector3 min = tilemap.CellToWorld(area.min);
        Vector3 max = tilemap.CellToWorld(area.max);
        Gizmos.DrawWireCube((min + max) / 2f, max - min);
    }
}
