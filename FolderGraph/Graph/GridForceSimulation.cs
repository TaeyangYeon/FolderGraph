using System;
using System.Collections.Generic;
using FolderGraph.Graph.Abstractions;

namespace FolderGraph.Graph
{
    /// <summary>
    /// 3D Fruchterman-Reingold 시뮬레이션을 "균일 공간 격자(uniform grid)"로 가속한 버전.
    /// 반발력을 같은 칸 + 인접 26칸(3D) 안에서만 계산하므로, 노드가 퍼져 있으면
    /// 사실상 O(n)에 가깝다. 기본 ForceDirectedSimulation과 인터페이스가 같다.
    ///
    /// 전환: App.xaml.cs에서 new ForceDirectedSimulation() → new GridForceSimulation()
    /// </summary>
    public class GridForceSimulation : IForceDirectedSimulation
    {
        private IPhysicsBody[] _bodies;
        private double[] _px;
        private double[] _py;
        private double[] _pz;
        private double[] _dx;
        private double[] _dy;
        private double[] _dz;
        private bool[] _pinned;
        private int[] _linkA;
        private int[] _linkB;

        private double _k;
        private double _centerX;
        private double _centerY;
        private double _centerZ;
        private double _temperature;
        private double _initialTemp;
        private double _cutoff;

        // 튜닝 상수(ForceDirectedSimulation과 의미 동일)
        private const double Cooling = 0.985;
        private const double SettleRatio = 0.02;
        private const double Gravity = 0.012;
        private const double ReheatFactor = 0.35;
        private const double Epsilon = 0.01;
        private const double CutoffFactor = 2.5;

        private Dictionary<long, List<int>> _grid;

        public bool IsSettled
        {
            get { return _temperature <= _initialTemp * SettleRatio; }
        }

        public void Initialize(IList<IPhysicsBody> bodies, IList<GraphLink> links,
                               double centerX, double centerY, double initialEnergy)
        {
            int n = bodies != null ? bodies.Count : 0;

            _bodies = new IPhysicsBody[n];
            _px = new double[n];
            _py = new double[n];
            _pz = new double[n];
            _dx = new double[n];
            _dy = new double[n];
            _dz = new double[n];
            _pinned = new bool[n];

            _centerX = centerX;
            _centerY = centerY;
            _centerZ = 0.0;

            var indexOf = new Dictionary<IPhysicsBody, int>(n);
            var rnd = new Random(12345);
            for (int i = 0; i < n; i++)
            {
                IPhysicsBody b = bodies[i];
                _bodies[i] = b;
                _px[i] = b.X + (rnd.NextDouble() - 0.5);
                _py[i] = b.Y + (rnd.NextDouble() - 0.5);
                _pz[i] = b.Z + (rnd.NextDouble() - 0.5);
                _pinned[i] = b.IsPinned;
                indexOf[b] = i;
            }

            var la = new List<int>();
            var lb = new List<int>();
            if (links != null)
            {
                foreach (GraphLink link in links)
                {
                    int ia, ib;
                    if (indexOf.TryGetValue(link.A, out ia) &&
                        indexOf.TryGetValue(link.B, out ib))
                    {
                        la.Add(ia);
                        lb.Add(ib);
                    }
                }
            }
            _linkA = la.ToArray();
            _linkB = lb.ToArray();

            double area = Math.Max(1.0, Math.Abs(centerX) * 2.0 * Math.Abs(centerY) * 2.0);
            double kRaw = 0.8 * Math.Sqrt(area / Math.Max(1, n));
            _k = Clamp(kRaw, 40.0, 160.0);
            _cutoff = _k * CutoffFactor;

            double span = Math.Max(centerX, centerY) * 2.0;
            _initialTemp = Math.Max(20.0, span * 0.08);
            _temperature = _initialTemp * initialEnergy;

            _grid = new Dictionary<long, List<int>>();
        }

        public void Reheat()
        {
            double target = _initialTemp * ReheatFactor;
            if (_temperature < target)
            {
                _temperature = target;
            }
        }

        public bool Step()
        {
            if (_bodies == null || _bodies.Length == 0)
            {
                return false;
            }

            int n = _bodies.Length;

            for (int i = 0; i < n; i++)
            {
                _pinned[i] = _bodies[i].IsPinned;
                if (_pinned[i])
                {
                    _px[i] = _bodies[i].X;
                    _py[i] = _bodies[i].Y;
                    _pz[i] = _bodies[i].Z;
                }
                _dx[i] = 0.0;
                _dy[i] = 0.0;
                _dz[i] = 0.0;
            }

            BuildGrid(n);

            double kSq = _k * _k;
            double cutoffSq = _cutoff * _cutoff;

            // 1) 반발력 — 같은 칸 + 인접 26칸(3D)
            foreach (KeyValuePair<long, List<int>> cell in _grid)
            {
                int cx, cy, cz;
                DecodeKey(cell.Key, out cx, out cy, out cz);
                List<int> here = cell.Value;

                for (int gx = cx - 1; gx <= cx + 1; gx++)
                {
                    for (int gy = cy - 1; gy <= cy + 1; gy++)
                    {
                        for (int gz = cz - 1; gz <= cz + 1; gz++)
                        {
                            List<int> neighbors;
                            if (!_grid.TryGetValue(EncodeKey(gx, gy, gz), out neighbors))
                            {
                                continue;
                            }

                            for (int a = 0; a < here.Count; a++)
                            {
                                int i = here[a];
                                double xi = _px[i];
                                double yi = _py[i];
                                double zi = _pz[i];

                                for (int bIdx = 0; bIdx < neighbors.Count; bIdx++)
                                {
                                    int j = neighbors[bIdx];
                                    if (j <= i)
                                    {
                                        continue;
                                    }

                                    double ddx = xi - _px[j];
                                    double ddy = yi - _py[j];
                                    double ddz = zi - _pz[j];
                                    double distSq = ddx * ddx + ddy * ddy + ddz * ddz;
                                    if (distSq > cutoffSq)
                                    {
                                        continue;
                                    }
                                    if (distSq < Epsilon)
                                    {
                                        distSq = Epsilon;
                                    }
                                    double dist = Math.Sqrt(distSq);
                                    double force = kSq / dist;
                                    double ux = ddx / dist;
                                    double uy = ddy / dist;
                                    double uz = ddz / dist;
                                    _dx[i] += ux * force;
                                    _dy[i] += uy * force;
                                    _dz[i] += uz * force;
                                    _dx[j] -= ux * force;
                                    _dy[j] -= uy * force;
                                    _dz[j] -= uz * force;
                                }
                            }
                        }
                    }
                }
            }

            // 2) 인력(스프링)
            for (int e = 0; e < _linkA.Length; e++)
            {
                int a = _linkA[e];
                int b = _linkB[e];
                double ddx = _px[b] - _px[a];
                double ddy = _py[b] - _py[a];
                double ddz = _pz[b] - _pz[a];
                double dist = Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz);
                if (dist < Epsilon)
                {
                    dist = Epsilon;
                }
                double force = (dist * dist) / _k;
                double ux = ddx / dist;
                double uy = ddy / dist;
                double uz = ddz / dist;
                _dx[a] += ux * force;
                _dy[a] += uy * force;
                _dz[a] += uz * force;
                _dx[b] -= ux * force;
                _dy[b] -= uy * force;
                _dz[b] -= uz * force;
            }

            // 3) 중심 인력
            for (int i = 0; i < n; i++)
            {
                _dx[i] += (_centerX - _px[i]) * Gravity * _k;
                _dy[i] += (_centerY - _py[i]) * Gravity * _k;
                _dz[i] += (_centerZ - _pz[i]) * Gravity * _k;
            }

            // 4) 온도 제한 후 적용
            for (int i = 0; i < n; i++)
            {
                if (_pinned[i])
                {
                    continue;
                }
                double dispLen = Math.Sqrt(_dx[i] * _dx[i] + _dy[i] * _dy[i] + _dz[i] * _dz[i]);
                if (dispLen < Epsilon)
                {
                    continue;
                }
                double capped = Math.Min(dispLen, _temperature);
                double s = capped / dispLen;
                _px[i] += _dx[i] * s;
                _py[i] += _dy[i] * s;
                _pz[i] += _dz[i] * s;
                _bodies[i].X = _px[i];
                _bodies[i].Y = _py[i];
                _bodies[i].Z = _pz[i];
            }

            _temperature *= Cooling;
            return !IsSettled;
        }

        private void BuildGrid(int n)
        {
            _grid.Clear();
            double inv = 1.0 / _cutoff;
            for (int i = 0; i < n; i++)
            {
                int cx = (int)Math.Floor(_px[i] * inv);
                int cy = (int)Math.Floor(_py[i] * inv);
                int cz = (int)Math.Floor(_pz[i] * inv);
                long key = EncodeKey(cx, cy, cz);
                List<int> list;
                if (!_grid.TryGetValue(key, out list))
                {
                    list = new List<int>();
                    _grid[key] = list;
                }
                list.Add(i);
            }
        }

        // 3개의 21비트 정수를 하나의 long 키로(±~100만 범위)
        private static long EncodeKey(int x, int y, int z)
        {
            long lx = (long)(x + 1048576) & 0x1FFFFF;
            long ly = (long)(y + 1048576) & 0x1FFFFF;
            long lz = (long)(z + 1048576) & 0x1FFFFF;
            return (lx << 42) | (ly << 21) | lz;
        }

        private static void DecodeKey(long key, out int x, out int y, out int z)
        {
            x = (int)((key >> 42) & 0x1FFFFF) - 1048576;
            y = (int)((key >> 21) & 0x1FFFFF) - 1048576;
            z = (int)(key & 0x1FFFFF) - 1048576;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
