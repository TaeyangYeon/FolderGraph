using System;
using System.Collections.Generic;
using FolderGraph.Graph.Abstractions;

namespace FolderGraph.Graph
{
    /// <summary>
    /// Fruchterman-Reingold 기반 힘-방향 레이아웃.
    /// - 모든 노드 쌍 사이에 반발력(k^2 / dist)
    /// - 연결된 노드 사이에 인력(dist^2 / k)
    /// - 중심으로의 약한 인력(흩어짐 방지)
    /// - 온도(temperature)로 한 틱당 이동량을 제한하고, 매 틱 냉각
    ///
    /// 한 틱당 반발력 계산이 O(n^2)이므로 노드가 매우 많으면 느려진다.
    /// (Phase 7에서 Barnes-Hut 등으로 최적화 예정)
    /// </summary>
    public class ForceDirectedSimulation : IForceDirectedSimulation
    {
        // 입자 상태(배열로 보관해 캐시 효율 확보)
        private IPhysicsBody[] _bodies;
        private double[] _px;
        private double[] _py;
        private double[] _dx;
        private double[] _dy;
        private bool[] _pinned;

        // 연결(인덱스 쌍)
        private int[] _linkA;
        private int[] _linkB;

        private double _k;            // 이상적 노드 간 거리
        private double _centerX;
        private double _centerY;
        private double _temperature;  // 현재 온도(틱당 최대 이동량)
        private double _initialTemp;  // Reheat 시 복귀할 온도

        // 튜닝 상수
        private const double Cooling = 0.5;    // 틱당 냉각률(클수록 더 완만/부드러움)
        private const double SettleRatio = 0.02; // 초기 온도 대비 이 비율 이하면 안정
        private const double Gravity = 0.012;    // 중심 인력 계수
        private const double Epsilon = 0.01;

        // 드래그 등 상호작용으로 깨울 때 쓰는 온도(초기 온도보다 낮게 → 부드럽게).
        // 너무 높으면 클릭 순간 노드들이 크게 튀고, 너무 낮으면 폴더 노드가
        // 부모를 따라오지 못한다. 0.35는 그 균형점.
        private const double ReheatFactor = 0.1;

        public bool IsSettled
        {
            get { return _temperature <= _initialTemp * SettleRatio; }
        }

        public void Initialize(IList<IPhysicsBody> bodies, IList<GraphLink> links,
                               double centerX, double centerY)
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

            // body → 인덱스 매핑 (링크 변환용)
            var indexOf = new Dictionary<IPhysicsBody, int>(n);

            var rnd = new Random(12345); // 결정적 시드(재현성)
            for (int i = 0; i < n; i++)
            {
                IPhysicsBody b = bodies[i];
                _bodies[i] = b;
                // 대칭 고착을 깨기 위한 미세 지터
                _px[i] = b.X + (rnd.NextDouble() - 0.5);
                _py[i] = b.Y + (rnd.NextDouble() - 0.5);
                _pinned[i] = b.IsPinned;
                indexOf[b] = i;
            }

            // 링크 인덱스화 (양 끝이 모두 알려진 경우만)
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

            // 영역 넓이 기준 이상 거리 k 산정
            double area = Math.Max(1.0, Math.Abs(centerX) * 2.0 * Math.Abs(centerY) * 2.0);
            double kRaw = 0.8 * Math.Sqrt(area / Math.Max(1, n));
            _k = Clamp(kRaw, 40.0, 160.0);

            // 초기 온도: 영역 한 변의 약 8%
            double span = Math.Max(centerX, centerY) * 2.0;
            _initialTemp = Math.Max(20.0, span * 0.08);
            _temperature = _initialTemp;
        }

        public void Reheat()
        {
            // 초기 온도까지 한 번에 올리면 클릭 순간 노드들이 크게 튄다.
            // 낮은 reheat 온도로 부드럽게 깨운다. 단 이미 더 뜨거우면 낮추지 않는다.
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

            // 외부(드래그 등)에서 바뀐 위치/고정상태를 반영
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

            // 1) 반발력 (모든 쌍) — O(n^2)
            double kSq = _k * _k;
            for (int i = 0; i < n; i++)
            {
                double xi = _px[i];
                double yi = _py[i];
                for (int j = i + 1; j < n; j++)
                {
                    double ddx = xi - _px[j];
                    double ddy = yi - _py[j];
                    double distSq = ddx * ddx + ddy * ddy;
                    if (distSq < Epsilon)
                    {
                        distSq = Epsilon;
                    }
                    double dist = Math.Sqrt(distSq);
                    double force = kSq / dist;       // 반발 크기
                    double ux = ddx / dist;
                    double uy = ddy / dist;
                    _dx[i] += ux * force;
                    _dy[i] += uy * force;
                    _dx[j] -= ux * force;
                    _dy[j] -= uy * force;
                }
            }

            // 2) 인력 (연결된 쌍, 스프링)
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
                double force = (dist * dist) / _k;   // 인력 크기
                double ux = ddx / dist;
                double uy = ddy / dist;
                _dx[a] += ux * force;
                _dy[a] += uy * force;
                _dx[b] -= ux * force;
                _dy[b] -= uy * force;
            }

            // 3) 중심 인력(흩어짐 방지)
            for (int i = 0; i < n; i++)
            {
                _dx[i] += (_centerX - _px[i]) * Gravity * _k;
                _dy[i] += (_centerY - _py[i]) * Gravity * _k;
            }

            // 4) 온도로 이동량 제한 후 적용
            for (int i = 0; i < n; i++)
            {
                if (_pinned[i])
                {
                    continue; // 고정 노드는 위치 유지
                }

                double dispLen = Math.Sqrt(_dx[i] * _dx[i] + _dy[i] * _dy[i]);
                if (dispLen < Epsilon)
                {
                    continue;
                }
                double capped = Math.Min(dispLen, _temperature);
                _px[i] += (_dx[i] / dispLen) * capped;
                _py[i] += (_dy[i] / dispLen) * capped;

                // 결과를 실제 body에 반영(좌표 setter가 PropertyChanged를 발생 → UI 갱신)
                _bodies[i].X = _px[i];
                _bodies[i].Y = _py[i];
            }

            // 냉각
            _temperature *= Cooling;

            return !IsSettled;
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}