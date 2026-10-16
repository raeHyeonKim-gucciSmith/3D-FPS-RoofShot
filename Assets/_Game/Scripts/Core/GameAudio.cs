using UnityEngine;

namespace RooftopContract
{
    /// <summary>Scene-wide audio hub. The scene builder assigns every clip.</summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio Instance { get; private set; }

        [Header("Player")]
        public AudioClip sniperShot;
        public AudioClip sniperAction;
        public AudioClip empty;
        public AudioClip pistolShot;
        public AudioClip pistolMagRemove;
        public AudioClip pistolMagInsert;
        public AudioClip pistolSlideReturn;

        [Header("Enemies / impacts")]
        public AudioClip enemySmgShot;
        public AudioClip enemyPistolShot;
        public AudioClip[] metalImpacts;

        [Header("Ambience")]
        public AudioClip cityAmbience;
        public AudioClip helicopterAmbience;
        public AudioClip tenseBackground;

        AudioSource oneShot;
        AudioSource city;
        AudioSource helicopter;
        AudioSource tense;
        SniperRig sniper;
        PistolRig pistol;
        MissionDirector director;

        void Awake()
        {
            Instance = this;
#if UNITY_EDITOR
            // An already-open scene can retain its in-memory serialized state after the scene
            // file changes on disk. Keep direct Mission play tests honest without affecting the
            // Web build, where the serialized scene reference is used normally.
            if (tenseBackground == null)
                tenseBackground = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/Sound/tensebackgroundsound.wav");
#endif
            oneShot = AddSource(false, false, 1f);
            city = AddSource(true, true, 0.22f);
            city.clip = cityAmbience;
            if (city.clip != null) city.Play();

            helicopter = AddSource(true, false, 0f);
            helicopter.clip = helicopterAmbience;

            tense = AddSource(true, false, 0.52f);
            tense.clip = tenseBackground;
            if (tense.clip == null)
                Debug.LogError("[Tense Audio] tensebackgroundsound.wav reference is missing.");
            else
            {
                tense.clip.LoadAudioData();
                Debug.Log($"[Tense Audio] ready clip='{tense.clip.name}' length={tense.clip.length:0.00}s");
            }
        }

        void Start()
        {
            sniper = FindFirstObjectByType<SniperRig>();
            pistol = FindFirstObjectByType<PistolRig>();
            director = MissionDirector.Instance;

            if (sniper != null)
            {
                sniper.Fired += OnSniperFired;
                sniper.OutOfAmmo += OnEmpty;
            }
            if (pistol != null)
            {
                pistol.Fired += OnPistolFired;
                pistol.ReloadStarted += OnReload;
            }
            if (director != null)
            {
                director.PhaseChanged += OnPhaseChanged;
                director.ExtractionStarted += OnExtraction;
                if (director.Phase == MissionPhase.Engaged || director.Phase == MissionPhase.Defense)
                    StartTense();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (sniper != null) { sniper.Fired -= OnSniperFired; sniper.OutOfAmmo -= OnEmpty; }
            if (pistol != null) { pistol.Fired -= OnPistolFired; pistol.ReloadStarted -= OnReload; }
            if (director != null)
            {
                director.PhaseChanged -= OnPhaseChanged;
                director.ExtractionStarted -= OnExtraction;
            }
        }

        AudioSource AddSource(bool loop, bool playOnAwake, float volume)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = playOnAwake;
            source.volume = volume;
            source.spatialBlend = 0f;
            return source;
        }

        void OnSniperFired()
        {
            Play2D(sniperShot, 0.9f, Random.Range(0.97f, 1.03f));
            if (sniperAction != null) Invoke(nameof(PlaySniperAction), 0.28f);
        }

        void PlaySniperAction() => Play2D(sniperAction, 0.5f, Random.Range(0.98f, 1.03f));
        void OnEmpty() => Play2D(empty, 0.7f, 1f);
        void OnPistolFired() => Play2D(pistolShot, 0.72f, Random.Range(0.97f, 1.04f));
        void OnReload()
        {
            if (pistol == null) return;

            CancelInvoke(nameof(PlayPistolMagRemove));
            CancelInvoke(nameof(PlayPistolMagInsert));
            CancelInvoke(nameof(PlayPistolSlideReturn));

            // These normalized points match PistolViewModel's visible reload beats.
            Invoke(nameof(PlayPistolMagRemove), pistol.reloadTime * 0.23f);
            Invoke(nameof(PlayPistolMagInsert), pistol.reloadTime * 0.72f);
            if (pistol.AmmoInMag <= 0)
                Invoke(nameof(PlayPistolSlideReturn), pistol.reloadTime * 0.80f);
        }

        void PlayPistolMagRemove() => Play2D(pistolMagRemove, 0.72f, Random.Range(0.98f, 1.02f));
        void PlayPistolMagInsert() => Play2D(pistolMagInsert, 0.82f, Random.Range(0.98f, 1.02f));
        void PlayPistolSlideReturn() => Play2D(pistolSlideReturn, 0.88f, Random.Range(0.99f, 1.01f));

        void OnExtraction()
        {
            // The tense bed runs through the assault. The helicopter taking over the soundscape
            // is the transition point, so only then does the bed begin fading away.
            if (helicopter.clip == null) return;
            helicopter.volume = 0f;
            helicopter.Play();
            Debug.Log("[Tense Audio] helicopter started; fading tense background.");
        }

        void OnPhaseChanged(MissionPhase phase)
        {
            if (phase == MissionPhase.Engaged && tense != null && tense.clip != null)
                StartTense();
            else if (phase == MissionPhase.Result && tense != null)
            {
                tense.Stop();
            }
        }

        void StartTense()
        {
            if (tense == null || tense.clip == null) return;
            tense.volume = 0.52f;
            if (!tense.isPlaying) tense.Play();
            Debug.Log($"[Tense Audio] playing={tense.isPlaying} volume={tense.volume:0.00}");
        }

        void Update()
        {
            if (helicopter != null && helicopter.isPlaying)
                helicopter.volume = Mathf.MoveTowards(helicopter.volume, 0.58f, Time.unscaledDeltaTime * 0.16f);

            if (director != null && director.IsExtracting && tense != null && tense.isPlaying)
            {
                tense.volume = Mathf.MoveTowards(tense.volume, 0f, Time.unscaledDeltaTime * 0.28f);
                if (tense.volume <= 0.001f)
                {
                    tense.Stop();
                    Debug.Log("[Tense Audio] fade complete; stopped.");
                }
            }
        }

        void Play2D(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || oneShot == null) return;
            oneShot.pitch = pitch;
            oneShot.PlayOneShot(clip, volume);
        }

        public static void PlayEnemyShot(Vector3 point, bool smg)
        {
            var i = Instance;
            if (i == null) return;
            i.PlayAtPoint(smg ? i.enemySmgShot : i.enemyPistolShot, point, smg ? 0.55f : 0.65f, 45f);
        }

        public static void PlayMetalImpact(Vector3 point)
        {
            var i = Instance;
            if (i == null || i.metalImpacts == null || i.metalImpacts.Length == 0) return;
            i.PlayAtPoint(i.metalImpacts[Random.Range(0, i.metalImpacts.Length)], point, 0.6f, 35f);
        }

        void PlayAtPoint(AudioClip clip, Vector3 point, float volume, float maxDistance)
        {
            if (clip == null) return;
            var go = new GameObject("Audio_OneShot");
            go.transform.position = point;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = Random.Range(0.96f, 1.04f);
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = maxDistance;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.01f, source.pitch) + 0.1f);
        }
    }
}
