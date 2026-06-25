using System;
using System.Windows.Media.Media3D;

namespace FolderGraph.View3D
{
    /// <summary>
    /// CAD 스타일 궤도(orbit) 카메라 컨트롤러.
    /// 카메라는 항상 Target을 바라보며, 구 좌표(거리/방위각/고도각)로 위치가 결정된다.
    /// - Orbit(회전): 방위각/고도각 변경 (빈 공간 좌클릭 드래그)
    /// - Pan(평행이동): Target 자체를 화면 평면 방향으로 이동 (휠 버튼 드래그)
    /// - Zoom: Target까지의 거리 변경 (휠 스크롤)
    /// </summary>
    public class OrbitCameraController
    {
        private readonly PerspectiveCamera _camera;

        private Point3D _target;       // 바라보는 중심점
        private double _distance;      // Target까지의 거리
        private double _yaw;           // 방위각(수평 회전), 라디안
        private double _pitch;         // 고도각(상하 회전), 라디안

        private const double MinPitch = -1.45;   // ±83° 부근에서 제한(짐벌락 방지)
        private const double MaxPitch = 1.45;
        private const double MinDistance = 5.0;
        private const double MaxDistance = 100000.0;

        public OrbitCameraController(PerspectiveCamera camera)
        {
            _camera = camera;
            _target = new Point3D(0, 0, 0);
            _distance = 1200;
            _yaw = 0.8;
            _pitch = 0.5;
            Update();
        }

        public PerspectiveCamera Camera { get { return _camera; } }
        public Point3D Target { get { return _target; } }
        public double Distance { get { return _distance; } }

        /// <summary>회전: 마우스 이동량(px)에 비례해 방위각/고도각을 바꾼다.</summary>
        public void Orbit(double deltaX, double deltaY)
        {
            const double speed = 0.01;
            _yaw -= deltaX * speed;
            _pitch += deltaY * speed;

            if (_pitch < MinPitch) _pitch = MinPitch;
            if (_pitch > MaxPitch) _pitch = MaxPitch;
            Update();
        }

        /// <summary>줌: 휠 방향에 따라 거리를 지수적으로 조절(가까울수록 천천히).</summary>
        public void Zoom(int wheelDelta)
        {
            double factor = wheelDelta > 0 ? 0.9 : 1.0 / 0.9;
            _distance *= factor;
            if (_distance < MinDistance) _distance = MinDistance;
            if (_distance > MaxDistance) _distance = MaxDistance;
            Update();
        }

        /// <summary>
        /// 팬: 화면(카메라) 평면의 가로/세로 방향으로 Target을 옮긴다.
        /// 거리에 비례한 이동량으로 줌 레벨과 무관하게 자연스럽게 움직인다.
        /// </summary>
        public void Pan(double deltaX, double deltaY)
        {
            Vector3D forward = _target - _camera.Position;
            forward.Normalize();

            Vector3D up = new Vector3D(0, 1, 0);
            Vector3D right = Vector3D.CrossProduct(forward, up);
            if (right.Length < 1e-6)
            {
                right = new Vector3D(1, 0, 0);
            }
            right.Normalize();
            Vector3D camUp = Vector3D.CrossProduct(right, forward);
            camUp.Normalize();

            double scale = _distance * 0.0015;
            _target += (-deltaX * scale) * right + (deltaY * scale) * camUp;
            Update();
        }

        /// <summary>Target과 거리를 직접 지정(예: 그래프 전체가 보이도록 맞추기).</summary>
        public void SetView(Point3D target, double distance)
        {
            _target = target;
            _distance = Clamp(distance, MinDistance, MaxDistance);
            Update();
        }

        /// <summary>구 좌표 → 카메라 위치/방향을 다시 계산한다.</summary>
        private void Update()
        {
            double cosPitch = Math.Cos(_pitch);
            Vector3D offset = new Vector3D(
                _distance * cosPitch * Math.Cos(_yaw),
                _distance * Math.Sin(_pitch),
                _distance * cosPitch * Math.Sin(_yaw));

            Point3D position = _target + offset;
            _camera.Position = position;
            _camera.LookDirection = _target - position;
            _camera.UpDirection = new Vector3D(0, 1, 0);
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
