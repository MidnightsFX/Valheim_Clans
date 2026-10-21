using Jotunn.Managers;
using UnityEngine;

namespace Clans.Gameplay {
    // A column of dark mist over a World Boss fight's spawn point, so the clan sees where the waves will come from.
    // Nothing networked: every client with the totem loaded draws it from the points the totem's owner stored on the
    // totem (ClanTotem). A particle system made here, with the smoke material of a vanilla effect the game keeps
    // loaded, rather than a copy of an effect prefab stripped down, so nothing is ever destroyed immediately.
    internal static class PortalMist {
        // Vanilla effects whose smoke looks like dark mist, most fitting first.
        private static readonly string[] SmokeSources = { "vfx_odin_despawn", "vfx_spawn", "vfx_corpse_destruction_small" };
        private static Material smoke;
        private static bool looked;

        internal static GameObject Create(Vector3 at) {
            Material material = Smoke();
            if (material == null) { return null; }

            GameObject go = new GameObject("Clans_PortalMist");
            go.transform.position = at;
            ParticleSystem mist = go.AddComponent<ParticleSystem>();
            // A new system starts playing at once; its duration can only be set while it is stopped.
            mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = mist.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.05f, 0.03f, 0.07f, 0.7f), new Color(0.13f, 0.07f, 0.17f, 0.55f));
            main.gravityModifier = -0.015f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            ParticleSystem.EmissionModule emission = mist.emission;
            emission.rateOverTime = 16f;

            // A low cone, pointing up.
            ParticleSystem.ShapeModule shape = mist.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 1.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem.ColorOverLifetimeModule fade = mist.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystem.SizeOverLifetimeModule grow = mist.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.5f));

            ParticleSystem.RotationOverLifetimeModule turn = mist.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            mist.Play();
            return go;
        }

        private static Material Smoke() {
            if (looked) { return smoke; }
            looked = true;
            foreach (string name in SmokeSources) {
                GameObject prefab = PrefabManager.Cache.GetPrefab<GameObject>(name);
                if (prefab == null) { continue; }
                // The smoke itself rather than any sparks or flashes beside it.
                Material any = null;
                foreach (ParticleSystemRenderer renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                    if (renderer.sharedMaterial == null) { continue; }
                    if (renderer.name.IndexOf("smoke", System.StringComparison.OrdinalIgnoreCase) >= 0) {
                        smoke = renderer.sharedMaterial;
                        return smoke;
                    }
                    any = any ?? renderer.sharedMaterial;
                }
                if (any != null) {
                    smoke = any;
                    return smoke;
                }
            }
            Logger.LogWarning("No smoke material found for the World Boss spawn mist; the spawn points are not marked.");
            return null;
        }
    }
}
