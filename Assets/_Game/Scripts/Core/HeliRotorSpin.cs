using UnityEngine;

namespace RooftopContract
{
    /// <summary>
    /// Turns the rotors of a helicopter that is parked and waiting, and nothing else.
    ///
    /// <see cref="HeliExtraction"/> already spins rotors, but it also owns an approach curve, a
    /// hover point and a mission-phase subscription — none of which apply to the bird sitting on
    /// the target's helipad. Pulling out the four lines that actually turn the discs is cheaper
    /// than teaching the exfil rig to do nothing.
    ///
    /// It matters visually out of all proportion to its size: a helicopter with still rotors
    /// reads as scenery, and this one is a countdown.
    /// </summary>
    public class HeliRotorSpin : MonoBehaviour
    {
        [Tooltip("회전시킬 로터. 동축 반전이면 순서대로 방향이 번갈아 적용된다.")]
        public Transform[] rotors;
        [Tooltip("초당 회전 각도.")]
        public float rotorSpeed = 1400f;

        void Update()
        {
            if (rotors == null) return;
            for (int i = 0; i < rotors.Length; i++)
            {
                if (rotors[i] == null) continue;
                // Coaxial rotors turn against each other; alternating the sign is free and it is
                // the difference between "a helicopter" and "a model with a spinning disc".
                float dir = (i % 2 == 0) ? 1f : -1f;
                rotors[i].Rotate(Vector3.up, rotorSpeed * dir * Time.deltaTime, Space.Self);
            }
        }
    }
}
