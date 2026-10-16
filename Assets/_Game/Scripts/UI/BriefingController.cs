using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RooftopContract
{
    /// <summary>
    /// One contract as the office knows it. This is a flattened copy of the fields the briefing
    /// needs rather than a reference to the MissionSet asset, for the same reason every other
    /// number in this project is baked at generation time: the office scene has to be openable
    /// and playable on its own, without the generator having run in that session.
    /// </summary>
    [Serializable]
    public class ContractBrief
    {
        public string codename = "CONTRACT";
        [Range(1, 3)] public int difficulty = 1;
        public string targetName = "UNKNOWN";
        public string targetNote = "";
        [TextArea(2, 3)] public string brief = "";
        public string strikeByClock = "19:40";
        public string pickupClock = "19:47";
        public int guards = 3;
        public int ammo = 5;
        public float range = 250f;
        public float elevation = 30f;
        public float survivalDuration = 60f;
        [Tooltip("이 계약의 표적을 비추는 전용 렌더 텍스처. 계약마다 하나씩이다.")]
        public RenderTexture photo;
        [Tooltip("사진 부스에 서 있는 이 계약의 표적. 펼쳐진 서류의 인물만 회전한다.")]
        public GameObject dossierSubject;
        public string sceneName = "Mission1";
    }

    /// <summary>
    /// The office: a stack of contracts on a desk, one of them open in front of you.
    ///
    /// The contracts are not a queue. All three are on the board from the start and the player
    /// takes whichever they want, in whatever order, as many times as they want. What changes is
    /// the paperwork — a target who is down gets greyed out and stamped, and a target who
    /// walked away is simply still there, still selectable, no retry prompt required. That is
    /// the whole failure-handling design: there is no state a bad run can strand you in.
    ///
    /// Everything printed on the open file is a real number pulled from the contract that is
    /// about to be generated — the guard count is the guard count, the pickup time is the survival
    /// clock. Nothing on this desk is decoration that lies.
    ///
    /// The dossier photographs are live renders, not image files. A camera per contract looks at
    /// the actual VIP prefab standing in a lit booth below the floor. That means a photograph is
    /// always of the character the player is about to meet, with no export step to forget.
    /// </summary>
    public class BriefingController : MonoBehaviour
    {
        [Header("Audio")]
        public AudioClip officeMusic;
        public AudioClip flipMissionSound;
        public AudioClip selectMissionSound;
        public Texture2D lobbyLogo;
        public GameObject officePistolPrefab;
        public GameObject officeBulletPrefab;
        [Range(0f, 1f)] public float musicVolume = 0.32f;
        [Range(0f, 1f)] public float uiVolume = 0.75f;

        [Header("Contracts")]
        public ContractBrief[] contracts = new ContractBrief[0];

        [Header("Dossier photos")]
        [Tooltip("선택된 카드의 인물이 도는 속도 (도/초). 좌우로 흔들리는 경우엔 최고 속도.")]
        public float dossierSpinSpeed = 14f;
        [Tooltip("좌우로 도는 범위 (도). 0이면 한 방향으로 계속 회전한다.")]
        public float dossierSpinSweep = 42f;

        [Header("View")]
        [Tooltip("마우스로 살짝 둘러보는 카메라 피벗. 이동은 없다.")]
        public Transform lookPivot;
        public float lookYawRange = 9f;
        public float lookPitchRange = 5f;
        public float lookDamping = 6f;

        [Header("Style")]
        public Color accent = new Color(1f, 0.78f, 0.25f);
        public Color danger = new Color(0.95f, 0.28f, 0.24f);
        public Color ink = new Color(0.92f, 0.94f, 0.95f);

        enum Screen_ { Debrief, Lobby, Board, CampaignDone }

        Screen_ screen;
        int index;
        MissionOutcome lastOutcome;
        int lastMissionIndex;
        Texture2D px;
        GUIStyle label, mono, big, huge, center, control, body, subjectName, stamp, arrow;
        Quaternion lookHome;
        float openClock;
        Transform spinTarget;
        float spinBaseYaw;
        float spinClock;
        AudioSource musicSource;
        AudioSource uiSource;
        bool accepting;
        Transform missionFile;
        Transform resetPistol;
        float resetNoticeUntil;

        void Awake()
        {
            px = new Texture2D(1, 1);
            px.SetPixel(0, 0, Color.white);
            px.Apply();

            Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (lookPivot != null) lookHome = lookPivot.localRotation;

            SetupLobbyProps();

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = officeMusic;
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.volume = musicVolume;
            musicSource.spatialBlend = 0f;
            if (musicSource.clip != null) musicSource.Play();

            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.spatialBlend = 0f;
        }

        void Start()
        {
            index = Mathf.Clamp(CampaignState.Cursor, 0, Mathf.Max(0, contracts.Length - 1));
            lastOutcome = CampaignState.LastOutcome;
            lastMissionIndex = CampaignState.LastMissionIndex;

            // A debrief only makes sense if there was a mission to debrief. Pressing Play
            // directly on the office scene during development lands straight on the board.
            if (CampaignState.HasDebrief && lastOutcome != MissionOutcome.None)
            {
                screen = Screen_.Debrief;
                CampaignState.ConsumeDebrief();
            }
            else
            {
                screen = Screen_.Lobby;
            }

            RefreshSubjects();
        }

        /// <summary>
        /// All targets stand in their booths the whole time — a photo is live whether its file
        /// is the open one or not, so switching files never waits on anything. Only the man in
        /// the open file turns.
        /// </summary>
        void RefreshSubjects()
        {
            spinTarget = null;
            for (int i = 0; i < contracts.Length; i++)
            {
                var subject = contracts[i].dossierSubject;
                if (subject == null) continue;
                subject.SetActive(true);

                if (i != index) continue;

                // Whatever pose the generator left them in is the pose the turn is measured
                // from, so recasting or re-aiming a booth does not need this number changed.
                spinTarget = subject.transform;
                spinBaseYaw = spinTarget.localEulerAngles.y;
                spinClock = 0f;
            }
        }

        void Select(int next)
        {
            if (contracts.Length == 0) return;
            next = (next % contracts.Length + contracts.Length) % contracts.Length;
            if (next == index) return;

            // Put the one being left down flat again, so the board does not end up with three
            // subjects frozen at three random angles.
            if (spinTarget != null) spinTarget.localRotation = Quaternion.Euler(0f, spinBaseYaw, 0f);

            index = next;
            CampaignState.Cursor = index;
            RefreshSubjects();
            if (uiSource != null && flipMissionSound != null)
                uiSource.PlayOneShot(flipMissionSound, uiVolume);
        }

        void Update()
        {
            openClock += Time.deltaTime;
            TickLook();
            TickDossierSpin();

            var kb = Keyboard.current;
            if (kb == null) return;

            // A quarter second of input deadtime. Otherwise the SPACE that dismissed the results
            // screen is still held on the office's first frame and the player never sees it.
            if (openClock < 0.25f) return;

            if (screen == Screen_.Debrief)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                    screen = Screen_.Lobby;
                return;
            }

            if (screen == Screen_.Lobby) return;

            if (screen == Screen_.CampaignDone)
            {
                if (kb.rKey.wasPressedThisFrame)
                {
                    CampaignState.ResetCampaign(contracts.Length);
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
                if (kb.spaceKey.wasPressedThisFrame) screen = Screen_.Board;
                return;
            }

            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Select(index - 1);
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Select(index + 1);

            for (int i = 0; i < contracts.Length && i < 9; i++)
                if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) Select(i);

            if (kb.spaceKey.wasPressedThisFrame) Accept();
            if (kb.escapeKey.wasPressedThisFrame) screen = Screen_.Lobby;
        }

        void Accept()
        {
            if (accepting) return;
            var contract = Current;
            if (contract == null || string.IsNullOrEmpty(contract.sceneName)) return;
            if (CampaignState.IsEliminated(index)) return;
            CampaignState.Cursor = index;
            StartCoroutine(AcceptAndLoad(contract.sceneName));
        }

        IEnumerator AcceptAndLoad(string sceneName)
        {
            accepting = true;
            if (uiSource != null && selectMissionSound != null)
            {
                uiSource.PlayOneShot(selectMissionSound, uiVolume);
                yield return new WaitForSecondsRealtime(Mathf.Min(0.55f, selectMissionSound.length));
            }
            SceneManager.LoadScene(sceneName);
        }

        ContractBrief Current =>
            contracts != null && index >= 0 && index < contracts.Length ? contracts[index] : null;

        void SetupLobbyProps()
        {
            GameObject folder = GameObject.Find("Folder");
            if (folder != null)
            {
                folder.name = "MissionFile";
                folder.transform.position = new Vector3(-0.55f, 0.787f, 1.18f);
                folder.transform.rotation = Quaternion.Euler(0f, 6f, 0f);
                missionFile = folder.transform;

                var renderer = folder.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material = new Material(renderer.sharedMaterial);
                    renderer.material.color = new Color(0.38f, 0.055f, 0.045f);
                }
            }

#if UNITY_EDITOR
            // Lets an already-open Office scene pick up the new prop without forcing a rebuild.
            if (lobbyLogo == null)
                lobbyLogo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/logo/RoofShot.png");
            if (officePistolPrefab == null)
                officePistolPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Weapons/Pistol_J.prefab");
            if (officeBulletPrefab == null)
                officeBulletPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Low Poly Pistol Weapon Pack 2/Prefabs/Bullets/Bullet_Pistol_A.prefab");
#endif
            if (officePistolPrefab == null) return;
            var pistolGo = Instantiate(officePistolPrefab);
            pistolGo.name = "MissionReset_Pistol";
            pistolGo.transform.position = new Vector3(0.58f, 0.82f, 1.20f);
            pistolGo.transform.rotation = Quaternion.Euler(0f, -9f, 90f);

            Bounds pistolBounds = RendererBounds(pistolGo.transform);
            float longest = Mathf.Max(pistolBounds.size.x, pistolBounds.size.z);
            if (longest > 0.001f)
                pistolGo.transform.localScale *= 0.34f / longest;
            pistolBounds = RendererBounds(pistolGo.transform);
            pistolGo.transform.position += Vector3.up * (0.782f - pistolBounds.min.y);
            resetPistol = pistolGo.transform;

            foreach (var behaviour in pistolGo.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            Transform propRoot = missionFile != null ? missionFile.parent : transform;
            CreateDeskRound(propRoot, "Round_Standing_A", new Vector3(0.88f, 0.782f, 1.17f), false);
            CreateDeskRound(propRoot, "Round_Standing_B", new Vector3(0.93f, 0.782f, 1.20f), false);
            CreateDeskRound(propRoot, "Round_Lying", new Vector3(0.90f, 0.782f, 1.28f), true);
        }

        void CreateDeskRound(Transform parent, string objectName, Vector3 position, bool lying)
        {
            if (officeBulletPrefab == null) return;
            var round = Instantiate(officeBulletPrefab, parent);
            round.name = objectName;
            round.transform.position = position;
            round.transform.rotation = lying ? Quaternion.Euler(0f, 18f, 0f)
                                              : Quaternion.Euler(-90f, 0f, 0f);

            Bounds bounds = RendererBounds(round.transform);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest > 0.001f) round.transform.localScale *= 0.032f / longest;
            bounds = RendererBounds(round.transform);
            round.transform.position += Vector3.up * (position.y - bounds.min.y);
            foreach (var collider in round.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        }

        static Bounds RendererBounds(Transform root)
        {
            var renderers = root != null ? root.GetComponentsInChildren<Renderer>(true) : null;
            if (renderers == null || renderers.Length == 0)
                return new Bounds(root != null ? root.position : Vector3.zero, Vector3.one * 0.1f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Rect ScreenRect(Transform root, Camera cam, float padding)
        {
            if (root == null || cam == null) return new Rect(-1000f, -1000f, 1f, 1f);
            Bounds b = RendererBounds(root);
            Vector3 min = b.min, max = b.max;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                Vector3 world = new Vector3((i & 1) == 0 ? min.x : max.x,
                                            (i & 2) == 0 ? min.y : max.y,
                                            (i & 4) == 0 ? min.z : max.z);
                Vector3 screen = cam.WorldToScreenPoint(world);
                if (screen.z <= 0f) continue;
                float sy = UnityEngine.Screen.height - screen.y;
                minX = Mathf.Min(minX, screen.x); maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, sy);       maxY = Mathf.Max(maxY, sy);
            }
            if (float.IsInfinity(minX)) return new Rect(-1000f, -1000f, 1f, 1f);
            return Rect.MinMaxRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
        }

        /// <summary>
        /// A slow parallax lean towards wherever the mouse is, with no cursor lock. The office
        /// is a menu, so the pointer has to stay usable — the arrows and the cards are clicked
        /// with it, and a locked-cursor mouselook here would mean the player cannot leave
        /// without alt-tabbing.
        /// </summary>
        void TickLook()
        {
            if (lookPivot == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 p = mouse.position.ReadValue();
            float nx = Mathf.Clamp((p.x / Mathf.Max(1, UnityEngine.Screen.width)) * 2f - 1f, -1f, 1f);
            float ny = Mathf.Clamp((p.y / Mathf.Max(1, UnityEngine.Screen.height)) * 2f - 1f, -1f, 1f);

            Quaternion want = lookHome * Quaternion.Euler(-ny * lookPitchRange, nx * lookYawRange, 0f);
            lookPivot.localRotation = Quaternion.Slerp(lookPivot.localRotation, want,
                1f - Mathf.Exp(-lookDamping * Time.deltaTime));
        }

        /// <summary>
        /// Turns the man in the selected photograph.
        ///
        /// A sweep rather than a full turntable by default: this is a file on a target, and the
        /// half of a 360 that shows the back of his head is the half that tells the player
        /// nothing. Swinging between two three-quarter views keeps the face readable the whole
        /// time while still being unmistakably three-dimensional. Set
        /// <see cref="dossierSpinSweep"/> to 0 for a continuous spin.
        ///
        /// The sine matters as much as the range — it eases into each end, so the turn reads as
        /// someone being examined rather than as a prop on a motor snapping direction.
        /// </summary>
        void TickDossierSpin()
        {
            if (spinTarget == null || dossierSpinSpeed <= 0f) return;
            spinClock += Time.deltaTime;

            float yaw;
            if (dossierSpinSweep <= 0f)
            {
                yaw = spinClock * dossierSpinSpeed;
            }
            else
            {
                // Angular frequency chosen so the fastest point of the swing is exactly
                // dossierSpinSpeed — the two fields stay independent to tune.
                float w = dossierSpinSpeed / dossierSpinSweep;
                yaw = dossierSpinSweep * Mathf.Sin(spinClock * w);
            }

            spinTarget.localRotation = Quaternion.Euler(0f, spinBaseYaw + yaw, 0f);
        }

        // ------------------------------------------------------------------ gui

        void EnsureStyles()
        {
            if (label != null) return;
            label = UIFontProvider.Style(GUI.skin.label); label.fontSize = 15; label.fontStyle = FontStyle.Bold;
            mono = UIFontProvider.Style(GUI.skin.label); mono.fontSize = 16; mono.wordWrap = true;
            body = UIFontProvider.Style(GUI.skin.label); body.fontSize = 15; body.wordWrap = true;
            big = UIFontProvider.Style(GUI.skin.label); big.fontSize = 26; big.fontStyle = FontStyle.Bold;
            huge = UIFontProvider.Style(GUI.skin.label); huge.fontSize = 46; huge.fontStyle = FontStyle.Bold;
            huge.alignment = TextAnchor.MiddleCenter;
            center = UIFontProvider.Style(GUI.skin.label); center.fontSize = 18;
            center.alignment = TextAnchor.MiddleCenter;
            control = new GUIStyle(center); control.fontStyle = FontStyle.Bold;
            subjectName = UIFontProvider.Style(GUI.skin.label); subjectName.fontSize = 19;
            subjectName.fontStyle = FontStyle.Bold;
            stamp = UIFontProvider.Style(GUI.skin.label); stamp.fontSize = 58; stamp.fontStyle = FontStyle.Bold;
            stamp.alignment = TextAnchor.MiddleCenter;
            arrow = UIFontProvider.Style(GUI.skin.label); arrow.fontSize = 40; arrow.fontStyle = FontStyle.Bold;
            arrow.alignment = TextAnchor.MiddleCenter;
        }

        void OnGUI()
        {
            EnsureStyles();
            float w = UnityEngine.Screen.width, h = UnityEngine.Screen.height;

            switch (screen)
            {
                case Screen_.Debrief: DrawDebrief(w, h); break;
                case Screen_.Lobby: DrawLobby(w, h); break;
                case Screen_.CampaignDone: DrawCampaignDone(w, h); break;
                default: DrawBoard(w, h); break;
            }
            GUI.color = Color.white;
        }

        void DrawLobby(float w, float h)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (lobbyLogo != null)
            {
                GUI.color = Color.white;
                // RoofShot.png has a large transparent border. Crop it while drawing so the
                // visible logo, rather than the empty canvas, fills the requested screen area.
                float logoWidth = Mathf.Min(540f, w * 0.66f);
                float logoHeight = logoWidth / 1.758f;
                Vector2 logoMouse = Event.current.mousePosition;
                float mouseX = Mathf.Clamp((logoMouse.x / Mathf.Max(1f, w)) * 2f - 1f, -1f, 1f);
                float mouseY = Mathf.Clamp((logoMouse.y / Mathf.Max(1f, h)) * 2f - 1f, -1f, 1f);
                float floatClock = Time.unscaledTime;
                Vector2 drift = new Vector2(
                    mouseX * 10f + Mathf.Sin(floatClock * 0.52f) * 1.4f,
                    mouseY * 6f + Mathf.Sin(floatClock * 0.86f) * 3f);
                var logoRect = new Rect(w - logoWidth - 20f + drift.x, 18f + drift.y,
                    logoWidth, logoHeight);
                var logoUv = new Rect(0.0367f, 0.2648f, 0.9282f, 0.5279f);

                Matrix4x4 previousMatrix = GUI.matrix;
                float tilt = mouseX * 0.55f + Mathf.Sin(floatClock * 0.61f) * 0.18f;
                GUIUtility.RotateAroundPivot(tilt, logoRect.center);
                GUI.DrawTextureWithTexCoords(logoRect, lobbyLogo, logoUv, true);
                GUI.matrix = previousMatrix;
            }

            Rect fileRect = ScreenRect(missionFile, cam, 16f);
            Rect pistolRect = ScreenRect(resetPistol, cam, 18f);
            Vector2 mouse = Event.current.mousePosition;
            bool fileHover = fileRect.Contains(mouse);
            bool pistolHover = pistolRect.Contains(mouse);

            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(new Rect(fileRect.x - 18f, fileRect.y - 36f, fileRect.width + 36f, 30f), px);
            const float resetLabelWidth = 218f;
            float resetLabelX = Mathf.Clamp(
                pistolRect.center.x - resetLabelWidth * 0.5f,
                20f,
                w - resetLabelWidth - 20f);
            var resetLabelRect = new Rect(resetLabelX, Mathf.Max(18f, pistolRect.y - 38f), resetLabelWidth, 30f);
            GUI.DrawTexture(resetLabelRect, px);

            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.Label(new Rect(fileRect.x - 30f, fileRect.y - 34f, fileRect.width + 60f, 28f),
                "MISSION SELECT", center);
            GUI.Label(resetLabelRect, "MISSION RESET", center);

            if (fileHover) Frame(fileRect, new Color(0.25f, 0.72f, 1f, 0.95f), 2f);
            if (pistolHover) Frame(pistolRect, new Color(0.25f, 0.72f, 1f, 0.95f), 2f);

            if (Time.unscaledTime < resetNoticeUntil)
            {
                GUI.color = new Color(0.25f, 0.72f, 1f, 0.95f);
                GUI.Label(new Rect(0, h * 0.72f, w, 30f), "MISSION PROGRESS RESET", center);
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (fileHover)
                {
                    screen = Screen_.Board;
                    if (uiSource != null && flipMissionSound != null)
                        uiSource.PlayOneShot(flipMissionSound, uiVolume);
                    Event.current.Use();
                }
                else if (pistolHover)
                {
                    CampaignState.ResetCampaign(contracts.Length);
                    index = 0;
                    RefreshSubjects();
                    resetNoticeUntil = Time.unscaledTime + 1.5f;
                    if (uiSource != null && selectMissionSound != null)
                        uiSource.PlayOneShot(selectMissionSound, uiVolume * 0.7f);
                    Event.current.Use();
                }
            }
        }

        void DrawDebrief(float w, float h)
        {
            GUI.color = new Color(0, 0, 0, 0.70f);
            GUI.DrawTexture(new Rect(0, 0, w, h), px);

            string title, sub;
            switch (lastOutcome)
            {
                case MissionOutcome.Complete:
                    title = "CONTRACT FULFILLED";
                    sub = "계약이 완료되었습니다.";
                    GUI.color = accent; break;
                case MissionOutcome.Partial:
                    title = "CONTRACT FAILED";
                    sub = "표적 제거 실패. 계약을 다시 선택할 수 있습니다.";
                    GUI.color = ink; break;
                default:
                    title = "YOU DIDN'T COME BACK";
                    sub = CampaignState.IsEliminated(lastMissionIndex)
                        ? "표적은 제거했지만 철수하지 못했습니다."
                        : "작전 중 사망했습니다. 계약은 완료되지 않았습니다.";
                    GUI.color = danger; break;
            }

            GUI.Label(new Rect(0, h * 0.34f, w, 60), title, huge);
            GUI.color = new Color(1, 1, 1, 0.72f);
            GUI.Label(new Rect(24f, h * 0.44f, w - 48f, 40f), sub, center);
            GUI.color = accent;
            GUI.Label(new Rect(12f, h * 0.56f, w - 24f, 32f), "PRESS SPACE", control);
        }

        void DrawCampaignDone(float w, float h)
        {
            GUI.color = new Color(0, 0, 0, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, w, h), px);
            GUI.color = accent;
            GUI.Label(new Rect(0, h * 0.32f, w, 60), "ALL CONTRACTS CLEARED", huge);
            GUI.color = new Color(1, 1, 1, 0.72f);
            GUI.Label(new Rect(24f, h * 0.42f, w - 48f, 36f), "모든 표적을 제거했습니다.", center);
            GUI.color = new Color(1, 1, 1, 0.45f);
            GUI.Label(new Rect(12f, h * 0.54f, w - 24f, 32f),
                "SPACE 계약서 확인 · R 진행 상황 초기화", control);
        }

        // ------------------------------------------------------------------ the open file

        /// <summary>
        /// One contract, open on the desk, with the rest of the stack showing behind it.
        ///
        /// This used to be a row of three cards with a detail pane underneath, which is a level
        /// select widget — the photographs shrank to icons and the contract itself got demoted
        /// to a footnote below the thing you were looking at. A dossier is not a card. So the
        /// open file gets the whole panel at document scale, and the other contracts exist the
        /// way paper does: as edges sticking out from under it.
        ///
        /// The stack is honest. There is one sheet back there per unopened contract, each is
        /// clickable, and a stamped file marks its own edge — so the desk shows how much work is
        /// left without a progress bar having to say it.
        /// </summary>
        void DrawBoard(float w, float h)
        {
            var c = Current;
            if (c == null) return;

            float pw = Mathf.Min(920f, w - 48f);
            float ph = Mathf.Min(560f, h - 40f);
            // Nudged down, because the stack grows upwards out of the top edge.
            var panel = new Rect((w - pw) * 0.5f, (h - ph) * 0.5f + 12f, pw, ph);

            DrawStackBehind(panel);

            GUI.color = new Color(0.06f, 0.06f, 0.07f, 0.94f);
            GUI.DrawTexture(panel, px);
            GUI.color = new Color(accent.r, accent.g, accent.b, 0.8f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), px);
            GUI.DrawTexture(new Rect(panel.x, panel.yMax - 2f, panel.width, 2f), px);

            const float pad = 28f;
            float lx = panel.x + pad;
            int done = CampaignState.EliminatedCount(contracts.Length);
            bool eliminated = CampaignState.IsEliminated(index);

            // ---- header ---------------------------------------------------------------
            GUI.color = new Color(1, 1, 1, 0.42f);
            GUI.Label(new Rect(lx, panel.y + 14f, 300f, 22f),
                $"CONTRACT  {index + 1} / {contracts.Length}", label);
            GUI.Label(new Rect(panel.xMax - pad - 240f, panel.y + 14f, 240f, 22f),
                $"제거 완료 {done} / {contracts.Length}",
                new GUIStyle(label) { alignment = TextAnchor.MiddleRight });

            GUI.color = eliminated ? new Color(1, 1, 1, 0.45f) : accent;
            GUI.Label(new Rect(lx, panel.y + 34f, pw * 0.6f, 40f), c.codename, big);
            DrawDifficulty(new Rect(panel.xMax - pad - 200f, panel.y + 42f, 200f, 22f), c.difficulty, eliminated);

            // ---- the photograph, at document scale ------------------------------------
            const float photoW = 224f, photoH = 290f;
            var photo = new Rect(lx, panel.y + 84f, photoW, photoH);

            GUI.color = new Color(1, 1, 1, 0.10f);
            GUI.DrawTexture(new Rect(photo.x - 4f, photo.y - 4f, photo.width + 8f, photo.height + 8f), px);

            if (c.photo != null)
            {
                // A killed target is drawn dimmed and then washed with neutral grey. IMGUI has
                // no desaturating blend and this build is not shipping a shader for one file
                // state — two passes collapse the colour far enough that the suit and the tie
                // read as tone rather than as red and blue, which is the whole point.
                GUI.color = eliminated ? new Color(0.60f, 0.60f, 0.64f, 1f) : Color.white;
                GUI.DrawTexture(photo, c.photo, ScaleMode.ScaleAndCrop);
                if (eliminated)
                {
                    GUI.color = new Color(0.42f, 0.42f, 0.45f, 0.52f);
                    GUI.DrawTexture(photo, px);
                }
            }
            else
            {
                GUI.color = new Color(0, 0, 0, 0.6f);
                GUI.DrawTexture(photo, px);
                GUI.color = new Color(1, 1, 1, 0.35f);
                GUI.Label(new Rect(photo.x, photo.center.y - 14f, photo.width, 26f), "NO PHOTO", center);
            }
            Frame(photo, new Color(0, 0, 0, 0.55f), 1f);

            if (eliminated)
            {
                // Pressed onto the file rather than printed on it: big, angled, over the face.
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(-12f, photo.center);
                GUI.color = new Color(danger.r, danger.g, danger.b, 0.92f);
                GUI.Label(new Rect(photo.x - 20f, photo.center.y - 42f, photo.width + 40f, 80f), "제거 완료", stamp);
                GUI.matrix = m;
            }

            float ny = photo.yMax + 10f;
            float identityWidth = photoW + 26f;
            GUIStyle fittedName = FitSingleLine(subjectName, c.targetName, identityWidth, 14);
            GUI.color = eliminated ? new Color(1, 1, 1, 0.45f) : ink;
            GUI.Label(new Rect(lx, ny, identityWidth, 34f), c.targetName, fittedName);
            GUI.color = new Color(1, 1, 1, 0.38f);
            float noteHeight = Mathf.Max(24f, mono.CalcHeight(new GUIContent(c.targetNote), identityWidth));
            GUI.Label(new Rect(lx, ny + 32f, identityWidth, noteHeight), c.targetNote, mono);

            // ---- the file's contents --------------------------------------------------
            float rx = photo.xMax + 34f;
            float rw = panel.xMax - pad - rx;
            float ry = photo.y - 4f;

            GUI.color = new Color(1, 1, 1, 0.80f);
            GUI.Label(new Rect(rx, ry, rw, 52f), c.brief, body);
            ry += 58f;

            ry = Row(rx, ry, rw, "경호", $"{c.guards}명 밀착 경호");
            ry = Row(rx, ry, rw, "거리", $"{c.range:0} m   ·   고도차 +{c.elevation:0} m");
            ry = Row(rx, ry, rw, "지급 탄약", $"{c.ammo} 발");
            ry += 12f;

            GUI.color = new Color(accent.r, accent.g, accent.b, 0.30f);
            GUI.DrawTexture(new Rect(rx, ry, rw, 1f), px);
            ry += 14f;

            ry = Row(rx, ry, rw, c.strikeByClock, "사격 가능 시간 종료 — 이전까지 제거", accent);
            ry = Row(rx, ry, rw, c.pickupClock, $"철수 예정 — 표적 제거 후 {c.survivalDuration:0}초 생존", accent);

            if (eliminated)
            {
                ry += 12f;
                GUI.color = new Color(danger.r, danger.g, danger.b, 0.8f);
                GUI.Label(new Rect(rx, ry, rw, 24f), "완료한 계약 — 미션 리셋 후 다시 플레이 가능", label);
            }

            // ---- arrows + prompt ------------------------------------------------------
            if (contracts.Length > 1)
            {
                if (ArrowButton(new Rect(panel.x - 64f, panel.center.y - 34f, 48f, 68f), "◀")) Select(index - 1);
                if (ArrowButton(new Rect(panel.xMax + 40f, panel.center.y - 34f, 48f, 68f), "▶")) Select(index + 1);
            }

            GUI.color = accent;
            GUI.Label(new Rect(panel.x, panel.yMax - 58f, pw, 28f),
                eliminated ? "CONTRACT COMPLETE — SELECT ANOTHER MISSION"
                           : "PRESS SPACE TO START THIS CONTRACT", control);
            GUI.color = new Color(1, 1, 1, 0.35f);
            GUI.Label(new Rect(panel.x, panel.yMax - 32f, pw, 24f),
                "A / D 또는 ◀ ▶ 계약 선택 · ESC 사무실로 돌아가기", control);
        }

        /// <summary>
        /// The unopened contracts, as sheets stacked under the open one — offset up and to the
        /// right so only their edges show.
        ///
        /// Drawn furthest-first so nearer sheets overlap them, and each sheet's click target is
        /// the strip sticking out past the panel, which is the part of a real stack you would
        /// actually take hold of.
        /// </summary>
        void DrawStackBehind(Rect panel)
        {
            const float dx = 15f, dy = 13f;

            // Every contract except the open one, in board order, so a given sheet is always the
            // same contract rather than reshuffling as the selection moves.
            int slot = contracts.Length - 2;
            for (int i = contracts.Length - 1; i >= 0; i--)
            {
                if (i == index) continue;

                var sheet = new Rect(panel.x + (slot + 1) * dx, panel.y - (slot + 1) * dy,
                                     panel.width, panel.height);

                // Only the strip past the panel's right edge is visible, and it is the only part
                // that takes the click.
                var grab = new Rect(panel.xMax + slot * dx, sheet.y, dx, sheet.height * 0.6f);
                bool hover = grab.Contains(Event.current.mousePosition);
                if (GUI.Button(grab, GUIContent.none, GUIStyle.none)) Select(i);

                GUI.color = hover ? new Color(0.25f, 0.24f, 0.22f, 0.97f)
                                  : new Color(0.155f, 0.15f, 0.14f, 0.95f);
                GUI.DrawTexture(sheet, px);

                // A lit top and right edge, so each sheet separates from the one behind it.
                GUI.color = new Color(1, 1, 1, hover ? 0.24f : 0.10f);
                GUI.DrawTexture(new Rect(sheet.x, sheet.y, sheet.width, 1f), px);
                GUI.DrawTexture(new Rect(sheet.xMax - 1f, sheet.y, 1f, sheet.height), px);

                // A stamped file is marked on its edge, so the stack says how much is left
                // without anything having to spell it out.
                GUI.color = CampaignState.IsEliminated(i)
                    ? new Color(danger.r, danger.g, danger.b, 0.9f)
                    : new Color(accent.r, accent.g, accent.b, 0.5f);
                GUI.DrawTexture(new Rect(sheet.xMax - dx + 3f, sheet.y + 18f, dx - 6f, 4f), px);

                slot--;
            }
            GUI.color = Color.white;
        }

        /// <summary>Three pips and a word. Pips read at a glance, the word removes the ambiguity.</summary>
        void DrawDifficulty(Rect r, int difficulty, bool dimmed)
        {
            difficulty = Mathf.Clamp(difficulty, 1, 3);
            string word = difficulty switch { 1 => "쉬움", 2 => "보통", _ => "어려움" };
            Color tone = difficulty switch
            {
                1 => new Color(0.45f, 0.80f, 0.55f),
                2 => accent,
                _ => danger,
            };
            if (dimmed) tone = new Color(tone.r, tone.g, tone.b, 0.4f);

            const float pip = 11f, pipGap = 5f, gapToWord = 10f, wordW = 62f;
            float total = 3f * pip + 2f * pipGap + gapToWord + wordW;
            float x = r.xMax - total;
            float y = r.y + (r.height - pip) * 0.5f;

            GUI.color = new Color(1, 1, 1, dimmed ? 0.25f : 0.42f);
            GUI.Label(new Rect(x - 76f, r.y - 2f, 70f, 22f), "난이도",
                new GUIStyle(label) { fontSize = 13, alignment = TextAnchor.MiddleRight });

            for (int i = 0; i < 3; i++)
            {
                GUI.color = i < difficulty ? tone : new Color(1, 1, 1, dimmed ? 0.08f : 0.15f);
                GUI.DrawTexture(new Rect(x + i * (pip + pipGap), y, pip, pip), px);
            }

            GUI.color = tone;
            GUI.Label(new Rect(x + 3f * pip + 2f * pipGap + gapToWord, r.y - 2f, wordW, 22f), word,
                new GUIStyle(label) { fontSize = 14 });
        }

        /// <summary>
        /// A clickable arrow. Drawn rather than using GUI.skin.button so it matches the rest of
        /// the panel, which has no chrome anywhere else.
        /// </summary>
        bool ArrowButton(Rect r, string glyph)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);

            GUI.color = hover ? new Color(accent.r, accent.g, accent.b, 0.18f) : new Color(1, 1, 1, 0.05f);
            GUI.DrawTexture(r, px);
            GUI.color = hover ? accent : new Color(1, 1, 1, 0.55f);
            GUI.Label(r, glyph, arrow);
            GUI.color = Color.white;
            return clicked;
        }

        void Frame(Rect r, Color color, float t)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), px);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), px);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), px);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), px);
            GUI.color = Color.white;
        }

        float Row(float x, float y, float w, string key, string value, Color? keyColor = null)
        {
            float valueWidth = Mathf.Max(80f, w - 100f);
            float rowHeight = Mathf.Max(24f, mono.CalcHeight(new GUIContent(value), valueWidth));
            GUI.color = keyColor ?? new Color(1, 1, 1, 0.45f);
            GUI.Label(new Rect(x, y, 96f, rowHeight), key, label);
            GUI.color = ink;
            GUI.Label(new Rect(x + 100f, y, valueWidth, rowHeight), value, mono);
            return y + rowHeight + 2f;
        }

        static GUIStyle FitSingleLine(GUIStyle source, string text, float width, int minimumFontSize)
        {
            var fitted = new GUIStyle(source) { wordWrap = false, clipping = TextClipping.Clip };
            var content = new GUIContent(text ?? string.Empty);
            while (fitted.fontSize > minimumFontSize && fitted.CalcSize(content).x > width)
                fitted.fontSize--;
            return fitted;
        }
    }
}
