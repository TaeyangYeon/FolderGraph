using System;
using System.Collections.Generic;
using FolderGraph.Graph.Abstractions;

namespace FolderGraph.Graph
{
    /// <summary>
    /// ForceDirectedSimulation과 동일한 Fruchterman-Reingold 모델이지만,
    /// 반발력 계산을 "균일 공간 격자(uniform grid)"로 가속한 버전.
    ///
    /// 기본 구현은 모든 노드 쌍을 보아 O(n^2)이라 노드가 많으면 느리다.
    /// 이 구현은 각 노드를 격자 칸에 넣고, 같은 칸 + 인접 8칸의 노드끼리만
    /// 반발력을 계산한다(컷오프 거리 밖은 영향이 작아 무시). 노드가 고르게
    /// 퍼져 있으면 사실상 O(n)에 가깝다.
    ///
    /// 사용법: App.xaml.cs의 조립부에서
    ///   IForceDirectedSimulation simulation = new ForceDirectedSimulation();
    /// 를
    ///   IForceDirectedSimulation simulation = new GridForceSimulation();
    /// 로 바꾸면 된다(인터페이스가 같아 다른 코드는 그대로).
    ///
    /// ※ 아래 Cooling/ReheatFactor 등 튜닝 상수는 ForceDirectedSimulation의
    ///   본인 설정값과 맞추고 싶으면 동일하게 바꿔 사용한다.
    /// </summary>
    public class GridForceSimulation : IForceDirectedSimulation
    {
        private IPhysicsBody[] _bodies;
        private double[] _px;
        private double[] _py;
        private double[] _dx;
        private double[] _dy;
        private bool[] _pinned;
        private int[] _linkA;
        private int[] _linkB;

        private double _k;
        private double _centerX;
        private double _centerY;
        private double _temperature;
        private double _initialTemp;
        private double _cutoff;          // 반발력 컷오프(= 격자 칸 크기)

        // 튜닝 상수(ForceDirectedSimulation과 의미 동일)
        private const double Cooling = 0.985;
        private const double SettleRatio = 0.02;
        private const double Gravity = 0.012;
        private const double ReheatFactor = 0.35;
        private const double Epsilon = 0.01;
        private const double CutoffFactor = 2.5; // 컷오프 = k * 이 값

        // 격자 상태(Step마다 재구성)
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
            _dx = new double[n];
            _dy = new double[n];
            _pinned = new bool[n];

            _centerX = centerX;
            _centerY = centerY;

            var indexOf = new Dictionary<IPhysicsBody, int>(n);
            var rnd = new Random(12345);
            for (int i = 0; i < n; i++)
            {
                IPhysicsBody b = bodies[i];
                _bodies[i] = b;
                _px[i] = b.X + (rnd.NextDouble() - 0.5);
                _py[i] = b.Y + (rnd.NextDouble() - 0.5);
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
                }
                _dx[i] = 0.0;
                _dy[i] = 0.0;
            }

            BuildGrid(n);

            // 1) 반발력 — 같은 칸 + 인접 8칸 내의 노드끼리만
            double kSq = _k * _k;
            double cutoffSq = _cutoff * _cutoff;

            foreach (KeyValuePair<long, List<int>> cell in _grid)
            {
                int cx, cy;
                DecodeKey(cell.Key, out cx, out cy);

                for (int gx = cx - 1; gx <= cx + 1; gx++)
                {
                    for (int gy = cy - 1; gy <= cy + 1; gy++)
                    {
                        List<int> neighbors;
                        if (!_grid.TryGetValue(EncodeKey(gx, gy), out neighbors))
                        {
                            continue;
                        }

                        List<int> here = cell.Value;
                        for (int a = 0; a < here.Count; a++)
                        {
                            int i = here[a];
                            double xi = _px[i];
                            double yi = _py[i];

                            for (int b = 0; b < neighbors.Count; b++)
                            {
                                int j = neighbors[b];
                                if (j <= i)
                                {
                                    continue; // 각 쌍 1회만(중복/자기자신 방지)
                                }

                                double ddx = xi - _px[j];
                                double ddy = yi - _py[j];
                                double distSq = ddx * ddx + ddy * ddy;
                                if (distSq > cutoffSq)
                                {
                                    continue; // 컷오프 밖은 무시
                                }
                                if (distSq < Epsilon)
                                {
                                    distSq = Epsilon;
                                }
                                double dist = Math.Sqrt(distSq);
                                double force = kSq / dist;
                                double ux = ddx / dist;
                                double uy = ddy / dist;
                                _dx[i] += ux * force;
                                _dy[i] += uy * force;
                                _dx[j] -= ux * force;
                                _dy[j] -= uy * force;
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
                double dist = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (dist < Epsilon)
                {
                    dist = Epsilon;
                }
                double force = (dist * dist) / _k;
                double ux = ddx / dist;
                double uy = ddy / dist;
                _dx[a] += ux * force;
                _dy[a] += uy * force;
                _dx[b] -= ux * force;
                _dy[b] -= uy * force;
            }

            // 3) 중심 인력
            for (int i = 0; i < n; i++)
            {
                _dx[i] += (_centerX - _px[i]) * Gravity * _k;
                _dy[i] += (_centerY - _py[i]) * Gravity * _k;
            }

            // 4) 온도 제한 후 적용
            for (int i = 0; i < n; i++)
            {
                if (_pinned[i])
                {
                    continue;
                }
                double dispLen = Math.Sqrt(_dx[i] * _dx[i] + _dy[i] * _dy[i]);
                if (dispLen < Epsilon)
                {
                    continue;
                }
                double capped = Math.Min(dispLen, _temperature);
                _px[i] += (_dx[i] / dispLen) * capped;
                _py[i] += (_dy[i] / dispLen) * capped;
                _bodies[i].X = _px[i];
                _bodies[i].Y = _py[i];
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
                long key = EncodeKey(cx, cy);
                List<int> list;
                if (!_grid.TryGetValue(key, out list))
                {
                    list = new List<int>();
                    _grid[key] = list;
                }
                list.Add(i);
            }
        }

        private static long EncodeKey(int x, int y)
        {
            // 두 32비트 정수를 하나의 long 키로
            return ((long)x << 32) ^ (uint)y;
        }

        private static void DecodeKey(long key, out int x, out int y)
        {
            x = (int)(key >> 32);
            y = (int)(uint)(key & 0xFFFFFFFF);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
