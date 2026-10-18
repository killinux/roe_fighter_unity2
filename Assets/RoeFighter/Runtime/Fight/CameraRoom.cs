using System;
using UnityEngine;

namespace RoeFighter.Fight
{
    /// <summary>
    /// Where the fight camera may stand on a stage: a top-down grid around the arena (measured when the fight scene is
    /// built, RoeFightScene.MeasureRoom) that holds, for every cell, how far it is from the nearest piece of the stage
    /// at the height of the camera and the fighters (a cell is "blocked" when stage geometry passes through it between
    /// <see cref="bandLow"/> and <see cref="bandHigh"/> above the arena floor: walls, props, the ropes of a ring).
    /// FightGame keeps the camera where it has room and nothing of the stage stands between it and the two.
    /// </summary>
    [Serializable]
    public class CameraRoom
    {
        public Vector3 origin;           // corner of cell (0, 0); y = the arena floor
        public float cell = 0.25f;       // metres
        public int size;                 // cells per side
        public float bandLow = 0.3f, bandHigh = 2.6f;
        public byte[] clearance;         // per cell (x + z * size): metres to the nearest blocked cell, in Unit steps; 0 = blocked

        public const float Unit = 0.05f;
        public const float Far = 255 * Unit;

        public bool Valid => clearance != null && size > 0 && clearance.Length == size * size;

        /// <summary>Metres from a point (its height is ignored) to the nearest stage geometry; outside the grid: <see cref="Far"/>.</summary>
        public float Clearance(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - origin.x) / cell);
            int z = Mathf.FloorToInt((p.z - origin.z) / cell);
            if (x < 0 || z < 0 || x >= size || z >= size)
                return Far;
            return clearance[x + z * size] * Unit;
        }

        /// <summary>
        /// Is the straight line between two points (seen from above) clear of the stage by more than
        /// <paramref name="need"/> metres?  Walks the line in steps as long as the room around each point allows.
        /// </summary>
        public bool Clear(Vector3 from, Vector3 to, float need = 0.02f) => LastBlocked(from, to, need) < 0f;

        /// <summary>How far along the line from <paramref name="from"/> the last blocked point lies (-1: none).</summary>
        public float LastBlocked(Vector3 from, Vector3 to, float need = 0.02f)
        {
            var d = new Vector2(to.x - from.x, to.z - from.z);
            float length = d.magnitude;
            if (length < 1e-4f)
                return Clearance(from) > need ? -1f : 0f;
            d /= length;
            float last = -1f, t = 0f, step = cell * 0.5f;
            while (t <= length)
            {
                float c = Clearance(new Vector3(from.x + d.x * t, 0f, from.z + d.y * t));
                if (c <= need)
                {
                    last = t;
                    t += step;
                }
                else
                    t += Mathf.Max(c - need - cell * 0.71f, step);
            }
            return last;
        }

        /// <summary>
        /// Fills <see cref="clearance"/> from a grid of blocked cells: the exact distance to the nearest blocked cell
        /// (two passes of the 1D squared-distance transform, Felzenszwalb and Huttenlocher), less half a cell.
        /// </summary>
        public void SetBlocked(bool[] blocked)
        {
            int n = size;
            const double inf = 1e12;
            var g = new double[n * n];
            for (int i = 0; i < g.Length; i++)
                g[i] = blocked[i] ? 0.0 : inf;
            var f = new double[n];
            var d = new double[n];
            var v = new int[n];
            var zz = new double[n + 1];
            void Pass(Func<int, int, int> index)
            {
                for (int line = 0; line < n; line++)
                {
                    for (int q = 0; q < n; q++)
                        f[q] = g[index(line, q)];
                    int k = 0;
                    v[0] = 0;
                    zz[0] = double.NegativeInfinity;
                    zz[1] = double.PositiveInfinity;
                    for (int q = 1; q < n; q++)
                    {
                        double s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                        while (s <= zz[k])
                        {
                            k--;
                            s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                        }
                        k++;
                        v[k] = q;
                        zz[k] = s;
                        zz[k + 1] = double.PositiveInfinity;
                    }
                    k = 0;
                    for (int q = 0; q < n; q++)
                    {
                        while (zz[k + 1] < q)
                            k++;
                        d[q] = (q - v[k]) * (double)(q - v[k]) + f[v[k]];
                    }
                    for (int q = 0; q < n; q++)
                        g[index(line, q)] = d[q];
                }
            }
            Pass((row, q) => q + row * n);      // along x
            Pass((col, q) => col + q * n);      // along z
            clearance = new byte[n * n];
            for (int i = 0; i < g.Length; i++)
            {
                if (blocked[i])
                    continue;
                double metres = Math.Sqrt(g[i]) * cell - cell * 0.5;
                clearance[i] = (byte)Mathf.Clamp(Mathf.FloorToInt((float)(metres / Unit)), 1, 255);
            }
        }
    }
}
