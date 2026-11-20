using System;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Tools → Vila → Preparar animações.
/// Recorta (fatiar) as folhas de animação que estão em qualquer pasta "Resources/Animacoes":
/// Personagem_0..5.png (andar do personagem, 6 cores) e Agua_Anim_128.png (água animada).
/// Cada .png tem um .json de mesmo nome dizendo onde fica cada quadro.
/// </summary>
public static class PrepararAnimacoes
{
    [Serializable] class Quadro { public string n; public int r, c; }
    [Serializable] class Folha { public string arquivo; public int tamanho, margem, passo, linhas; public float[] pivo; public Quadro[] tiles; }

    [MenuItem("Tools/Vila/Preparar animações")]
    public static void Preparar()
    {
        var jsons = AssetDatabase.FindAssets("t:TextAsset")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.Replace('\\', '/').Contains("/Resources/Animacoes/") && p.EndsWith(".json"))
            .ToList();
        if (jsons.Count == 0)
        {
            EditorUtility.DisplayDialog("Animações", "Não achei nenhum .json em uma pasta Resources/Animacoes.\nExtraia o zip das animações dentro de Assets.", "OK");
            return;
        }
        int feitas = 0;
        foreach (var jp in jsons)
        {
            var folha = JsonUtility.FromJson<Folha>(AssetDatabase.LoadAssetAtPath<TextAsset>(jp).text);
            string png = System.IO.Path.ChangeExtension(jp, ".png");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(png) == null) { Debug.LogWarning("[Animações] Falta a imagem " + png); continue; }
            try { Fatiar(png, folha); feitas++; }
            catch (Exception e) { Debug.LogError($"[Animações] Erro ao preparar {png}: {e.Message}"); }   // continua com as outras folhas
        }
        Debug.Log($"[Animações] {feitas} folha(s) preparada(s).");
        EditorUtility.DisplayDialog("Animações", $"{feitas} folha(s) de animação preparada(s).", "OK");
    }

    static void Fatiar(string png, Folha f)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(png);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Multiple;
        imp.spritePixelsPerUnit = 64;                    // 128 px = 2 unidades, igual ao resto do jogo
        imp.filterMode = FilterMode.Bilinear;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;
        imp.alphaIsTransparency = true;
        imp.maxTextureSize = 4096;
        var st = new TextureImporterSettings();
        imp.ReadTextureSettings(st);
        st.spriteMeshType = SpriteMeshType.FullRect;
        st.spriteGenerateFallbackPhysicsShape = false;
        imp.SetTextureSettings(st);
        imp.SaveAndReimport();

        var fab = new SpriteDataProviderFactories(); fab.Init();
        var dp = fab.GetSpriteEditorDataProviderFromObject(imp);
        dp.InitSpriteEditorDataProvider();
        // se a folha já tiver quadros com nome repetido (de uma preparação anterior), fica com o primeiro de cada
        var existentes = dp.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);

        int passo = f.passo > 0 ? f.passo : f.tamanho;
        int altura = f.linhas * passo;
        Vector2 pivo = f.pivo != null && f.pivo.Length == 2 ? new Vector2(f.pivo[0], f.pivo[1]) : new Vector2(.5f, .5f);
        var usados = new System.Collections.Generic.HashSet<GUID>();   // cada quadro com um ID único (a folha antiga podia ter IDs repetidos)
        var rects = f.tiles.Select(t =>
        {
            GUID id = existentes.TryGetValue(t.n, out var antigo) && !usados.Contains(antigo) ? antigo : GUID.Generate();
            usados.Add(id);
            return new SpriteRect
            {
                name = t.n,
                rect = new Rect(t.c * passo + f.margem, altura - (t.r * passo + f.margem + f.tamanho), f.tamanho, f.tamanho),
                alignment = SpriteAlignment.Custom,
                pivot = pivo,
                spriteID = id,
            };
        }).ToArray();
        dp.SetSpriteRects(rects);
        dp.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
        dp.Apply();
        imp.SaveAndReimport();
        Debug.Log($"[Animações] {png}: {rects.Length} quadros");
    }
}
