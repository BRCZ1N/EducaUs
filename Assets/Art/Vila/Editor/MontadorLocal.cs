using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// Tools → Vila → Montar local (JSON selecionado)
/// Selecione um .json de local (ex.: ong.json) na janela Project e rode o menu.
/// Fatia os tilesets, cria os Tiles, monta as camadas (Chao / Paredes / Faces),
/// coloca os objetos com ordenação por Y e colisor, e cria spawns e pontos de tarefa.
/// Precisa do pacote "2D Sprite" (vem no template 2D).
/// </summary>
public static class MontadorLocal
{
    // ---------- formato do JSON (v2) ----------
    [Serializable] class TileInfo { public string n; public int r, c; }
    [Serializable] class Tileset { public string arquivo; public int tamanho, margem, passo, linhas; public TileInfo[] tiles; }
    [Serializable] class Camada { public string nome, forma; public int ordem; public bool colisao; public string[] tiles; }
    [Serializable] class Prop { public string sprite, tipo, area; public float x, y, cw, ch, coy; }
    [Serializable] class PontoF { public float x, y; }
    [Serializable] class Tarefa { public string nome; public float x, y; public int jogadores = 1; }
    [Serializable] class Colisao { public float celula; public int largura, altura; public string[] linhas; }
    [Serializable] class Par { public string p, r; }
    [Serializable] class Area { public string nome; public int x, y, w, h; public Par[] pares; }
    [Serializable] class Origem { public float x, y; }
    [Serializable] class Local
    {
        public string nome;
        public int largura, altura;
        public float celula;
        public Tileset[] tilesets;
        public Camada[] camadas;
        public Prop[] props;
        public PontoF[] spawns;
        public Tarefa[] tarefas;
        public Colisao colisao;
        public Origem origem;        // posição do local na planta geral (unidades, a partir do canto de cima à esquerda)
        public float mundoAltura;    // altura total do mundo (unidades)
        public Area[] areas;         // áreas que podem ser poluídas / recuperadas
    }

    const int OrdemChaoDecal = 1;   // tapetes
    const int OrdemParede = 4;      // quadros, placas presos na parede
    const int OrdemObjetos = 10;    // móveis e personagens (ordenados por Y)

    [MenuItem("Tools/Vila/Montar local (JSON selecionado)")]
    public static void Montar()
    {
        var json = Selection.activeObject as TextAsset;
        if (json == null)
        {
            EditorUtility.DisplayDialog("Vila", "Selecione o arquivo .json do local (ex.: ong.json) na janela Project e rode de novo.", "OK");
            return;
        }
        var L = JsonUtility.FromJson<Local>(json.text);
        pastaJson = Path.GetDirectoryName(AssetDatabase.GetAssetPath(json)).Replace('\\', '/');
        int W = L.largura, H = L.altura;
        float cel = L.celula > 0 ? L.celula : 2f;

        // ---------- tiles ----------
        // "grid" = bloqueia a célula inteira (paredes); "sprite" = bloqueia só o desenho (faces das paredes)
        var colisores = new Dictionary<string, Tile.ColliderType>();
        foreach (var c in L.camadas.Where(c => c.colisao))
            foreach (var n in c.tiles.Where(n => !string.IsNullOrEmpty(n)))
                colisores[n] = c.forma == "sprite" ? Tile.ColliderType.Sprite : Tile.ColliderType.Grid;
        var tiles = new Dictionary<string, TileBase>();
        foreach (var ts in L.tilesets)
        {
            string tex = EncontrarExato(ts.arquivo, "Texture2D");
            if (tex == null) { Erro($"Não achei {ts.arquivo}.png dentro de Assets."); return; }
            Fatiar(tex, ts);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(tex).OfType<Sprite>();
            foreach (var kv in CriarTiles(tex, sprites, colisores)) tiles[kv.Key] = kv.Value;
        }

        // ---------- hierarquia ----------
        string nomeRaiz = "Local_" + L.nome;
        var antigo = GameObject.Find(nomeRaiz);
        if (antigo != null) Undo.DestroyObjectImmediate(antigo);
        var raiz = new GameObject(nomeRaiz, typeof(Grid));
        Undo.RegisterCreatedObjectUndo(raiz, "Montar " + L.nome);
        raiz.GetComponent<Grid>().cellSize = new Vector3(cel, cel, 0);

        Vector3Int Cel(int x, int y) => new Vector3Int(x, H - 1 - y, 0);
        Vector3 Mundo(float ux, float uy) => new Vector3(ux, H * cel - uy, 0);

        Tilemap primeira = null;
        foreach (var c in L.camadas)
        {
            var go = new GameObject(c.nome, typeof(Tilemap), typeof(TilemapRenderer));
            go.transform.SetParent(raiz.transform, false);
            go.GetComponent<TilemapRenderer>().sortingOrder = c.ordem;
            var tm = go.GetComponent<Tilemap>();
            if (primeira == null) primeira = tm;
            if (c.colisao)
            {
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
                var tc = go.AddComponent<TilemapCollider2D>();
                go.AddComponent<CompositeCollider2D>();
#if UNITY_2023_1_OR_NEWER
                tc.compositeOperation = Collider2D.CompositeOperation.Merge;
#else
                tc.usedByComposite = true;
#endif
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    string n = c.tiles[y * W + x];
                    if (string.IsNullOrEmpty(n)) continue;
                    if (tiles.TryGetValue(n, out var t)) tm.SetTile(Cel(x, y), t);
                    else Debug.LogWarning($"[Vila] Tile não encontrado: {n}");
                }
        }

        // ---------- colisão fina (grade própria, invisível) ----------
        if (L.colisao != null && L.colisao.linhas != null && L.colisao.linhas.Length > 0)
        {
            var gridGo = new GameObject("Colisao", typeof(Grid));
            gridGo.transform.SetParent(raiz.transform, false);
            gridGo.GetComponent<Grid>().cellSize = new Vector3(L.colisao.celula, L.colisao.celula, 0);
            var tmGo = new GameObject("Bloqueios", typeof(Tilemap), typeof(TilemapRenderer));
            tmGo.transform.SetParent(gridGo.transform, false);
            tmGo.GetComponent<TilemapRenderer>().enabled = false;
            var rbc = tmGo.AddComponent<Rigidbody2D>();
            rbc.bodyType = RigidbodyType2D.Static;
            var tcc = tmGo.AddComponent<TilemapCollider2D>();
            tmGo.AddComponent<CompositeCollider2D>();
#if UNITY_2023_1_OR_NEWER
            tcc.compositeOperation = Collider2D.CompositeOperation.Merge;
#else
            tcc.usedByComposite = true;
#endif
            var bloco = TileDeColisao(L.tilesets[0].arquivo, tiles.Values.OfType<Tile>().FirstOrDefault(tl => tl.sprite != null));
            var tmc = tmGo.GetComponent<Tilemap>();
            int hc = L.colisao.altura;
            for (int cy = 0; cy < hc; cy++)
            {
                string linha = L.colisao.linhas[cy];
                for (int cx = 0; cx < linha.Length; cx++)
                    if (linha[cx] == '1') tmc.SetTile(new Vector3Int(cx, hc - 1 - cy, 0), bloco);
            }
        }

        // ---------- objetos ----------
        var objetos = new GameObject("Objetos");
        objetos.transform.SetParent(raiz.transform, false);
        var cache = new Dictionary<string, Sprite>();
        var lixoPorArea = new Dictionary<string, List<GameObject>>();   // objetos que somem quando a área é recuperada
        foreach (var p in L.props)
        {
            if (!cache.TryGetValue(p.sprite, out var spr)) cache[p.sprite] = spr = ImportarProp(p.sprite);
            if (spr == null) { Debug.LogWarning($"[Vila] Sprite de objeto não encontrado: {p.sprite}"); continue; }
            var go = new GameObject(p.sprite);
            go.transform.SetParent(objetos.transform, false);
            go.transform.localPosition = Mundo(p.x, p.y);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spr;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;   // compara pelos pés (base), não pelo meio
            sr.sortingOrder = p.tipo == "chao" ? OrdemChaoDecal : p.tipo == "parede" ? OrdemParede : OrdemObjetos;
            if (!string.IsNullOrEmpty(p.area))
            {
                if (!lixoPorArea.TryGetValue(p.area, out var lst)) lixoPorArea[p.area] = lst = new List<GameObject>();
                lst.Add(go);
            }
            if (p.cw > 0 && p.ch > 0)
            {
                var bc = go.AddComponent<BoxCollider2D>();
                bc.size = new Vector2(p.cw, p.ch);
                bc.offset = new Vector2(0, p.coy + p.ch / 2f);
            }
        }

        // ---------- spawns e tarefas ----------
        var spawns = new GameObject("Spawns");
        spawns.transform.SetParent(raiz.transform, false);
        for (int i = 0; i < L.spawns.Length; i++)
        {
            var s = new GameObject("Spawn_" + (i + 1));
            s.transform.SetParent(spawns.transform, false);
            s.transform.localPosition = Mundo(L.spawns[i].x, L.spawns[i].y);
        }
        var tarefas = new GameObject("Tarefas");
        tarefas.transform.SetParent(raiz.transform, false);
        foreach (var t in L.tarefas ?? new Tarefa[0])
        {
            var g = new GameObject("Tarefa - " + t.nome);
            g.transform.SetParent(tarefas.transform, false);
            g.transform.localPosition = Mundo(t.x, t.y);
            var cc = g.AddComponent<CircleCollider2D>();
            cc.isTrigger = true;
            cc.radius = 1.2f;
            var pt = g.AddComponent<PontoDeTarefa>();          // marcador e interação (GerenciadorAmbiental)
            pt.nome = t.nome;
            pt.jogadores = Mathf.Max(1, t.jogadores);
        }

        // ---------- áreas recuperáveis ----------
        if (L.areas != null && L.areas.Length > 0)
        {
            Tilemap chaoTm = raiz.GetComponentsInChildren<Tilemap>().FirstOrDefault(tm => tm.name == "Chao") ?? primeira;
            var areasRaiz = new GameObject("Areas");
            areasRaiz.transform.SetParent(raiz.transform, false);
            foreach (var a in L.areas)
            {
                var go = new GameObject("Area_" + a.nome);
                go.transform.SetParent(areasRaiz.transform, false);
                var comp = go.AddComponent<AreaAmbiental>();
                var so = new SerializedObject(comp);
                so.FindProperty("tilemap").objectReferenceValue = chaoTm;
                so.FindProperty("area").boundsIntValue = new BoundsInt(a.x, H - a.y - a.h, 0, a.w, a.h, 1);
                var lista = so.FindProperty("pares");
                lista.arraySize = a.pares.Length;
                for (int i = 0; i < a.pares.Length; i++)
                {
                    var el = lista.GetArrayElementAtIndex(i);
                    tiles.TryGetValue(a.pares[i].p, out var tp);
                    tiles.TryGetValue(a.pares[i].r, out var tr);
                    el.FindPropertyRelative("poluido").objectReferenceValue = tp;
                    el.FindPropertyRelative("recuperado").objectReferenceValue = tr;
                }
                so.FindProperty("comecaPoluida").boolValue = true;
                // "Rio" = objetos de lixo (somem ao recuperar); "+Rio" = recompensas (aparecem ao recuperar)
                foreach (var (chave, campo) in new[] { (a.nome, "objetosPoluicao"), ("+" + a.nome, "objetosRecuperacao") })
                {
                    if (!lixoPorArea.TryGetValue(chave, out var objs)) continue;
                    var lo = so.FindProperty(campo);
                    lo.arraySize = objs.Count;
                    for (int i = 0; i < objs.Count; i++) lo.GetArrayElementAtIndex(i).objectReferenceValue = objs[i];
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---------- posição na planta geral ----------
        if (L.origem != null && L.mundoAltura > 0)
            raiz.transform.position = new Vector3(L.origem.x, L.mundoAltura - L.origem.y - H * cel, 0);

        ConfigurarOrdenacaoPorY();
        EditorSceneManager.MarkSceneDirty(raiz.scene);
        Selection.activeGameObject = raiz;
        Debug.Log($"[Vila] {L.nome} montado: {W}x{H} tiles ({W * cel}x{H * cel} unidades), {L.props.Length} objetos.");
    }

    // ================================================================== helpers

    static void Erro(string msg) => EditorUtility.DisplayDialog("Vila", msg, "OK");

    static string pastaJson;

    /// <summary>Procura primeiro na pasta do .json (e subpastas); só depois no projeto inteiro.
    /// Evita pegar uma cópia antiga com o mesmo nome em outra pasta.</summary>
    static string EncontrarExato(string nome, string tipo)
    {
        if (!string.IsNullOrEmpty(pastaJson) && AssetDatabase.IsValidFolder(pastaJson))
            foreach (var g in AssetDatabase.FindAssets($"{nome} t:{tipo}", new[] { pastaJson }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == nome) return p;
            }
        foreach (var g in AssetDatabase.FindAssets($"{nome} t:{tipo}"))
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) == nome) return p;
        }
        return null;
    }

    static void Fatiar(string texPath, Tileset ts)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit = ts.tamanho / 2f;     // tile = 2 unidades
        imp.filterMode = FilterMode.Bilinear;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.maxTextureSize = 4096;
        var st = new TextureImporterSettings();
        imp.ReadTextureSettings(st);
        st.spriteGenerateFallbackPhysicsShape = true;   // forma de colisão a partir do desenho
        st.spriteMeshType = SpriteMeshType.FullRect;     // quadrado inteiro: sem frestas entre tiles
        imp.SetTextureSettings(st);
        imp.SaveAndReimport();

        var f = new SpriteDataProviderFactories(); f.Init();
        var dp = f.GetSpriteEditorDataProviderFromObject(imp);
        dp.InitSpriteEditorDataProvider();
        var existentes = dp.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        // cada tile tem uma margem estendida em volta (evita linhas entre tiles com filtro Bilinear).
        // Confere o tamanho REAL da imagem: se for a versão antiga sem margem, corta do jeito antigo.
        int passo = ts.passo > 0 ? ts.passo : ts.tamanho;
        int margem = ts.margem;
        imp.GetSourceTextureWidthAndHeight(out int largTex, out int altTex);
        if (altTex == ts.linhas * ts.tamanho && passo != ts.tamanho)
        {
            Debug.LogWarning($"[Vila] {texPath} é a versão ANTIGA (sem margem, {largTex}x{altTex}). Funciona, mas substitua pela nova para evitar linhas entre tiles.");
            passo = ts.tamanho; margem = 0;
        }
        else if (altTex != ts.linhas * passo)
        {
            Debug.LogError($"[Vila] {texPath} tem {largTex}x{altTex}, mas o .json espera {8 * passo}x{ts.linhas * passo}. Essa imagem não é a certa para este .json.");
        }
        Debug.Log($"[Vila] Tileset usado: {texPath} ({largTex}x{altTex})");
        int altura = ts.linhas * passo;
        var rects = ts.tiles.Select(t => new SpriteRect
        {
            name = t.n,
            rect = new Rect(t.c * passo + margem, altura - (t.r * passo + margem + ts.tamanho), ts.tamanho, ts.tamanho),
            alignment = SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            spriteID = existentes.TryGetValue(t.n, out var id) ? id : GUID.Generate(),
        }).ToArray();
        dp.SetSpriteRects(rects);
        dp.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        dp.Apply();
        imp.SaveAndReimport();
    }

    static Dictionary<string, TileBase> CriarTiles(string texPath, IEnumerable<Sprite> sprites, Dictionary<string, Tile.ColliderType> colisores)
    {
        string dir = Path.GetDirectoryName(texPath).Replace('\\', '/');
        string pasta = dir + "/Tiles";
        if (!AssetDatabase.IsValidFolder(pasta)) AssetDatabase.CreateFolder(dir, "Tiles");
        var res = new Dictionary<string, TileBase>();
        foreach (var s in sprites)
        {
            string path = $"{pasta}/{s.name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, path); }
            tile.sprite = s;
            tile.colliderType = colisores.TryGetValue(s.name, out var tipo) ? tipo : Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            res[s.name] = tile;
        }
        AssetDatabase.SaveAssets();
        return res;
    }

    /// <summary>Tile invisível que bloqueia a célula inteira (usado só na grade de colisão).</summary>
    static Tile TileDeColisao(string tilesetRef, Tile modelo)
    {
        string tex = EncontrarExato(tilesetRef, "Texture2D");
        string pasta = Path.GetDirectoryName(tex).Replace('\\', '/') + "/Tiles";
        string path = pasta + "/_colisao_bloco.asset";
        var t = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (t == null) { t = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(t, path); }
        t.sprite = modelo != null ? modelo.sprite : null;
        t.colliderType = Tile.ColliderType.Grid;
        EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();
        return t;
    }

    static Sprite ImportarProp(string nome)
    {
        string path = EncontrarExato(nome, "Texture2D");
        if (path == null) return null;
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        var st = new TextureImporterSettings();
        imp.ReadTextureSettings(st);
        st.spritePixelsPerUnit = 64;
        st.spriteAlignment = (int)SpriteAlignment.BottomCenter;   // pivô na base: ordenação por Y correta
        st.filterMode = FilterMode.Bilinear;
        st.mipmapEnabled = false;
        st.alphaIsTransparency = true;
        imp.SetTextureSettings(st);
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// <summary>Quem está mais "embaixo" na tela é desenhado na frente (como no Among Us).</summary>
    static void ConfigurarOrdenacaoPorY()
    {
        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = new Vector3(0, 1, 0);
        // URP 2D: a configuração fica no Renderer2DData
        foreach (var g in AssetDatabase.FindAssets("t:Renderer2DData"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g));
            if (asset == null) continue;
            var so = new SerializedObject(asset);
            var mode = so.FindProperty("m_TransparencySortMode");
            var axis = so.FindProperty("m_TransparencySortAxis");
            if (mode != null) mode.intValue = (int)TransparencySortMode.CustomAxis;
            if (axis != null) axis.vector3Value = new Vector3(0, 1, 0);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
