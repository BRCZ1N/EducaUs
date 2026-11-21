using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Faz a água do rio ondular e a da lagoa cintilar (8 quadros em loop), inclusive nas bordas.
/// Troca os tiles de água por quadros animados de Resources/Animacoes/Agua_Anim_128.
/// Funciona junto com a AreaAmbiental: quando o rio é recuperado, a água poluída vira água limpa e continua animada.
/// O GerenciadorAmbiental adiciona este componente sozinho.
/// </summary>
public class AnimarAgua : MonoBehaviour
{
    [SerializeField] private float quadrosPorSegundo = 6f;

    private const int N = 8;
    private static readonly string[] Tipos = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE", "iNW", "iNE", "iSW", "iSE" };

    // tile animado (de qualquer quadro) -> tile original do mapa, para a AreaAmbiental reconhecer
    private static readonly Dictionary<TileBase, TileBase> original = new Dictionary<TileBase, TileBase>();
    /// Devolve o tile "de verdade" do mapa, mesmo que no momento esteja aparecendo um quadro da animação.
    public static TileBase Original(TileBase t) => t != null && original.TryGetValue(t, out var o) ? o : t;

    private readonly Dictionary<string, Tile[]> quadros = new Dictionary<string, Tile[]>();   // "agua|C" -> 8 tiles
    private readonly Dictionary<string, string> chaveDoNome = new Dictionary<string, string>(); // nome do tile do mapa -> "agua|C"
    private readonly List<(Tilemap mapa, List<Vector3Int> celulas)> agua = new List<(Tilemap, List<Vector3Int>)>();
    private int quadro;
    private float proximo;

    private void Start()
    {
        original.Clear();
        foreach (var s in Resources.LoadAll<Sprite>("Animacoes/Agua_Anim_128"))
        {
            // nome: agua_f3_NW, agua_poluida_f0_C, agua_poluida_oleo_f5_C
            int i = s.name.LastIndexOf("_f");
            if (i < 0) continue;
            string familia = s.name.Substring(0, i);
            string resto = s.name.Substring(i + 2);                 // "3_NW"
            int sep = resto.IndexOf('_');
            if (sep < 0 || !int.TryParse(resto.Substring(0, sep), out int k) || k >= N) continue;
            string chave = familia + "|" + resto.Substring(sep + 1);
            if (!quadros.TryGetValue(chave, out var arr)) quadros[chave] = arr = new Tile[N];
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = s; t.colliderType = Tile.ColliderType.None; t.name = s.name;
            arr[k] = t;
        }
        if (quadros.Count == 0)
        {
            Debug.LogWarning("[AnimarAgua] Quadros da água não encontrados. Rode Tools → Vila → Preparar animações.");
            enabled = false; return;
        }

        // nomes dos tiles do mapa que são água
        foreach (var tipo in Tipos)
        {
            chaveDoNome["agua_" + tipo] = "agua|" + tipo;
            chaveDoNome["agua_poluida_" + tipo] = "agua_poluida|" + tipo;
            chaveDoNome["lago_" + tipo] = "lago|" + tipo;              // lagoa: água parada que só cintila
        }
        chaveDoNome["lago_1"] = chaveDoNome["lago_2"] = "lago|C";
        chaveDoNome["agua_1"] = chaveDoNome["agua_2"] = "agua|C";
        chaveDoNome["agua_poluida_1"] = "agua_poluida|C";
        chaveDoNome["agua_poluida_oleo"] = "agua_poluida_oleo|C";

        // guarda onde há água em cada tilemap (menos os de colisão)
        foreach (var mapa in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (mapa.GetComponent<TilemapCollider2D>() != null) continue;
            var cel = new List<Vector3Int>();
            foreach (var pos in mapa.cellBounds.allPositionsWithin)
            {
                var t = mapa.GetTile(pos);
                if (t != null && chaveDoNome.ContainsKey(t.name)) cel.Add(pos);
            }
            if (cel.Count > 0) agua.Add((mapa, cel));
        }
        Atualizar();
    }

    private void Update()
    {
        if (Time.time < proximo) return;
        proximo = Time.time + 1f / quadrosPorSegundo;
        quadro = (quadro + 1) % N;
        Atualizar();
    }

    private void Atualizar()
    {
        foreach (var (mapa, celulas) in agua)
        {
            var pos = new List<Vector3Int>(celulas.Count);
            var tiles = new List<TileBase>(celulas.Count);
            foreach (var p in celulas)
            {
                TileBase base_ = Original(mapa.GetTile(p));             // o que o mapa "é" nesta célula agora
                if (base_ == null || !chaveDoNome.TryGetValue(base_.name, out var chave)) continue;
                if (!quadros.TryGetValue(chave, out var arr) || arr[quadro] == null) continue;
                foreach (var t in arr) if (t != null && !original.ContainsKey(t)) original[t] = base_;
                pos.Add(p); tiles.Add(arr[quadro]);
            }
            if (pos.Count > 0) mapa.SetTiles(pos.ToArray(), tiles.ToArray());
        }
    }
}
