using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Pooled impact puffs. Built from primitives at runtime so the build has no art dependency:
    /// a short-lived particle burst is enough to read "the round landed there" at 250m.
    /// </summary>
    public static class ImpactFx
    {
        static ParticleSystem dust;
        static ParticleSystem blood;
        static ParticleSystem headBlood;

        public static void Play(Vector3 point, Vector3 normal, bool flesh, bool headshot = false)
        {
            // World-sized particles preserve perspective naturally. The old dust used speeds
            // above 20m/s and half-metre particles, so a distant miss read as an explosion.
            // This is a narrow little vertical spit: visible through the scope, small at 1x.
            var ps = headshot ? Get(ref headBlood, true, true)
                   : flesh   ? Get(ref blood, true, false)
                             : Get(ref dust, false, false);
            if (ps == null) return;
            ps.transform.position = point;
            Vector3 direction = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
            ps.transform.rotation = Quaternion.LookRotation(direction);
            ps.Emit(headshot ? 30 : (flesh ? 20 : 9));
        }

        static ParticleSystem Get(ref ParticleSystem cache, bool flesh, bool headshot)
        {
            if (cache != null) return cache;

            Color color = flesh ? new Color(0.72f, 0.09f, 0.12f)
                                : new Color(0.68f, 0.64f, 0.56f);
            var go = new GameObject(headshot ? "FX_HeadshotBlood" : (flesh ? "FX_Blood" : "FX_DustPlume"));
            Object.DontDestroyOnLoad(go);
            cache = go.AddComponent<ParticleSystem>();

            // AddComponent starts the system playing immediately, and several MainModule
            // properties (duration among them) refuse to be set on a running system.
            cache.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = cache.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = flesh
                ? (headshot ? new ParticleSystem.MinMaxCurve(0.22f, 0.46f)
                            : new ParticleSystem.MinMaxCurve(0.30f, 0.58f))
                : new ParticleSystem.MinMaxCurve(0.38f, 0.68f);
            main.startSpeed = flesh
                ? (headshot ? new ParticleSystem.MinMaxCurve(5.2f, 9.2f)
                            : new ParticleSystem.MinMaxCurve(2.4f, 5.8f))
                : new ParticleSystem.MinMaxCurve(2.1f, 4.6f);
            main.startSize = flesh
                ? (headshot ? new ParticleSystem.MinMaxCurve(0.025f, 0.085f)
                            : new ParticleSystem.MinMaxCurve(0.035f, 0.11f))
                : new ParticleSystem.MinMaxCurve(0.045f, 0.13f);
            main.startColor = flesh
                ? new ParticleSystem.MinMaxGradient(
                    new Color(0.28f, 0.015f, 0.02f, 1f),
                    new Color(0.78f, 0.045f, 0.055f, 1f))
                : new ParticleSystem.MinMaxGradient(color);
            main.gravityModifier = flesh ? (headshot ? 1.25f : 1.65f) : 0.75f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            var emission = cache.emission; emission.enabled = false;
            var shape = cache.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = headshot ? 13f : (flesh ? 26f : 9f);
            shape.radius = flesh ? 0.015f : 0.008f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = MaterialLibrary.Unlit(color);

            cache.Play();
            return cache;
        }
    }

    /// <summary>Runtime-created materials, so nothing here depends on an imported art asset.</summary>
    public static class MaterialLibrary
    {
        static Shader unlitShader;
        static Shader UnlitShader
        {
            get
            {
                if (unlitShader == null)
                    unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                return unlitShader;
            }
        }

        public static Material Unlit(Color c)
        {
            var m = new Material(UnlitShader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        static Material tracer;
        public static Material Tracer
        {
            get
            {
                if (tracer == null)
                {
                    tracer = Unlit(new Color(1f, 0.85f, 0.45f, 1f));
                    tracer.renderQueue = 3000;
                }
                return tracer;
            }
        }
    }
}
