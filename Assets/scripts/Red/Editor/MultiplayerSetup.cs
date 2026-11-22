using System.Linq;
using Fusion;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Herramienta de editor: Tools > Arco AR > Configurar multijugador
// Crea los prefabs de red, prepara la escena pofa y crea la escena de menu.
public static class MultiplayerSetup
{
    private const string Folder = "Assets/Multijugador";
    private const string PlayerPrefabPath = Folder + "/PlayerPrefab.prefab";
    private const string RunnerPrefabPath = Folder + "/RunnerPrefab.prefab";
    private const string AvatarMatPath = Folder + "/AvatarMat.mat";
    private const string VisorMatPath = Folder + "/AvatarVisorMat.mat";
    private const string MenuScenePath = "Assets/Scenes/MenuMultijugador.unity";
    private const string GameScenePath = "Assets/Scenes/pofa.unity";

    private const string RoomChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    [MenuItem("Tools/Arco AR/Configurar multijugador")]
    public static void SetupAll()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) != null &&
            !EditorUtility.DisplayDialog("Arco AR",
                "Ya existe " + MenuScenePath + ".\nSe va a REEMPLAZAR por una nueva. ¿Continuar?",
                "Reemplazar", "Cancelar"))
            return;

        EnsureFolder();

        GameObject player = CreatePlayerPrefab();
        GameObject runner = CreateRunnerPrefab();
        if (player == null || runner == null) return;

        if (!SetupGameScene()) return;
        if (!CreateMenuScene(runner)) return;
        SetupBuildSettings();

        EditorUtility.DisplayDialog("Arco AR",
            "Listo:\n\n" +
            "• " + PlayerPrefabPath + "\n" +
            "• " + RunnerPrefabPath + "\n" +
            "• pofa: HardwareRig, ARAnchorManager y ColocationManager en el XR Origin\n" +
            "• " + MenuScenePath + " (indice 0 en Build Profiles)\n\n" +
            "Abre MenuMultijugador y dale Play para probar la conexion.",
            "OK");
    }

    // ------------------------------------------------------------ PREFABS

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets", "Multijugador");
    }

    private static GameObject CreatePlayerPrefab()
    {
        // No se sobrescribe: si ya existe, puede que lo hayas personalizado
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (existing != null)
            return existing;

        Material body = GetOrCreateMaterial(AvatarMatPath, new Color(0.2f, 0.6f, 1f));
        Material visor = GetOrCreateMaterial(VisorMatPath, new Color(0.1f, 0.1f, 0.12f));

        GameObject root = new GameObject("PlayerPrefab");
        NetworkObject no = root.AddComponent<NetworkObject>();
        // Si el jugador se va, su avatar desaparece para los demas
        no.Flags |= NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        root.AddComponent<NetworkRig>();

        GameObject head = Part(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.6f, 0f), Vector3.one * 0.22f, body);
        // "Visera" para que se vea hacia donde mira el otro jugador
        Part(PrimitiveType.Cube, "Visor", head.transform, new Vector3(0f, 0.05f, 0.42f), new Vector3(0.8f, 0.3f, 0.25f), visor);
        Part(PrimitiveType.Cube, "LeftHand", root.transform, new Vector3(-0.25f, 1.1f, 0.3f), Vector3.one * 0.08f, body);
        Part(PrimitiveType.Cube, "RightHand", root.transform, new Vector3(0.25f, 1.1f, 0.3f), Vector3.one * 0.08f, body);
        Part(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 0.3f, 0.25f), body);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        // Sin collider: que las flechas no choquen con los avatares
        Collider col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static Material GetOrCreateMaterial(string path, Color color)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static GameObject CreateRunnerPrefab()
    {
        string playerGuid = AssetDatabase.AssetPathToGUID(PlayerPrefabPath);
        if (string.IsNullOrEmpty(playerGuid))
        {
            Debug.LogError("[Arco AR] No se encontro el PlayerPrefab.");
            return null;
        }

        GameObject go = new GameObject("RunnerPrefab");
        go.AddComponent<NetworkRunner>();
        go.AddComponent<NetworkSceneManagerDefault>();
        PlayerSpawner spawner = go.AddComponent<PlayerSpawner>();
        spawner.EditorSetPlayerPrefab((NetworkPrefabRef)NetworkObjectGuid.Parse(playerGuid));

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, RunnerPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------------------------------------------ ESCENA DE JUEGO

    private static bool SetupGameScene()
    {
        var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);

        XROrigin origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origin == null)
        {
            EditorUtility.DisplayDialog("Arco AR", "No encontre un XR Origin en pofa.", "OK");
            return false;
        }

        GetOrAdd<HardwareRig>(origin.gameObject);
        GetOrAdd<ARAnchorManager>(origin.gameObject);
        GetOrAdd<ColocationManager>(origin.gameObject);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    // ------------------------------------------------------------ ESCENA DE MENU

    // Se copia pofa (para tener el mismo XR Origin, AR Session y EventSystem ya configurados)
    // y se borra todo lo que es del juego.
    private static bool CreateMenuScene(GameObject runnerPrefab)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) != null)
            AssetDatabase.DeleteAsset(MenuScenePath);

        if (!AssetDatabase.CopyAsset(GameScenePath, MenuScenePath))
        {
            EditorUtility.DisplayDialog("Arco AR", "No se pudo copiar pofa para crear el menu.", "OK");
            return false;
        }

        var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            bool keep =
                root.GetComponentInChildren<XROrigin>(true) != null ||
                root.GetComponentInChildren<ARSession>(true) != null ||
                root.GetComponentInChildren<EventSystem>(true) != null ||
                root.GetComponentInChildren<Light>(true) != null ||
                root.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>(true) != null;

            if (!keep)
            {
                Object.DestroyImmediate(root);
                continue;
            }

            // Quitar los scripts del juego (arco, dianas, timer, multijugador del juego...)
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Assembly.GetName().Name == "Assembly-CSharp")
                    Object.DestroyImmediate(mb);
            }
        }

        RemoveGameCanvases();

        XROrigin origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origin == null)
        {
            EditorUtility.DisplayDialog("Arco AR", "El menu quedo sin XR Origin.", "OK");
            return false;
        }

        // Passthrough: fondo transparente + ARCameraManager
        Camera cam = origin.Camera;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            GetOrAdd<ARCameraManager>(cam.gameObject);
        }

        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<XRUIInputModule>();
        }

        // NetworkManager
        GameObject nmGO = new GameObject("NetworkManager");
        NetworkManager nm = nmGO.AddComponent<NetworkManager>();
        SerializedObject nmSO = new SerializedObject(nm);
        nmSO.FindProperty("_runnerPrefab").objectReferenceValue = runnerPrefab;
        nmSO.ApplyModifiedPropertiesWithoutUndo();

        BuildMenuCanvas(origin, cam);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return true;
    }

    private static void BuildMenuCanvas(XROrigin origin, Camera cam)
    {
        Transform o = origin.transform;
        Vector3 forward = Vector3.ProjectOnPlane(o.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        GameObject canvasGO = new GameObject("MenuMultijugadorCanvas", typeof(RectTransform));
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = cam;
        canvasGO.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        canvasGO.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(900f, 1100f);
        crt.localScale = Vector3.one * 0.0009f;
        crt.position = o.position + forward * 1.2f + Vector3.up * 1.3f;
        crt.rotation = Quaternion.LookRotation(forward);

        Image bg = new GameObject("Fondo", typeof(RectTransform)).AddComponent<Image>();
        bg.transform.SetParent(crt, false);
        bg.color = new Color(0.04f, 0.06f, 0.11f, 0.92f);
        Stretch(bg.rectTransform);

        ConnectionManager cm = canvasGO.AddComponent<ConnectionManager>();
        var res = new TMP_DefaultControls.Resources();

        Text(crt, "Titulo", "Arco AR - Multijugador", 56, new Vector2(0f, 430f), new Vector2(860f, 80f), Color.white);
        Text(crt, "Instrucciones", "Crea una sala, o escribe el codigo de tu companero y presiona Unirse", 28,
            new Vector2(0f, 360f), new Vector2(840f, 70f), new Color(0.75f, 0.85f, 1f));

        // Campo del codigo
        GameObject inputGO = TMP_DefaultControls.CreateInputField(res);
        inputGO.name = "CodigoSala";
        inputGO.transform.SetParent(crt, false);
        RectTransform irt = (RectTransform)inputGO.transform;
        irt.sizeDelta = new Vector2(400f, 90f);
        irt.anchoredPosition = new Vector2(0f, 265f);
        TMP_InputField input = inputGO.GetComponent<TMP_InputField>();
        input.characterLimit = 4;
        input.contentType = TMP_InputField.ContentType.Alphanumeric;
        input.textComponent.fontSize = 56;
        input.textComponent.alignment = TextAlignmentOptions.Center;
        if (input.placeholder is TMP_Text ph)
        {
            ph.text = "CODIGO";
            ph.fontSize = 48;
            ph.alignment = TextAlignmentOptions.Center;
        }

        TMP_Text roomLabel = Text(crt, "CodigoGenerado", "", 32, new Vector2(0f, 190f), new Vector2(840f, 50f), new Color(0.6f, 1f, 0.6f));

        // Teclado: 8 columnas x 4 filas
        for (int i = 0; i < RoomChars.Length; i++)
        {
            int col = i % 8, row = i / 8;
            string ch = RoomChars[i].ToString();
            Button key = MakeButton(crt, res, ch, new Vector2(-350f + col * 100f, 100f - row * 90f), new Vector2(90f, 80f), 40);
            UnityEventTools.AddStringPersistentListener(key.onClick, new UnityAction<string>(cm.AddChar), ch);
        }

        Button del = MakeButton(crt, res, "Borrar", new Vector2(-200f, -275f), new Vector2(340f, 80f), 36);
        UnityEventTools.AddPersistentListener(del.onClick, new UnityAction(cm.DeleteChar));

        Button join = MakeButton(crt, res, "Unirse", new Vector2(200f, -275f), new Vector2(340f, 80f), 36);
        join.GetComponent<Image>().color = new Color(0.15f, 0.45f, 0.25f);
        UnityEventTools.AddPersistentListener(join.onClick, new UnityAction(cm.JoinRoom));

        Button create = MakeButton(crt, res, "Crear sala", new Vector2(0f, -375f), new Vector2(740f, 80f), 38);
        create.GetComponent<Image>().color = new Color(0.25f, 0.35f, 0.6f);
        UnityEventTools.AddPersistentListener(create.onClick, new UnityAction(cm.CreateRoom));

        TMP_Text errorLabel = Text(crt, "Error", "", 30, new Vector2(0f, -540f), new Vector2(840f, 50f), new Color(1f, 0.45f, 0.45f));

        AddSoloButton(crt, cm, res);
        AddLevelPanel(crt, cm, res);

        SerializedObject so = new SerializedObject(cm);
        so.FindProperty("inputField").objectReferenceValue = input;
        so.FindProperty("roomCodeLabel").objectReferenceValue = roomLabel;
        so.FindProperty("errorLabel").objectReferenceValue = errorLabel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private const string SoloButtonName = "Boton_Jugar solo";
    private const string MenuCanvasName = "MenuMultijugadorCanvas";

    // Borra los Canvas del juego (HUD, pausa, fin de juego) que se copiaron de pofa.
    // Solo borra los que agregaste tu en la escena; los que vienen dentro de un prefab
    // (por ejemplo del XR Origin) no se pueden ni se deben borrar.
    private static int RemoveGameCanvases()
    {
        int removed = 0;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null) continue;
            GameObject go = c.gameObject;
            if (go.name == MenuCanvasName) continue;

            // Solo el Canvas de mas arriba (los hijos se van con el)
            if (go.transform.parent != null && go.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;

            bool removable = !PrefabUtility.IsPartOfPrefabInstance(go) || PrefabUtility.IsAddedGameObjectOverride(go);
            if (!removable) continue;

            Debug.Log("[Arco AR] Se borra del menu el Canvas del juego: " + go.name);
            Object.DestroyImmediate(go);
            removed++;
        }
        return removed;
    }

    private const string LevelPanelName = "PanelNiveles";

    private static void AddSoloButton(RectTransform crt, ConnectionManager cm, TMP_DefaultControls.Resources res)
    {
        Button solo = MakeButton(crt, res, "Jugar solo", new Vector2(0f, -470f), new Vector2(740f, 80f), 38);
        solo.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.15f);
        UnityEventTools.AddPersistentListener(solo.onClick, new UnityAction(cm.MostrarNiveles));
    }

    // "Jugar solo" abre el panel de niveles (antes entraba directo)
    private static void RewireSoloButton(Button solo, ConnectionManager cm)
    {
        for (int i = solo.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(solo.onClick, i);
        UnityEventTools.AddPersistentListener(solo.onClick, new UnityAction(cm.MostrarNiveles));
    }

    // Panel que tapa todo el menu: Facil / Intermedio / Dificil / Volver
    private static void AddLevelPanel(RectTransform crt, ConnectionManager cm, TMP_DefaultControls.Resources res)
    {
        GameObject panel = new GameObject(LevelPanelName, typeof(RectTransform));
        panel.transform.SetParent(crt, false);
        RectTransform prt = (RectTransform)panel.transform;
        Stretch(prt);

        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.06f, 0.11f, 1f); // opaco: bloquea los botones de atras

        Text(prt, "Titulo", "Elige la dificultad", 60, new Vector2(0f, 420f), new Vector2(860f, 90f), Color.white);
        Text(prt, "Info", "Hay que derribar 10 dianas en los tres niveles", 30,
            new Vector2(0f, 345f), new Vector2(840f, 60f), new Color(0.75f, 0.85f, 1f));

        string[] nombres = { "Fácil", "Intermedio", "Difícil" };
        string[] detalles = { "Las dianas no se mueven", "Las dianas se mueven lento", "Las dianas se mueven más rápido" };
        Color[] colores =
        {
            new Color(0.15f, 0.45f, 0.25f),
            new Color(0.55f, 0.45f, 0.12f),
            new Color(0.6f, 0.2f, 0.2f)
        };

        for (int i = 0; i < 3; i++)
        {
            Button b = MakeButton(prt, res, nombres[i], new Vector2(0f, 190f - i * 170f), new Vector2(680f, 140f), 52);
            b.name = "Boton_Nivel_" + i;
            b.GetComponent<Image>().color = colores[i];
            b.GetComponentInChildren<TMP_Text>().text = nombres[i] + "\n<size=55%>" + detalles[i] + "</size>";
            UnityEventTools.AddIntPersistentListener(b.onClick, new UnityAction<int>(cm.ElegirNivel), i);
        }

        Button volver = MakeButton(prt, res, "Volver", new Vector2(0f, -400f), new Vector2(400f, 90f), 38);
        UnityEventTools.AddPersistentListener(volver.onClick, new UnityAction(cm.OcultarNiveles));

        panel.transform.SetAsLastSibling();
        panel.SetActive(false);

        SerializedObject so = new SerializedObject(cm);
        so.FindProperty("panelNiveles").objectReferenceValue = panel;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Para el menu que ya creaste antes: agrega "Jugar solo", el panel de niveles y quita
    // el HUD del juego, sin rehacer toda la escena
    [MenuItem("Tools/Arco AR/Actualizar menu (niveles + limpiar)")]
    public static void AddSoloButtonToExistingMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) == null)
        {
            EditorUtility.DisplayDialog("Arco AR", "No existe " + MenuScenePath + ". Corre primero 'Configurar multijugador'.", "OK");
            return;
        }

        var scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        ConnectionManager cm = Object.FindFirstObjectByType<ConnectionManager>(FindObjectsInactive.Include);
        if (cm == null)
        {
            EditorUtility.DisplayDialog("Arco AR", "No encontre el ConnectionManager en el menu.", "OK");
            return;
        }

        int removed = RemoveGameCanvases();

        RectTransform crt = cm.GetComponent<RectTransform>();
        var res = new TMP_DefaultControls.Resources();

        Transform soloT = crt.Find(SoloButtonName);
        if (soloT == null)
        {
            // Hacer el panel un poco mas alto y bajar el texto de error
            crt.sizeDelta = new Vector2(crt.sizeDelta.x, 1100f);
            Transform error = crt.Find("Error");
            if (error is RectTransform ert)
            {
                ert.anchoredPosition = new Vector2(0f, -540f);
                ert.sizeDelta = new Vector2(840f, 50f);
            }
            AddSoloButton(crt, cm, res);
        }
        else
        {
            RewireSoloButton(soloT.GetComponent<Button>(), cm);
        }

        Transform oldPanel = crt.Find(LevelPanelName);
        if (oldPanel != null) Object.DestroyImmediate(oldPanel.gameObject);
        AddLevelPanel(crt, cm, res);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorUtility.DisplayDialog("Arco AR",
            "Listo: 'Jugar solo' abre el panel de niveles (Facil / Intermedio / Dificil).\nCanvas del juego borrados: " + removed, "OK");
    }

    private static TMP_Text Text(Transform parent, string name, string text, float size, Vector2 pos, Vector2 sz, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.rectTransform.sizeDelta = sz;
        t.rectTransform.anchoredPosition = pos;
        return t;
    }

    private static Button MakeButton(Transform parent, TMP_DefaultControls.Resources res, string label, Vector2 pos, Vector2 size, float font)
    {
        GameObject go = TMP_DefaultControls.CreateButton(res);
        go.name = "Boton_" + label;
        go.transform.SetParent(parent, false);

        RectTransform rt = (RectTransform)go.transform;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        go.GetComponent<Image>().color = new Color(0.14f, 0.20f, 0.32f);

        TMP_Text t = go.GetComponentInChildren<TMP_Text>();
        t.text = label;
        t.fontSize = font;
        t.color = Color.white;
        return go.GetComponent<Button>();
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------ BUILD

    private static void SetupBuildSettings()
    {
        var others = EditorBuildSettings.scenes
            .Where(s => s.path != MenuScenePath && s.path != GameScenePath)
            .Select(s => new EditorBuildSettingsScene(s.path, false));

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MenuScenePath, true),
            new EditorBuildSettingsScene(GameScenePath, true)
        }.Concat(others).ToArray();
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
