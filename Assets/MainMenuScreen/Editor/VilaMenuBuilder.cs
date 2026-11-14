using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

public static class VilaMenuBuilder
{
    const string Root = "Assets/MainMenuScreen/";
    static readonly Vector4 Border = new Vector4(56, 42, 40, 42); // esquerda, baixo, direita, cima
    static Sprite campoN, campoF;                                   // fundo dos campos de texto
    static readonly Color Ouro = new Color32(255, 210, 90, 255);     // amarelo do EducaUs
    static readonly Color Cal = new Color32(241, 227, 200, 255);
    static readonly Color Ferrugem = new Color32(42, 30, 26, 255);    // contorno escuro (mesmo dos desenhos)

    [MenuItem("Tools/Menu principal")]
    public static void Build()
    {
        var bg = Import("Sprites/Background.png", Vector4.zero);
        var bN = Import("Sprites/btn_normal.png", Border);
        var bH = Import("Sprites/btn_highlight.png", Border);
        var bP = Import("Sprites/btn_pressed.png", Border);
        var bM = Import("Sprites/btn_primary.png", Border);
        campoN = Import("Sprites/field_normal.png", Border);
        campoF = Import("Sprites/field_focus.png", Border);
        if (!bg || !bN || !bH || !bP || !bM) return;

        var fTitle = Font("Inter-ExtraBold");
        var fBody = Font("Inter-SemiBold");

        var old = GameObject.Find("MainMenuCanvas");
        if (old) Undo.DestroyObjectImmediate(old);

        var canvasGO = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster), typeof(MainMenuController));
        Undo.RegisterCreatedObjectUndo(canvasGO, "Criar menu");
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // Fundo: preenche a tela sem distorcer (corta nas bordas se a proporção não for 16:9)
        var bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgGO.GetComponent<Image>();
        bgImg.sprite = bg; bgImg.raycastTarget = false;
        var fit = bgGO.GetComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = 1920f / 1080f;

        // Container do menu, canto inferior esquerdo
        var menuGO = new GameObject("Menu", typeof(RectTransform));
        menuGO.transform.SetParent(canvasGO.transform, false);
        var mrt = (RectTransform)menuGO.transform;
        mrt.anchorMin = mrt.anchorMax = Vector2.zero; mrt.pivot = Vector2.zero;
        mrt.anchoredPosition = new Vector2(96, 70); mrt.sizeDelta = new Vector2(560, 700);

        MakeText(menuGO.transform, "Titulo", "EducaUs", 574, 100, 84, Ouro, fTitle, true);
        MakeText(menuGO.transform, "Subtitulo", "Organize a comunidade. Recupere a vila.", 534, 36, 26, Cal, fBody, false);

        var rotuloNome = MakeText(menuGO.transform, "RotuloNome", "SEU NOME", 476, 32, 22, Ouro, fBody, false);
        rotuloNome.characterSpacing = 8; rotuloNome.fontStyle = FontStyles.Bold;
        var nome = MakeInput(menuGO.transform, 396, 76, bN, bH, fBody, "NomeInput", "Ex.: Bruno", 16, 34, 2);

        var ctrl = canvasGO.GetComponent<MainMenuController>();
        var novo = MakeButton(menuGO.transform, "Novo jogo", 290, 84, bN, bH, bP, fBody, 36, Cal);
        var joinLobby = MakeButton(menuGO.transform, "Juntar-se a partida", 190, 84, bN, bH, bP, fBody, 36, Cal);
        //var cont = MakeButton(menuGO.transform, "Continuar", 312, 84, bN, bH, bP, fBody, 36, Cal);
        //var opc  = MakeButton(menuGO.transform, "Opções", 204, 84, bN, bH, bP, fBody, 36, Cal);
        //var cred = MakeButton(menuGO.transform, "Créditos", 96, 84, bN, bH, bP, fBody, 36, Cal);
        var exit = MakeButton(menuGO.transform, "Sair", 90, 84, bN, bH, bP, fBody, 36, Cal);
        var status = MakeText(menuGO.transform, "StatusConexao", "Conectando ao servidor...", 30, 40, 24, Cal, fBody, false);
        status.fontStyle = FontStyles.Italic;

        // Tela "Juntar-se a partida" (começa fechada; o botão abre ela direto)
        var joinScreen = BuildJoinScreen(canvasGO.transform, menuGO, bN, bH, bP, bM, fTitle, fBody);

        UnityEventTools.AddPersistentListener(novo.onClick, ctrl.newGame);
        UnityEventTools.AddPersistentListener(joinLobby.onClick, joinScreen.Open);
        //UnityEventTools.AddPersistentListener(cont.onClick, ctrl.Continue);
        //UnityEventTools.AddPersistentListener(opc.onClick, ctrl.Options);
        //UnityEventTools.AddPersistentListener(cred.onClick, ctrl.Credits);
        UnityEventTools.AddPersistentListener(exit.onClick, ctrl.quit);

        // Liga o campo de nome, o texto de conexão e o botão que precisa do servidor
        var cso = new SerializedObject(ctrl);
        cso.FindProperty("joinRoomScreen").objectReferenceValue = joinScreen;
        cso.FindProperty("nomeInput").objectReferenceValue = nome;
        cso.FindProperty("statusConexao").objectReferenceValue = status;
        cso.FindProperty("botaoEntrar").objectReferenceValue = joinLobby;
        cso.ApplyModifiedPropertiesWithoutUndo();

        // Navegação vertical por teclado/controle
        //Selectable[] order = { novo, cont, opc, cred, sair };
        Selectable[] order = { nome, novo, joinLobby, exit };
        foreach (var s in order)
            s.navigation = new Navigation { mode = Navigation.Mode.Vertical };

#if UNITY_2023_1_OR_NEWER
        var es = Object.FindFirstObjectByType<EventSystem>();
#else
        var es = Object.FindObjectOfType<EventSystem>();
#endif
        if (!es)
        {
            var esGO = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            esGO.AddComponent<InputSystemUIInputModule>();
#else
            esGO.AddComponent<StandaloneInputModule>();
#endif
            Undo.RegisterCreatedObjectUndo(esGO, "Criar EventSystem");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = canvasGO;
        Debug.Log("Menu criado! Ajuste o nome da cena em MainMenuController > New Game Scene.");
    }

    // =====================================================================
    // Tela "Juntar-se a partida" — mesmo layout, sprites e fontes do menu
    // =====================================================================
    static JoinRoomScreen BuildJoinScreen(Transform canvas, GameObject menuGO,
        Sprite bN, Sprite bH, Sprite bP, Sprite bM, TMP_FontAsset fTitle, TMP_FontAsset fBody)
    {
        // Raiz: tela cheia, escurece um pouco o fundo
        var root = new GameObject("JoinRoomScreen", typeof(RectTransform), typeof(Image),
            typeof(CanvasGroup), typeof(JoinRoomScreen));
        root.transform.SetParent(canvas, false);
        var rrt = (RectTransform)root.transform;
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
        rrt.offsetMin = rrt.offsetMax = Vector2.zero;
        var dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.4f);

        // Painel: mesma posição e tamanho do container do menu
        var panel = new GameObject("Painel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = Vector2.zero; prt.pivot = Vector2.zero;
        prt.anchoredPosition = new Vector2(96, 70); prt.sizeDelta = new Vector2(560, 700);

        MakeText(panel.transform, "Titulo", "Juntar-se a partida", 574, 100, 78, Ouro, fTitle, true);
        MakeText(panel.transform, "Subtitulo", "Digite o código que o anfitrião compartilhou", 534, 36, 24, Cal, fBody, false);

        var label = MakeText(panel.transform, "Rotulo", "CÓDIGO DA SALA", 456, 32, 22, Ouro, fBody, false);
        label.characterSpacing = 8; label.fontStyle = FontStyles.Bold;

        var input = MakeInput(panel.transform, 360, 84, bN, bH, fBody, "CodigoInput", "EX: K7QX", 8, 40, 12);

        var status = MakeText(panel.transform, "Status", "", 310, 40, 26, Cal, fBody, false);
        status.fontStyle = FontStyles.Italic;

        var entrar = MakeButton(panel.transform, "Entrar", 200, 84, bM, bH, bP, fBody, 36, Cal);
        var voltar = MakeButton(panel.transform, "Voltar", 100, 84, bN, bH, bP, fBody, 36, Cal);

        Selectable[] order = { input, entrar, voltar };
        foreach (var s in order)
            s.navigation = new Navigation { mode = Navigation.Mode.Vertical };

        // Liga as referências do script
        var screen = root.GetComponent<JoinRoomScreen>();
        var so = new SerializedObject(screen);
        so.FindProperty("codeInput").objectReferenceValue = input;
        so.FindProperty("joinButton").objectReferenceValue = entrar;
        so.FindProperty("joinButtonLabel").objectReferenceValue = entrar.GetComponentInChildren<TMP_Text>();
        so.FindProperty("backButton").objectReferenceValue = voltar;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("shakeTarget").objectReferenceValue = input.transform;
        so.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        so.FindProperty("previousScreen").objectReferenceValue = menuGO;
        so.FindProperty("infoColor").colorValue = Cal;
        so.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(false);
        return screen;
    }

    static TMP_InputField MakeInput(Transform parent, float y, float h, Sprite normal, Sprite hi, TMP_FontAsset font,
        string nome, string placeholder, int limite, float tamanho, float espacamento)
    {
        var go = new GameObject(nome, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(540, h);

        if (campoN) normal = campoN;            // campo escuro próprio (se existir)
        if (campoF) hi = campoF;
        var img = go.GetComponent<Image>();
        img.sprite = normal; img.type = Image.Type.Sliced;

        // Área do texto (respeita a listra da esquerda, igual aos botões)
        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var art = (RectTransform)area.transform;
        art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
        art.offsetMin = new Vector2(72, 8); art.offsetMax = new Vector2(-24, -8);

        var ph = MakeText(area.transform, "Placeholder", placeholder, 0, 0, tamanho - 6,
            new Color(Cal.r, Cal.g, Cal.b, 0.35f), font, false);
        Fill(ph.rectTransform);
        ph.alignment = TextAlignmentOptions.Center;
        ph.fontStyle = FontStyles.Italic;
        ph.characterSpacing = espacamento * 0.6f;

        var tx = MakeText(area.transform, "Texto", "", 0, 0, tamanho, Cal, font, false);
        Fill(tx.rectTransform);
        tx.alignment = TextAlignmentOptions.Center;
        tx.characterSpacing = espacamento;

        // Adicionado depois dos filhos existirem
        var input = go.AddComponent<TMP_InputField>();
        input.textViewport = art;
        input.textComponent = tx;
        input.placeholder = ph;
        input.targetGraphic = img;
        input.transition = Selectable.Transition.SpriteSwap;
        input.spriteState = new SpriteState { highlightedSprite = hi, selectedSprite = hi, pressedSprite = hi };
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.characterValidation = TMP_InputField.CharacterValidation.None; // validação feita pelos scripts
        input.characterLimit = limite;
        input.pointSize = tamanho;
        input.customCaretColor = true;
        input.caretColor = Ouro;
        input.caretWidth = 3;
        input.selectionColor = new Color(Ouro.r, Ouro.g, Ouro.b, 0.35f);
        return input;
    }

    static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static Sprite Import(string rel, Vector4 border)
    {
        string path = Root + rel;
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError("Arquivo não encontrado: " + path); return null; }
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spriteBorder = border;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Procura a fonte TMP; se só existir o arquivo .otf/.ttf em Fonts/, cria a fonte TMP (dinâmica) sozinho.
    static TMP_FontAsset Font(string key)
    {
        string sdf = Root + "Fonts/" + key + " SDF.asset";
        var pronta = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdf);
        if (pronta) return pronta;

        Font arquivo = AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/" + key + ".otf")
                    ?? AssetDatabase.LoadAssetAtPath<Font>(Root + "Fonts/" + key + ".ttf");
        if (arquivo)
        {
            var fa = TMP_FontAsset.CreateFontAsset(arquivo);
            if (fa)
            {
                fa.name = key + " SDF";
                AssetDatabase.CreateAsset(fa, sdf);
                if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0])
                {
                    fa.atlasTextures[0].name = key + " Atlas";
                    AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                }
                if (fa.material) { fa.material.name = key + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
                AssetDatabase.SaveAssets();
                return fa;
            }
        }
        Debug.LogWarning("[Menu] Fonte " + key + " não encontrada em " + Root + "Fonts. Usando a padrão do TextMeshPro.");
        return null;
    }

    static TextMeshProUGUI MakeText(Transform parent, string name, string text, float y, float h,
        float size, Color color, TMP_FontAsset font, bool outline)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(560, h);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap;
        if (font) t.font = font;
        if (outline) { t.outlineWidth = 0.2f; t.outlineColor = Ferrugem; }
        return t;
    }

    static Button MakeButton(Transform parent, string label, float y, float h,
        Sprite normal, Sprite hi, Sprite pressed, TMP_FontAsset font, float size, Color color)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(MenuButtonFX));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(0, y); rt.sizeDelta = new Vector2(540, h);

        var img = go.GetComponent<Image>();
        img.sprite = normal; img.type = Image.Type.Sliced;

        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        b.transition = Selectable.Transition.SpriteSwap;
        b.spriteState = new SpriteState { highlightedSprite = hi, selectedSprite = hi, pressedSprite = pressed };

        var txt = MakeText(go.transform, "Texto", label, 0, 0, size, color, font, false);
        var trt = (RectTransform)txt.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(76, 7); trt.offsetMax = new Vector2(-24, 0);   // centraliza na face (acima da espessura)
        txt.characterSpacing = 2;
        txt.outlineWidth = 0.12f; txt.outlineColor = Ferrugem;                    // contorno leve: lê bem no verde e no laranja
        return b;
    }
}
