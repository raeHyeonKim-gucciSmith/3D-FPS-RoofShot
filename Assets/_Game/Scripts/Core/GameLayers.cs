using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Single place that resolves the project's layer names, so scripts never hardcode indices.
    /// The layers themselves are created by GameSceneBuilder (editor).
    /// </summary>
    public static class GameLayers
    {
        public const string PlayerName = "Player";
        public const string ActorName = "Actor";       // guards + VIP bodies
        public const string HitboxName = "Hitbox";     // head / torso trigger volumes

        static int player = -1, actor = -1, hitbox = -1;

        public static int Player  { get { if (player < 0) player = LayerMask.NameToLayer(PlayerName);  return Mathf.Max(0, player); } }
        public static int Actor   { get { if (actor  < 0) actor  = LayerMask.NameToLayer(ActorName);   return Mathf.Max(0, actor);  } }
        public static int Hitbox  { get { if (hitbox < 0) hitbox = LayerMask.NameToLayer(HitboxName);  return Mathf.Max(0, hitbox); } }

        /// <summary>Everything a bullet should stop on: world geometry, actors and their hitboxes.</summary>
        public static int BulletMask => ~((1 << 2) | (1 << 5)); // exclude IgnoreRaycast + UI
    }
}
