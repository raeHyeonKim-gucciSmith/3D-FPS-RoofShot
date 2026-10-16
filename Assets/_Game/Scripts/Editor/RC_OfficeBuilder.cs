using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RooftopContract.EditorTools
{
    /// <summary>
    /// Builds the briefing office — the room the campaign starts, returns to, and ends in.
    ///
    /// It is generated for the same reason the rooftops are: the scene is disposable and this
    /// file is the original. Everything it prints on the desk is copied out of the same
    /// MissionSet that generates the mission scenes, so the briefing cannot drift out of sync
    /// with the job. If a contract's guard count changes, the paperwork changes with it.
    ///
    /// The room itself is primitives. That is not laziness — the building pack has no interiors,
    /// and a box room with one window, one desk lamp and a pinboard is a stronger read at this
    /// art level than a detailed office would be. What the player is meant to look at is the
    /// photograph.
    /// </summary>
    public static partial class RC_SceneBuilder
    {
        /// <summary>One per contract — the board shows every card's photo at the same time.</summary>
        public static string DossierRtPath(int i) => $"Assets/_Game/UI/DossierRT_{i + 1}.renderTexture";
        public const string HeliRtPath = "Assets/_Game/UI/HeliRT.renderTexture";

        // The photo booths live well below the room. Lit sets in one scene need to not see each
        // other's lights or geometry, and moving them 600m down is cheaper and more reliable
        // than layer-masking every light in the room. The booths are spaced apart for the same
        // reason — far enough that no set's point lights reach the next one.
        static readonly Vector3 BoothOrigin = new Vector3(0f, -600f, 0f);
        const float BoothSpacing = 12f;
        static readonly Vector3 HeliBoothOrigin = new Vector3(-60f, -600f, 0f);

        [MenuItem("Rooftop Contract/도구/씬 — 사무실만 생성", priority = 3)]
        public static void BuildOfficeMenu()
        {
            casting = LoadOrCreateCasting();
            var set = LoadOrCreateMissionSet();
            EnsureFolders();
            BuildOffice(set);
            EditorSceneManager.OpenScene(MissionSet.OfficeScenePath);
            Debug.Log("[Rooftop Contract] 사무실 씬 생성 완료 → " + MissionSet.OfficeScenePath);
        }

        /// <summary>
        /// Wipes the board back to three live contracts.
        ///
        /// The campaign lives in PlayerPrefs, which survives entering and leaving play mode —
        /// so once a target is down there is no way back to a fresh board from inside the game
        /// except by clearing all three and pressing R. That is fine for a player and useless
        /// for testing, which is what this is for.
        /// </summary>
        [MenuItem("Rooftop Contract/계약 보드 초기화 (테스트용)", priority = -78)]
        public static void ResetCampaignMenu()
        {
            var set = LoadOrCreateMissionSet();
            if (!EditorUtility.DisplayDialog("Reset campaign",
                "계약 보드를 초기화합니다.\n\n제거된 표적 기록이 전부 지워지고 3건 모두 다시 살아납니다.\n\nContinue?",
                "Reset", "Cancel")) return;

            CampaignState.ResetCampaign(set.Count);
            Debug.Log("[Rooftop Contract] 계약 보드를 초기화했습니다. 표적 3명 전부 생존 상태입니다.");
        }

        public static void BuildOffice(MissionSet set)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("OFFICE").transform;
            BuildOfficeLighting();
            BuildRoom(root);
            BuildDeskSet(root, out Transform boardPhotoAnchor);
            BuildCityOutside(root);

            // Booths first: they create the subjects the board holds references to.
            // The single shared portrait texture the old one-briefing-at-a-time office used is
            // orphaned now that every card has its own.
            AssetDatabase.DeleteAsset("Assets/_Game/UI/DossierRT.renderTexture");
            var portraits = new RenderTexture[set.Count];
            for (int i = 0; i < set.Count; i++)
                portraits[i] = LoadOrCreateRenderTexture(DossierRtPath(i), $"DossierRT_{i + 1}", 512, 664);
            BuildPhotoBooths(root, set, portraits);
            var briefing = BuildBriefingRig(root, set, portraits);

            // The pinboard shows the exfil bird, not the target. The target's face is on the
            // contract in front of the player — putting it on the wall as well says nothing the
            // briefing has not already said, and a photo of the man you are about to kill is not
            // what someone pins over their own desk. The helicopter is: it is the part of the
            // job you come back from.
            var heliRt = LoadOrCreateRenderTexture(HeliRtPath, "HeliRT", 664, 512);
            BuildHeliBooth(root, heliRt);
            BuildBoardPhoto(boardPhotoAnchor, heliRt);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MissionSet.OfficeScenePath);
            if (briefing == null)
                Debug.LogWarning("[Rooftop Contract] 브리핑 컨트롤러 생성 실패 — 사무실이 비어 있습니다.");
        }

        // ------------------------------------------------------------------ room

        const float RoomHalfX = 4.5f, RoomHalfZ = 3.5f, RoomHeight = 3.2f;
        const float WindowMinY = 1.05f, WindowMaxY = 2.45f, WindowMinZ = -1.2f, WindowMaxZ = 1.8f;

        static void BuildOfficeLighting()
        {
            // Late evening, one hour before the contract. Low warm key through the window,
            // cool fill everywhere else — the same magic-hour palette the rooftops use, seen
            // from inside instead of on top.
            var sunGo = new GameObject("WindowLight");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sunGo.transform.rotation = Quaternion.Euler(12f, 68f, 0f);
            sun.color = new Color(1f, 0.78f, 0.55f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.26f, 0.29f, 0.36f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.19f, 0.20f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.09f, 0.10f);
            RenderSettings.fog = false;
        }

        static void BuildRoom(Transform root)
        {
            var t = RC_BuildUtil.Empty(root, "Room").transform;
            var wall = RC_Palette.Wall;

            RC_BuildUtil.Box(t, "Floor", new Vector3(0f, -0.06f, 0f),
                new Vector3(RoomHalfX * 2f, 0.12f, RoomHalfZ * 2f), RC_Palette.Wood);
            RC_BuildUtil.Box(t, "Ceiling", new Vector3(0f, RoomHeight, 0f),
                new Vector3(RoomHalfX * 2f, 0.12f, RoomHalfZ * 2f), RC_Palette.ConcreteDark);

            // Three solid walls.
            RC_BuildUtil.Box(t, "Wall_Front", new Vector3(0f, RoomHeight * 0.5f, RoomHalfZ),
                new Vector3(RoomHalfX * 2f, RoomHeight, 0.2f), wall);
            RC_BuildUtil.Box(t, "Wall_Back", new Vector3(0f, RoomHeight * 0.5f, -RoomHalfZ),
                new Vector3(RoomHalfX * 2f, RoomHeight, 0.2f), wall);
            RC_BuildUtil.Box(t, "Wall_Right", new Vector3(RoomHalfX, RoomHeight * 0.5f, 0f),
                new Vector3(0.2f, RoomHeight, RoomHalfZ * 2f), wall);

            // Left wall, built as four pieces around a window rather than as one wall with a
            // decal. The opening has to be a real hole: the city outside is what tells the
            // player this room is 60 storeys up in the same city they are about to shoot into.
            float x = -RoomHalfX;
            RC_BuildUtil.Box(t, "Wall_Left_Below", new Vector3(x, WindowMinY * 0.5f, 0f),
                new Vector3(0.2f, WindowMinY, RoomHalfZ * 2f), wall);
            RC_BuildUtil.Box(t, "Wall_Left_Above", new Vector3(x, (RoomHeight + WindowMaxY) * 0.5f, 0f),
                new Vector3(0.2f, RoomHeight - WindowMaxY, RoomHalfZ * 2f), wall);
            float backPierZ = (WindowMinZ - RoomHalfZ) * 0.5f;
            RC_BuildUtil.Box(t, "Wall_Left_PierBack", new Vector3(x, (WindowMinY + WindowMaxY) * 0.5f, backPierZ),
                new Vector3(0.2f, WindowMaxY - WindowMinY, RoomHalfZ + WindowMinZ), wall);
            float frontPierZ = (WindowMaxZ + RoomHalfZ) * 0.5f;
            RC_BuildUtil.Box(t, "Wall_Left_PierFront", new Vector3(x, (WindowMinY + WindowMaxY) * 0.5f, frontPierZ),
                new Vector3(0.2f, WindowMaxY - WindowMinY, RoomHalfZ - WindowMaxZ), wall);

            // Frame and mullion, so the opening reads as a window and not as a missing wall.
            var frameMat = RC_Palette.Metal;
            RC_BuildUtil.Box(t, "Window_Mullion", new Vector3(x, (WindowMinY + WindowMaxY) * 0.5f,
                (WindowMinZ + WindowMaxZ) * 0.5f), new Vector3(0.12f, WindowMaxY - WindowMinY, 0.09f), frameMat, 0f, false);
            RC_BuildUtil.Box(t, "Window_Sill", new Vector3(x + 0.12f, WindowMinY - 0.04f,
                (WindowMinZ + WindowMaxZ) * 0.5f), new Vector3(0.4f, 0.08f, WindowMaxZ - WindowMinZ), frameMat, 0f, false);

            RC_BuildUtil.MarkStaticRecursive(t.gameObject);
        }

        // ------------------------------------------------------------------ desk

        static void BuildDeskSet(Transform root, out Transform boardPhotoAnchor)
        {
            var t = RC_BuildUtil.Empty(root, "Desk").transform;

            // Desk sits against the front wall; the player's chair is behind it looking that way.
            RC_BuildUtil.Box(t, "Desk_Top", new Vector3(0f, 0.74f, 1.25f),
                new Vector3(2.6f, 0.07f, 1.15f), RC_Palette.Wood);
            RC_BuildUtil.Box(t, "Desk_Modesty", new Vector3(0f, 0.38f, 1.72f),
                new Vector3(2.5f, 0.7f, 0.06f), RC_Palette.Wood, 0f, false);
            foreach (float lx in new[] { -1.2f, 1.2f })
                RC_BuildUtil.Box(t, "Desk_Leg", new Vector3(lx, 0.36f, 1.25f),
                    new Vector3(0.09f, 0.72f, 1.0f), RC_Palette.ConcreteDark, 0f, false);

            // Paperwork. Flat white boxes read as a scatter of documents at this scale and cost
            // nothing; the desk being bare is what would look wrong.
            RC_BuildUtil.Box(t, "Paper_A", new Vector3(-0.55f, 0.782f, 1.15f),
                new Vector3(0.44f, 0.008f, 0.6f), RC_Palette.Plant, 6f, false);
            RC_BuildUtil.Box(t, "Paper_B", new Vector3(-0.42f, 0.79f, 1.32f),
                new Vector3(0.42f, 0.006f, 0.58f), RC_Palette.Plant, -11f, false);
            RC_BuildUtil.Box(t, "Folder", new Vector3(0.62f, 0.784f, 1.2f),
                new Vector3(0.52f, 0.02f, 0.68f), RC_Palette.RustMetal, 4f, false);

            // Desk lamp — the only warm source in the room, and the reason the paperwork is
            // the brightest thing in frame.
            RC_BuildUtil.Cylinder(t, "Lamp_Base", new Vector3(0.95f, 0.79f, 1.62f), 0.1f, 0.04f, RC_Palette.Metal, false);
            RC_BuildUtil.Cylinder(t, "Lamp_Stem", new Vector3(0.95f, 1.02f, 1.62f), 0.02f, 0.44f, RC_Palette.Metal, false);
            RC_BuildUtil.Box(t, "Lamp_Head", new Vector3(0.78f, 1.22f, 1.55f),
                new Vector3(0.22f, 0.12f, 0.22f), RC_Palette.Metal, -20f, false);

            var lampGo = RC_BuildUtil.Empty(t, "Lamp_Light", new Vector3(0.72f, 1.12f, 1.5f));
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.85f, 0.62f);
            lamp.range = 4.2f;
            lamp.intensity = 2.6f;
            lamp.shadows = LightShadows.None;

            // Chair, seen only as the arms at the bottom of frame.
            RC_BuildUtil.Box(t, "Chair_Seat", new Vector3(0f, 0.46f, -0.15f),
                new Vector3(0.62f, 0.09f, 0.6f), RC_Palette.ConcreteDark, 0f, false);
            RC_BuildUtil.Box(t, "Chair_Back", new Vector3(0f, 0.86f, -0.44f),
                new Vector3(0.62f, 0.72f, 0.08f), RC_Palette.ConcreteDark, 0f, false);

            // Filing cabinets against the right wall.
            for (int i = 0; i < 2; i++)
                RC_BuildUtil.Box(t, $"Cabinet_{i}", new Vector3(3.9f, 0.68f, -1.4f + i * 1.15f),
                    new Vector3(0.9f, 1.36f, 1.05f), RC_Palette.Metal);

            // ---- pinboard ------------------------------------------------------------
            var board = RC_BuildUtil.Empty(t, "Pinboard", new Vector3(-1.5f, 1.85f, 3.34f)).transform;
            RC_BuildUtil.Box(board, "Board_Cork", new Vector3(-1.5f, 1.85f, 3.36f),
                new Vector3(3.2f, 1.9f, 0.05f), RC_Palette.Cork, 0f, false);
            RC_BuildUtil.Box(board, "Board_Frame", new Vector3(-1.5f, 1.85f, 3.38f),
                new Vector3(3.34f, 2.04f, 0.03f), RC_Palette.ConcreteDark, 0f, false);

            // Pinned clippings around where the photograph goes.
            RC_BuildUtil.Box(board, "Pin_Note_A", new Vector3(-0.35f, 2.28f, 3.33f),
                new Vector3(0.5f, 0.36f, 0.01f), RC_Palette.Plant, 5f, false);
            RC_BuildUtil.Box(board, "Pin_Note_B", new Vector3(-0.28f, 1.62f, 3.33f),
                new Vector3(0.62f, 0.44f, 0.01f), RC_Palette.Plant, -7f, false);
            RC_BuildUtil.Box(board, "Pin_Note_C", new Vector3(-2.55f, 1.5f, 3.33f),
                new Vector3(0.44f, 0.3f, 0.01f), RC_Palette.Plant, 11f, false);

            boardPhotoAnchor = board;

            RC_BuildUtil.MarkStaticRecursive(t.gameObject);
        }

        /// <summary>
        /// The photograph pinned over the desk: the extraction helicopter, landscape.
        ///
        /// It used to be the dossier render — the same texture the briefing panel samples — and
        /// having the target's face in two places at once was one place too many. The panel is
        /// the contract; the wall is the room the player lives in between contracts.
        /// </summary>
        static void BuildBoardPhoto(Transform board, RenderTexture rt)
        {
            if (board == null || rt == null) return;

            // Landscape now, and the aspect is matched to the texture (664x512) so the bird is
            // not stretched. Change one, change the other.
            const float photoW = 1.02f, photoH = 0.786f;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Board_Photo";
            quad.transform.SetParent(board, false);
            quad.transform.position = new Vector3(-2.05f, 2.06f, 3.31f);
            // Identity, not a 180 flip. Unity's Quad has its normal on -Z, and the player sits
            // on the -Z side of this wall — turning it to "face" the room would point it into
            // the plaster and mirror the photograph on the way.
            quad.transform.rotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(photoW, photoH, 1f);
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            // Unlit so the render texture is not re-lit by the room. A photo that responds to
            // the desk lamp reads as a screen, not as paper.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var mat = new Material(shader);
            mat.mainTexture = rt;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", rt);
            AssetDatabase.DeleteAsset("Assets/_Game/UI/DossierPhoto.mat");   // the portrait it replaced
            AssetDatabase.CreateAsset(mat, "Assets/_Game/UI/BoardPhoto.mat");
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // A paper border behind it, so the photo has an edge against the cork.
            RC_BuildUtil.Box(board, "Board_Photo_Mat", new Vector3(-2.05f, 2.06f, 3.335f),
                new Vector3(photoW + 0.1f, photoH + 0.1f, 0.01f), RC_Palette.Plant, 0f, false);
        }

        // ------------------------------------------------------------------ outside

        /// <summary>
        /// What is through the window. Not the real skyline generator — that builds a 600m
        /// square of city and bakes NavMesh into it. Here it is nine boxes at three depths,
        /// which is all that survives being seen through a 3m opening anyway.
        /// </summary>
        static void BuildCityOutside(Transform root)
        {
            var t = RC_BuildUtil.Empty(root, "Outside").transform;

            // Backdrop: dusk sky. Placed far enough back that the parallax from the camera lean
            // never reveals its edges.
            RC_BuildUtil.Box(t, "Sky", new Vector3(-90f, 30f, 0f),
                new Vector3(1f, 160f, 220f), RC_Palette.Get("RC_OfficeSky", new Color(0.42f, 0.44f, 0.55f), 0.1f), 0f, false);

            var far = RC_Palette.Get("RC_CityFar", new Color(0.26f, 0.28f, 0.36f), 0.1f);
            var mid = RC_Palette.Get("RC_CityMid", new Color(0.20f, 0.21f, 0.26f), 0.1f);
            var near = RC_Palette.Get("RC_CityNear", new Color(0.14f, 0.15f, 0.18f), 0.1f);

            Random.InitState(9021);
            for (int i = 0; i < 9; i++)
            {
                float depth = -14f - i * 7.5f;
                float height = Random.Range(22f, 64f);
                float width = Random.Range(7f, 15f);
                float z = Random.Range(-16f, 16f);
                var mat = i < 3 ? near : (i < 6 ? mid : far);
                RC_BuildUtil.Box(t, $"Bld_{i}", new Vector3(depth, height * 0.5f - 22f, z),
                    new Vector3(width, height, width * 0.9f), mat, Random.Range(-8f, 8f), false);
            }

            RC_BuildUtil.MarkStaticRecursive(t.gameObject);
        }

        // ------------------------------------------------------------------ dossier booth

        /// <summary>
        /// Both booths render into one of these. Sized to what actually gets drawn — the
        /// briefing panel puts the dossier on screen at 168x216 and the pinboard quad is
        /// smaller than that in most frames, so there is nothing to gain above 512-ish.
        /// </summary>
        static RenderTexture LoadOrCreateRenderTexture(string path, string name, int w, int h)
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (existing != null) return existing;

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = name,
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            AssetDatabase.CreateAsset(rt, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        }

        /// <summary>
        /// The second booth: the exfil helicopter, banked, photographed against dusk.
        ///
        /// Built from the same parts as the dossier booth for the same reason — the machinery
        /// already works, and a live render means the picture on the wall is of the actual
        /// airframe the player rides out on rather than an image someone drew once.
        ///
        /// Its rotors do NOT spin. This is a photograph pinned to cork, and a photograph whose
        /// contents move reads as a monitor.
        /// </summary>
        static void BuildHeliBooth(Transform root, RenderTexture rt)
        {
            var t = RC_BuildUtil.Empty(root, "HeliBooth", HeliBoothOrigin).transform;

            // Dusk backdrop, lit from behind the subject — the same magic hour the contracts
            // happen in, so the wall photo belongs to the same evening as the job.
            RC_BuildUtil.Box(t, "Heli_Backdrop", HeliBoothOrigin + new Vector3(0f, 4f, -9f),
                new Vector3(30f, 20f, 0.4f),
                RC_Palette.Get("RC_HeliBackdrop", new Color(0.46f, 0.40f, 0.44f), 0.05f), 0f, false);
            RC_BuildUtil.Box(t, "Heli_Horizon", HeliBoothOrigin + new Vector3(0f, -2.2f, -8.6f),
                new Vector3(30f, 6f, 0.4f),
                RC_Palette.Get("RC_HeliHorizon", new Color(0.20f, 0.19f, 0.24f), 0.05f), 0f, false);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RC_MapBuilder.HeliPrefab);
            if (prefab == null)
            {
                Debug.LogWarning($"[Rooftop Contract] 헬기 프리팹 없음 — 사무실 벽 사진이 빕니다: {RC_MapBuilder.HeliPrefab}");
                return;
            }

            // Measure, then scale to a known span. The framing below is expressed in metres
            // against that span, so it holds even if the pack's airframe is a different size
            // than expected.
            const float span = 4.5f;
            Bounds b = RC_BuildUtil.MeasurePrefab(prefab);
            float longest = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            float scale = span / longest;

            var heli = (GameObject)PrefabUtility.InstantiatePrefab(prefab, t);
            heli.name = "Heli_Subject";
            heli.transform.localScale = Vector3.one * scale;
            // Three-quarter view, nose slightly down, banked into a turn: the pose of something
            // leaving in a hurry rather than something parked.
            heli.transform.rotation = Quaternion.Euler(-6f, 152f, 15f);
            heli.transform.position = HeliBoothOrigin + new Vector3(0f, 1.1f, 0f) - heli.transform.rotation * (b.center * scale);
            foreach (var c in heli.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            var keyGo = RC_BuildUtil.Empty(t, "Heli_Key", HeliBoothOrigin + new Vector3(-3.4f, 4.2f, 3.4f));
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Point;
            key.color = new Color(1f, 0.86f, 0.66f);
            key.range = 18f;
            key.intensity = 5.5f;
            key.shadows = LightShadows.None;

            var rimGo = RC_BuildUtil.Empty(t, "Heli_Rim", HeliBoothOrigin + new Vector3(3.0f, 2.6f, -3.4f));
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(0.62f, 0.72f, 0.95f);
            rim.range = 16f;
            rim.intensity = 3.2f;
            rim.shadows = LightShadows.None;

            // Landscape framing: vertical FOV 30 on a 664x512 texture gives ~38 degrees across,
            // so 6.6m back holds the whole 4.5m airframe with air around it.
            var camGo = RC_BuildUtil.Empty(t, "HeliPhotoCamera",
                HeliBoothOrigin + new Vector3(-1.1f, 2.5f, 6.5f));
            camGo.transform.rotation = Quaternion.LookRotation(
                (HeliBoothOrigin + new Vector3(0f, 1.1f, 0f)) - camGo.transform.position, Vector3.up);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.38f, 0.42f, 1f);
            cam.targetTexture = rt;
            cam.depth = -11;
        }

        /// <summary>
        /// A row of lit alcoves — one per contract — each with its own target standing in it and
        /// its own camera rendering into its own texture.
        ///
        /// One booth would have been enough while the office showed a single briefing. The board
        /// shows all three cards at once, so all three photographs have to be live at once:
        /// three cameras, three RenderTextures, three subjects that never switch off. At a menu's
        /// frame budget that is free, and the alternative — one booth with the subject swapped on
        /// selection — would leave the other two cards holding stale or blank images.
        ///
        /// This is why a briefing photograph is never wrong: it is not an image someone exported,
        /// it is the character. Recasting a target in CastingConfig changes the face on the board
        /// on the next build, with no second step to forget.
        /// </summary>
        static void BuildPhotoBooths(Transform root, MissionSet set, RenderTexture[] textures)
        {
            var group = RC_BuildUtil.Empty(root, "PhotoBooths").transform;
            dossierSubjects = new GameObject[set.Count];

            for (int i = 0; i < set.Count; i++)
            {
                // Far enough apart that neither booth's point lights nor its backdrop reach the
                // next one. The lights below have a 7m range; the spacing is 12.
                Vector3 o = BoothOrigin + new Vector3(i * BoothSpacing, 0f, 0f);
                var t = RC_BuildUtil.Empty(group, $"Booth_{i + 1}", o).transform;

                // Grey backdrop and a floor mark, like every ID photo ever taken.
                RC_BuildUtil.Box(t, "Booth_Backdrop", o + new Vector3(0f, 1.6f, -1.4f),
                    new Vector3(4f, 3.4f, 0.2f), RC_Palette.Get("RC_BoothWall", new Color(0.30f, 0.32f, 0.35f), 0.05f), 0f, false);
                RC_BuildUtil.Box(t, "Booth_Floor", o + new Vector3(0f, -0.05f, 0f),
                    new Vector3(4f, 0.1f, 3f), RC_Palette.Get("RC_BoothFloor", new Color(0.20f, 0.21f, 0.23f), 0.05f), 0f, false);

                var keyGo = RC_BuildUtil.Empty(t, "Booth_Key", o + new Vector3(1.1f, 2.3f, 1.6f));
                var key = keyGo.AddComponent<Light>();
                key.type = LightType.Point;
                key.color = new Color(1f, 0.95f, 0.88f);
                key.range = 7f;
                key.intensity = 4.5f;
                key.shadows = LightShadows.None;

                var fillGo = RC_BuildUtil.Empty(t, "Booth_Fill", o + new Vector3(-1.6f, 1.6f, 1.2f));
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Point;
                fill.color = new Color(0.7f, 0.78f, 0.95f);
                fill.range = 6f;
                fill.intensity = 1.6f;
                fill.shadows = LightShadows.None;

                // Camera: head-and-shoulders, slightly long lens so the face is not distorted.
                float eye = Mathf.Max(1.2f, casting.targetHeight * 0.86f);
                var camGo = RC_BuildUtil.Empty(t, $"DossierCamera_{i + 1}", o + new Vector3(0f, eye, 2.35f));
                camGo.transform.rotation = Quaternion.Euler(2f, 180f, 0f);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 26f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 30f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.14f, 0.15f, 0.17f, 1f);
                cam.targetTexture = textures[i];
                // Below the room camera so every texture is filled before the room is drawn.
                cam.depth = -10 - i;

                var prefab = VipPrefabFor(set.Get(i), i);
                if (prefab == null) continue;

                // Vip role, not Civilian: the clip resolver reads role tags out of filenames, so
                // asking for a civilian here would refuse the VIP-tagged idle these characters
                // actually ship with and the portrait would fall back to somebody else's pose.
                //
                // No NavMeshAgent: there is no NavMesh 600 metres under the office, and an agent
                // that cannot find one warps its transform to the origin on the first frame.
                var subject = SpawnActor(prefab, $"Dossier_{i + 1}", o, t,
                    100f, ActorRole.Vip, addAgent: false);
                subject.transform.rotation = Quaternion.Euler(0f, 8f, 0f);

                // Everything that makes this a target rather than a photograph comes off: it is
                // never shot at, never dies, and the hitbox rig it would build on Awake is pure
                // overhead in a menu.
                foreach (var c in subject.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                StripComponent<ActorDeath>(subject);
                StripComponent<ActorSetup>(subject);
                StripComponent<Damageable>(subject);

                dossierSubjects[i] = subject;
            }
        }

        static void StripComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) Object.DestroyImmediate(c);
        }

        static GameObject[] dossierSubjects;

        // ------------------------------------------------------------------ rig

        static BriefingController BuildBriefingRig(Transform root, MissionSet set, RenderTexture[] portraits)
        {
            // Seated at the desk, looking at the pinboard. Eye height for someone in a chair.
            var pivot = RC_BuildUtil.Empty(root, "LookPivot", new Vector3(0f, 1.16f, -0.35f)).transform;
            var camGo = RC_BuildUtil.Empty(pivot, "OfficeCamera");
            camGo.tag = "MainCamera";
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 58f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            camGo.AddComponent<AudioListener>();

            var go = new GameObject("Briefing");
            go.transform.SetParent(root, false);
            var briefing = go.AddComponent<BriefingController>();
            briefing.lookPivot = pivot;
            briefing.officeMusic = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sound/officeMusic.wav");
            briefing.flipMissionSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sound/FlipMissionbutton.wav");
            briefing.selectMissionSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sound/selectMission.wav");
            briefing.lobbyLogo = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/logo/RoofShot.png");
            briefing.officePistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Weapons/Pistol_J.prefab");
            briefing.officeBulletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Bullets/Bullet_Pistol_A.prefab");

            briefing.contracts = new ContractBrief[set.Count];
            for (int i = 0; i < set.Count; i++)
            {
                var m = set.Get(i);
                briefing.contracts[i] = new ContractBrief
                {
                    codename = m.codename,
                    difficulty = m.difficulty,
                    photo = i < portraits.Length ? portraits[i] : null,
                    targetName = m.targetName,
                    targetNote = m.targetNote,
                    brief = m.brief,
                    strikeByClock = m.strikeByClock,
                    pickupClock = m.pickupClock,
                    guards = m.protectorCount,
                    ammo = m.sniperAmmo,
                    range = m.engagementRange,
                    elevation = m.elevationAdvantage,
                    survivalDuration = m.survivalDuration,
                    dossierSubject = dossierSubjects != null && i < dossierSubjects.Length ? dossierSubjects[i] : null,
                    sceneName = MissionSet.SceneNameFor(i),
                };
            }

            return briefing;
        }

    }
}
