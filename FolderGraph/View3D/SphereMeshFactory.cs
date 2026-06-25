using System;
using System.Windows.Media.Media3D;

namespace FolderGraph.View3D
{
    /// <summary>
    /// 반지름 1의 단위 구 메시를 생성한다(한 번 만들어 재사용).
    /// 각 노드는 이 메시를 ScaleTransform/TranslateTransform으로 배치한다.
    /// </summary>
    public static class SphereMeshFactory
    {
        /// <summary>위도/경도 분할 수로 단위 구 메시를 만든다.</summary>
        public static MeshGeometry3D CreateUnitSphere(int stacks, int slices)
        {
            var mesh = new MeshGeometry3D();

            for (int stack = 0; stack <= stacks; stack++)
            {
                double phi = Math.PI * stack / stacks;       // 0..π
                double y = Math.Cos(phi);
                double r = Math.Sin(phi);

                for (int slice = 0; slice <= slices; slice++)
                {
                    double theta = 2.0 * Math.PI * slice / slices; // 0..2π
                    double x = r * Math.Cos(theta);
                    double z = r * Math.Sin(theta);

                    var p = new Point3D(x, y, z);
                    mesh.Positions.Add(p);
                    mesh.Normals.Add(new Vector3D(x, y, z));
                }
            }

            int ringSize = slices + 1;
            for (int stack = 0; stack < stacks; stack++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    int a = stack * ringSize + slice;
                    int b = a + ringSize;

                    mesh.TriangleIndices.Add(a);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(a + 1);

                    mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(b);
                    mesh.TriangleIndices.Add(b + 1);
                }
            }

            mesh.Freeze();
            return mesh;
        }
    }
}
