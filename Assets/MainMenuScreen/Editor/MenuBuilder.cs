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

public static class MenuBuilder
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
        var nome = MakeInput(menuGO.transform, 400, 76, bN, bH, fBody, "NomeInput", "Ex.: Voluntario", 16, 34, 2);

        var ctrl = canvasGO.GetComponent<MainMenuController>();
        var novo = MakeButton(menuGO.transform, "Novo jogo", 300, 84, bN, bH, bP, fBody, 36, Cal);
        var salas = MakeButton(menuGO.transform, "Salas abertas", 210, 84, bN, bH, bP, fBody, 36, Cal);
        var joinLobby = MakeButton(menuGO.transform, "Entrar com código", 120, 84, bN, bH, bP, fBody, 36, Cal);
        //var cont = MakeButton(menuGO.transform, "Continuar", 312, 84, bN, bH, bP, fBody, 36, Cal);
        //var opc  = MakeButton(menuGO.transform, "Opções", 204, 84, bN, bH, bP, fBody, 36, Cal);
        //var cred = MakeButton(menuGO.transform, "Créditos", 96, 84, bN, bH, bP, fBody, 36, Cal);
        var exit = MakeButton(menuGO.transform, "Sair", 30, 84, bN, bH, bP, fBody, 36, Cal);
        var status = MakeText(menuGO.transform, "StatusConexao", "Conectando ao servidor...", -36, 40, 24, Cal, fBody, false);
        status.fontStyle = FontStyles.Italic;

        // Tela "Juntar-se a partida" (começa fechada; o botão abre ela direto)
        var joinScreen = BuildJoinScreen(canvasGO.transform, menuGO, bN, bH, bP, bM, fTitle, fBody);

        UnityEventTools.AddPersistentListener(novo.onClick, ctrl.newGame);
        UnityEventTools.AddPersistentListener(joinLobby.onClick, joinScreen.Open);
        //UnityEventTools.AddPersistentListener(cont.onClick, ctrl.Continue);
        //UnityEventTools.AddPersistentListener(opc.onClick, ctrl.Options);
        //UnityEventTools.AddPersistentListener(cred.onClick, ctrl.Credits);
        UnityEventTools.AddPersistentListener(exit.onClick, ctrl.quit);
        var listaSalas = BuildListaSalasScreen(canvasGO.transform, menuGO, bN, bH, bP, bM, fTitle, fBody);
        UnityEventTools.AddPersistentListener(salas.onClick, listaSalas.Open);

        // Liga o campo de nome, o texto de conexão e o botão que precisa do servidor
        var cso = new SerializedObject(ctrl);
        cso.FindProperty("joinRoomScreen").objectReferenceValue = joinScreen;
        cso.FindProperty("nomeInput").objectReferenceValue = nome;
        cso.FindProperty("statusConexao").objectReferenceValue = status;
        cso.FindProperty("botaoEntrar").objectReferenceValue = joinLobby;
        cso.ApplyModifiedPropertiesWithoutUndo();

        // Navegação vertical por teclado/controle
        //Selectable[] order = { novo, cont, opc, cred, sair };
        Selectable[] order = { nome, novo, salas, joinLobby, exit };
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

    // =====================================================================
    // Tela "Salas abertas" — painel grande no centro, estilo lista de servidores
    // =====================================================================
    static void Ret(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
    }

    static TextMeshProUGUI Col(Transform pai, string nome, string texto, float x, float w, float h, float tam, Color cor,
        TMP_FontAsset f, TextAlignmentOptions al = TextAlignmentOptions.MidlineLeft, bool negrito = false)
    {
        var t = MakeText(pai, nome, texto, 0, h, tam, cor, f, false);
        Ret((RectTransform)t.transform, x, 0, w, h);
        t.alignment = al; if (negrito) t.fontStyle = FontStyles.Bold;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    static ListaSalasScreen BuildListaSalasScreen(Transform canvas, GameObject menuGO,
        Sprite bN, Sprite bH, Sprite bP, Sprite bM, TMP_FontAsset fTitle, TMP_FontAsset fBody)
    {
        const float PW = 1300, PH = 820;
        var root = new GameObject("ListaSalasScreen", typeof(RectTransform), typeof(Image), typeof(ListaSalasScreen));
        root.transform.SetParent(canvas, false);
        var rrt = (RectTransform)root.transform;
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one; rrt.offsetMin = rrt.offsetMax = Vector2.zero;
        root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        // painel central
        var panel = new GameObject("Painel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero; prt.sizeDelta = new Vector2(PW, PH);
        var pimg = panel.GetComponent<Image>();
        if (campoN) { pimg.sprite = campoN; pimg.type = Image.Type.Sliced; pimg.color = new Color(0.85f, 0.85f, 0.85f, 0.97f); }
        else pimg.color = new Color(0.08f, 0.12f, 0.1f, 0.95f);

        var titulo = MakeText(panel.transform, "Titulo", "Salas abertas", 0, 90, 72, Ouro, fTitle, true);
        Ret((RectTransform)titulo.transform, 0, PH - 120, PW, 90); titulo.alignment = TextAlignmentOptions.Center;
        var sub = MakeText(panel.transform, "Subtitulo", "Escolha uma partida para entrar", 0, 36, 26, Cal, fBody, false);
        Ret((RectTransform)sub.transform, 0, PH - 160, PW, 36); sub.alignment = TextAlignmentOptions.Center;

        // cabeçalho das colunas
        var cab = new GameObject("Cabecalho", typeof(RectTransform));
        cab.transform.SetParent(panel.transform, false);
        Ret((RectTransform)cab.transform, 50, PH - 225, PW - 100, 40);
        foreach (var (nome, texto, x, w) in new[] { ("CSala", "SALA", 32f, 520f), ("CAnf", "ANFITRIÃO", 580f, 280f), ("CJog", "JOGADORES", 880f, 180f) })
        {
            var c = Col(cab.transform, nome, texto, x, w, 40, 22, Ouro, fBody, TextAlignmentOptions.MidlineLeft, true);
            c.characterSpacing = 6;
        }

        // lista com rolagem
        var scrollGO = new GameObject("Lista", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollGO.transform.SetParent(panel.transform, false);
        Ret((RectTransform)scrollGO.transform, 50, 150, PW - 100, PH - 385);
        scrollGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGO.transform, false);
        var vrt = (RectTransform)viewport.transform;
        vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = new Vector2(12, 12); vrt.offsetMax = new Vector2(-12, -12);

        var content = new GameObject("Conteudo", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        var crt = (RectTransform)content.transform;
        crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var vl = content.GetComponent<VerticalLayoutGroup>();
        vl.spacing = 10; vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var sr = scrollGO.GetComponent<ScrollRect>();
        sr.viewport = vrt; sr.content = crt; sr.horizontal = false; sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30;

        // linha modelo (uma por sala)
        var linha = new GameObject("Sala", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        linha.transform.SetParent(content.transform, false);
        linha.GetComponent<LayoutElement>().preferredHeight = 80;
        var limg = linha.GetComponent<Image>(); limg.sprite = bN; limg.type = Image.Type.Sliced;
        var lb = linha.GetComponent<Button>(); lb.targetGraphic = limg; lb.transition = Selectable.Transition.SpriteSwap;
        lb.spriteState = new SpriteState { highlightedSprite = bH, selectedSprite = bH, pressedSprite = bP };
        foreach (var (nome, x, w, tam, cor, al, neg) in new[]
        {
            ("ColNome", 32f, 520f, 32f, Cal, TextAlignmentOptions.MidlineLeft, true),
            ("ColAnfitriao", 580f, 280f, 28f, Cal, TextAlignmentOptions.MidlineLeft, false),
            ("ColJogadores", 880f, 180f, 28f, Cal, TextAlignmentOptions.MidlineLeft, false),
            ("Entrar", 1000f, 160f, 24f, Ouro, TextAlignmentOptions.MidlineRight, true),
        })
        {
            var c = Col(linha.transform, nome, nome == "Entrar" ? "ENTRAR ›" : "", x, w, 80, tam, cor, fBody, al, neg);
            var crt2 = (RectTransform)c.transform; crt2.anchoredPosition = new Vector2(x, 4);   // na face, acima da espessura
            c.outlineWidth = 0.12f; c.outlineColor = Ferrugem;
        }
        linha.SetActive(false);

        var vazio = MakeText(scrollGO.transform, "Vazio", "Nenhuma sala aberta agora.", 0, 0, 28, Cal, fBody, false);
        var vzt = (RectTransform)vazio.transform; vzt.anchorMin = Vector2.zero; vzt.anchorMax = Vector2.one;
        vzt.offsetMin = new Vector2(40, 0); vzt.offsetMax = new Vector2(-40, 0);
        vazio.alignment = TextAlignmentOptions.Center; vazio.fontStyle = FontStyles.Italic;
        vazio.textWrappingMode = TextWrappingModes.Normal;

        // rodapé: Voltar, status, Atualizar
        var voltar = MakeButton(panel.transform, "Voltar", 40, 84, bN, bH, bP, fBody, 36, Cal);
        Ret((RectTransform)voltar.transform, 50, 40, 320, 84);
        var atualizar = MakeButton(panel.transform, "Atualizar", 40, 84, bM, bH, bP, fBody, 36, Cal);
        Ret((RectTransform)atualizar.transform, PW - 370, 40, 320, 84);
        var status = MakeText(panel.transform, "Status", "", 0, 40, 24, Cal, fBody, false);
        Ret((RectTransform)status.transform, 380, 62, PW - 760, 40);
        status.alignment = TextAlignmentOptions.Center; status.fontStyle = FontStyles.Italic;

        var screen = root.GetComponent<ListaSalasScreen>();
        var so = new SerializedObject(screen);
        so.FindProperty("conteudo").objectReferenceValue = crt;
        so.FindProperty("modeloItem").objectReferenceValue = lb;
        so.FindProperty("textoVazio").objectReferenceValue = vazio;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("backButton").objectReferenceValue = voltar;
        so.FindProperty("atualizarButton").objectReferenceValue = atualizar;
        so.FindProperty("previousScreen").objectReferenceValue = menuGO;
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
        trt.offsetMin = new Vector2(32, 7); trt.offsetMax = new Vector2(-24, 0);   // texto alinhado à esquerda, na face (acima da espessura)
        txt.characterSpacing = 2;
        txt.outlineWidth = 0.12f; txt.outlineColor = Ferrugem;                    // contorno leve: lê bem no verde e no laranja
        return b;
    }
}
